namespace Zenith
{
	using System.Reflection;
	using CounterStrikeSharp.API;
	using CounterStrikeSharp.API.Core;
	using CounterStrikeSharp.API.Modules.Commands;
	using CounterStrikeSharp.API.Modules.Utils;
	using Zenith.Models;

	public sealed partial class Plugin : BasePlugin
	{
		public void Initialize_Commands() // ? Decide whether or not its needed
		{
			/*RegisterZenithCommand("css_zvar", "Change a Zenith variable", (CCSPlayerController? player, CommandInfo command) =>
			{
				if (command.ArgCount > 3)
				{
					// set
					// command.ArgByIndex(1) = setting-group
					// command.ArgByIndex(2) = setting-name
					// command.ArgByIndex(3) = value
				}
				else
				{
					// get
					// command.ArgByIndex(1) = setting-group
					// command.ArgByIndex(2) = setting-name
				}
			}, CommandUsage.CLIENT_AND_SERVER, 2, "<setting-group> <setting-name> <value?>", "@zenith/settings");*/

			RegisterZenithCommand("css_placeholderlist", "List all active placeholders in Zenith", (CCSPlayerController? player, CommandInfo command) =>
			{
				ListAllPlaceholders(player: player);
			}, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith/admin");

			RegisterZenithCommand("css_commandlist", "List all active commands in Zenith", (CCSPlayerController? player, CommandInfo command) =>
			{
				ListAllCommands(player: player);
			}, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith/admin");

			RegisterZenithCommand("css_zreload", "Reload Zenith configurations manually", (CCSPlayerController? player, CommandInfo command) =>
			{
				ConfigManager.ReloadAllConfigs();
			}, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith/admin");

			/*Vector vecEntity = new Vector(50, 50, 50);
			RegisterZenithCommand("css_testhud", "Reload Zenith configurations manually", (CCSPlayerController? player, CommandInfo command) =>
			{
				var entity = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
				if (entity == null || !entity.IsValid) return;

				QAngle vAngle = new QAngle(player.PlayerPawn.Value.AbsRotation.X, player.PlayerPawn.Value.AbsRotation.Y, player.PlayerPawn.Value.AbsRotation.Z);

				player.Pawn.Value.Teleport(player.PlayerPawn.Value.AbsOrigin, new QAngle(0, 0, 0), player.PlayerPawn.Value.AbsVelocity);

				entity.Teleport(new Vector(
				player.PlayerPawn.Value.AbsOrigin.X + vecEntity.X,
				player.PlayerPawn.Value.AbsOrigin.Y + vecEntity.Y,
				player.PlayerPawn.Value.AbsOrigin.Z + vecEntity.Z
				),
				new QAngle(0, 270, 75),
				player.PlayerPawn.Value.AbsVelocity);
				entity.FontSize = 48;
				entity.FontName = "Consolas";
				entity.Enabled = true;
				entity.Fullbright = true;
				entity.WorldUnitsPerPx = 0.1f;
				entity.Color = System.Drawing.Color.Red;
				entity.MessageText = "This is a test hud message";
				entity.MessageText += "\nThis is a new line";
				entity.DispatchSpawn();
				entity.AcceptInput("SetParent", player.PlayerPawn.Value, null, "!activator");

				player?.Pawn.Value?.Teleport(player.PlayerPawn.Value.AbsOrigin, vAngle, player.PlayerPawn.Value.AbsVelocity);
			}, CommandUsage.CLIENT_ONLY);*/
		}
	}
}