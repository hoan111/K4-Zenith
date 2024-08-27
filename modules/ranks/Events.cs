using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using ZenithAPI;

namespace Zenith_Ranks
{
	public sealed partial class Plugin : BasePlugin
	{
		private EventManager? _eventManager;
		Dictionary<string, (string targetProperty, decimal points)> _experienceEvents = new();

		private void Initialize_Events()
		{
			_eventManager = new EventManager(this);

			RegisterListener<Listeners.OnMapStart>((mapName) =>
			{
				AddTimer(1.0f, () =>
				{
					GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules;
				});
			});

			RegisterListener<Listeners.OnTick>(() =>
			{
				if (_configAccessor.GetValue<bool>("Settings", "UseScoreboardRanks"))
				{
					foreach (var player in GetValidPlayers())
					{
						long currentPoints = player.GetStorage<long>("Points");
						var (determinedRank, _) = DetermineRanks(currentPoints);
						int rankId = determinedRank?.Id ?? 0;

						player.Controller.CompetitiveWins = 10;
						switch (_configAccessor.GetValue<int>("Settings", "ScoreboardMode"))
						{
							// Premier
							case 1:
								{
									player.Controller.CompetitiveRankType = 11;
									player.Controller.CompetitiveRanking = (int)currentPoints;
									break;
								}
							// Competitive
							case 2:
								{
									player.Controller.CompetitiveRankType = 12;
									player.Controller.CompetitiveRanking = rankId >= 19 ? 18 : rankId - 1;
									break;
								}
							// Wingman
							case 3:
								{
									player.Controller.CompetitiveRankType = 7;
									player.Controller.CompetitiveRanking = rankId >= 19 ? 18 : rankId - 1;
									break;
								}
							// Danger Zone (!! DOES NOT WORK !!)
							case 4:
								{
									player.Controller.CompetitiveRankType = 10;
									player.Controller.CompetitiveRanking = rankId >= 16 ? 15 : rankId - 1;
									break;
								}
							// Custom Rank
							default:
								{
									int rankMax = _configAccessor.GetValue<int>("Settings", "RankMax");
									int rankBase = _configAccessor.GetValue<int>("Settings", "RankBase");
									int rankMargin = _configAccessor.GetValue<int>("Settings", "RankMargin");

									int rank = rankId > rankMax ? rankBase + rankMax - rankMargin : rankBase + (rankId - rankMargin - 1);

									player.Controller.CompetitiveRankType = 12;

									player.Controller.CompetitiveRanking = rank;
									break;
								}
						}

						Utilities.SetStateChanged(player.Controller, "CCSPlayerController", "m_iCompetitiveRankType");

						var message = UserMessage.FromPartialName("ServerRankRevealAll");

						message.Recipients.Add(player.Controller);
						message.Send();
					}
				}
			});

			RegisterEventHandler((EventRoundEnd @event, GameEventInfo info) =>
			{
				if (!_configAccessor.GetValue<bool>("Settings", "PointSummaries"))
				{
					GetValidPlayers().ToList().ForEach(player =>
					{
						if (!player.GetSetting<bool>("ShowRankChanges"))
							return;

						if (_roundPoints.TryGetValue(player.Controller, out int points))
						{
							string message = points > 0 ? Localizer["k4.phrases.round-summary-earn", points] : Localizer["k4.phrases.round-summary-lose", points];
							player.Print(message);
						}
					});
					_roundPoints.Clear();
				}
				return HookResult.Continue;
			}, HookMode.Post);

			_experienceEvents = new()
			{
				{ "EventRoundMvp", ("Userid", _configAccessor.GetValue<int>("Points", "MVP")) },
				{ "EventHostageRescued", ("Userid", _configAccessor.GetValue<int>("Points", "HostageRescue")) },
				{ "EventBombDefused", ("Userid", _configAccessor.GetValue<int>("Points", "BombDefused")) },
				{ "EventBombPlanted", ("Userid", _configAccessor.GetValue<int>("Points", "BombPlant")) },
				{ "EventPlayerDeath", ("Userid", _configAccessor.GetValue<int>("Points", "Death")) },
				{ "EventHostageKilled", ("Userid", _configAccessor.GetValue<int>("Points", "HostageKill")) },
				{ "EventHostageHurt", ("Userid", _configAccessor.GetValue<int>("Points", "HostageHurt")) },
				{ "EventBombPickup", ("Userid", _configAccessor.GetValue<int>("Points", "BombPickup")) },
				{ "EventBombDropped", ("Userid", _configAccessor.GetValue<int>("Points", "BombDrop")) },
				{ "EventBombExploded", ("Team", _configAccessor.GetValue<int>("Points", "BombExploded")) },
				{ "EventHostageRescuedAll", ("Team", _configAccessor.GetValue<int>("Points", "HostageRescueAll")) },
				{ "EventRoundEnd", ("winner", 0) }

			};

			foreach (var eventEntry in _experienceEvents)
			{
				RegisterMassEventHandler(eventEntry.Key);
			}
		}

		private void RegisterMassEventHandler(string eventName)
		{
			try
			{
				string fullyQualifiedTypeName = $"CounterStrikeSharp.API.Core.{eventName}, CounterStrikeSharp.API";
				Type? eventType = Type.GetType(fullyQualifiedTypeName);
				if (eventType != null && typeof(GameEvent).IsAssignableFrom(eventType))
				{
					MethodInfo? baseRegisterMethod = typeof(BasePlugin).GetMethod(nameof(RegisterEventHandler), BindingFlags.Public | BindingFlags.Instance);

					if (baseRegisterMethod != null)
					{
						MethodInfo registerMethod = baseRegisterMethod.MakeGenericMethod(eventType);

						MethodInfo? methodInfo = typeof(EventManager).GetMethod(nameof(EventManager.OnEventHappens), BindingFlags.Public | BindingFlags.Instance)?.MakeGenericMethod(eventType);
						if (methodInfo != null)
						{
							Delegate? handlerDelegate = Delegate.CreateDelegate(typeof(GameEventHandler<>).MakeGenericType(eventType), _eventManager, methodInfo);
							if (handlerDelegate != null)
							{
								registerMethod.Invoke(this, new object[] { handlerDelegate, HookMode.Post });
							}
							else
							{
								Logger.LogError($"Failed to create delegate for event type {eventType.Name}.");
							}
						}
						else
						{
							Logger.LogError($"OnEventHappens method not found for event type {eventType.Name}.");
						}
					}
					else
						Logger.LogError("RegisterEventHandler method not found.");
				}
				else
					Logger.LogError($"Event type not found in specified assembly. Event: {eventName}.");
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Failed to register event handler for {0}.", eventName);
			}
		}

		public class EventManager
		{
			private readonly Plugin _plugin;
			private Dictionary<ulong, (int killStreak, long lastKillTime)> _playerKillStreaks = new();

			public EventManager(Plugin plugin)
			{
				_plugin = plugin;
			}

			public HookResult OnEventHappens<T>(T gameEvent, GameEventInfo info) where T : GameEvent
			{
				if (_plugin._configAccessor.GetValue<int>("Settings", "MinPlayers") > _plugin.GetValidPlayers().Count())
					return HookResult.Continue;

				if (!_plugin._configAccessor.GetValue<bool>("Settings", "WarmupPoints") && _plugin.GameRules?.WarmupPeriod == true)
					return HookResult.Continue;

				string eventName = typeof(T).Name;
				if (_plugin._experienceEvents.TryGetValue(eventName, out var eventInfo))
				{
					HandleEvent(eventName, gameEvent, eventInfo.points);
				}

				return HookResult.Continue;
			}

			private void HandleEvent<T>(string eventName, T gameEvent, decimal points) where T : GameEvent
			{
				if (eventName == "EventRoundEnd")
				{
					HandleRoundEndEvent(gameEvent as EventRoundEnd);
				}
				else if (eventName == "EventPlayerDeath")
				{
					HandlePlayerDeathEvent(gameEvent as EventPlayerDeath);
				}
				else if (_plugin._experienceEvents.TryGetValue(eventName, out var eventInfo))
				{
					HandleRegularEvent(eventName, eventInfo.targetProperty, gameEvent, points);
				}
			}

			private void HandleRoundEndEvent(EventRoundEnd? roundEndEvent)
			{
				if (roundEndEvent != null)
				{
					foreach (var player in _plugin.GetValidPlayers())
					{
						bool isWinner = player.Controller.TeamNum == roundEndEvent.Winner;
						decimal points = isWinner ? _plugin._configAccessor.GetValue<int>("Points", "RoundWin") : _plugin._configAccessor.GetValue<int>("Points", "RoundLose");
						_plugin.ModifyPlayerPoints(player, points, isWinner ? "k4.events.roundwin" : "k4.events.roundloss");
					}
				}
			}

			private void HandlePlayerDeathEvent(EventPlayerDeath? deathEvent)
			{
				if (deathEvent != null)
				{
					IPlayerServices? victim = _plugin.GetZenithPlayer(deathEvent.Userid);
					IPlayerServices? attacker = _plugin.GetZenithPlayer(deathEvent.Attacker);

					if (victim != null)
					{
						if (attacker == null || attacker.Controller.SteamID == victim.Controller.SteamID)
						{
							_plugin.ModifyPlayerPoints(victim, _plugin._configAccessor.GetValue<int>("Points", "Suicide"), "k4.events.suicide");
						}
						else
						{
							if (!_plugin._configAccessor.GetValue<bool>("Settings", "PointsForBots") && deathEvent.Userid?.IsBot == true)
								goto AttackerDeathEvent;

							string? eventInfo = _plugin._configAccessor.GetValue<bool>("Settings", "ExtendedDeathMessages")
								? _plugin.Localizer["k4.phrases.death-extended", attacker.Name, attacker.GetStorage<long>("Points")] ?? string.Empty
								: null;
							_plugin.ModifyPlayerPoints(victim, _plugin._configAccessor.GetValue<bool>("Settings", "DynamicDeathPoints") ? _plugin.CalculateDynamicPoints(attacker, victim, _plugin._configAccessor.GetValue<int>("Points", "Death")) : _plugin._configAccessor.GetValue<int>("Points", "Death"), "k4.events.playerdeath", eventInfo);
						}

						ResetKillStreak(victim);
					}

				AttackerDeathEvent:

					if (attacker != null && attacker.Controller.SteamID != victim?.Controller.SteamID)
					{
						if (deathEvent.Userid == null || (!_plugin._configAccessor.GetValue<bool>("Settings", "PointsForBots") && deathEvent.Userid.IsBot))
							goto AssisterDeathEvent;

						if (!_plugin._configAccessor.GetValue<bool>("Settings", "FFAMode") && attacker.Controller.Team == deathEvent.Userid?.Team)
						{
							_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "TeamKill"), "k4.events.teamkill");
							goto AssisterDeathEvent;
						}

						HandleKillEvent(attacker, victim, deathEvent);
					}

				AssisterDeathEvent:

					IPlayerServices? assister = _plugin.GetZenithPlayer(deathEvent.Assister);
					if (assister != null && assister.Controller.SteamID != deathEvent.Userid?.SteamID)
					{
						if (!_plugin._configAccessor.GetValue<bool>("Settings", "FFAMode") && attacker?.Controller.Team == deathEvent.Userid?.Team && assister.Controller.Team == deathEvent.Userid?.Team)
						{
							_plugin.ModifyPlayerPoints(assister, _plugin._configAccessor.GetValue<int>("Points", "TeamKillAssist"), "k4.events.teamkillassist");
							if (deathEvent.Assistedflash)
							{
								_plugin.ModifyPlayerPoints(assister, _plugin._configAccessor.GetValue<int>("Points", "TeamKillAssistFlash"), "k4.events.teamkillassistflash");
							}
						}
						else
						{
							string? eventInfo = victim != null && _plugin._configAccessor.GetValue<bool>("Settings", "ExtendedDeathMessages")
								? _plugin.Localizer["k4.phrases.assist-extended", victim.Name, victim.GetStorage<long>("Points")] ?? string.Empty
								: null;
							_plugin.ModifyPlayerPoints(assister, _plugin._configAccessor.GetValue<int>("Points", "Assist"), "k4.events.assist", eventInfo);
							if (deathEvent.Assistedflash)
							{
								_plugin.ModifyPlayerPoints(assister, _plugin._configAccessor.GetValue<int>("Points", "AssistFlash"), "k4.events.assistflash");
							}
						}

					}
				}
			}

			private void HandleKillEvent(IPlayerServices attacker, IPlayerServices? victim, EventPlayerDeath deathEvent)
			{
				string? eventInfo = victim != null && _plugin._configAccessor.GetValue<bool>("Settings", "ExtendedDeathMessages")
								? _plugin.Localizer["k4.phrases.kill-extended", victim.Name, victim.GetStorage<long>("Points")] ?? string.Empty
								: null;
				_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<bool>("Settings", "DynamicDeathPoints") && victim != null ? _plugin.CalculateDynamicPoints(attacker, victim, _plugin._configAccessor.GetValue<int>("Points", "Kill")) : _plugin._configAccessor.GetValue<int>("Points", "Kill"), "k4.events.kill", eventInfo);

				if (deathEvent.Headshot)
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "Headshot"), "k4.events.headshot");

				if (deathEvent.Penetrated > 0)
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "Penetrated") * deathEvent.Penetrated, "k4.events.penetrated");

				if (deathEvent.Noscope)
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "NoScope"), "k4.events.noscope");

				if (deathEvent.Thrusmoke)
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "Thrusmoke"), "k4.events.thrusmoke");

				if (deathEvent.Attackerblind)
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "BlindKill"), "k4.events.blindkill");

				if (deathEvent.Distance >= _plugin._configAccessor.GetValue<int>("Points", "LongDistance"))
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "LongDistanceKill"), "k4.events.longdistance");

				HandleSpecialKills(attacker, deathEvent.Weapon);

				HandleKillStreak(attacker);
			}

			private void HandleSpecialKills(IPlayerServices attacker, string weapon)
			{
				string lowerCaseWeaponName = weapon.ToLower();

				if (lowerCaseWeaponName.Contains("hegrenade"))
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "GrenadeKill"), "k4.events.grenadekill");
				else if (lowerCaseWeaponName.Contains("inferno"))
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "InfernoKill"), "k4.events.infernokill");
				else if (lowerCaseWeaponName.Contains("grenade") || lowerCaseWeaponName.Contains("molotov") || lowerCaseWeaponName.Contains("flashbang") || lowerCaseWeaponName.Contains("bumpmine"))
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "ImpactKill"), "k4.events.impactkill");
				else if (lowerCaseWeaponName.Contains("knife") || lowerCaseWeaponName.Contains("bayonet"))
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "KnifeKill"), "k4.events.knifekill");
				else if (lowerCaseWeaponName == "taser")
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "TaserKill"), "k4.events.taserkill");
			}

			private void HandleKillStreak(IPlayerServices attacker)
			{
				ulong steamId = attacker.Controller.SteamID;
				long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

				if (!_playerKillStreaks.TryGetValue(steamId, out var streakInfo))
				{
					_playerKillStreaks[steamId] = (1, currentTime);
					return;
				}

				int killStreak = streakInfo.killStreak;
				long lastKillTime = streakInfo.lastKillTime;

				int timeBetweenKills = _plugin._configAccessor.GetValue<int>("Points", "SecondsBetweenKills");
				bool isValidStreak = timeBetweenKills <= 0 || (currentTime - lastKillTime <= timeBetweenKills);

				if (isValidStreak)
				{
					killStreak++;
					_playerKillStreaks[steamId] = (killStreak, currentTime);

					decimal streakPoints = GetKillStreakPoints(killStreak);
					if (streakPoints != 0)
					{
						_plugin.ModifyPlayerPoints(attacker, streakPoints, $"k4.events.killstreak{killStreak}");
					}
				}
				else
				{
					_playerKillStreaks[steamId] = (1, currentTime);
				}
			}

			private decimal GetKillStreakPoints(int killStreak)
			{
				return killStreak switch
				{
					2 => _plugin._configAccessor.GetValue<int>("Points", "DoubleKill"),
					3 => _plugin._configAccessor.GetValue<int>("Points", "TripleKill"),
					4 => _plugin._configAccessor.GetValue<int>("Points", "Domination"),
					5 => _plugin._configAccessor.GetValue<int>("Points", "Rampage"),
					6 => _plugin._configAccessor.GetValue<int>("Points", "MegaKill"),
					7 => _plugin._configAccessor.GetValue<int>("Points", "Ownage"),
					8 => _plugin._configAccessor.GetValue<int>("Points", "UltraKill"),
					9 => _plugin._configAccessor.GetValue<int>("Points", "KillingSpree"),
					10 => _plugin._configAccessor.GetValue<int>("Points", "MonsterKill"),
					11 => _plugin._configAccessor.GetValue<int>("Points", "Unstoppable"),
					12 => _plugin._configAccessor.GetValue<int>("Points", "GodLike"),
					_ => 0
				};
			}

			private void ResetKillStreak(IPlayerServices player)
			{
				ulong steamId = player.Controller.SteamID;
				_playerKillStreaks.Remove(steamId);
			}

			private void HandleRegularEvent<T>(string eventName, string targetProperty, T gameEvent, decimal points) where T : GameEvent
			{
				var targetProp = typeof(T).GetProperty(targetProperty);
				if (targetProp != null)
				{
					object? targetValue = targetProp.GetValue(gameEvent);
					if (targetValue is CCSPlayerController playerController)
					{
						var player = _plugin.GetZenithPlayer(playerController);
						if (player != null)
						{
							string eventKey = $"k4.events.{eventName.ToLower().Replace("event", "")}";
							_plugin.ModifyPlayerPoints(player, points, eventKey);
						}
					}
					else if (targetProperty == "Team")
					{
						Dictionary<string, CsTeam> eventTeams = new()
						{
							{ "EventBombExploded", CsTeam.Terrorist },
							{ "EventHostageRescuedAll", CsTeam.CounterTerrorist }
						};

						if (eventTeams.TryGetValue(eventName, out CsTeam team))
						{
							string eventKey = $"k4.events.{eventName.ToLower().Replace("event", "")}";
							RewardTeam(team, points, eventKey);
						}
					}
				}
			}

			private void RewardTeam(CsTeam team, decimal points, string eventKey)
			{
				foreach (var player in _plugin.GetValidPlayers())
				{
					if (player.Controller.Team == team)
					{
						_plugin.ModifyPlayerPoints(player, points, eventKey);
					}
				}
			}
		}
	}
}
