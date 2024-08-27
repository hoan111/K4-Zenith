using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using MaxMind.GeoIP2;
using Zenith.Models;
using System.Reflection;

namespace Zenith
{
	public sealed partial class Plugin : BasePlugin
	{
		private readonly Dictionary<string, Dictionary<string, Func<CCSPlayerController, string>>> _pluginPlayerPlaceholders = new();
		private readonly Dictionary<string, Dictionary<string, Func<string>>> _pluginServerPlaceholders = new();

		private void Initialize_Placeholders()
		{
			RegisterZenithPlayerPlaceholder("userid", p => p.UserId?.ToString() ?? "Unknown");
			RegisterZenithPlayerPlaceholder("name", p => p.PlayerName);
			RegisterZenithPlayerPlaceholder("steamid", p => p.SteamID.ToString());
			RegisterZenithPlayerPlaceholder("ip", p => p.IpAddress ?? "Unknown");
			RegisterZenithPlayerPlaceholder("country_short", p => GetCountryFromIP(p).ShortName);
			RegisterZenithPlayerPlaceholder("country_long", p => GetCountryFromIP(p).LongName);

			RegisterZenithServerPlaceholder("server_name", () => ConVar.Find("hostname")?.StringValue ?? "Unknown");
			RegisterZenithServerPlaceholder("map_name", () => Server.MapName);
			RegisterZenithServerPlaceholder("max_players", () => Server.MaxPlayers.ToString());
		}

		public string ReplacePlaceholders(CCSPlayerController? player, string text)
		{
			text = ReplaceServerPlaceholders(text);
			text = ReplacePlayerPlaceholders(player, text);
			return text;
		}

		public string ReplaceServerPlaceholders(string text)
		{
			foreach (var pluginPlaceholders in _pluginServerPlaceholders.Values)
			{
				foreach (var placeholder in pluginPlaceholders)
				{
					text = text.Replace($"{{{placeholder.Key}}}", placeholder.Value());
				}
			}
			return text;
		}

		public string ReplacePlayerPlaceholders(CCSPlayerController? player, string text)
		{
			if (player != null)
			{
				foreach (var pluginPlaceholders in _pluginPlayerPlaceholders.Values)
				{
					foreach (var placeholder in pluginPlaceholders)
					{
						text = text.Replace($"{{{placeholder.Key}}}", placeholder.Value(player));
					}
				}
			}
			return text;
		}

		public void RegisterZenithPlayerPlaceholder(string key, Func<CCSPlayerController, string> valueFunc)
		{
			string callingPlugin = CallerIdentifier.GetCallingPluginName();

			if (!_pluginPlayerPlaceholders.TryGetValue(callingPlugin, out var placeholders))
			{
				placeholders = new Dictionary<string, Func<CCSPlayerController, string>>();
				_pluginPlayerPlaceholders[callingPlugin] = placeholders;
			}

			if (placeholders.ContainsKey(key))
			{
				Logger.LogWarning($"Player placeholder '{key}' already exists for plugin '{callingPlugin}', overwriting.");
			}

			placeholders[key] = valueFunc;
		}

		public void RegisterZenithServerPlaceholder(string key, Func<string> valueFunc)
		{
			string callingPlugin = CallerIdentifier.GetCallingPluginName();

			if (!_pluginServerPlaceholders.TryGetValue(callingPlugin, out var placeholders))
			{
				placeholders = new Dictionary<string, Func<string>>();
				_pluginServerPlaceholders[callingPlugin] = placeholders;
			}

			if (placeholders.ContainsKey(key))
			{
				Logger.LogWarning($"Server placeholder '{key}' already exists for plugin '{callingPlugin}', overwriting.");
			}

			placeholders[key] = valueFunc;
		}

		public void RemoveModulePlaceholders(string callingPlugin)
		{
			_pluginPlayerPlaceholders.Remove(callingPlugin);
			_pluginServerPlaceholders.Remove(callingPlugin);
		}

		public void DisposeModule()
		{
			string callingPlugin = CallerIdentifier.GetCallingPluginName();

			Logger.LogInformation($"Disposing module '{callingPlugin}' and freeing resources.");

			RemoveModuleCommands(callingPlugin);
			RemoveModulePlaceholders(callingPlugin);
			Player.DisposeModuleData(this, callingPlugin);
		}

		public void DisposeModule(Assembly assembly)
		{
			string callingPlugin = assembly.GetName().Name!;

			Logger.LogInformation($"Disposing module '{callingPlugin}' and freeing resources.");

			RemoveModuleCommands(callingPlugin);
			RemoveModulePlaceholders(callingPlugin);
			Player.DisposeModuleData(this, callingPlugin);
		}

		public void ListAllPlaceholders(string? pluginName = null, CCSPlayerController? player = null)
		{
			if (pluginName != null)
			{
				ListPlaceholdersForPlugin(pluginName, player);
			}
			else
			{
				foreach (var plugin in _pluginPlayerPlaceholders.Keys.Union(_pluginServerPlaceholders.Keys).Distinct())
				{
					ListPlaceholdersForPlugin(plugin, player);
				}
			}
		}

		private void ListPlaceholdersForPlugin(string pluginName, CCSPlayerController? player = null)
		{
			PrintToConsole($"Placeholders for plugin '{pluginName}':", player);

			if (_pluginPlayerPlaceholders.TryGetValue(pluginName, out var playerPlaceholders))
			{
				PrintToConsole("  Player placeholders:", player);
				foreach (var placeholder in playerPlaceholders.Keys)
				{
					PrintToConsole($"    - {placeholder}", player);
				}
			}

			if (_pluginServerPlaceholders.TryGetValue(pluginName, out var serverPlaceholders))
			{
				PrintToConsole("  Server placeholders:", player);
				foreach (var placeholder in serverPlaceholders.Keys)
				{
					PrintToConsole($"    - {placeholder}", player);
				}
			}
		}

		public static void PrintToConsole(string text, CCSPlayerController? player)
		{
			if (player == null)
			{
				Server.PrintToConsole(text);
			}
			else
			{
				player.PrintToConsole(text);
			}
		}

		private (string ShortName, string LongName) GetCountryFromIP(CCSPlayerController? player)
		{
			if (player == null)
				return ("??", "Unknown");

			return GetCountryFromIP(player.IpAddress?.Split(':')[0]);
		}

		private (string ShortName, string LongName) GetCountryFromIP(string? ipAddress)
		{
			if (string.IsNullOrEmpty(ipAddress))
				return ("??", "Unknown");

			string databasePath = Path.Combine(ModuleDirectory, "GeoLite2-Country.mmdb");
			if (!File.Exists(databasePath))
				return ("??", "Unknown");

			try
			{
				using var reader = new DatabaseReader(databasePath);
				var response = reader.Country(ipAddress);

				string shortName = response.Country.IsoCode ?? "??";
				string longName = response.Country.Name ?? "Unknown";

				return (shortName, longName);
			}
			catch (Exception ex)
			{
				Logger.LogInformation($"Error getting country information: {ex.Message}");
				return ("??", "Unknown");
			}
		}
	}
}
