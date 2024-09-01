
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Utils;

namespace Zenith_ExtendedCommands;

public sealed partial class Plugin : BasePlugin
{
	private void ProcessTargetAction(CCSPlayerController? caller, CCSPlayerController target, Action<CCSPlayerController> action, bool aliveOnly = false)
	{
		if (!AdminManager.CanPlayerTarget(caller, target))
		{
			_moduleServices?.PrintForPlayer(caller, Localizer["commands.error.invalid_immunity", target.PlayerName]);
			return;
		}

		if (aliveOnly && target.PlayerPawn.Value != null && target.PlayerPawn.Value.Health <= 0 && target.PlayerPawn.Value.LifeState != (byte)LifeState_t.LIFE_ALIVE)
		{
			_moduleServices?.PrintForPlayer(caller, Localizer["commands.error.invalid_dead", target.PlayerName]);
			return;
		}

		action.Invoke(target);
	}

	private void ProcessTargetAction(CCSPlayerController? caller, TargetResult targetResult, Action<CCSPlayerController> action, bool aliveOnly = false)
	{
		if (!targetResult.Any())
		{
			_moduleServices?.PrintForPlayer(caller, Localizer["commands.error.invalid_target"]);
			return;
		}

		foreach (CCSPlayerController target in targetResult.Players)
		{
			ProcessTargetAction(caller, target, action, aliveOnly);
		}
	}

	private void RemoveWeaponInSlot(CCSPlayerController player, gear_slot_t slot)
	{
		if (player.PlayerPawn.Value?.WeaponServices is null)
			return;

		List<CHandle<CBasePlayerWeapon>> weaponList = player.PlayerPawn.Value.WeaponServices.MyWeapons.ToList();
		foreach (CHandle<CBasePlayerWeapon> weapon in weaponList)
		{
			if (weapon.IsValid && weapon.Value != null)
			{
				CCSWeaponBase ccsWeaponBase = weapon.Value.As<CCSWeaponBase>();
				if (ccsWeaponBase?.IsValid == true)
				{
					CCSWeaponBaseVData? weaponData = ccsWeaponBase.VData;

					if (weaponData == null || weaponData.GearSlot != slot)
						continue;

					player.PlayerPawn.Value.WeaponServices.ActiveWeapon.Raw = weapon.Raw;
					player.DropActiveWeapon();

					Server.NextFrame(() =>
					{
						if (ccsWeaponBase != null && ccsWeaponBase.IsValid)
						{
							ccsWeaponBase.AcceptInput("Kill");
						}
					});
				}
			}
		}
	}

	static float RandomVelocityComponent()
	{
		return (Random.Shared.Next(180) + 50) * (Random.Shared.Next(2) == 1 ? -1 : 1);
	}

	private void ShowActivityToPlayers(ulong? callerSteamId, string localizerKey, params object[] args)
	{
		foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
		{
			if (ShouldShowActivity(callerSteamId, player, true))
			{
				_moduleServices?.PrintForPlayer(player, Localizer[localizerKey, args]);
			}
			else if (ShouldShowActivity(callerSteamId, player, false))
			{
				var anonymousArgs = args.ToList();
				anonymousArgs[0] = Localizer["k4.general.admin"];
				_moduleServices?.PrintForPlayer(player, Localizer[localizerKey, anonymousArgs.ToArray()]);
			}
		}
	}

	private bool ShouldShowActivity(ulong? adminSteamId, CCSPlayerController player, bool showName)
	{
		if (!adminSteamId.HasValue) return true; // Always show console activity
		if (!_coreAccessor.HasValue("Config", "ShowActivity")) return true; // Show activity if no ZenithBans installed

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

	private static CsTeam GetTeamFromName(string teamName)
	{
		return teamName.ToLower() switch
		{
			"t" => CsTeam.Terrorist,
			"terrorist" => CsTeam.Terrorist,
			"tt" => CsTeam.Terrorist,
			"ct" => CsTeam.CounterTerrorist,
			"counterterrorist" => CsTeam.CounterTerrorist,
			"spec" => CsTeam.Spectator,
			"spectator" => CsTeam.Spectator,
			_ => CsTeam.None
		};
	}
}