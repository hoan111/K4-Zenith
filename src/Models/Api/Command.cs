using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using Zenith.Models;

namespace Zenith
{
	public sealed partial class Plugin : BasePlugin
	{
		private readonly Dictionary<string, Dictionary<string, CommandInfo.CommandCallback>> _pluginCommands = [];

		public void RegisterZenithCommand(string command, string description, CommandInfo.CommandCallback handler, CommandUsage usage = CommandUsage.CLIENT_AND_SERVER, int argCount = 0, string? helpText = null, string? permission = null)
		{
			if (!command.StartsWith("css_"))
				command = "css_" + command;

			string callingPlugin = CallerIdentifier.GetCallingPluginName();

			foreach (var pluginEntry in _pluginCommands)
			{
				if (pluginEntry.Value.TryGetValue(command, out CommandInfo.CommandCallback? existingHandler))
				{
					if (pluginEntry.Key != callingPlugin)
					{
						Logger.LogError($"Command '{command}' is already registered by plugin '{pluginEntry.Key}'. Registration by '{callingPlugin}' is not allowed.");
						return;
					}
					else
					{
						RemoveCommand(command, existingHandler);
						pluginEntry.Value.Remove(command);
						Logger.LogWarning($"Command '{command}' already exists for plugin '{callingPlugin}', overwriting.");
						break;
					}
				}
			}

			if (!_pluginCommands.TryGetValue(callingPlugin, out var pluginCommandDict))
			{
				pluginCommandDict = new Dictionary<string, CommandInfo.CommandCallback>();
				_pluginCommands[callingPlugin] = pluginCommandDict;
			}

			AddCommand(command, description, (controller, info) =>
			{
				if (!CommandHelper(controller, info, usage, argCount, helpText, permission))
					return;

				handler(controller, info);
			});

			pluginCommandDict[command] = handler;
		}

		public void RemoveModuleCommands(string callingPlugin)
		{
			if (_pluginCommands.TryGetValue(callingPlugin, out var pluginCommandDict))
			{
				foreach (var command in pluginCommandDict.Keys)
				{
					RemoveCommand(command, pluginCommandDict[command]);
				}
				_pluginCommands.Remove(callingPlugin);
			}
		}

		public void ListAllCommands(string? pluginName = null)
		{
			if (pluginName != null)
			{
				if (_pluginCommands.TryGetValue(pluginName, out var pluginCommandDict))
				{
					Logger.LogInformation($"Commands for plugin '{pluginName}':");
					foreach (var command in pluginCommandDict.Keys)
					{
						Logger.LogInformation($"  - {command}");
					}
				}
				else
				{
					Logger.LogWarning($"No commands found for plugin '{pluginName}'.");
				}
			}
			else
			{
				foreach (var pluginEntry in _pluginCommands)
				{
					Logger.LogInformation($"Commands for plugin '{pluginEntry.Key}':");
					foreach (var command in pluginEntry.Value.Keys)
					{
						Logger.LogInformation($"  - {command}");
					}
				}
			}
		}

		public void RegisterZenithCommand(List<string> commands, string description, CommandInfo.CommandCallback handler, CommandUsage usage = CommandUsage.CLIENT_AND_SERVER, int argCount = 0, string? helpText = null, string? permission = null)
		{
			foreach (string command in commands)
			{
				RegisterZenithCommand(command, description, handler, usage, argCount, helpText, permission);
			}
		}

		public bool CommandHelper(CCSPlayerController? controller, CommandInfo info, CommandUsage usage, int argCount = 0, string? helpText = null, string? permission = null)
		{
			Player? player = Player.Find(controller);

			switch (usage)
			{
				case CommandUsage.CLIENT_ONLY:
					if (player == null || !player.IsValid)
					{
						info.ReplyToCommand($" {Localizer["k4.general.prefix"]} {Localizer["k4.command.client-only"]}");
						return false;
					}
					break;
				case CommandUsage.SERVER_ONLY:
					if (player != null)
					{
						info.ReplyToCommand($" {Localizer["k4.general.prefix"]} {Localizer["k4.command.server-only"]}");
						return false;
					}
					break;
			}

			if (permission != null && permission.Length > 0)
			{
				if (player != null && !AdminManager.PlayerHasPermissions(controller, permission))
				{
					info.ReplyToCommand($" {Localizer["k4.general.prefix"]} {Localizer["k4.command.no-permission"]}");
					return false;
				}
			}

			if (argCount > 0 && helpText != null)
			{
				int checkArgCount = argCount + 1;
				if (info.ArgCount < checkArgCount)
				{
					info.ReplyToCommand($" {Localizer["k4.general.prefix"]} {Localizer["k4.command.help", info.ArgByIndex(0), helpText]}");
					return false;
				}
			}

			return true;
		}
	}
}


