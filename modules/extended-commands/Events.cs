using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace Zenith_ExtendedCommands
{
	public sealed partial class Plugin : BasePlugin
	{
		private readonly Dictionary<CCSPlayerController, Vector> _deathLocations = [];

		private void Initialize_Events()
		{
			RegisterEventHandler((EventPlayerDeath @event, GameEventInfo info) =>
			{
				CCSPlayerController? player = @event.Userid;
				if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
					return HookResult.Continue;

				var location = player.PlayerPawn.Value?.AbsOrigin;
				if (location == null)
					return HookResult.Continue;

				_deathLocations[player] = new Vector(location.X, location.Y, location.Z);
				return HookResult.Continue;
			}, HookMode.Pre);
		}
	}
}