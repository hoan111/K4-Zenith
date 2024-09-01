using System.Text;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using Microsoft.Extensions.Logging;
using ZenithAPI;

namespace Zenith_Bans
{
	public sealed partial class Plugin : BasePlugin
	{
		private async Task InitializeServerIpAsync(int port)
		{
			try
			{
				using var client = new HttpClient();
				var externalIpString = await client.GetStringAsync("http://icanhazip.com");
				_serverIp = $"{externalIpString.Trim()}:{port}";

				Server.NextWorldUpdate(() =>
				{
					Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).ToList().ForEach((p) => ProcessPlayerData(p, false));
				});
			}
			catch (Exception ex)
			{
				Logger.LogError($"Failed to get server IP: {ex.Message}");
				_serverIp = "all";
			}
		}

		private void ProcessTargetAction(CCSPlayerController? caller, CCSPlayerController target, Action<CCSPlayerController> action, Action<TargetFailureReason>? onFail = null)
		{
			if (!AdminManager.CanPlayerTarget(caller, target))
			{
				_moduleServices?.PrintForPlayer(caller, Localizer["k4.general.targetimmunity", target.PlayerName]);
				onFail?.Invoke(TargetFailureReason.TargetImmunity);
				return;
			}

			action.Invoke(target);
		}

		private void ProcessTargetAction(CCSPlayerController? caller, TargetResult targetResult, Action<CCSPlayerController> action, Action<TargetFailureReason>? onFail = null)
		{
			if (!targetResult.Any())
			{
				_moduleServices?.PrintForPlayer(caller, Localizer["k4.general.targetnotfound"]);
				onFail?.Invoke(TargetFailureReason.TargetNotFound);
				return;
			}

			foreach (CCSPlayerController target in targetResult.Players)
			{
				ProcessTargetAction(caller, target, action, onFail);
			}
		}

		public IPlayerServices? GetZenithPlayer(CCSPlayerController? player)
		{
			if (player == null) return null;
			try { return _playerServicesCapability?.Get(player); }
			catch { return null; }
		}

		private void HandlePunishmentCommand(CCSPlayerController? controller, CommandInfo info, PunishmentType type, string durationConfigKey, string reasonConfigKey)
		{
			try
			{
				if (controller == null)
				{
					// Console handling
					int minArgs = type == PunishmentType.Kick ? 3 : 4;
					if (info.ArgCount < minArgs)
					{
						string usage = type == PunishmentType.Kick ?
							"<player> <reason>" :
							"<player> <duration> <reason>";
						_moduleServices?.PrintForPlayer(controller, Localizer["k4.general.console-usage", info.GetArg(0), usage]);
						return;
					}

					string reason;
					int? duration = null;
					if (type == PunishmentType.Kick)
					{
						reason = string.Join(" ", info.GetArg(2));
					}
					else
					{
						if (!int.TryParse(info.GetArg(2), out int parsedDuration))
						{
							_moduleServices?.PrintForPlayer(controller, Localizer["k4.general.invalid-punish-length"]);
							return;
						}
						duration = parsedDuration;
						reason = string.Join(" ", info.GetArg(3));
					}

					TargetResult targetResult = info.GetArgTargetResult(1);
					string targetString = info.GetArg(1);

					ProcessTargetAction(null, targetResult, (target) => ApplyPunishment(controller, target, type, duration, reason), (failureReason) =>
					{
						if (failureReason == TargetFailureReason.TargetNotFound)
						{
							if (SteamID.TryParse(targetString, out SteamID? steamId) && steamId?.IsValid() == true)
							{
								ApplyPunishment(controller, steamId, type, duration, reason);
							}
							else
							{
								_moduleServices?.PrintForPlayer(controller, Localizer["k4.general.invalid-target"]);
							}
						}
					});
					return;
				}

				// Player-initiated commands
				switch (info.ArgCount)
				{
					case 1:
						ShowPlayerSelectionMenu(controller, (target) =>
						{
							if (type == PunishmentType.Kick)
							{
								ShowReasonSelectionMenu(controller, _coreAccessor.GetValue<List<string>>("Config", reasonConfigKey), (reason) =>
								{
									ProcessTargetAction(controller, target, (t) => ApplyPunishment(controller, t, type, null, reason));
								});
							}
							else
							{
								var durations = _coreAccessor.GetValue<List<int>>("Config", durationConfigKey);
								if (durations.Count != 0)
								{
									ShowLengthSelectionMenu(controller, durations, (duration) =>
									{
										ShowReasonSelectionMenu(controller, _coreAccessor.GetValue<List<string>>("Config", reasonConfigKey), (reason) =>
										{
											ProcessTargetAction(controller, target, (t) => ApplyPunishment(controller, t, type, duration, reason));
										});
									});
								}
								else
								{
									ShowReasonSelectionMenu(controller, _coreAccessor.GetValue<List<string>>("Config", reasonConfigKey), (reason) =>
									{
										ProcessTargetAction(controller, target, (t) => ApplyPunishment(controller, t, type, null, reason));
									});
								}
							}
						});
						break;

					case 2:
						TargetResult targetResult = info.GetArgTargetResult(1);
						string targetString = info.GetArg(1);

						if (type == PunishmentType.Kick)
						{
							ShowReasonSelectionMenu(controller, _coreAccessor.GetValue<List<string>>("Config", reasonConfigKey), (reason) =>
							{
								ProcessTargetAction(controller, targetResult, (target) => ApplyPunishment(controller, target, type, null, reason));
							});
						}
						else
						{
							var durations = _coreAccessor.GetValue<List<int>>("Config", durationConfigKey);
							if (durations.Count != 0)
							{
								ShowLengthSelectionMenu(controller, durations, (duration) =>
								{
									ShowReasonSelectionMenu(controller, _coreAccessor.GetValue<List<string>>("Config", reasonConfigKey), (reason) =>
									{
										ProcessTargetAction(controller, targetResult, (target) => ApplyPunishment(controller, target, type, duration, reason));
									});
								});
							}
							else
							{
								ShowReasonSelectionMenu(controller, _coreAccessor.GetValue<List<string>>("Config", reasonConfigKey), (reason) =>
								{
									ProcessTargetAction(controller, targetResult, (target) => ApplyPunishment(controller, target, type, null, reason));
								});
							}
						}
						break;

					default:
						TargetResult cacheResult = info.GetArgTargetResult(1);
						string cacheString = info.GetArg(1);

						if (type == PunishmentType.Kick)
						{
							string reason = info.GetArg(2);
							ProcessTargetAction(controller, cacheResult, (target) => ApplyPunishment(controller, target, type, null, reason));
						}
						else if (int.TryParse(info.GetArg(2), out int duration))
						{
							if (info.ArgCount == 3 && _coreAccessor.GetValue<bool>("Config", "ForcePunishmentReasons"))
							{
								ShowReasonSelectionMenu(controller, _coreAccessor.GetValue<List<string>>("Config", reasonConfigKey), (reason) =>
								{
									ProcessTargetAction(controller, cacheResult, (target) => ApplyPunishment(controller, target, type, duration, reason));
								});
							}
							else
							{
								string reason = info.ArgCount > 3 ? info.GetArg(3) : Localizer["k4.general.no-reason"];
								ProcessTargetAction(controller, cacheResult, (target) => ApplyPunishment(controller, target, type, duration, reason), (failureReason) =>
								{
									if (failureReason == TargetFailureReason.TargetNotFound)
									{
										if (SteamID.TryParse(cacheString, out SteamID? steamId) && steamId?.IsValid() == true)
										{
											ApplyPunishment(controller, steamId, type, duration, reason);
										}
										else
										{
											_moduleServices?.PrintForPlayer(controller, Localizer["k4.general.invalid-target"]);
										}
									}
								});
							}
						}
						else
						{
							_moduleServices?.PrintForPlayer(controller, Localizer["k4.general.invalid-punish-length"]);
						}
						break;
				}
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error in HandlePunishmentCommand: {ex.Message}\nStackTrace: {ex.StackTrace}");
				_moduleServices?.PrintForPlayer(controller, "An error occurred while processing the command.");
			}
		}

		private void ApplyPunishment(CCSPlayerController? caller, SteamID steamId, PunishmentType type, int? duration, string reason)
		{
			string callerName = caller?.PlayerName ?? Localizer["k4.general.console"];
			ulong? callerSteamId = caller?.SteamID;
			ulong targetSteamId = steamId.SteamId64;

			Task.Run(async () =>
			{
				string targetName = await GetPlayerNameAsync(targetSteamId);
				await ApplyPunishmentInternal(callerName, callerSteamId, targetSteamId, targetName, type, duration, reason);
			});
		}

		private void ApplyPunishment(CCSPlayerController? caller, CCSPlayerController target, PunishmentType type, int? duration, string reason)
		{
			string callerName = caller?.PlayerName ?? Localizer["k4.general.console"];
			ulong? callerSteamId = caller?.SteamID;
			ulong targetSteamId = target.SteamID;
			string targetName = target.PlayerName;

			Task.Run(async () =>
			{
				await ApplyPunishmentInternal(callerName, callerSteamId, targetSteamId, targetName, type, duration, reason);
			});
		}

		private async Task ApplyPunishmentInternal(string callerName, ulong? callerSteamId, ulong targetSteamId, string targetName, PunishmentType type, int? duration, string reason)
		{
			var activePunishments = await GetActivePunishmentsAsync(targetSteamId);
			if (type == PunishmentType.Warn)
			{
				int warnCount = activePunishments.Count(p => p.Type == PunishmentType.Warn) + 1; // +1 for the new warning
				int maxWarnings = _coreAccessor.GetValue<int>("Config", "WarnMax");

				if (warnCount >= maxWarnings)
				{
					var target = FindPlayerBySteamID(targetSteamId);
					if (target != null)
					{
						ApplyWarnBan(target);
						return; // Exit the method as we've applied a ban instead of a warning
					}
				}
			}
			else if (type != PunishmentType.Kick && activePunishments.Any(p => p.Type == type))
			{
				Server.NextFrame(() =>
				{
					CCSPlayerController? caller = FindPlayerBySteamID(callerSteamId ?? 0);
					_moduleServices?.PrintForPlayer(caller, Localizer["k4.general.punishment-already-active", type.ToString().ToLower()]);
				});
				return;
			}

			int punishmentId = await AddPunishmentAsync(targetSteamId, type, duration, reason, callerSteamId);

			Server.NextFrame(() =>
			{
				var target = FindPlayerBySteamID(targetSteamId);
				if (target != null && _playerCache.TryGetValue(targetSteamId, out var playerData))
				{
					playerData.Punishments.Add(new Punishment
					{
						Id = punishmentId,
						Type = type,
						Duration = duration,
						ExpiresAt = duration.HasValue && duration.Value != 0 ? DateTime.Now.AddMinutes(duration.Value) : null,
						PunisherName = string.IsNullOrEmpty(callerName) ? Localizer["k4.general.console"] : callerName,
						Reason = reason,
						AdminSteamId = callerSteamId
					});

					ApplyPunishmentEffect(target, type, reason);

					Task.Run(async () =>
					{
						string durationString = duration == 0 || duration == null ?
							Localizer["k4.general.permanent"] :
							$"{duration} {Localizer["k4.general.minutes"]}";

						await SendDiscordWebhookAsync("k4.discord.punishment", new Dictionary<string, string>
						{
							["player"] = $"{targetName} ({targetSteamId})",
							["type"] = type.ToString(),
							["duration"] = durationString,
							["reason"] = reason,
							["admin"] = $"{callerName} {(callerSteamId.HasValue ? $"({callerSteamId})" : "")}"
						});
					});
				}
				else
				{
					Logger.LogWarning($"Failed to find player or player data for {targetName} ({targetSteamId})");
				}

				if (type == PunishmentType.Kick)
				{
					string kickLocalizationKey = "k4.chat.kick";
					Logger.LogWarning($"Player {targetName} ({targetSteamId}) was {type.ToString().ToLower()}ed by {callerName} {(callerSteamId.HasValue ? $"({callerSteamId})" : "")}");

					foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
					{
						if (ShouldShowActivity(callerSteamId, player, true))
						{
							_moduleServices?.PrintForPlayer(player, Localizer[kickLocalizationKey, callerName, targetName, reason]);
						}
						else if (ShouldShowActivity(callerSteamId, player, false))
						{
							_moduleServices?.PrintForPlayer(player, Localizer[kickLocalizationKey, Localizer["k4.general.admin"], targetName, reason]);
						}
					}
				}
				else
				{
					string durationString = duration == 0 || duration == null ?
						Localizer["k4.general.permanent"] :
						$"{duration} {Localizer["k4.general.minutes"]}";

					string localizationKey = duration == 0 || duration == null ?
						$"k4.chat.{type.ToString().ToLower()}.permanent" :
						$"k4.chat.{type.ToString().ToLower()}";

					Logger.LogWarning($"Player {targetName} ({targetSteamId}) was {type.ToString().ToLower()}ed by {callerName} {(callerSteamId.HasValue ? $"({callerSteamId})" : "")} for {durationString} ({reason})");

					foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
					{
						if (ShouldShowActivity(callerSteamId, player, true))
						{
							if (duration == 0 || duration == null)
							{
								_moduleServices?.PrintForPlayer(player, Localizer[localizationKey, callerName, targetName, reason]);
							}
							else
							{
								_moduleServices?.PrintForPlayer(player, Localizer[localizationKey, callerName, targetName, durationString, reason]);
							}
						}
						else if (ShouldShowActivity(callerSteamId, player, false))
						{
							if (duration == 0 || duration == null)
							{
								_moduleServices?.PrintForPlayer(player, Localizer[localizationKey, Localizer["k4.general.admin"], targetName, reason]);
							}
							else
							{
								_moduleServices?.PrintForPlayer(player, Localizer[localizationKey, Localizer["k4.general.admin"], targetName, durationString, reason]);
							}
						}
					}
				}
			});
		}

		private bool ShouldShowActivity(ulong? adminSteamId, CCSPlayerController player, bool showName)
		{
			if (!adminSteamId.HasValue) return true; // Always show console activity
			int _showActivity = _coreAccessor.GetValue<int>("Config", "ShowActivity");

			bool isRoot = AdminManager.PlayerHasPermissions(player, "@zenith/root");
			bool isPlayerAdmin = AdminManager.PlayerHasPermissions(player, "@zenith-admin/admin");

			if (isRoot && (_showActivity & 16) != 0) return true; // Always show to root

			if (isPlayerAdmin)
			{
				if ((_showActivity & 4) == 0) return false; // Don't show to admins
				if (showName && (_showActivity & 8) == 0) return false; // Don't show names to admins
			}
			else
			{
				if ((_showActivity & 1) == 0) return false; // Don't show to non-admins
				if (showName && (_showActivity & 2) == 0) return false; // Don't show names to non-admins
			}

			return true;
		}

		private void ApplyWarnBan(CCSPlayerController target)
		{
			int banLength = _coreAccessor.GetValue<int>("Config", "WarnBanLength");
			string reason = Localizer["k4.general.max-warnings-reached"];

			Logger.LogWarning($"Player {target.PlayerName} ({target.SteamID}) was banned for reaching the maximum number of warnings");

			ApplyPunishment(null, target, PunishmentType.Ban, banLength, reason);

			// Clear warnings
			Task.Run(async () =>
			{
				await RemovePunishmentAsync(target.SteamID, PunishmentType.Warn, null);
				if (_playerCache.TryGetValue(target.SteamID, out var playerData))
				{
					playerData.Punishments.RemoveAll(p => p.Type == PunishmentType.Warn);
				}
			});
		}

		private void ApplyPunishmentEffect(CCSPlayerController target, PunishmentType type, string reason)
		{
			var zenithPlayer = GetZenithPlayer(target);
			if (zenithPlayer == null)
			{
				Logger.LogWarning($"Failed to get ZenithPlayer for {target.PlayerName} ({target.SteamID})");
				return;
			}

			switch (type)
			{
				case PunishmentType.Mute:
					zenithPlayer.SetMute(true, ActionPriority.High);
					break;
				case PunishmentType.Gag:
					zenithPlayer.SetGag(true, ActionPriority.High);
					break;
				case PunishmentType.Silence:
					zenithPlayer.SetMute(true, ActionPriority.High);
					zenithPlayer.SetGag(true, ActionPriority.High);
					break;
				case PunishmentType.Ban:
					DisconnectPlayer(target, NetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED, reason, false);
					break;
				case PunishmentType.Kick:
					DisconnectPlayer(target, NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED, reason, true);
					break;
			}
		}

		private void HandleRemovePunishmentCommand(CCSPlayerController? controller, CommandInfo info, PunishmentType type)
		{
			if (info.ArgCount == 1)
			{
				ShowPlayerSelectionMenu(controller, (target) => ProcessTargetAction(controller, target, (t) => RemovePunishment(controller, t, type)));
				return;
			}

			ProcessTargetAction(controller, info.GetArgTargetResult(1), (target) => RemovePunishment(controller, target, type), (reason) =>
			{
				if (reason == TargetFailureReason.TargetNotFound)
					RemovePunishment(controller, new SteamID(info.GetArg(1)), type);
			});
		}

		private void RemovePunishment(CCSPlayerController? caller, CCSPlayerController target, PunishmentType type)
		{
			string callerName = caller?.PlayerName ?? Localizer["k4.general.console"];
			ulong? callerSteamId = caller?.SteamID;

			ulong targetSteamId = target.SteamID;
			string targetName = target.PlayerName;

			Task.Run(async () =>
			{
				bool removed = await RemovePunishmentAsync(targetSteamId, type, callerSteamId);

				Server.NextFrame(() =>
				{
					if (removed)
					{
						if (_playerCache.TryGetValue(targetSteamId, out var playerData))
						{
							playerData.Punishments.RemoveAll(p => p.Type == type);
						}

						RemovePunishmentEffect(target, type);
						Logger.LogWarning($"Player {targetName} ({targetSteamId}) was un{type.ToString().ToLower()}ed by {callerName} {(callerSteamId.HasValue ? $"({callerSteamId})" : "")}");

						foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
						{
							if (ShouldShowActivity(callerSteamId, player, true))
							{
								_moduleServices?.PrintForPlayer(player, Localizer[$"k4.chat.un{type.ToString().ToLower()}", callerName, targetName]);
							}
							else if (ShouldShowActivity(callerSteamId, player, false))
							{
								_moduleServices?.PrintForPlayer(player, Localizer[$"k4.chat.un{type.ToString().ToLower()}", Localizer["k4.general.admin"], targetName]);
							}
						}
					}
					else
					{
						_moduleServices?.PrintForPlayer(caller, Localizer["k4.general.no-active-punishment", type.ToString().ToLower()]);
					}
				});
			});
		}

		private void RemovePunishment(CCSPlayerController? caller, SteamID steamId, PunishmentType type)
		{
			string callerName = caller?.PlayerName ?? Localizer["k4.general.console"];
			ulong? callerSteamId = caller?.SteamID;
			ulong targetSteamId = steamId.SteamId64;

			Task.Run(async () =>
			{
				bool removed = await RemovePunishmentAsync(targetSteamId, type, callerSteamId);
				string targetName = await GetPlayerNameAsync(targetSteamId);

				Server.NextFrame(() =>
				{
					if (removed)
					{
						Logger.LogWarning($"Player {targetName} ({targetSteamId}) was un{type.ToString().ToLower()}ed by {callerName} {(callerSteamId.HasValue ? $"({callerSteamId})" : "")}");
						_moduleServices?.PrintForAll(Localizer[$"k4.chat.un{type.ToString().ToLower()}", callerName, targetName]);

						Task.Run(async () =>
						{
							await SendDiscordWebhookAsync("k4.discord.unpunishment", new Dictionary<string, string>
							{
								["player"] = $"{targetName} ({targetSteamId})",
								["type"] = type.ToString(),
								["admin"] = $"{callerName} {(callerSteamId.HasValue ? $"({callerSteamId})" : "")}"
							});
						});
					}
					else
					{
						_moduleServices?.PrintForPlayer(caller, Localizer["k4.general.no-active-punishment", type.ToString().ToLower()]);
					}
				});
			});
		}

		private void RemovePunishmentEffect(CCSPlayerController target, PunishmentType type)
		{
			var zenithPlayer = GetZenithPlayer(target);
			switch (type)
			{
				case PunishmentType.Mute:
					zenithPlayer?.SetMute(false, ActionPriority.High);
					break;
				case PunishmentType.Gag:
					zenithPlayer?.SetGag(false, ActionPriority.High);
					break;
				case PunishmentType.Silence:
					zenithPlayer?.SetMute(false, ActionPriority.High);
					zenithPlayer?.SetGag(false, ActionPriority.High);
					break;
			}
		}

		private void CheckPlayerComms(CCSPlayerController? checker, CCSPlayerController? target)
		{
			if (target == null || checker == null) return;

			Task.Run(async () =>
			{
				var punishments = _playerCache.TryGetValue(target.SteamID, out var playerData)
					? playerData.Punishments
					: await GetActivePunishmentsAsync(target.SteamID);

				var mutePunishment = punishments.FirstOrDefault(p => p.Type == PunishmentType.Mute);
				var gagPunishment = punishments.FirstOrDefault(p => p.Type == PunishmentType.Gag);

				Server.NextFrame(() =>
				{
					_moduleServices?.PrintForPlayer(checker, Localizer["k4.commscheck.header", target.PlayerName]);

					// Mute check
					if (mutePunishment != null)
					{
						string duration = GetPunishmentDuration(mutePunishment);
						bool showAdminName = ShouldShowActivity(mutePunishment.AdminSteamId, checker, true);
						bool showAnonymous = !showAdminName && ShouldShowActivity(mutePunishment.AdminSteamId, checker, false);

						if (showAdminName || showAnonymous)
						{
							string adminName = showAdminName
								? (string.IsNullOrEmpty(mutePunishment.PunisherName) ? Localizer["k4.general.console"] : mutePunishment.PunisherName)
								: Localizer["k4.general.admin"];

							_moduleServices?.PrintForPlayer(checker, Localizer["k4.commscheck.mute", adminName, duration], false);
						}
					}
					else
					{
						_moduleServices?.PrintForPlayer(checker, Localizer["k4.commscheck.no-mute"], false);
					}

					// Gag check
					if (gagPunishment != null)
					{
						string duration = GetPunishmentDuration(gagPunishment);
						bool showAdminName = ShouldShowActivity(gagPunishment.AdminSteamId, checker, true);
						bool showAnonymous = !showAdminName && ShouldShowActivity(gagPunishment.AdminSteamId, checker, false);

						if (showAdminName || showAnonymous)
						{
							string adminName = showAdminName
								? (string.IsNullOrEmpty(gagPunishment.PunisherName) ? Localizer["k4.general.console"] : gagPunishment.PunisherName)
								: Localizer["k4.general.admin"];

							_moduleServices?.PrintForPlayer(checker, Localizer["k4.commscheck.gag", adminName, duration], false);
						}
					}
					else
					{
						_moduleServices?.PrintForPlayer(checker, Localizer["k4.commscheck.no-gag"], false);
					}
				});
			});
		}

		private string GetPunishmentDuration(Punishment punishment)
		{
			if (!punishment.ExpiresAt.HasValue)
				return Localizer["k4.general.permanent"];

			var timeLeft = punishment.ExpiresAt.Value - DateTime.Now;

			if (timeLeft <= TimeSpan.Zero)
				return "0 " + Localizer["k4.general.minutes"];

			return $"{Math.Ceiling(timeLeft.TotalMinutes)} {Localizer["k4.general.minutes"]}";
		}

		public void AddDisconnectedPlayer(DisconnectedPlayer player)
		{
			_disconnectedPlayers.Insert(0, player);

			if (_disconnectedPlayers.Count > _coreAccessor.GetValue<int>("Config", "DisconnectMaxPlayers"))
			{
				_disconnectedPlayers.RemoveAt(_disconnectedPlayers.Count - 1);
			}
		}

		public CCSPlayerController? FindPlayerBySteamID(ulong steamId)
		{
			return Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).FirstOrDefault(p => p.SteamID == steamId);
		}

		private void CheckPlayerWarns(CCSPlayerController? checker, CCSPlayerController? target)
		{
			if (target == null) return;

			Task.Run(async () =>
			{
				var punishments = _playerCache.TryGetValue(target.SteamID, out var playerData) ? playerData.Punishments : await GetActivePunishmentsAsync(target.SteamID);
				var warnings = punishments.Where(p => p.Type == PunishmentType.Warn).ToList();

				Server.NextFrame(() =>
				{
					_moduleServices?.PrintForPlayer(checker, Localizer["k4.warncheck.header", target.PlayerName, warnings.Count]);

					if (warnings.Any())
					{
						foreach (var warning in warnings)
						{
							_moduleServices?.PrintForPlayer(checker, Localizer["k4.warncheck.warn", string.IsNullOrEmpty(warning.PunisherName) ? Localizer["k4.general.console"] : warning.PunisherName, warning.Reason], false);
						}
					}
					else
					{
						_moduleServices?.PrintForPlayer(checker, Localizer["k4.warncheck.no-warns"], false);
					}
				});
			});
		}

		public void ProcessPlayerData(CCSPlayerController player, bool connect)
		{
			if (string.IsNullOrEmpty(player.IpAddress) || player.IpAddress.Contains("127.0.0.1") || !player.UserId.HasValue)
				return;

			if (player.AuthorizedSteamID is null)
			{
				Logger.LogError($"Failed to get authorize SteamID with Steam API for {player.PlayerName}");
				player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED_NOSTEAMLOGIN);
				return;
			}

			ulong steamId = player.AuthorizedSteamID.SteamId64;
			string playerName = player.PlayerName;
			string ipAddress = player.IpAddress.Split(":")[0];

			Task.Run(async () =>
			{
				PlayerData? playerData = await LoadOrUpdatePlayerDataAsync(steamId, playerName, ipAddress);
				if (playerData == null)
				{
					Logger.LogError($"Failed to load player data for {playerName} ({steamId})");
					return;
				}

				if (_coreAccessor.GetValue<bool>("Config", "ApplyIPBans"))
				{
					// Check if any banned player has the same IP
					bool ipBanned = await IsIpBannedAsync(ipAddress);
					if (ipBanned)
					{
						Server.NextFrame(() =>
						{
							player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_REJECT_BANNED);
						});
						return;
					}
				}

				Server.NextFrame(() =>
				{
					if (playerData.Punishments.Any(p => p.Type == PunishmentType.Ban && p.ExpiresAt > DateTime.UtcNow))
					{
						player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_REJECT_BANNED);
						return;
					}

					if (playerData.Punishments.Count > 0 && connect && _coreAccessor.GetValue<bool>("Config", "ConnectAdminInfo"))
					{
						Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV && AdminManager.PlayerHasPermissions(p, "@zenith-admin/admin")).ToList().ForEach(p =>
						{
							ShowPunishmentSummary(p, player, playerData);
						});
					}

					AdminManager.SetPlayerImmunity(player, (uint)playerData.Immunity.GetValueOrDefault());
					AdminManager.AddPlayerPermissions(player, [.. playerData.Permissions]);
					AdminManager.AddPlayerToGroup(player, [.. playerData.Groups]);

					IPlayerServices? playerServices = GetZenithPlayer(player);
					if (playerData.Punishments.Any(p => p.Type == PunishmentType.Mute && p.ExpiresAt > DateTime.UtcNow))
						playerServices?.SetMute(true, ActionPriority.High);

					if (playerData.Punishments.Any(p => p.Type == PunishmentType.Gag && p.ExpiresAt > DateTime.UtcNow))
						playerServices?.SetGag(true, ActionPriority.High);

					_playerCache[steamId] = playerData;

					if (_disconnectedPlayers.Any(p => p.SteamId == steamId))
					{
						DisconnectedPlayer disconnectedPlayer = _disconnectedPlayers.First(p => p.SteamId == steamId);
						_disconnectedPlayers.Remove(disconnectedPlayer);
					}
				});
			});
		}

		private void AddAdmin(CCSPlayerController? controller, CCSPlayerController target, string group)
		{
			string callerName = controller?.PlayerName ?? Localizer["k4.general.console"];
			ulong targetSteamId = target.SteamID;

			Task.Run(async () =>
			{
				await AddAdminAsync(targetSteamId, group);
				var (permissions, immunity) = await GetGroupDetailsAsync(group);

				Server.NextFrame(() =>
				{
					_moduleServices?.PrintForAll(Localizer["k4.addadmin.success", callerName, target.PlayerName, group]);

					// Refresh player's permissions
					AdminManager.AddPlayerPermissions(target, permissions.ToArray());

					if (immunity.HasValue)
						AdminManager.SetPlayerImmunity(target, (uint)immunity.Value);

					// Update player cache if necessary
					if (_playerCache.TryGetValue(targetSteamId, out var playerData))
					{
						playerData.Groups = [group];
						playerData.Permissions = permissions;
					}

					Task.Run(async () =>
					{
						await SendDiscordWebhookAsync("k4.discord.addadmin", new Dictionary<string, string>
						{
							["player"] = $"{target.PlayerName} ({target.SteamID})",
							["group"] = group,
							["admin"] = callerName
						});
					});
				});
			});
		}

		private void RemoveAdmin(CCSPlayerController? controller, CCSPlayerController target)
		{
			string callerName = controller?.PlayerName ?? Localizer["k4.general.console"];
			ulong targetSteamId = target.SteamID;

			Task.Run(async () =>
			{
				await RemoveAdminAsync(targetSteamId);

				Server.NextFrame(() =>
				{
					_moduleServices?.PrintForAll(Localizer["k4.removeadmin.success", callerName, target.PlayerName]);

					// Remove all permissions
					AdminManager.RemovePlayerPermissions(target);
					AdminManager.SetPlayerImmunity(target, 0);

					// Update player cache if necessary
					if (_playerCache.TryGetValue(targetSteamId, out var playerData))
					{
						playerData.Groups = [];
						playerData.Permissions = [];
						playerData.Immunity = null;
					}

					Task.Run(async () =>
					{
						await SendDiscordWebhookAsync("k4.discord.removeadmin", new Dictionary<string, string>
						{
							["player"] = $"{target.PlayerName} ({target.SteamID})",
							["admin"] = callerName
						});
					});
				});
			});
		}

		private void AddOfflineAdmin(CCSPlayerController? controller, SteamID steamId, string group)
		{
			string callerName = controller?.PlayerName ?? Localizer["k4.general.console"];
			Task.Run(async () =>
			{
				await AddAdminAsync(steamId.SteamId64, group);
				string targetName = await GetPlayerNameAsync(steamId.SteamId64);
				Server.NextFrame(() =>
				{
					_moduleServices?.PrintForAll(Localizer["k4.addadmin.success", callerName, targetName, group]);
				});
			});
		}

		private void RemoveOfflineAdmin(CCSPlayerController? controller, SteamID steamId)
		{
			string callerName = controller?.PlayerName ?? Localizer["k4.general.console"];
			ulong targetSteamId = steamId.SteamId64;

			Task.Run(async () =>
			{
				await RemoveAdminAsync(targetSteamId);
				string targetName = await GetPlayerNameAsync(targetSteamId);

				Server.NextFrame(() =>
				{
					_moduleServices?.PrintForAll(Localizer["k4.removeadmin.success", callerName, targetName]);
				});
			});
		}

		private void ShowPunishmentSummary(CCSPlayerController admin, CCSPlayerController player, PlayerData playerData)
		{
			var zenithAdmin = GetZenithPlayer(admin);
			if (zenithAdmin == null) return;

			int banCount = playerData.Punishments.Count(p => p.Type == PunishmentType.Ban);
			int commsBlockCount = playerData.Punishments.Count(p => p.Type == PunishmentType.Mute || p.Type == PunishmentType.Gag || p.Type == PunishmentType.Silence);
			int warnCount = playerData.Punishments.Count(p => p.Type == PunishmentType.Warn);

			_moduleServices?.PrintForPlayer(admin, Localizer["k4.punishment_summary.header", player.PlayerName], false);

			if (banCount > 0 || commsBlockCount > 0 || warnCount > 0)
			{
				if (banCount > 0)
					_moduleServices?.PrintForPlayer(admin, Localizer["k4.punishment_summary.bans", banCount], false);

				if (commsBlockCount > 0)
					_moduleServices?.PrintForPlayer(admin, Localizer["k4.punishment_summary.comms_blocks", commsBlockCount], false);

				if (warnCount > 0)
					_moduleServices?.PrintForPlayer(admin, Localizer["k4.punishment_summary.warns", warnCount], false);
			}
			else
			{
				_moduleServices?.PrintForPlayer(admin, Localizer["k4.punishment_summary.clean"], false);
			}
		}

		private async Task SendDiscordWebhookAsync(string localizerKey, Dictionary<string, string> replacements)
		{
			string _webhookUrl = _coreAccessor.GetValue<string>("Config", "DiscordWebhookUrl");
			if (string.IsNullOrEmpty(_webhookUrl))
				return;

			string jsonPayload = Localizer[localizerKey];

			foreach (var kvp in replacements)
			{
				jsonPayload = jsonPayload.Replace($"{{{kvp.Key}}}", kvp.Value);
			}

			jsonPayload = jsonPayload.Replace("{timestamp}", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

			var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

			try
			{
				var response = await _httpClient.PostAsync(_webhookUrl, content);
				if (!response.IsSuccessStatusCode)
				{
					Logger.LogError($"Failed to send Discord webhook. Status code: {response.StatusCode}");
				}
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error sending Discord webhook: {ex.Message}");
			}
		}

		public SteamID GetSteamID(string input)
		{
			if (ulong.TryParse(input, out ulong steamId))
			{
				return new SteamID(steamId);
			}
			else
			{
				return new SteamID(input);
			}
		}

		public void DisconnectPlayer(CCSPlayerController player, NetworkDisconnectionReason reason, string stringReason, bool kick)
		{
			int delay = _coreAccessor.GetValue<int>("Config", "DelayPlayerRemoval");
			if (delay <= 0)
			{
				player.Disconnect(reason);
				return;
			}

			CCSPlayerPawn? pawn = player.PlayerPawn.Value;

			if (pawn?.IsValid == true)
			{
				Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
				Schema.GetRef<MoveType_t>(pawn.Handle, "CBaseEntity", "m_nActualMoveType") = MoveType_t.MOVETYPE_OBSOLETE;
			}

			int countdown = delay;
			player.PrintToCenterAlert(Localizer[kick ? "k4.alert.kick" : "k4.alert.ban", countdown, stringReason]);

			_disconnectTImers[player] = AddTimer(1.0f, () =>
			{
				countdown--;

				if (player.IsValid)
				{
					player.PrintToCenterAlert(Localizer[kick ? "k4.alert.kick" : "k4.alert.ban", countdown, stringReason]);

					if (countdown <= 0)
					{
						player.Disconnect(reason);
						StopTimer();
					}
				}
				else
					StopTimer();

				void StopTimer()
				{
					_disconnectTImers[player]?.Kill();
					_disconnectTImers.Remove(player);
				}
			}, TimerFlags.REPEAT);
		}
	}
}