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
using CounterStrikeSharp.API.Modules.Commands;
using Menu;
using Menu.Enums;

namespace Zenith_CustomTags;

[MinimumApiVersion(260)]
public class Plugin : BasePlugin
{
	private const string MODULE_ID = "CustomTags";

	private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
	private readonly Dictionary<ulong, string> _playerSelectedConfigs = new();

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.3";

	private PlayerCapability<IPlayerServices>? _playerServicesCapability;
	private PluginCapability<IModuleServices>? _moduleServicesCapability;

	private IZenithEvents? _zenithEvents;
	private IModuleServices? _moduleServices;

	public KitsuneMenu Menu { get; private set; } = null!;
	public IModuleConfigAccessor _coreAccessor = null!;

	private Dictionary<string, TagConfig>? _tagConfigs;
	private Dictionary<string, PredefinedTagConfig>? _predefinedConfigs;

	private static readonly Dictionary<string, char> _chatColors = typeof(ChatColors).GetFields()
		.Where(f => f.FieldType == typeof(char))
		.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
		.Select(g => g.First()) // This will take the first occurrence if there are duplicates (DarkRed and Darkred)
		.ToDictionary(f => f.Name, f => (char)f.GetValue(null)!, StringComparer.OrdinalIgnoreCase);

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

		Menu = new KitsuneMenu(this);
		_coreAccessor = _moduleServices.GetModuleConfigAccessor();

		_moduleServices!.RegisterModuleStorage(new Dictionary<string, object?>
		{
			{ "ChoosenTag", "Default" },
		});

		EnsureConfigFileExists();
		EnsurePredefinedConfigFileExists();

		_moduleServices?.RegisterModuleCommands(["tags", "tag"], "Change player tag configuration", (player, info) =>
		{
			if (player == null) return;
			var zenithPlayer = GetZenithPlayer(player);
			if (zenithPlayer == null) return;
			ShowTagSelectionMenu(player);
		}, CommandUsage.CLIENT_ONLY);

		AddTimer(5.0f, () =>
		{
			_moduleServices?.LoadAllOnlinePlayerData();

			var players = Utilities.GetPlayers();
			for (int i = 0; i < players.Count; i++)
			{
				var player = players[i];
				if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
				{
					ApplyTagConfig(player);
				}
			}
		});

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	private void ShowTagSelectionMenu(CCSPlayerController player)
	{
		if (Menu == null)
		{
			Logger.LogError("Menu object is null. Cannot show tag selection menu.");
			return;
		}

		_tagConfigs ??= GetTagConfigs();
		_predefinedConfigs ??= GetPredefinedTagConfigs();

		List<MenuItem> items = [];
		List<string> configKeys = [];
		HashSet<string> availableConfigs = [];

		if (_tagConfigs.TryGetValue("all", out var allConfig) && allConfig.AvailableConfigs != null)
		{
			availableConfigs.UnionWith(allConfig.AvailableConfigs);
		}

		bool hasCustomConfig = false;
		if (_tagConfigs.TryGetValue(player.SteamID.ToString(), out var playerConfig))
		{
			hasCustomConfig = playerConfig.ChatColor != null || playerConfig.ClanTag != null ||
							  playerConfig.NameColor != null || playerConfig.NameTag != null;

			if (playerConfig.AvailableConfigs != null)
			{
				availableConfigs.UnionWith(playerConfig.AvailableConfigs);
			}
		}

		items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(Localizer["customtags.menu.none"])]));
		configKeys.Add("none");

		if (hasCustomConfig)
		{
			items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(Localizer["customtags.menu.default"])]));
			configKeys.Add("default");
		}

		foreach (var configName in availableConfigs)
		{
			if (_predefinedConfigs.TryGetValue(configName, out var config))
			{
				items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(config.Name)]));
				configKeys.Add(configName);
			}
		}

		if (items.Count == 1)
			items.Clear();

		string currentConfig = _playerSelectedConfigs.TryGetValue(player.SteamID, out var selectedConfig) ? selectedConfig : "Default";

		try
		{
			Menu.ShowScrollableMenu(player, Localizer["customtags.menu.title"], items, (buttons, menu, selected) =>
			{
				if (selected == null) return;

				if (menu.Option >= 0 && menu.Option < configKeys.Count)
				{
					string selectedConfigKey = configKeys[menu.Option];

					if (buttons == MenuButtons.Select)
					{
						if (selectedConfigKey == "default")
						{
							_playerSelectedConfigs.Remove(player.SteamID);
							ApplyTagConfig(player);
							_moduleServices?.PrintForPlayer(player, Localizer["customtags.applied.default"]);
						}
						else if (selectedConfigKey == "none")
						{
							_playerSelectedConfigs[player.SteamID] = "none";
							ApplyNullConfig(player);
							_moduleServices?.PrintForPlayer(player, Localizer["customtags.applied.none"]);
						}
						else if (_predefinedConfigs.TryGetValue(selectedConfigKey, out var selectedPredefinedConfig))
						{
							_playerSelectedConfigs[player.SteamID] = selectedConfigKey;
							ApplyPredefinedConfig(player, selectedPredefinedConfig);
							_moduleServices?.PrintForPlayer(player, Localizer["customtags.applied.config", selectedPredefinedConfig.Name]);
						}
						else
						{
							_moduleServices?.PrintForPlayer(player, $"Invalid tag configuration: {selectedConfigKey}");
						}
					}
				}
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu"), 5, disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error showing tag selection menu: {ex.Message}");
		}
	}

	private void EnsureConfigFileExists()
	{
		string configPath = Path.Combine(ModuleDirectory, "tags.json");
		if (!File.Exists(configPath))
		{
			var defaultConfig = new Dictionary<string, TagConfig>
			{
				["all"] = new TagConfig
				{
					ClanTag = "Player | ",
					NameColor = "white",
					NameTag = "{white}[Player] ",
					AvailableConfigs = ["player"]
				},
				["@zenith/root"] = new TagConfig
				{
					ChatColor = "lightred",
					ClanTag = "OWNER | ",
					NameColor = "lightred",
					NameTag = "{lightred}[OWNER] ",
					AvailableConfigs = ["owner"]
				},
				["@css/admin"] = new TagConfig
				{
					ClanTag = "ADMIN | ",
					NameColor = "blue",
					NameTag = "{blue}[ADMIN] ",
					AvailableConfigs = ["admin"]
				},
				["76561198345583467"] = new TagConfig
				{
					ChatColor = "gold",
					ClanTag = "Zenith | ",
					NameColor = "gold",
					NameTag = "{gold}[Zenith] ",
					AvailableConfigs = ["vip", "donator"]
				}
			};

			var jsonConfig = JsonSerializer.Serialize(defaultConfig, _jsonOptions);
			var jsonWithComments = @"// This configuration file defines tag settings for players.
// You can use the following keys to target specific players or groups:
// - ""all"": Applies to all players
// - ""#GroupName"": Applies to players in a specific group (e.g., ""#Owner"", ""#Admin"")
// - ""@Permission"": Applies to players with a specific permission (e.g., ""@css/admin"")
// - ""SteamID"": Applies to a specific player by their Steam ID

" + jsonConfig;

			File.WriteAllText(configPath, jsonWithComments);
		}
	}

	private void EnsurePredefinedConfigFileExists()
	{
		string configPath = Path.Combine(ModuleDirectory, "predefined_tags.json");
		if (!File.Exists(configPath))
		{
			var defaultConfig = new Dictionary<string, PredefinedTagConfig>
			{
				["player"] = new PredefinedTagConfig
				{
					Name = "Player",
					ChatColor = "white",
					ClanTag = "Player | ",
					NameColor = "white",
					NameTag = "{white}[Player] "
				},
				["owner"] = new PredefinedTagConfig
				{
					Name = "Owner",
					ChatColor = "lightred",
					ClanTag = "OWNER | ",
					NameColor = "lightred",
					NameTag = "{lightred}[OWNER] "
				},
				["admin"] = new PredefinedTagConfig
				{
					Name = "Admin",
					ChatColor = "blue",
					ClanTag = "ADMIN | ",
					NameColor = "blue",
					NameTag = "{blue}[ADMIN] "
				},
				["vip"] = new PredefinedTagConfig
				{
					Name = "VIP",
					ChatColor = "gold",
					ClanTag = "VIP | ",
					NameColor = "gold",
					NameTag = "{gold}[VIP] "
				},
				["donator"] = new PredefinedTagConfig
				{
					Name = "Donator",
					ChatColor = "green",
					ClanTag = "DONATOR | ",
					NameColor = "green",
					NameTag = "{green}[DONATOR] "
				}
			};

			var jsonConfig = JsonSerializer.Serialize(defaultConfig, _jsonOptions);
			var jsonWithComments = @"// This configuration file defines predefined tag configurations that can be applied to players.
// These configurations can be referenced in the 'AvailableConfigs' list in the main tags.json file.

" + jsonConfig;

			File.WriteAllText(configPath, jsonWithComments);
		}
	}

	private Dictionary<string, TagConfig> GetTagConfigs()
	{
		try
		{
			string configPath = Path.Combine(ModuleDirectory, "tags.json");
			string json = File.ReadAllText(configPath);
			string strippedJson = StripComments(json);
			return JsonSerializer.Deserialize<Dictionary<string, TagConfig>>(strippedJson, _jsonOptions) ?? [];
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error reading tag configs: {ex.Message}");
			return [];
		}
	}

	private Dictionary<string, PredefinedTagConfig> GetPredefinedTagConfigs()
	{
		try
		{
			string configPath = Path.Combine(ModuleDirectory, "predefined_tags.json");
			string json = File.ReadAllText(configPath);
			string strippedJson = StripComments(json);
			return JsonSerializer.Deserialize<Dictionary<string, PredefinedTagConfig>>(strippedJson, _jsonOptions) ?? [];
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error reading predefined tag configs: {ex.Message}");
			return [];
		}
	}

	private static string StripComments(string json)
	{
		if (string.IsNullOrEmpty(json))
			return json;

		var result = new System.Text.StringBuilder(json.Length);
		using (var reader = new StringReader(json))
		{
			string? line;
			while ((line = reader.ReadLine()) != null)
			{
				string trimmedLine = line.TrimStart();
				if (!trimmedLine.StartsWith("//"))
				{
					result.AppendLine(line);
				}
			}
		}
		return result.ToString();
	}


	private void ApplyTagConfig(CCSPlayerController player)
	{
		try
		{
			var zenithPlayer = GetZenithPlayer(player);
			if (zenithPlayer is null) return;

			_tagConfigs ??= GetTagConfigs();
			_predefinedConfigs ??= GetPredefinedTagConfigs();

			if (_playerSelectedConfigs.TryGetValue(player.SteamID, out var selectedConfig) && selectedConfig == "none")
			{
				ApplyNullConfig(player);
				return;
			}

			bool configApplied = false;
			List<string> availableConfigs = [];

			if (_tagConfigs.TryGetValue("all", out var allConfig))
			{
				if (HasTagConfigValues(allConfig))
				{
					ApplyConfig(zenithPlayer, allConfig);
					configApplied = true;
				}
				if (allConfig.AvailableConfigs != null)
				{
					availableConfigs.AddRange(allConfig.AvailableConfigs);
				}
			}

			foreach (var kvp in _tagConfigs)
			{
				if (kvp.Key == "all")
					continue;

				if (CheckPermissionOrSteamID(player, kvp.Key))
				{
					var config = kvp.Value;
					if (HasTagConfigValues(config))
					{
						ApplyConfig(zenithPlayer, config);
						configApplied = true;
					}
					if (config.AvailableConfigs != null)
					{
						availableConfigs.AddRange(config.AvailableConfigs);
					}
					break;
				}
			}

			if (!configApplied && availableConfigs.Count > 0)
			{
				foreach (var configName in availableConfigs)
				{
					if (_predefinedConfigs.TryGetValue(configName, out var predefinedConfig))
					{
						ApplyConfig(zenithPlayer, predefinedConfig);
						_moduleServices?.PrintForPlayer(player, Localizer["customtags.applied.default_predefined", predefinedConfig.Name]);
						configApplied = true;
						break;
					}
				}
			}

			if (_playerSelectedConfigs.TryGetValue(player.SteamID, out selectedConfig))
			{
				if (_predefinedConfigs.TryGetValue(selectedConfig, out var predefinedConfig))
				{
					ApplyConfig(zenithPlayer, predefinedConfig);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error applying tag config for player {player.PlayerName}: {ex.Message}");
		}
	}

	private static bool HasTagConfigValues(TagConfig config)
	{
		return !string.IsNullOrEmpty(config.ChatColor) ||
			   !string.IsNullOrEmpty(config.ClanTag) ||
			   !string.IsNullOrEmpty(config.NameColor) ||
			   !string.IsNullOrEmpty(config.NameTag);
	}

	private bool CheckPermissionOrSteamID(CCSPlayerController player, string key)
	{
		if (key.StartsWith("#"))
		{
			return AdminManager.PlayerInGroup(player, key[1..]);
		}

		AdminData? adminData = AdminManager.GetPlayerAdminData(player);
		if (adminData != null)
		{
			string permissionKey = key.StartsWith('@') ? key : "@" + key;
			if (adminData.Flags.Any(flagEntry =>
				flagEntry.Value.Contains(permissionKey, StringComparer.OrdinalIgnoreCase) ||
				flagEntry.Value.Any(flag => permissionKey.StartsWith(flag, StringComparison.OrdinalIgnoreCase))))
			{
				return true;
			}
		}

		return SteamID.TryParse(key, out SteamID? keySteamID) &&
			   keySteamID != null &&
			   Equals(keySteamID, new SteamID(player.SteamID));
	}

	private void ApplyConfig(IPlayerServices zenithPlayer, TagConfig config)
	{
		if (!string.IsNullOrEmpty(config.ChatColor))
			zenithPlayer.SetChatColor(GetChatColorValue(config.ChatColor));
		if (!string.IsNullOrEmpty(config.ClanTag))
			zenithPlayer.SetClanTag(config.ClanTag);
		if (!string.IsNullOrEmpty(config.NameColor))
			zenithPlayer.SetNameColor(GetChatColorValue(config.NameColor));
		if (!string.IsNullOrEmpty(config.NameTag))
			zenithPlayer.SetNameTag(ApplyPrefixColors(config.NameTag));
	}

	private void ApplyConfig(IPlayerServices zenithPlayer, PredefinedTagConfig config)
	{
		if (!string.IsNullOrEmpty(config.ChatColor))
			zenithPlayer.SetChatColor(GetChatColorValue(config.ChatColor));
		if (!string.IsNullOrEmpty(config.ClanTag))
			zenithPlayer.SetClanTag(config.ClanTag);
		if (!string.IsNullOrEmpty(config.NameColor))
			zenithPlayer.SetNameColor(GetChatColorValue(config.NameColor));
		if (!string.IsNullOrEmpty(config.NameTag))
			zenithPlayer.SetNameTag(ApplyPrefixColors(config.NameTag));
	}

	private void ApplyPredefinedConfig(CCSPlayerController player, PredefinedTagConfig config)
	{
		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer is null) return;

		ApplyConfig(zenithPlayer, config);
	}

	private void ApplyNullConfig(CCSPlayerController player)
	{
		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer is null) return;

		zenithPlayer.SetChatColor(null);
		zenithPlayer.SetClanTag(null);
		zenithPlayer.SetNameColor(null);
		zenithPlayer.SetNameTag(null);
	}

	public static string ApplyPrefixColors(string msg)
	{
		if (string.IsNullOrEmpty(msg))
			return msg;

		var sortedColors = _chatColors
			.OrderByDescending(color => color.Key.Length)
			.ThenBy(color => color.Key)
			.ToList();

		foreach (var color in sortedColors)
		{
			string pattern = $@"(\{{{color.Key}\}}|{color.Key})";
			msg = Regex.Replace(msg, pattern, color.Value.ToString(), RegexOptions.IgnoreCase);
		}

		return msg;
	}

	private static char GetChatColorValue(string colorName)
	{
		if (_chatColors.TryGetValue(colorName, out char color))
			return color;

		return ChatColors.Default;
	}

	private void OnZenithPlayerLoaded(CCSPlayerController player)
	{
		AddTimer(3.0f, () => ApplyTagConfig(player), TimerFlags.STOP_ON_MAPCHANGE);
	}

	public override void Unload(bool hotReload)
	{
		IModuleServices? moduleServices = _moduleServicesCapability?.Get();
		moduleServices?.DisposeModule(this.GetType().Assembly);
	}

	private void OnZenithCoreUnload(bool hotReload)
	{
		if (hotReload)
		{
			AddTimer(3.0f, () =>
			{
				try { File.SetLastWriteTime(ModulePath, DateTime.Now); }
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
	public List<string> AvailableConfigs { get; set; } = [];
}

public class PredefinedTagConfig
{
	public string Name { get; set; } = "";
	public string? ChatColor { get; set; }
	public string? ClanTag { get; set; }
	public string? NameColor { get; set; }
	public string? NameTag { get; set; }
}
