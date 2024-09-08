using System.Collections.Concurrent;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using Zenith.Models;

namespace Zenith
{
	public sealed partial class Plugin : BasePlugin
	{
		private readonly ConcurrentDictionary<string, List<CommandDefinition>> _pluginCommands = new();
		private readonly ConcurrentDictionary<string, string> _commandPermissions = new();

		public void RegisterZenithCommand(string command, string description, CommandInfo.CommandCallback handler, CommandUsage usage = CommandUsage.CLIENT_AND_SERVER, int argCount = 0, string? helpText = null, string? permission = null)
		{
			command = EnsureCommandPrefix(command);
			string callingPlugin = CallerIdentifier.GetCallingPluginName();

			var existingCommand = FindExistingCommand(command);

			if (existingCommand.HasValue)
			{
				if (existingCommand.Value.Plugin != callingPlugin)
				{
					Logger.LogError($"Command '{command}' is already registered by plugin '{existingCommand.Value.Plugin}'. Registration by '{callingPlugin}' is not allowed.");
					return;
				}

				RemoveExistingCommand(existingCommand);
				Logger.LogWarning($"Command '{command}' already exists for plugin '{callingPlugin}', overwriting.");
			}

			RegisterNewCommand(command, description, handler, usage, argCount, helpText, permission, callingPlugin);
		}

		private static string EnsureCommandPrefix(string command) => command.StartsWith("css_") ? command : "css_" + command;

		private (string Plugin, CommandDefinition Command)? FindExistingCommand(string command)
		{
			var existingCommand = _pluginCommands
				.SelectMany(kvp => kvp.Value.Select(cmd => new { Plugin = kvp.Key, Command = cmd }))
				.FirstOrDefault(x => x.Command.Name == command);

			if (existingCommand != null)
			{
				return (existingCommand.Plugin, existingCommand.Command);
			}

			return null;
		}

		private void RemoveExistingCommand((string Plugin, CommandDefinition Command)? existingCommand)
		{
			if (existingCommand.HasValue)
			{
				CommandManager.RemoveCommand(existingCommand.Value.Command);
				_pluginCommands[existingCommand.Value.Plugin].Remove(existingCommand.Value.Command);
			}
		}

		private void RegisterNewCommand(string command, string description, CommandInfo.CommandCallback handler, CommandUsage usage, int argCount, string? helpText, string? permission, string callingPlugin)
		{
			var newCommand = new CommandDefinition(command, description, (controller, info) =>
			{
				if (!CommandHelper(controller, info, usage, argCount, helpText, permission))
					return;

				handler(controller, info);
			});

			CommandManager.RegisterCommand(newCommand);
			_pluginCommands.GetOrAdd(callingPlugin, _ => []).Add(newCommand);
			_commandPermissions[command] = permission ?? string.Empty;
		}

		public void RemoveModuleCommands(string callingPlugin)
		{
			if (_pluginCommands.TryGetValue(callingPlugin, out var pluginCommands))
			{
				foreach (var command in pluginCommands)
				{
					CommandManager.RemoveCommand(command);
				}
				_pluginCommands.TryRemove(callingPlugin, out _);

				var keysToRemove = new List<string>();
				foreach (var cmd in _commandPermissions.Keys)
				{
					if (cmd.StartsWith(callingPlugin + "_"))
					{
						keysToRemove.Add(cmd);
					}
				}

				foreach (var key in keysToRemove)
				{
					_commandPermissions.TryRemove(key, out _);
				}
			}
		}

		public void ListAllCommands(string? pluginName = null, CCSPlayerController? player = null)
		{
			if (pluginName != null)
			{
				PrintPluginCommands(pluginName, player);
			}
			else
			{
				foreach (var pluginEntry in _pluginCommands)
				{
					PrintPluginCommands(pluginEntry.Key, player);
				}
			}
		}

		private void PrintPluginCommands(string pluginName, CCSPlayerController? player)
		{
			if (_pluginCommands.TryGetValue(pluginName, out var pluginCommands))
			{
				PrintToConsole($"Commands for plugin '{pluginName}':", player);
				foreach (var command in pluginCommands)
				{
					string permission = _commandPermissions[command.Name];
					PrintToConsole($"  - {command.Name}: {command.Description} {(string.IsNullOrEmpty(permission) ? "" : $"({permission})")}", player);
				}
			}
			else
			{
				PrintToConsole($"No commands found for plugin '{pluginName}'.", player);
			}
		}

		public void RemoveAllCommands()
		{
			foreach (var pluginEntry in _pluginCommands)
			{
				foreach (var command in pluginEntry.Value)
				{
					CommandManager.RemoveCommand(command);
				}
			}

			_pluginCommands.Clear();
			_commandPermissions.Clear();
		}

		public void RegisterZenithCommand(List<string> commands, string description, CommandInfo.CommandCallback handler, CommandUsage usage = CommandUsage.CLIENT_AND_SERVER, int argCount = 0, string? helpText = null, string? permission = null)
		{
			foreach (var command in commands)
			{
				RegisterZenithCommand(command, description, handler, usage, argCount, helpText, permission);
			}
		}

		public bool CommandHelper(CCSPlayerController? controller, CommandInfo info, CommandUsage usage, int argCount = 0, string? helpText = null, string? permission = null)
		{
			Player? player = Player.Find(controller);

			if (!IsCommandUsageValid(player, info, usage))
				return false;

			if (!HasPermission(player, controller, info, permission))
				return false;

			if (IsArgumentCountInvalid(info, argCount, helpText))
				return false;

			return true;
		}

		private bool IsCommandUsageValid(Player? player, CommandInfo info, CommandUsage usage)
		{
			switch (usage)
			{
				case CommandUsage.CLIENT_ONLY when player == null || !player.IsValid:
					info.ReplyToCommand($" {Localizer["k4.general.prefix"]} {Localizer["k4.command.client-only"]}");
					return false;
				case CommandUsage.SERVER_ONLY when player != null:
					info.ReplyToCommand($" {Localizer["k4.general.prefix"]} {Localizer["k4.command.server-only"]}");
					return false;
				default:
					return true;
			}
		}

		private bool HasPermission(Player? player, CCSPlayerController? controller, CommandInfo info, string? permission)
		{
			if (string.IsNullOrEmpty(permission))
				return true;

			if (player != null && !AdminManager.PlayerHasPermissions(controller, permission) &&
				!AdminManager.PlayerHasPermissions(controller, "@zenith/root") &&
				!AdminManager.PlayerHasPermissions(controller, "@css/root") &&
				!AdminManager.PlayerHasCommandOverride(controller, info.GetArg(0)))
			{
				info.ReplyToCommand($" {Localizer["k4.general.prefix"]} {Localizer["k4.command.no-permission"]}");
				return false;
			}

			return true;
		}

		private bool IsArgumentCountInvalid(CommandInfo info, int argCount, string? helpText)
		{
			if (argCount > 0 && info.ArgCount < argCount + 1 && helpText != null)
			{
				info.ReplyToCommand($" {Localizer["k4.general.prefix"]} {Localizer["k4.command.help", info.ArgByIndex(0), helpText]}");
				return true;
			}
			return false;
		}
	}
}
