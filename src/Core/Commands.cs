namespace Zenith
{
	using CounterStrikeSharp.API.Core;
	using CounterStrikeSharp.API.Modules.Commands;

	public sealed partial class Plugin : BasePlugin
	{
		public void Initialize_Commands() // ? Decide whether or not its needed
		{
			RegisterZenithCommand("css_placeholderlist", "List all active placeholders in Zenith", (CCSPlayerController? player, CommandInfo command) =>
			{
				ListAllPlaceholders(player: player);
			}, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith/placeholders");

			RegisterZenithCommand("css_commandlist", "List all active commands in Zenith", (CCSPlayerController? player, CommandInfo command) =>
			{
				ListAllCommands(player: player);
			}, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith/commands");

			RegisterZenithCommand("css_zreload", "Reload Zenith configurations manually", (CCSPlayerController? player, CommandInfo command) =>
			{
				ConfigManager.ReloadAllConfigs();
			}, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith/reload");
		}
	}
}