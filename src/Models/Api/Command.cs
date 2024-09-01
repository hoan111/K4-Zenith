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
		private readonly ConcurrentDictionary<string, List<CommandDefinition>> _pluginCommands = [];
		private readonly ConcurrentDictionary<string, string> _commandPermissions = [];

		public void RegisterZenithCommand(string command, string description, CommandInfo.CommandCallback handler, CommandUsage usage = CommandUsage.CLIENT_AND_SERVER, int argCount = 0, string? helpText = null, string? permission = null)
		{
			if (!command.StartsWith("css_"))
				command = "css_" + command;

			string callingPlugin = CallerIdentifier.GetCallingPluginName();

			var existingCommand = _pluginCommands
				.SelectMany(kvp => kvp.Value.Select(cmd => new { Plugin = kvp.Key, Command = cmd }))
				.FirstOrDefault(x => x.Command.Name == command);

			if (existingCommand != null)
			{
				if (existingCommand.Plugin != callingPlugin)
				{
					Logger.LogError($"Command '{command}' is already registered by plugin '{existingCommand.Plugin}'. Registration by '{callingPlugin}' is not allowed.");
					return;
				}
				else
				{
					CommandManager.RemoveCommand(existingCommand.Command);
					_pluginCommands[callingPlugin].Remove(existingCommand.Command);
					Logger.LogWarning($"Command '{command}' already exists for plugin '{callingPlugin}', overwriting.");
				}
			}

			if (!_pluginCommands.ContainsKey(callingPlugin))
				_pluginCommands[callingPlugin] = [];

			var newCommand = new CommandDefinition(command, description, (controller, info) =>
			{
				if (!CommandHelper(controller, info, usage, argCount, helpText, permission))
					return;

				handler(controller, info);
			});

			// ? Using CommandManager due to AddCommand cannot unregister modular commands
			CommandManager.RegisterCommand(newCommand);
			_pluginCommands[callingPlugin].Add(newCommand);
			_commandPermissions[command] = permission ?? string.Empty;
		}

		public void RemoveModuleCommands(string callingPlugin)
		{
			if (_pluginCommands.TryGetValue(callingPlugin, out var pluginCommands))
			{
				foreach (var command in pluginCommands.ToList())
				{
					CommandManager.RemoveCommand(command);
				}
				_pluginCommands.TryRemove(callingPlugin, out _);
				_commandPermissions.TryRemove(callingPlugin, out _);
			}
		}

		public void ListAllCommands(string? pluginName = null, CCSPlayerController? player = null)
		{
			if (pluginName != null)
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
			else
			{
				foreach (var pluginEntry in _pluginCommands)
				{
					PrintToConsole($"Commands for plugin '{pluginEntry.Key}':", player);
					foreach (var command in pluginEntry.Value)
					{
						string permission = _commandPermissions[command.Name];
						PrintToConsole($"  - {command.Name}: {command.Description} {(string.IsNullOrEmpty(permission) ? "" : $"({permission})")}", player);
					}
				}
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
				if (player != null && !AdminManager.PlayerHasPermissions(controller, permission) && !AdminManager.PlayerHasPermissions(controller, "@zenith/root"))
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


