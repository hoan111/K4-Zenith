using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace Zenith_Bans
{
	public sealed partial class Plugin : BasePlugin
	{
		public Dictionary<CCSPlayerController, CounterStrikeSharp.API.Modules.Timers.Timer> _disconnectTImers = [];

		private void Initialize_Events()
		{
			RegisterEventHandler((EventPlayerConnectFull @event, GameEventInfo info) =>
			{
				CCSPlayerController? player = @event.Userid;
				if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
					return HookResult.Continue;

				ProcessPlayerData(player, true);
				return HookResult.Continue;
			});

			RegisterEventHandler((EventPlayerDisconnect @event, GameEventInfo info) =>
			{
				CCSPlayerController? player = @event.Userid;
				if (player?.IsValid == true && !player.IsBot && !player.IsHLTV)
				{
					ulong steamID = player.SteamID;
					_ = Task.Run(async () =>
					{
						await HandlePlayerDisconnectAsync(steamID);
					});

					AddDisconnectedPlayer(new DisconnectedPlayer
					{
						SteamId = steamID,
						PlayerName = player.PlayerName,
						DisconnectedAt = DateTime.UtcNow
					});
					_playerCache.Remove(steamID);
				}
				return HookResult.Continue;
			});

			RegisterEventHandler((EventPlayerHurt @event, GameEventInfo info) =>
			{
				CCSPlayerController? attacker = @event.Attacker;
				if (attacker == null || !attacker.IsValid || attacker.IsBot || attacker.IsHLTV || !_disconnectTImers.ContainsKey(attacker))
					return HookResult.Continue;

				CCSPlayerController? victim = @event.Userid;
				if (victim == null || !victim.IsValid || victim.PlayerPawn.Value == null)
					return HookResult.Continue;

				CCSPlayerPawn playerPawn = victim.PlayerPawn.Value;

				victim.Health += @event.DmgHealth;
				Utilities.SetStateChanged(victim, "CBaseEntity", "m_iHealth");

				playerPawn.ArmorValue += @event.DmgArmor;
				Utilities.SetStateChanged(playerPawn, "CCSPlayerPawn", "m_ArmorValue");
				return HookResult.Continue;
			}, HookMode.Pre);
		}
	}
}