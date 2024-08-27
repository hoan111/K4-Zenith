namespace Zenith
{
	using CounterStrikeSharp.API;
	using CounterStrikeSharp.API.Core;
	using CounterStrikeSharp.API.Modules.Commands;
	using CounterStrikeSharp.API.Modules.Utils;
	using Zenith.Models;
	using ZenithAPI;

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
		}
	}
}