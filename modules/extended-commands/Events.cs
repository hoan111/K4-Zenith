using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace Zenith_ExtendedCommands
{
	public sealed partial class Plugin : BasePlugin
	{
		public Dictionary<CCSPlayerController, Vector> _deathLocations = [];

		private void Initialize_Events()
		{
			RegisterEventHandler((EventPlayerDeath @event, GameEventInfo info) =>
			{
				CCSPlayerController? player = @event.Userid;
				if (player == null || !player.IsValid || player.IsHLTV)
					return HookResult.Continue;

				_deathLocations[player] = player.AbsOrigin!;
				return HookResult.Continue;
			}, HookMode.Pre);
		}
	}
}