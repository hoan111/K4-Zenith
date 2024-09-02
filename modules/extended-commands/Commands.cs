
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace Zenith_ExtendedCommands;

public sealed partial class Plugin : BasePlugin
{
	public void Initialize_Commands()
	{
		_moduleServices?.RegisterModuleCommands(["hp", "health"], "Sets player health to a given value", (player, info) =>
		{
			int health = 100;
			if (info.ArgCount >= 2 && !int.TryParse(info.GetArg(2), out health))
			{
				_moduleServices?.PrintForPlayer(player, Localizer["commands.error.invalid_health"]);
				return;
			}

			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					target.PlayerPawn.Value.Health = health;
					Utilities.SetStateChanged(target.PlayerPawn.Value, "CBaseEntity", "m_iHealth");
					ShowActivityToPlayers(player?.SteamID, "commands.hp.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName, health);
				}
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 2, "<target> <health>", "@zenith-commands/health");

		_moduleServices?.RegisterModuleCommands(["armor"], "Sets player armor to a given value", (player, info) =>
		{
			int armor = 100;
			if (info.ArgCount >= 2 && !int.TryParse(info.GetArg(2), out armor))
			{
				_moduleServices?.PrintForPlayer(player, Localizer["commands.error.invalid_armor"]);
				return;
			}

			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					target.PlayerPawn.Value.ArmorValue = armor;
					Utilities.SetStateChanged(target.PlayerPawn.Value, "CCSPlayerPawn", "m_ArmorValue");
					ShowActivityToPlayers(player?.SteamID, "commands.armor.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName, armor);
				}
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target> <armor>", "@zenith-commands/armor");

		_moduleServices?.RegisterModuleCommands(["freeze"], "Freezes a player", (player, info) =>
		{
			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					Schema.GetRef<MoveType_t>(target.PlayerPawn.Value.Handle, "CBaseEntity", "m_nActualMoveType") = MoveType_t.MOVETYPE_OBSOLETE;
					Utilities.SetStateChanged(target.PlayerPawn.Value, "CBaseEntity", "m_MoveType");
					ShowActivityToPlayers(player?.SteamID, "commands.freeze.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName);
				}
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target>", "@zenith-commands/freeze");

		_moduleServices?.RegisterModuleCommands(["unfreeze"], "Unfreezes a player", (player, info) =>
		{
			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					Schema.GetRef<MoveType_t>(target.PlayerPawn.Value.Handle, "CBaseEntity", "m_nActualMoveType") = MoveType_t.MOVETYPE_WALK;
					Utilities.SetStateChanged(target.PlayerPawn.Value, "CBaseEntity", "m_MoveType");
					ShowActivityToPlayers(player?.SteamID, "commands.unfreeze.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName);
				}
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target>", "@zenith-commands/freeze");

		_moduleServices?.RegisterModuleCommands(["noclip"], "Toggles self-noclip", (player, info) =>
		{
			ProcessTargetAction(player, player!, (target) =>
			{
				if (target.PlayerPawn.Value != null)
				{
					var moveType = Schema.GetRef<MoveType_t>(target.PlayerPawn.Value.Handle, "CBaseEntity", "m_nActualMoveType");
					bool enableNoclip = moveType != MoveType_t.MOVETYPE_NOCLIP;
					moveType = enableNoclip ? MoveType_t.MOVETYPE_NOCLIP : MoveType_t.MOVETYPE_WALK;
					Utilities.SetStateChanged(target.PlayerPawn.Value, "CBaseEntity", "m_MoveType");
					ShowActivityToPlayers(player?.SteamID, enableNoclip ? "commands.noclip.enable" : "commands.noclip.disable", player?.PlayerName ?? Localizer["k4.general.console"]);
				}
			}, true);
		}, CommandUsage.CLIENT_ONLY, permission: "@zenith-commands/noclip");

		_moduleServices?.RegisterModuleCommands(["slay", "kill"], "Kills a player", (player, info) =>
		{
			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				target?.PlayerPawn.Value?.CommitSuicide(false, false);
				ShowActivityToPlayers(player?.SteamID, "commands.slay.success", player?.PlayerName ?? Localizer["k4.general.console"], target!.PlayerName);
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target>", "@zenith-commands/kill");

		_moduleServices?.RegisterModuleCommands(["rename"], "Renames a player", (player, info) =>
		{
			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					string newName = info.GetArg(2);
					target.PlayerName = newName;
					Utilities.SetStateChanged(target, "CBasePlayerController", "m_iszPlayerName");
					ShowActivityToPlayers(player?.SteamID, "commands.rename.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName, newName);
				}
			});
		}, CommandUsage.CLIENT_AND_SERVER, 2, "<target> <name>", "@zenith-commands/rename");

		_moduleServices?.RegisterModuleCommands(["respawn"], "Respawns a player", (player, info) =>
		{
			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				target?.Respawn();
				ShowActivityToPlayers(player?.SteamID, "commands.respawn.success", player?.PlayerName ?? Localizer["k4.general.console"], target!.PlayerName);
			});
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target>", "@zenith-commands/respawn");

		_moduleServices?.RegisterModuleCommands(["strip"], "Strips a player of all weapons", (player, info) =>
		{
			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				target?.RemoveWeapons();
				ShowActivityToPlayers(player?.SteamID, "commands.strip.success", player?.PlayerName ?? Localizer["k4.general.console"], target!.PlayerName);
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target>", "@zenith-commands/strip");

		_moduleServices?.RegisterModuleCommands(["tppos"], "Teleports a player to a given location", (player, info) =>
		{
			if (!float.TryParse(info.GetArg(2), out float x) || !float.TryParse(info.GetArg(3), out float y) || !float.TryParse(info.GetArg(4), out float z))
			{
				_moduleServices?.PrintForPlayer(player, Localizer["commands.error.invalid_coordinates"]);
				return;
			}

			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					target.PlayerPawn.Value.Teleport(new Vector(x, y, z));
					ShowActivityToPlayers(player?.SteamID, "commands.tppos.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName, x, y, z);
				}
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 4, "<target> <x> <y> <z>", "@zenith-commands/teleport");

		_moduleServices?.RegisterModuleCommands(["tp", "teleport", "goto"], "Teleports a player to another player", (player, info) =>
		{
			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				ProcessTargetAction(player, info.GetArgTargetResult(2), destination =>
				{
					if (target.PlayerPawn.Value != null && destination.PlayerPawn.Value != null)
					{
						target.PlayerPawn.Value.Teleport(destination.PlayerPawn.Value.AbsOrigin);
						ShowActivityToPlayers(player?.SteamID, "commands.tp.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName, destination.PlayerName);
					}
				}, true);
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 2, "<target> <destination>", "@zenith-commands/teleport");

		_moduleServices?.RegisterModuleCommands(["rcon"], "Executes an RCON command", (player, info) =>
		{
			string command = info.GetArg(1);
			Server.ExecuteCommand(command);
			ShowActivityToPlayers(player?.SteamID, "commands.rcon.success", player?.PlayerName ?? Localizer["k4.general.console"], command);
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<command>", "@zenith-commands/rcon");

		_moduleServices?.RegisterModuleCommands(["give"], "Gives a weapon to a player", (player, info) =>
		{
			Weapon? weapon = Weapon.List.FirstOrDefault(w => w.ClassName == info.GetArg(2));
			if (weapon == null)
			{
				_moduleServices?.PrintForPlayer(player, Localizer["commands.error.invalid_weapon"]);
				return;
			}

			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (weapon.Slot != gear_slot_t.GEAR_SLOT_GRENADES)
					RemoveWeaponInSlot(target, weapon.Slot); // ? Prevent crash from duplicate items

				target.GiveNamedItem(weapon.ClassName);
				ShowActivityToPlayers(player?.SteamID, "commands.give.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName, weapon.ClassName);
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 2, "<target> <weapon>", "@zenith-commands/give");

		_moduleServices?.RegisterModuleCommands(["cvar"], "Sets a ConVar value", (player, info) =>
		{
			string cvar = info.GetArg(1);
			string value = info.GetArg(2);
			Server.ExecuteCommand($"{cvar} {value}");
			ShowActivityToPlayers(player?.SteamID, "commands.cvar.success", player?.PlayerName ?? Localizer["k4.general.console"], cvar, value);
		}, CommandUsage.CLIENT_AND_SERVER, 2, "<convar> <value>", "@zenith-commands/cvar");

		_moduleServices?.RegisterModuleCommands(["speed"], "Sets player speed", (player, info) =>
		{
			if (!float.TryParse(info.GetArg(2), out float speed))
			{
				_moduleServices?.PrintForPlayer(player, Localizer["commands.error.invalid_speed"]);
				return;
			}

			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					target.PlayerPawn.Value.Speed = speed;
					Utilities.SetStateChanged(target.PlayerPawn.Value, "CBaseEntity", "m_flSpeed");
					ShowActivityToPlayers(player?.SteamID, "commands.speed.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName, speed);
				}
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 2, "<target> <speed>", "@zenith-commands/speed");

		_moduleServices?.RegisterModuleCommands(["revive"], "Revives a player on the death location", (player, info) =>
		{
			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					target.Respawn();

					if (_deathLocations.ContainsKey(target))
						target.Teleport(_deathLocations[target]);

					ShowActivityToPlayers(player?.SteamID, "commands.revive.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName);
				}
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target>", "@zenith-commands/revive");

		_moduleServices?.RegisterModuleCommands(["bury"], "Buries a player", (player, info) =>
		{
			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					Vector? absOrigin = target.PlayerPawn.Value.AbsOrigin;
					target.PlayerPawn.Value.Teleport(new(absOrigin!.X, absOrigin.Y, absOrigin.Z - 25.0f));
					ShowActivityToPlayers(player?.SteamID, "commands.bury.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName);
				}
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target>", "@zenith-commands/bury");

		_moduleServices?.RegisterModuleCommands(["unbury"], "Unburies a player", (player, info) =>
		{
			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					Vector? absOrigin = target.PlayerPawn.Value.AbsOrigin;
					target.PlayerPawn.Value.Teleport(new(absOrigin!.X, absOrigin.Y, absOrigin.Z + 30.0f));
					ShowActivityToPlayers(player?.SteamID, "commands.unbury.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName);
				}
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target>", "@zenith-commands/bury");
		_moduleServices?.RegisterModuleCommands(["slap"], "Slaps a player", (player, info) =>
		{
			int damage = 0;
			if (info.ArgCount >= 2 && !int.TryParse(info.GetArg(2), out damage))
			{
				_moduleServices?.PrintForPlayer(player, Localizer["commands.error.invalid_damage"]);
				return;
			}

			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					CCSPlayerPawn pawn = target.PlayerPawn.Value;

					if (pawn.Health - damage <= 0)
					{
						pawn.CommitSuicide(false, false);
						ShowActivityToPlayers(player?.SteamID, "commands.slay.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName);
						return;
					}

					pawn.Health -= damage;

					Vector vel = pawn.AbsVelocity;

					pawn.Teleport(pawn.AbsOrigin, pawn.AbsRotation, vel += new Vector(
						RandomVelocityComponent(),
						RandomVelocityComponent(),
						Random.Shared.Next(200) + 100
					));

					ShowActivityToPlayers(player?.SteamID, "commands.slap.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName, damage);
				}
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target> <damage>", "@zenith-commands/slap");

		_moduleServices?.RegisterModuleCommands(["blind"], "Blinds a player", (player, info) =>
		{
			float value = 0;
			if (info.ArgCount >= 2 && !float.TryParse(info.GetArg(2), out value))
			{
				_moduleServices?.PrintForPlayer(player, Localizer["commands.error.invalid_time"]);
				return;
			}

			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				CCSPlayerPawn? playerPawn = target.PlayerPawn.Value;
				if (playerPawn == null)
					return;

				playerPawn.FlashMaxAlpha = 255;
				playerPawn.FlashDuration = 999999;
				playerPawn.BlindStartTime = Server.CurrentTime;
				playerPawn.BlindUntilTime = 999999;

				Utilities.SetStateChanged(playerPawn, "CCSPlayerPawnBase", "m_flFlashMaxAlpha");
				Utilities.SetStateChanged(playerPawn, "CCSPlayerPawnBase", "m_flFlashDuration");

				if (value > 0.0)
				{
					AddTimer(value, () =>
					{
						if (target.IsValid && playerPawn.BlindUntilTime == 999999)
							playerPawn.BlindUntilTime = Server.CurrentTime - 1;
					});
				}

				ShowActivityToPlayers(player?.SteamID, "commands.blind.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName, value);
			});
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target> <time>", "@zenith-commands/blind");

		_moduleServices?.RegisterModuleCommands(["unblind"], "Unblinds a player", (player, info) =>
		{
			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					target.PlayerPawn.Value.BlindUntilTime = Server.CurrentTime - 1;
					ShowActivityToPlayers(player?.SteamID, "commands.unblind.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName);
				}
			});
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target>", "@zenith-commands/blind");

		_moduleServices?.RegisterModuleCommands(["god"], "Toggles god mode", (player, info) =>
		{
			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					bool godModeEnabled = !target.PlayerPawn.Value.TakesDamage;
					target.PlayerPawn.Value.TakesDamage = !godModeEnabled;
					ShowActivityToPlayers(player?.SteamID, godModeEnabled ? "commands.god.enable" : "commands.god.disable", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName);
				}
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target>", "@zenith-commands/god");

		_moduleServices?.RegisterModuleCommands(["team"], "Sets player team", (player, info) =>
		{
			CsTeam team = GetTeamFromName(info.GetArg(2));
			if (team == CsTeam.None)
			{
				_moduleServices?.PrintForPlayer(player, Localizer["commands.error.invalid_team"]);
				return;
			}

			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					target.ChangeTeam(team);
					ShowActivityToPlayers(player?.SteamID, "commands.team.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName, team);
				}
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 2, "<target> <team>", "@zenith-commands/team");

		_moduleServices?.RegisterModuleCommands(["swap"], "Swaps player team to the opposite", (player, info) =>
		{
			ProcessTargetAction(player, info.GetArgTargetResult(1), target =>
			{
				if (target.PlayerPawn.Value != null)
				{
					target.ChangeTeam(target.Team == CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist);
					ShowActivityToPlayers(player?.SteamID, "commands.swap.success", player?.PlayerName ?? Localizer["k4.general.console"], target.PlayerName);
				}
			}, true);
		}, CommandUsage.CLIENT_AND_SERVER, 1, "<target>", "@zenith-commands/swap");

		_moduleServices?.RegisterModuleCommands(["hide", "stealth"], "Hide yourself", (player, info) =>
		{
			if (player?.PlayerPawn.Value != null)
			{
				player.PlayerPawn.Value.CommitSuicide(true, false);

				Server.ExecuteCommand("sv_disable_teamselect_menu 1");

				player.ChangeTeam(CsTeam.None);
				AddTimer(0.2f, () => { Server.ExecuteCommand("sv_disable_teamselect_menu 0"); });

				_moduleServices.PrintForPlayer(player, Localizer["commands.hide.success"]);
			}
		}, CommandUsage.CLIENT_ONLY, permission: "@zenith-commands/hide");
	}
}