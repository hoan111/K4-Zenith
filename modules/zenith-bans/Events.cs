using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using Microsoft.Extensions.Logging;
using ZenithAPI;

namespace Zenith_Bans
{
	public sealed partial class Plugin : BasePlugin
	{
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
					AddDisconnectedPlayer(new DisconnectedPlayer
					{
						SteamId = player.SteamID,
						PlayerName = player.PlayerName,
						DisconnectedAt = DateTime.UtcNow
					});
					_playerCache.Remove(player.SteamID);
				}
				return HookResult.Continue;
			});
		}
	}
}