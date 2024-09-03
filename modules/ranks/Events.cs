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
		private readonly Dictionary<string, (string targetProperty, int points)> _experienceEvents = new(StringComparer.OrdinalIgnoreCase);

		private void Initialize_Events()
		{
			_eventManager = new EventManager(this);

			RegisterListener<Listeners.OnMapStart>(OnMapStart);
			RegisterListener<Listeners.OnTick>(UpdateScoreboards);

			RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post);
			RegisterEventHandler<EventRoundPrestart>(OnRoundPrestart, HookMode.Post);
			RegisterEventHandler<EventCsWinPanelMatch>(OnCsWinPanelMatch, HookMode.Post);
			RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post);

			InitializeExperienceEvents();
		}

		private void OnMapStart(string mapName)
		{
			_isGameEnd = false;
			AddTimer(1.0f, () =>
			{
				GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
			});
		}

		private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
		{
			if (_configAccessor.GetValue<bool>("Settings", "PointSummaries"))
			{
				foreach (var player in GetValidPlayers())
				{
					if (player.GetSetting<bool>("ShowRankChanges") && _roundPoints.TryGetValue(player.Controller, out int points))
					{
						string message = points > 0 ? Localizer["k4.phrases.round-summary-earn", points] : Localizer["k4.phrases.round-summary-lose", points];
						player.Print(message);
					}
				}
				_roundPoints.Clear();
			}
			return HookResult.Continue;
		}

		private HookResult OnRoundPrestart(EventRoundPrestart @event, GameEventInfo info)
		{
			_isGameEnd = false;
			_playerSpawned.Clear();
			return HookResult.Continue;
		}

		private HookResult OnCsWinPanelMatch(EventCsWinPanelMatch @event, GameEventInfo info)
		{
			_isGameEnd = true;
			return HookResult.Continue;
		}

		private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
		{
			HandlePlayerSpawn(@event.Userid);
			return HookResult.Continue;
		}

		private void InitializeExperienceEvents()
		{
			_experienceEvents.Clear();
			var events = new Dictionary<string, (string, int)>
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

			foreach (var eventEntry in events)
			{
				_experienceEvents[eventEntry.Key] = eventEntry.Value;
				RegisterMassEventHandler(eventEntry.Key);
			}
		}

		private void UpdateScoreboards()
		{
			if (!_configAccessor.GetValue<bool>("Settings", "UseScoreboardRanks"))
				return;

			int mode = _configAccessor.GetValue<int>("Settings", "ScoreboardMode");
			int rankMax = _configAccessor.GetValue<int>("Settings", "RankMax");
			int rankBase = _configAccessor.GetValue<int>("Settings", "RankBase");
			int rankMargin = _configAccessor.GetValue<int>("Settings", "RankMargin");

			foreach (var player in GetValidPlayers())
			{
				long currentPoints = player.GetStorage<long>("Points");
				var (determinedRank, _) = DetermineRanks(currentPoints);
				int rankId = determinedRank?.Id ?? 0;

				player.Controller.CompetitiveWins = 10;
				SetCompetitiveRank(player, mode, rankId, currentPoints, rankMax, rankBase, rankMargin);

				Utilities.SetStateChanged(player.Controller, "CCSPlayerController", "m_iCompetitiveRankType");

				var message = UserMessage.FromPartialName("ServerRankRevealAll");
				message.Recipients.Add(player.Controller);
				message.Send();
			}
		}

		private void SetCompetitiveRank(IPlayerServices player, int mode, int rankId, long currentPoints, int rankMax, int rankBase, int rankMargin)
		{
			switch (mode)
			{
				case 1:
					player.Controller.CompetitiveRankType = 11;
					player.Controller.CompetitiveRanking = (int)currentPoints;
					break;
				case 2:
				case 3:
					player.Controller.CompetitiveRankType = (sbyte)((mode == 2) ? 12 : 7);
					player.Controller.CompetitiveRanking = rankId >= 19 ? 18 : rankId;
					break;
				case 4:
					player.Controller.CompetitiveRankType = 10;
					player.Controller.CompetitiveRanking = rankId >= 16 ? 15 : rankId;
					break;
				default:
					int rank = rankId > rankMax ? rankBase + rankMax - rankMargin : rankBase + (rankId - rankMargin - 1);
					player.Controller.CompetitiveRankType = 12;
					player.Controller.CompetitiveRanking = rank;
					break;
			}
		}

		private void RegisterMassEventHandler(string eventName)
		{
			try
			{
				Type? eventType = Type.GetType($"CounterStrikeSharp.API.Core.{eventName}, CounterStrikeSharp.API");
				if (eventType != null && typeof(GameEvent).IsAssignableFrom(eventType))
				{
					var methodInfo = typeof(EventManager).GetMethod(nameof(EventManager.OnEventHappens))?.MakeGenericMethod(eventType);
					var handlerDelegate = methodInfo != null ? Delegate.CreateDelegate(typeof(GameEventHandler<>).MakeGenericType(eventType), _eventManager, methodInfo) : null;
					if (handlerDelegate != null)
					{
						var registerMethod = typeof(BasePlugin).GetMethod(nameof(RegisterEventHandler))?.MakeGenericMethod(eventType);
						registerMethod?.Invoke(this, new object[] { handlerDelegate, HookMode.Post });
					}
				}
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Failed to register event handler for {0}.", eventName);
			}
		}

		private void HandlePlayerSpawn(CCSPlayerController? player)
		{
			if (player == null || player.IsBot || player.IsHLTV)
				return;

			int requiredPlayers = _configAccessor.GetValue<int>("Settings", "MinPlayers");
			if (requiredPlayers > Utilities.GetPlayers().Count(p => p.IsValid && !p.IsBot && !p.IsHLTV) && !_playerSpawned.Contains(player))
			{
				_moduleServices?.PrintForPlayer(player, Localizer["k4.phrases.points_disabled", requiredPlayers]);
			}

			_playerSpawned.Add(player);
		}

		public class EventManager
		{
			private readonly Plugin _plugin;
			private readonly Dictionary<ulong, (int killStreak, long lastKillTime)> _playerKillStreaks = new();
			private readonly Dictionary<int, int> _killStreakPoints;

			public EventManager(Plugin plugin)
			{
				_plugin = plugin;
				_killStreakPoints = InitializeKillStreakPoints();
			}

			private Dictionary<int, int> InitializeKillStreakPoints()
			{
				return new Dictionary<int, int>
				{
					{ 2, _plugin._configAccessor.GetValue<int>("Points", "DoubleKill") },
					{ 3, _plugin._configAccessor.GetValue<int>("Points", "TripleKill") },
					{ 4, _plugin._configAccessor.GetValue<int>("Points", "Domination") },
					{ 5, _plugin._configAccessor.GetValue<int>("Points", "Rampage") },
					{ 6, _plugin._configAccessor.GetValue<int>("Points", "MegaKill") },
					{ 7, _plugin._configAccessor.GetValue<int>("Points", "Ownage") },
					{ 8, _plugin._configAccessor.GetValue<int>("Points", "UltraKill") },
					{ 9, _plugin._configAccessor.GetValue<int>("Points", "KillingSpree") },
					{ 10, _plugin._configAccessor.GetValue<int>("Points", "MonsterKill") },
					{ 11, _plugin._configAccessor.GetValue<int>("Points", "Unstoppable") },
					{ 12, _plugin._configAccessor.GetValue<int>("Points", "GodLike") }
				};
			}

			public HookResult OnEventHappens<T>(T gameEvent, GameEventInfo info) where T : GameEvent
			{
				if (_plugin._configAccessor.GetValue<int>("Settings", "MinPlayers") > _plugin.GetValidPlayers().Count())
					return HookResult.Continue;

				if (!_plugin._configAccessor.GetValue<bool>("Settings", "WarmupPoints") && _plugin.GameRules?.WarmupPeriod == true)
					return HookResult.Continue;

				if (_plugin._experienceEvents.TryGetValue(typeof(T).Name, out var eventInfo))
				{
					HandleEvent(typeof(T).Name, gameEvent, eventInfo.points);
				}

				return HookResult.Continue;
			}

			private void HandleEvent<T>(string eventName, T gameEvent, int points) where T : GameEvent
			{
				switch (eventName)
				{
					case "EventRoundEnd":
						HandleRoundEndEvent(gameEvent as EventRoundEnd);
						break;
					case "EventPlayerDeath":
						HandlePlayerDeathEvent(gameEvent as EventPlayerDeath);
						break;
					default:
						HandleRegularEvent(eventName, _plugin._experienceEvents[eventName].targetProperty, gameEvent, points);
						break;
				}
			}

			private void HandleRoundEndEvent(EventRoundEnd? roundEndEvent)
			{
				if (roundEndEvent == null) return;

				foreach (var player in _plugin.GetValidPlayers())
				{
					if (_plugin._playerSpawned.Contains(player.Controller))
						continue;

					int teamNum = player.Controller.TeamNum;
					if (teamNum <= (int)CsTeam.Spectator)
						continue;

					bool isWinner = teamNum == roundEndEvent.Winner;
					int points = isWinner ? _plugin._configAccessor.GetValue<int>("Points", "RoundWin") : _plugin._configAccessor.GetValue<int>("Points", "RoundLose");
					_plugin.ModifyPlayerPoints(player, points, isWinner ? "k4.events.roundwin" : "k4.events.roundlose");
				}
			}

			private void HandlePlayerDeathEvent(EventPlayerDeath? deathEvent)
			{
				if (deathEvent == null) return;

				IPlayerServices? victim = _plugin.GetZenithPlayer(deathEvent.Userid);
				IPlayerServices? attacker = _plugin.GetZenithPlayer(deathEvent.Attacker);

				if (victim != null)
				{
					HandleVictimDeath(victim, attacker, deathEvent);
				}

				if (attacker != null && attacker.Controller.SteamID != victim?.Controller.SteamID)
				{
					HandleAttackerKill(attacker, victim, deathEvent);
				}

				IPlayerServices? assister = _plugin.GetZenithPlayer(deathEvent.Assister);
				if (assister != null && assister.Controller.SteamID != deathEvent.Userid?.SteamID)
				{
					HandleAssisterEvent(assister, attacker, victim, deathEvent);
				}
			}

			private void HandleVictimDeath(IPlayerServices victim, IPlayerServices? attacker, EventPlayerDeath deathEvent)
			{
				if (deathEvent.Attacker == null || deathEvent.Attacker.SteamID == victim.Controller.SteamID)
				{
					if (!_plugin._isGameEnd)
						_plugin.ModifyPlayerPoints(victim, _plugin._configAccessor.GetValue<int>("Points", "Suicide"), "k4.events.suicide");
				}
				else
				{
					if (!_plugin._configAccessor.GetValue<bool>("Settings", "PointsForBots") && deathEvent.Userid?.IsBot == true)
						return;

					string? eventInfo = attacker != null && _plugin._configAccessor.GetValue<bool>("Settings", "ExtendedDeathMessages")
						? (_plugin.Localizer["k4.phrases.death-extended", attacker.Name, $"{attacker.GetStorage<long>("Points"):N0}"] ?? string.Empty)
						: null;

					int points = attacker != null && _plugin._configAccessor.GetValue<bool>("Settings", "DynamicDeathPoints")
						? _plugin.CalculateDynamicPoints(attacker, victim, _plugin._configAccessor.GetValue<int>("Points", "Death"))
						: _plugin._configAccessor.GetValue<int>("Points", "Death");

					_plugin.ModifyPlayerPoints(victim, points, "k4.events.playerdeath", eventInfo);
				}

				ResetKillStreak(victim);
			}

			private void HandleAttackerKill(IPlayerServices attacker, IPlayerServices? victim, EventPlayerDeath deathEvent)
			{
				if (deathEvent.Userid == null || (!_plugin._configAccessor.GetValue<bool>("Settings", "PointsForBots") && deathEvent.Userid.IsBot))
					return;

				if (!_plugin._configAccessor.GetValue<bool>("Settings", "FFAMode") && attacker.Controller.Team == deathEvent.Userid?.Team)
				{
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "TeamKill"), "k4.events.teamkill");
				}
				else
				{
					HandleKillEvent(attacker, victim, deathEvent);
				}
			}

			private void HandleKillEvent(IPlayerServices attacker, IPlayerServices? victim, EventPlayerDeath deathEvent)
			{
				string? eventInfo = victim != null && _plugin._configAccessor.GetValue<bool>("Settings", "ExtendedDeathMessages")
					? (_plugin.Localizer["k4.phrases.kill-extended", victim.Name, $"{victim.GetStorage<long>("Points"):N0}"] ?? string.Empty)
					: null;

				int points = _plugin._configAccessor.GetValue<bool>("Settings", "DynamicDeathPoints") && victim != null
					? _plugin.CalculateDynamicPoints(attacker, victim, _plugin._configAccessor.GetValue<int>("Points", "Kill"))
					: _plugin._configAccessor.GetValue<int>("Points", "Kill");

				_plugin.ModifyPlayerPoints(attacker, points, "k4.events.kill", eventInfo);

				HandleSpecialKillEvents(attacker, deathEvent);
				HandleKillStreak(attacker);
			}

			private void HandleSpecialKillEvents(IPlayerServices attacker, EventPlayerDeath deathEvent)
			{
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

				HandleSpecialWeaponKills(attacker, deathEvent.Weapon);
			}

			private void HandleSpecialWeaponKills(IPlayerServices attacker, string weapon)
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

					if (_killStreakPoints.TryGetValue(killStreak, out int streakPoints) && streakPoints != 0)
					{
						_plugin.ModifyPlayerPoints(attacker, streakPoints, $"k4.events.killstreak{killStreak}");
					}
				}
				else
				{
					_playerKillStreaks[steamId] = (1, currentTime);
				}
			}

			private void ResetKillStreak(IPlayerServices player)
			{
				_playerKillStreaks.Remove(player.Controller.SteamID);
			}

			private void HandleAssisterEvent(IPlayerServices assister, IPlayerServices? attacker, IPlayerServices? victim, EventPlayerDeath deathEvent)
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
						? (_plugin.Localizer["k4.phrases.assist-extended", victim.Name, $"{victim.GetStorage<long>("Points"):N0}"] ?? string.Empty)
						: null;

					_plugin.ModifyPlayerPoints(assister, _plugin._configAccessor.GetValue<int>("Points", "Assist"), "k4.events.assist", eventInfo);
					if (deathEvent.Assistedflash)
					{
						_plugin.ModifyPlayerPoints(assister, _plugin._configAccessor.GetValue<int>("Points", "AssistFlash"), "k4.events.assistflash");
					}
				}
			}

			private void HandleRegularEvent<T>(string eventName, string targetProperty, T gameEvent, int points) where T : GameEvent
			{
				var targetProp = typeof(T).GetProperty(targetProperty);
				if (targetProp != null)
				{
					var targetValue = targetProp.GetValue(gameEvent) as CCSPlayerController;
					if (targetValue != null)
					{
						var player = _plugin.GetZenithPlayer(targetValue);
						if (player != null)
						{
							string eventKey = $"k4.events.{eventName.ToLower().Replace("event", "")}";
							_plugin.ModifyPlayerPoints(player, points, eventKey);
						}
					}
					else if (targetProperty == "Team")
					{
						RewardTeamPoints(eventName, points);
					}
				}
			}

			private void RewardTeamPoints(string eventName, int points)
			{
				var eventTeams = new Dictionary<string, CsTeam>
				{
					{ "EventBombExploded", CsTeam.Terrorist },
					{ "EventHostageRescuedAll", CsTeam.CounterTerrorist }
				};

				if (eventTeams.TryGetValue(eventName, out CsTeam team))
				{
					foreach (var player in _plugin.GetValidPlayers())
					{
						if (player.Controller.Team == team)
						{
							string eventKey = $"k4.events.{eventName.ToLower().Replace("event", "")}";
							_plugin.ModifyPlayerPoints(player, points, eventKey);
						}
					}
				}
			}
		}
	}
}
