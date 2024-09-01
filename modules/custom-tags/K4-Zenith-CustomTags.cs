using System.Text.Json;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using ZenithAPI;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;

namespace Zenith_CustomTags;

[MinimumApiVersion(260)]
public class Plugin : BasePlugin
{
	private const string MODULE_ID = "CustomTags";

	private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.0";

	private PlayerCapability<IPlayerServices>? _playerServicesCapability;
	private PluginCapability<IModuleServices>? _moduleServicesCapability;

	private IZenithEvents? _zenithEvents;
	private IModuleServices? _moduleServices;

	public override void OnAllPluginsLoaded(bool hotReload)
	{
		try
		{
			_playerServicesCapability = new("zenith:player-services");
			_moduleServicesCapability = new("zenith:module-services");
		}
		catch (Exception ex)
		{
			Logger.LogError($"Failed to initialize Zenith API: {ex.Message}");
			Logger.LogInformation("Please check if Zenith is installed, configured and loaded correctly.");

			Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
			return;
		}

		_moduleServices = _moduleServicesCapability.Get();
		if (_moduleServices == null)
		{
			Logger.LogError("Failed to get Module-Services API for Zenith.");
			Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
			return;
		}

		_zenithEvents = _moduleServices.GetEventHandler();
		if (_zenithEvents != null)
		{
			_zenithEvents.OnZenithPlayerLoaded += OnZenithPlayerLoaded;
			_zenithEvents.OnZenithCoreUnload += OnZenithCoreUnload;
		}
		else
		{
			Logger.LogError("Failed to get Zenith event handler.");
		}

		EnsureConfigFileExists();

		AddTimer(5.0f, () =>
		{
			_moduleServices.LoadAllOnlinePlayerData();
			Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).ToList().ForEach(player =>
			{
				ApplyTagConfig(player);
			});
		});

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	private void EnsureConfigFileExists()
	{
		string configPath = Path.Combine(ModuleDirectory, "tags.json");
		if (!File.Exists(configPath))
		{
			var defaultConfig = new Dictionary<string, TagConfig>
			{
				{ "@zenith/root", new TagConfig { ChatColor = "lightred", ClanTag = "OWNER | ", NameColor = "lightred", NameTag = "{lightred}[OWNER] " } },
				{ "76561198345583467", new TagConfig { ChatColor = null, ClanTag = "Zenith | ", NameColor = "gold", NameTag = "{gold}[Zenith] " } }
			};
			File.WriteAllText(configPath, JsonSerializer.Serialize(defaultConfig, _jsonOptions));
		}
	}

	private Dictionary<string, TagConfig> GetTagConfigs()
	{
		try
		{
			string configPath = Path.Combine(ModuleDirectory, "tags.json");
			string json = File.ReadAllText(configPath);
			return JsonSerializer.Deserialize<Dictionary<string, TagConfig>>(json, _jsonOptions) ?? [];
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error reading tag configs: {ex.Message}");
			return [];
		}
	}

	private void ApplyTagConfig(CCSPlayerController player)
	{
		try
		{
			var zenithPlayer = GetZenithPlayer(player);
			if (zenithPlayer is null) return;

			var tagConfigs = GetTagConfigs();

			foreach (var (key, config) in tagConfigs)
			{
				SteamID steamID = new SteamID(player.SteamID);
				bool isMatch = steamID.SteamId64.ToString() == key
					|| steamID.SteamId32.ToString() == key
					|| steamID.SteamId2.ToString() == key
					|| steamID.SteamId3.ToString() == key
					|| AdminManager.PlayerHasPermissions(player, key)
					|| AdminManager.PlayerInGroup(player, key);

				if (isMatch)
				{
					if (!string.IsNullOrEmpty(config.ChatColor))
						zenithPlayer.SetChatColor(GetChatColorValue(config.ChatColor));

					if (!string.IsNullOrEmpty(config.ClanTag))
						zenithPlayer.SetClanTag(config.ClanTag);

					if (!string.IsNullOrEmpty(config.NameColor))
						zenithPlayer.SetNameColor(GetChatColorValue(config.NameColor));

					if (!string.IsNullOrEmpty(config.NameTag))
						zenithPlayer.SetNameTag(ApplyPrefixColors(config.NameTag));

					break;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error applying tag config for player {player.PlayerName}: {ex.Message}");
		}
	}

	private static char GetChatColorValue(string colorName)
	{
		var chatColors = typeof(ChatColors).GetFields()
			.Where(f => f.FieldType == typeof(char))
			.GroupBy(f => f.Name.ToLower())
			.ToDictionary(g => g.Key, g => (char)g.First().GetValue(null)!);

		if (chatColors.TryGetValue(colorName.ToLower(), out char colorValue))
		{
			return colorValue;
		}

		return chatColors["default"];
	}

	public static string ApplyPrefixColors(string msg)
	{
		var chatColors = typeof(ChatColors).GetFields()
			.Select(f => new { f.Name, Value = f.GetValue(null)?.ToString() })
			.OrderByDescending(c => c.Name.Length);

		foreach (var color in chatColors)
		{
			if (color.Value != null)
			{
				msg = Regex.Replace(msg, $@"(\{{)?{color.Name}(\}})?", color.Value, RegexOptions.IgnoreCase);
			}
		}

		return msg;
	}

	private void OnZenithPlayerLoaded(CCSPlayerController player)
	{
		AddTimer(3.0f, () =>
		{
			ApplyTagConfig(player);
		}, TimerFlags.STOP_ON_MAPCHANGE);
	}

	public override void Unload(bool hotReload)
	{
		IModuleServices? moduleServices = _moduleServicesCapability?.Get();
		if (moduleServices == null)
			return;

		moduleServices.DisposeModule(this.GetType().Assembly);
	}

	private void OnZenithCoreUnload(bool hotReload)
	{
		if (hotReload)
		{
			AddTimer(3.0f, () =>
			{
				try { File.SetLastWriteTime(Path.Combine(ModulePath), DateTime.Now); }
				catch (Exception ex) { Logger.LogError($"Failed to update file: {ex.Message}"); }
			});
		}
	}

	public IPlayerServices? GetZenithPlayer(CCSPlayerController? player)
	{
		if (player == null) return null;
		try { return _playerServicesCapability?.Get(player); }
		catch { return null; }
	}
}

public class TagConfig
{
	public string? ChatColor { get; set; }
	public string? ClanTag { get; set; }
	public string? NameColor { get; set; }
	public string? NameTag { get; set; }
}
