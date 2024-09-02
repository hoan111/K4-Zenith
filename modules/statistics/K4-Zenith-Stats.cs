using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using ZenithAPI;
using System.Reflection;
using CounterStrikeSharp.API.Modules.Events;
using Menu;
using Menu.Enums;
using MySqlConnector;
using Dapper;
using System.ComponentModel.DataAnnotations.Schema;

namespace Zenith_Stats;

[MinimumApiVersion(260)]
public class Plugin : BasePlugin
{
	public CCSGameRules? GameRules = null;
	public IModuleConfigAccessor _coreAccessor = null!;
	private const string MODULE_ID = "Stats";

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.0";

	public KitsuneMenu Menu { get; private set; } = null!;
	private PlayerCapability<IPlayerServices>? _playerServicesCapability;
	private PluginCapability<IModuleServices>? _moduleServicesCapability;

	private IZenithEvents? _zenithEvents;
	private EventManager? _eventManager;
	private IModuleServices? _moduleServices;
	private readonly Dictionary<ulong, PlayerStats> _playerStats = new Dictionary<ulong, PlayerStats>();
	private readonly List<CCSPlayerController> playerSpawned = [];

	private Dictionary<string, List<string>> _eventTargets = new Dictionary<string, List<string>>
	{
		{ "EventPlayerDeath", new List<string> { "Userid", "Attacker", "Assister" } },
		{ "EventGrenadeThrown", new List<string> { "Userid" } },
		{ "EventPlayerHurt", new List<string> { "Userid", "Attacker" } },
		{ "EventRoundStart", new List<string>() },
		{ "EventBombPlanted", new List<string> { "Userid" } },
		{ "EventHostageRescued", new List<string> { "Userid" } },
		{ "EventHostageKilled", new List<string> { "Userid" } },
		{ "EventBombDefused", new List<string> { "Userid" } },
		{ "EventRoundEnd", new List<string> { "winner" } },
		{ "EventWeaponFire", new List<string> { "Userid" } },
		{ "EventRoundMvp", new List<string> { "Userid" } },
		{ "EventCsWinPanelMatch", new List<string>() }
	};

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

		Menu = new KitsuneMenu(this);

		_coreAccessor = _moduleServices.GetModuleConfigAccessor();

		_moduleServices.RegisterModuleConfig("Config", "StatisticCommands", "List of commands that shows player statistics", new List<string> { "stats", "stat", "statistics" });
		_moduleServices.RegisterModuleConfig("Config", "MapStatisticCommands", "List of commands that shows map statistics", new List<string> { "mapstats", "mapstat", "mapstatistics" });
		_moduleServices.RegisterModuleConfig("Config", "WeaponStatisticCommands", "List of commands that shows weapon statistics", new List<string> { "weaponstats", "weaponstat", "weaponstatistics" });
		_moduleServices.RegisterModuleConfig("Config", "WarmupStats", "Allow stats during warmup", false);
		_moduleServices.RegisterModuleConfig("Config", "StatsForBots", "Allow stats for bots", false);
		_moduleServices.RegisterModuleConfig("Config", "MinPlayers", "Minimum number of players required for stats", 4);
		_moduleServices.RegisterModuleConfig("Config", "FFAMode", "Enable FFA mode", false);
		_moduleServices.RegisterModuleConfig("Config", "EnableWeaponStats", "Enable weapon-based statistics", true);
		_moduleServices.RegisterModuleConfig("Config", "EnableMapStats", "Enable map-based statistics", true);

		_moduleServices.RegisterModuleStorage(new Dictionary<string, object?>
		{
			{ "Kills", 0 },
			{ "FirstBlood", 0 },
			{ "Deaths", 0 },
			{ "Assists", 0 },
			{ "Shoots", 0 },
			{ "HitsTaken", 0 },
			{ "HitsGiven", 0 },
			{ "Headshots", 0 },
			{ "HeadHits", 0 },
			{ "ChestHits", 0 },
			{ "StomachHits", 0 },
			{ "LeftArmHits", 0 },
			{ "RightArmHits", 0 },
			{ "LeftLegHits", 0 },
			{ "RightLegHits", 0 },
			{ "NeckHits", 0 },
			{ "UnusedHits", 0 },
			{ "GearHits", 0 },
			{ "SpecialHits", 0 },
			{ "Grenades", 0 },
			{ "MVP", 0 },
			{ "RoundWin", 0 },
			{ "RoundLose", 0 },
			{ "GameWin", 0 },
			{ "GameLose", 0 },
			{ "RoundsOverall", 0 },
			{ "RoundsCT", 0 },
			{ "RoundsT", 0 },
			{ "BombPlanted", 0 },
			{ "BombDefused", 0 },
			{ "HostageRescued", 0 },
			{ "HostageKilled", 0 },
			{ "NoScopeKill", 0 },
			{ "PenetratedKill", 0 },
			{ "ThruSmokeKill", 0 },
			{ "FlashedKill", 0 },
			{ "DominatedKill", 0 },
			{ "RevengeKill", 0 },
			{ "AssistFlash", 0 }
		});

		_zenithEvents = _moduleServices.GetEventHandler();
		if (_zenithEvents != null)
		{
			_zenithEvents.OnZenithPlayerLoaded += OnZenithPlayerLoaded;
			_zenithEvents.OnZenithPlayerUnloaded += OnZenithPlayerUnloaded;
			_zenithEvents.OnZenithCoreUnload += OnZenithCoreUnload;
		}
		else
		{
			Logger.LogError("Failed to get Zenith event handler.");
		}

		_moduleServices.RegisterModuleCommands(_coreAccessor.GetValue<List<string>>("Config", "StatisticCommands"), "Show the player statistics.", OnStatsCommand, CommandUsage.CLIENT_ONLY);
		_moduleServices.RegisterModuleCommands(_coreAccessor.GetValue<List<string>>("Config", "WeaponStatisticCommands"), "Show the player statistics for weapons.", OnWeaponStatsCommand, CommandUsage.CLIENT_ONLY);
		_moduleServices.RegisterModuleCommands(_coreAccessor.GetValue<List<string>>("Config", "MapStatisticCommands"), "Show the player statistics for maps.", OnMapStatsCommand, CommandUsage.CLIENT_ONLY);

		_moduleServices.RegisterModulePlayerPlaceholder("kda", p => CalculateKDA(GetZenithPlayer(p)));
		_moduleServices.RegisterModulePlayerPlaceholder("kpr", p => CalculateKPR(GetZenithPlayer(p)));
		_moduleServices.RegisterModulePlayerPlaceholder("accuracy", p => CalculateAccuracy(GetZenithPlayer(p)));
		_moduleServices.RegisterModulePlayerPlaceholder("kd", p => CalculateKD(GetZenithPlayer(p)));

		Initialize_Events();
		InitializeDatabaseTables();

		RegisterListener<Listeners.OnMapStart>((mapName) =>
		{
			AddTimer(1.0f, () =>
			{
				GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules;

				foreach (var player in GetValidPlayers())
				{
					var stats = new PlayerStats(player, this);
					_playerStats[player.SteamID] = stats;
				}
			});
		});

		RegisterListener<Listeners.OnMapEnd>(() =>
		{
			foreach (var playerStats in _playerStats.Values)
			{
				Task.Run(async () =>
				{
					await playerStats.SaveWeaponStats();
					await playerStats.SaveMapStats();
					playerStats.ResetStats();
				});
			}

			_playerStats.Clear();
			playerSpawned.Clear();
		});

		RegisterEventHandler((EventRoundPrestart @event, GameEventInfo info) =>
		{
			playerSpawned.Clear();
			return HookResult.Continue;
		});

		RegisterEventHandler((EventPlayerSpawn @event, GameEventInfo info) =>
		{
			CCSPlayerController? player = @event.Userid;
			if (player == null || player.IsBot || player.IsHLTV)
				return HookResult.Continue;

			int reqiredPlayers = _coreAccessor.GetValue<int>("Config", "MinPlayers");
			if (reqiredPlayers > Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).Count() && _playerStats.TryGetValue(player.SteamID, out var stats) && stats.SpawnMessageTimer == null)
			{
				_moduleServices.PrintForPlayer(player, Localizer["k4.stats.stats_disabled", reqiredPlayers]);
				stats.SpawnMessageTimer = AddTimer(3.0f, () => { stats.SpawnMessageTimer = null; });
			}

			playerSpawned.Add(player);
			return HookResult.Continue;
		}, HookMode.Post);

		if (hotReload)
			GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules;

		AddTimer(3.0f, () =>
		{
			_moduleServices.LoadAllOnlinePlayerData();
			Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).ToList().ForEach(player =>
			{
				var zenithPlayer = GetZenithPlayer(player);
				if (zenithPlayer != null)
				{
					var stats = new PlayerStats(zenithPlayer, this);
					_playerStats[player.SteamID] = stats;
				}
			});
		});

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
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

	public override void Unload(bool hotReload)
	{
		IModuleServices? moduleServices = _moduleServicesCapability?.Get();
		if (moduleServices == null)
			return;

		moduleServices.DisposeModule(this.GetType().Assembly);
	}

	private string CalculateKD(IPlayerServices? player)
	{
		if (player == null) return "N/A";
		var stats = _playerStats.GetValueOrDefault(player.Controller.SteamID);
		if (stats == null) return "N/A";

		int kills = stats.GetGlobalStat("Kills");
		int deaths = stats.GetGlobalStat("Deaths");
		double kd = deaths == 0 ? kills : (double)kills / deaths;
		return kd.ToString("F2");
	}

	private string CalculateKDA(IPlayerServices? player)
	{
		if (player == null) return "N/A";
		var stats = _playerStats.GetValueOrDefault(player.Controller.SteamID);
		if (stats == null) return "N/A";

		int kills = stats.GetGlobalStat("Kills");
		int deaths = stats.GetGlobalStat("Deaths");
		int assists = stats.GetGlobalStat("Assists");
		double kda = (kills + assists) / (double)(deaths == 0 ? 1 : deaths);
		return kda.ToString("F2");
	}

	private string CalculateKPR(IPlayerServices? player)
	{
		if (player == null) return "N/A";
		var stats = _playerStats.GetValueOrDefault(player.Controller.SteamID);
		if (stats == null) return "N/A";

		int kills = stats.GetGlobalStat("Kills");
		int rounds = stats.GetGlobalStat("RoundsOverall");
		double kpr = rounds == 0 ? kills : (double)kills / rounds;
		return kpr.ToString("F2");
	}

	private string CalculateAccuracy(IPlayerServices? player)
	{
		if (player == null) return "N/A";
		var stats = _playerStats.GetValueOrDefault(player.Controller.SteamID);
		if (stats == null) return "N/A";

		int shoots = stats.GetGlobalStat("Shoots");
		int hitsGiven = stats.GetGlobalStat("HitsGiven");
		double accuracy = (shoots == 0) ? 0 : (double)hitsGiven / shoots * 100;
		if (accuracy > 100) // ? This is just to prevent shotguns making them over 100%
			accuracy = 100;

		return accuracy.ToString("F2") + "%";
	}

	private async void InitializeDatabaseTables()
	{
		if (_moduleServices == null)
			return;

		string createWeaponStatsTable = $@"
		CREATE TABLE IF NOT EXISTS `{_coreAccessor.GetValue<string>("Database", "TablePrefix")}zenith_weapon_stats` (
			`steam_id` VARCHAR(32) NOT NULL,
			`weapon` VARCHAR(64) NOT NULL,
			`kills` INT NOT NULL DEFAULT 0,
			`shots` INT NOT NULL DEFAULT 0,
			`hits` INT NOT NULL DEFAULT 0,
			`headshots` INT NOT NULL DEFAULT 0,
			`head_hits` INT NOT NULL DEFAULT 0,
			`chest_hits` INT NOT NULL DEFAULT 0,
			`stomach_hits` INT NOT NULL DEFAULT 0,
			`left_arm_hits` INT NOT NULL DEFAULT 0,
			`right_arm_hits` INT NOT NULL DEFAULT 0,
			`left_leg_hits` INT NOT NULL DEFAULT 0,
			`right_leg_hits` INT NOT NULL DEFAULT 0,
			`neck_hits` INT NOT NULL DEFAULT 0,
			`gear_hits` INT NOT NULL DEFAULT 0,
			PRIMARY KEY (`steam_id`, `weapon`)
		) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";

		string createMapStatsTable = $@"
		CREATE TABLE IF NOT EXISTS `{_coreAccessor.GetValue<string>("Database", "TablePrefix")}zenith_map_stats` (
			`steam_id` VARCHAR(32) NOT NULL,
			`map_name` VARCHAR(64) NOT NULL,
			`kills` INT NOT NULL DEFAULT 0,
			`first_blood` INT NOT NULL DEFAULT 0,
			`deaths` INT NOT NULL DEFAULT 0,
			`assists` INT NOT NULL DEFAULT 0,
			`shoots` INT NOT NULL DEFAULT 0,
			`hits_taken` INT NOT NULL DEFAULT 0,
			`hits_given` INT NOT NULL DEFAULT 0,
			`headshots` INT NOT NULL DEFAULT 0,
			`head_hits` INT NOT NULL DEFAULT 0,
			`chest_hits` INT NOT NULL DEFAULT 0,
			`stomach_hits` INT NOT NULL DEFAULT 0,
			`left_arm_hits` INT NOT NULL DEFAULT 0,
			`right_arm_hits` INT NOT NULL DEFAULT 0,
			`left_leg_hits` INT NOT NULL DEFAULT 0,
			`right_leg_hits` INT NOT NULL DEFAULT 0,
			`neck_hits` INT NOT NULL DEFAULT 0,
			`unused_hits` INT NOT NULL DEFAULT 0,
			`gear_hits` INT NOT NULL DEFAULT 0,
			`special_hits` INT NOT NULL DEFAULT 0,
			`grenades` INT NOT NULL DEFAULT 0,
			`mvp` INT NOT NULL DEFAULT 0,
			`round_win` INT NOT NULL DEFAULT 0,
			`round_lose` INT NOT NULL DEFAULT 0,
			`game_win` INT NOT NULL DEFAULT 0,
			`game_lose` INT NOT NULL DEFAULT 0,
			`rounds_overall` INT NOT NULL DEFAULT 0,
			`rounds_ct` INT NOT NULL DEFAULT 0,
			`rounds_t` INT NOT NULL DEFAULT 0,
			`bomb_planted` INT NOT NULL DEFAULT 0,
			`bomb_defused` INT NOT NULL DEFAULT 0,
			`hostage_rescued` INT NOT NULL DEFAULT 0,
			`hostage_killed` INT NOT NULL DEFAULT 0,
			`no_scope_kill` INT NOT NULL DEFAULT 0,
			`penetrated_kill` INT NOT NULL DEFAULT 0,
			`thru_smoke_kill` INT NOT NULL DEFAULT 0,
			`flashed_kill` INT NOT NULL DEFAULT 0,
			`dominated_kill` INT NOT NULL DEFAULT 0,
			`revenge_kill` INT NOT NULL DEFAULT 0,
			`assist_flash` INT NOT NULL DEFAULT 0,
			PRIMARY KEY (`steam_id`, `map_name`)
		) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";

		try
		{
			string connectionString = _moduleServices.GetConnectionString();
			using var connection = new MySqlConnection(connectionString);
			await connection.OpenAsync();

			await connection.ExecuteAsync(createWeaponStatsTable);
			await connection.ExecuteAsync(createMapStatsTable);
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error initializing database tables: {ex.Message}");
		}
	}

	private void OnZenithPlayerLoaded(CCSPlayerController player)
	{
		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer is null) return;

		var stats = new PlayerStats(zenithPlayer, this);
		_playerStats[zenithPlayer.SteamID] = stats;
	}

	private async void OnZenithPlayerUnloaded(CCSPlayerController player)
	{
		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer is null) return;

		if (_playerStats.TryGetValue(zenithPlayer.SteamID, out var stats))
		{
			try
			{
				await stats.SaveWeaponStats();
				await stats.SaveMapStats();
				_playerStats.Remove(zenithPlayer.SteamID);
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error saving stats for player {zenithPlayer.Name}: {ex.Message}");
			}
		}
	}

	public IPlayerServices? GetZenithPlayer(CCSPlayerController? player)
	{
		if (player == null) return null;
		try { return _playerServicesCapability?.Get(player); }
		catch { return null; }
	}

	private void OnStatsCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null) return;

		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer == null) return;

		if (_playerStats.TryGetValue(player.SteamID, out var stats))
		{
			List<MenuItem> items =
			[
				new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.accuracy"]}:</font> {CalculateAccuracy(zenithPlayer)}")),
				new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.kpr"]}:</font> {CalculateKPR(zenithPlayer)}")),
				new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.kda"]}:</font> {CalculateKDA(zenithPlayer)}")),
				new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.kd"]}:</font> {CalculateKD(zenithPlayer)}")),
			];

			var statNames = new[]
			{
				"Kills", "FirstBlood", "Deaths", "Assists", "Shoots", "HitsTaken", "HitsGiven",
				"Headshots", "HeadHits", "ChestHits", "StomachHits", "LeftArmHits", "RightArmHits",
				"LeftLegHits", "RightLegHits", "NeckHits", "GearHits", "Grenades", "MVP",
				"RoundWin", "RoundLose", "GameWin", "GameLose", "RoundsOverall", "RoundsCT",
				"RoundsT", "BombPlanted", "BombDefused", "HostageRescued", "HostageKilled",
				"NoScopeKill", "PenetratedKill", "ThruSmokeKill", "FlashedKill", "DominatedKill",
				"RevengeKill", "AssistFlash"
			};

			foreach (var statName in statNames)
			{
				int value = stats.GetGlobalStat(statName);
				if (value != 0)
				{
					string localizedName = Localizer[$"k4.stats.{statName.ToLower()}"];
					items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{localizedName}:</font> {value:N0}")));
				}
			}

			if (items.Count == 0)
			{
				items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.no_stats"]}</font>")));
			}

			Menu?.ShowScrollableMenu(player, Localizer["k4.stats.title"], items, (buttons, menu, selected) =>
			{
				// No selection handle as all items are just for display
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu"), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}
		else
		{
			zenithPlayer.Print(Localizer["k4.stats.no_stats"]);
		}
	}

	private void OnWeaponStatsCommand(CCSPlayerController? player, CommandInfo command)
	{
		try
		{
			if (player == null)
				return;

			var zenithPlayer = GetZenithPlayer(player);
			if (zenithPlayer == null)
			{
				Logger.LogWarning($"Failed to get ZenithPlayer for {player.PlayerName}");
				return;
			}

			if (!_coreAccessor.GetValue<bool>("Config", "EnableWeaponStats"))
			{
				zenithPlayer.Print(Localizer["k4.stats.weapon-disabled"]);
				return;
			}

			if (!_playerStats.TryGetValue(player.SteamID, out var stats) || stats == null)
			{
				zenithPlayer.Print(Localizer["k4.stats.no_stats"]);
				return;
			}

			if (stats.WeaponStats == null)
			{
				zenithPlayer.Print(Localizer["k4.stats.no_stats"]);
				return;
			}

			List<MenuItem> items = new List<MenuItem>();
			var defaultValues = new Dictionary<int, object>();
			var weaponStatsMap = new Dictionary<int, string>();

			int index = 0;
			foreach (var weaponStat in stats.WeaponStats)
			{
				if (string.IsNullOrEmpty(weaponStat.Key))
					continue;

				string weaponName = weaponStat.Key.ToUpper();
				items.Add(new MenuItem(MenuItemType.Button, new List<MenuValue> { new MenuValue(weaponName) }));
				defaultValues[index] = weaponName;
				weaponStatsMap[index] = weaponStat.Key;
				index++;
			}

			if (items.Count == 0)
			{
				items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.no_stats"]}</font>")));
			}

			Menu.ShowScrollableMenu(player, Localizer["k4.weaponstats.title"], items, (buttons, menu, selected) =>
			{
				if (selected == null) return;

				switch (buttons)
				{
					case MenuButtons.Select:
						if (weaponStatsMap.TryGetValue(menu.Option, out var weaponKey))
						{
							if (stats.WeaponStats.TryGetValue(weaponKey, out var weaponStat))
							{
								ShowWeaponDetails(player, weaponStat);
							}
						}
						break;
				}
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu"), 5, defaultValues, !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error in OnWeaponStatsCommand: {ex.Message}\n{ex.StackTrace}");
		}
	}

	public IEnumerable<IPlayerServices> GetValidPlayers()
	{
		foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
		{
			var zenithPlayer = GetZenithPlayer(player);
			if (zenithPlayer != null)
			{
				yield return zenithPlayer;
			}
		}
	}

	private void ShowWeaponDetails(CCSPlayerController player, WeaponStats weaponStat)
	{
		List<MenuItem> items = new List<MenuItem>
		{
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.kills"]}:</font> {weaponStat.Kills:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.shoots"]}:</font> {weaponStat.Shots:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.hitsgiven"]}:</font> {weaponStat.Hits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.accuracy"]}:</font> {(weaponStat.Shots > 0 ? Math.Min((float)weaponStat.Hits / weaponStat.Shots * 100, 100) : 0):F2}%")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.headshots"]}:</font> {weaponStat.Headshots:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.chesthits"]}:</font> {weaponStat.ChestHits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.stomachhits"]}:</font> {weaponStat.StomachHits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.leftarmhits"]}:</font> {weaponStat.LeftArmHits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.rightarmhits"]}:</font> {weaponStat.RightArmHits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.leftleghits"]}:</font> {weaponStat.LeftLegHits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.rightleghits"]}:</font> {weaponStat.RightLegHits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.neckhits"]}:</font> {weaponStat.NeckHits:N0}"))
		};

		Menu?.ShowScrollableMenu(player, weaponStat.Weapon.ToUpper(), items, (buttons, menu, selected) => { }, true, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu"), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
	}

	private void OnMapStatsCommand(CCSPlayerController? player, CommandInfo command)
	{
		try
		{
			if (player == null)
				return;

			var zenithPlayer = GetZenithPlayer(player);
			if (zenithPlayer == null)
			{
				Logger.LogWarning($"Failed to get ZenithPlayer for {player.PlayerName}");
				return;
			}

			if (!_coreAccessor.GetValue<bool>("Config", "EnableMapStats"))
			{
				zenithPlayer.Print(Localizer["k4.stats.map-disabled"]);
				return;
			}

			if (_playerStats.TryGetValue(player.SteamID, out var stats))
			{
				ShowMapStats(player, stats);
			}
			else
				zenithPlayer.Print(Localizer["k4.stats.no_stats"]);
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error in OnMapStatsCommand: {ex.Message}\n{ex.StackTrace}");
		}
	}

	private void ShowMapStats(CCSPlayerController player, PlayerStats playerStats)
	{
		try
		{
			List<MenuItem> items = new List<MenuItem>();

			var statNames = new[]
			{
				"Kills", "FirstBlood", "Deaths", "Assists", "Shoots", "HitsTaken", "HitsGiven",
				"Headshots", "HeadHits", "ChestHits", "StomachHits", "LeftArmHits", "RightArmHits",
				"LeftLegHits", "RightLegHits", "NeckHits", "GearHits", "Grenades", "MVP",
				"RoundWin", "RoundLose", "GameWin", "GameLose", "RoundsOverall", "RoundsCT",
				"RoundsT", "BombPlanted", "BombDefused", "HostageRescued", "HostageKilled",
				"NoScopeKill", "PenetratedKill", "ThruSmokeKill", "FlashedKill", "DominatedKill",
				"RevengeKill", "AssistFlash"
			};

			foreach (var statName in statNames)
			{
				int value = playerStats.GetMapStat(statName);
				if (value != 0)
				{
					string localizedName = Localizer[$"k4.stats.{statName.ToLower()}"];
					items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{localizedName}:</font> {value:N0}")));
				}
			}

			if (items.Count == 0)
			{
				items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.no_stats"]}</font>")));
			}

			Menu?.ShowScrollableMenu(player, playerStats.CurrentMapStats.MapName.ToUpper(), items, (buttons, menu, selected) =>
			{
				// No selection handle as all items are just for display
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu"), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error in ShowMapStats: {ex.Message}\n{ex.StackTrace}");
		}
	}

	private void Initialize_Events()
	{
		_eventManager = new EventManager(this);

		foreach (var eventEntry in _eventTargets)
		{
			RegisterMassEventHandler(eventEntry.Key);
		}
	}

	private void RegisterMassEventHandler(string eventName)
	{
		try
		{
			string fullyQualifiedTypeName = $"CounterStrikeSharp.API.Core.{eventName}, CounterStrikeSharp.API";
			Type? eventType = Type.GetType(fullyQualifiedTypeName);
			if (eventType != null && typeof(GameEvent).IsAssignableFrom(eventType))
			{
				MethodInfo? baseRegisterMethod = typeof(BasePlugin).GetMethod(nameof(RegisterEventHandler), BindingFlags.Public | BindingFlags.Instance);

				if (baseRegisterMethod != null)
				{
					MethodInfo registerMethod = baseRegisterMethod.MakeGenericMethod(eventType);

					MethodInfo? methodInfo = typeof(EventManager).GetMethod(nameof(EventManager.OnEventHappens), BindingFlags.Public | BindingFlags.Instance)?.MakeGenericMethod(eventType);
					if (methodInfo != null)
					{
						Delegate? handlerDelegate = Delegate.CreateDelegate(typeof(GameEventHandler<>).MakeGenericType(eventType), _eventManager, methodInfo);
						if (handlerDelegate != null)
						{
							registerMethod.Invoke(this, new object[] { handlerDelegate, HookMode.Post });
						}
						else
						{
							Logger.LogError($"Failed to create delegate for event type {eventType.Name}.");
						}
					}
					else
					{
						Logger.LogError($"OnEventHappens method not found for event type {eventType.Name}.");
					}
				}
				else
					Logger.LogError("RegisterEventHandler method not found.");
			}
			else
				Logger.LogError($"Event type not found in specified assembly. Event: {eventName}.");
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Failed to register event handler for {0}.", eventName);
		}
	}

	public class EventManager
	{
		private readonly Plugin _plugin;
		private bool FirstBlood = false;

		public EventManager(Plugin plugin)
		{
			_plugin = plugin;
		}

		public HookResult OnEventHappens<T>(T eventObj, GameEventInfo info) where T : GameEvent
		{
			if (!IsStatsAllowed())
				return HookResult.Continue;

			string eventType = typeof(T).Name;

			switch (eventType)
			{
				case "EventPlayerDeath":
					HandlePlayerDeath(eventObj as EventPlayerDeath);
					break;
				case "EventGrenadeThrown":
					HandleGrenadeThrown(eventObj as EventGrenadeThrown);
					break;
				case "EventPlayerHurt":
					HandlePlayerHurt(eventObj as EventPlayerHurt);
					break;
				case "EventRoundStart":
					FirstBlood = false;
					break;
				case "EventBombPlanted":
					HandleBombPlanted(eventObj as EventBombPlanted);
					break;
				case "EventHostageRescued":
					HandleHostageRescued(eventObj as EventHostageRescued);
					break;
				case "EventHostageKilled":
					HandleHostageKilled(eventObj as EventHostageKilled);
					break;
				case "EventBombDefused":
					HandleBombDefused(eventObj as EventBombDefused);
					break;
				case "EventRoundEnd":
					HandleRoundEnd(eventObj as EventRoundEnd);
					break;
				case "EventWeaponFire":
					HandleWeaponFire(eventObj as EventWeaponFire);
					break;
				case "EventRoundMvp":
					HandleRoundMvp(eventObj as EventRoundMvp);
					break;
				case "EventCsWinPanelMatch":
					HandleCsWinPanelMatch(eventObj as EventCsWinPanelMatch);
					break;
			}

			return HookResult.Continue;
		}

		private bool IsStatsAllowed()
		{
			int notBots = Utilities.GetPlayers().Count(player => !player.IsBot && !player.IsHLTV);
			bool warmupStats = _plugin._coreAccessor.GetValue<bool>("Config", "WarmupStats");
			int minPlayers = _plugin._coreAccessor.GetValue<int>("Config", "MinPlayers");

			return _plugin.GameRules != null && (!_plugin.GameRules.WarmupPeriod || warmupStats) && (minPlayers <= notBots);
		}

		private void HandlePlayerDeath(EventPlayerDeath? @event)
		{
			if (@event == null)
				return;

			bool statsForBots = _plugin._coreAccessor.GetValue<bool>("Config", "StatsForBots");

			var victim = _plugin.GetZenithPlayer(@event.Userid);
			var attacker = _plugin.GetZenithPlayer(@event.Attacker);
			var assister = _plugin.GetZenithPlayer(@event.Assister);

			bool isVictimBot = victim == null;
			bool isAttackerBot = attacker == null;
			bool isAssisterBot = assister == null;

			if (!isVictimBot && _plugin._playerStats.TryGetValue(@event.Userid!.SteamID, out var victimStats))
			{
				if (statsForBots || !isAttackerBot)
				{
					victimStats.IncrementStat("Deaths");
				}
			}

			if (!isAttackerBot && attacker != victim && _plugin._playerStats.TryGetValue(@event.Attacker!.SteamID, out var attackerStats))
			{
				if (statsForBots || !isVictimBot)
				{
					attackerStats.IncrementStat("Kills");
					if (!FirstBlood)
					{
						FirstBlood = true;
						attackerStats.IncrementStat("FirstBlood");
					}
					if (@event.Noscope) attackerStats.IncrementStat("NoScopeKill");
					if (@event.Penetrated > 0) attackerStats.IncrementStat("PenetratedKill");
					if (@event.Thrusmoke) attackerStats.IncrementStat("ThruSmokeKill");
					if (@event.Attackerblind) attackerStats.IncrementStat("FlashedKill");
					if (@event.Dominated > 0) attackerStats.IncrementStat("DominatedKill");
					if (@event.Revenge > 0) attackerStats.IncrementStat("RevengeKill");
					if (@event.Headshot) attackerStats.IncrementStat("Headshots");

					attackerStats.AddWeaponKill(@event);
				}
			}

			if (!isAssisterBot && _plugin._playerStats.TryGetValue(@event.Assister!.SteamID, out var assisterStats))
			{
				if (statsForBots || (!isVictimBot && !isAttackerBot))
				{
					assisterStats.IncrementStat("Assists");
					if (@event.Assistedflash) assisterStats.IncrementStat("AssistFlash");
				}
			}
		}

		private void HandleGrenadeThrown(EventGrenadeThrown? @event)
		{
			if (@event == null) return;

			var player = _plugin.GetZenithPlayer(@event.Userid);
			if (player != null && _plugin._playerStats.TryGetValue(player.Controller.SteamID, out var stats))
			{
				stats.IncrementStat("Grenades");
			}
		}

		private void HandlePlayerHurt(EventPlayerHurt? @event)
		{
			if (@event == null) return;

			var victim = _plugin.GetZenithPlayer(@event.Userid);
			var attacker = _plugin.GetZenithPlayer(@event.Attacker);

			if (victim != null && _plugin._playerStats.TryGetValue(victim.Controller.SteamID, out var victimStats))
			{
				victimStats.IncrementStat("HitsTaken");
			}

			if (attacker != null && attacker != victim && _plugin._playerStats.TryGetValue(attacker.Controller.SteamID, out var attackerStats))
			{
				attackerStats.AddWeaponHit(@event.Weapon, @event.Hitgroup);
			}
		}

		private void HandleBombPlanted(EventBombPlanted? @event)
		{
			if (@event == null) return;

			var player = _plugin.GetZenithPlayer(@event.Userid);
			if (player != null && _plugin._playerStats.TryGetValue(player.Controller.SteamID, out var stats))
			{
				stats.IncrementStat("BombPlanted");
			}
		}

		private void HandleHostageRescued(EventHostageRescued? @event)
		{
			if (@event == null) return;

			var player = _plugin.GetZenithPlayer(@event.Userid);
			if (player != null && _plugin._playerStats.TryGetValue(player.Controller.SteamID, out var stats))
			{
				stats.IncrementStat("HostageRescued");
			}
		}

		private void HandleHostageKilled(EventHostageKilled? @event)
		{
			if (@event == null) return;

			var player = _plugin.GetZenithPlayer(@event.Userid);
			if (player != null && _plugin._playerStats.TryGetValue(player.Controller.SteamID, out var stats))
			{
				stats.IncrementStat("HostageKilled");
			}
		}

		private void HandleBombDefused(EventBombDefused? @event)
		{
			if (@event == null) return;

			var player = _plugin.GetZenithPlayer(@event.Userid);
			if (player != null && _plugin._playerStats.TryGetValue(player.Controller.SteamID, out var stats))
			{
				stats.IncrementStat("BombDefused");
			}
		}

		private void HandleRoundEnd(EventRoundEnd? @event)
		{
			if (@event == null) return;

			foreach (var playerStats in _plugin._playerStats.Values)
			{
				if (_plugin.playerSpawned.Contains(playerStats.ZenithPlayer.Controller))
					continue;

				CsTeam team = playerStats.ZenithPlayer.Controller.Team;

				playerStats.IncrementStat("RoundsOverall");
				if (team == CsTeam.Terrorist)
					playerStats.IncrementStat("RoundsT");
				else if (team == CsTeam.CounterTerrorist)
					playerStats.IncrementStat("RoundsCT");

				if (team <= CsTeam.Spectator)
					continue;

				if ((int)team == @event.Winner)
					playerStats.IncrementStat("RoundWin");
				else
					playerStats.IncrementStat("RoundLose");
			}
		}

		private void HandleWeaponFire(EventWeaponFire? @event)
		{
			if (@event == null) return;

			var player = _plugin.GetZenithPlayer(@event.Userid);
			if (player != null && _plugin._playerStats.TryGetValue(player.Controller.SteamID, out var stats))
			{
				if (!@event.Weapon.Contains("knife") && !@event.Weapon.Contains("bayonet"))
				{
					stats.AddWeaponShot(@event.Weapon);
				}
			}
		}

		private void HandleRoundMvp(EventRoundMvp? @event)
		{
			if (@event == null) return;

			var player = _plugin.GetZenithPlayer(@event.Userid);
			if (player != null && _plugin._playerStats.TryGetValue(player.Controller.SteamID, out var stats))
			{
				stats.IncrementStat("MVP");
			}
		}

		private void HandleCsWinPanelMatch(EventCsWinPanelMatch? @event)
		{
			if (@event == null) return;

			if (!IsStatsAllowed())
				return;

			var players = _plugin._playerStats.Values.Where(p => p.ZenithPlayer.Controller.IsValid && p.ZenithPlayer.Controller.PlayerPawn.Value!.IsValid).ToList();

			if (_plugin._coreAccessor.GetValue<bool>("Config", "FFAMode"))
			{
				var winner = players.OrderByDescending(p => p.ZenithPlayer.Controller.Score).FirstOrDefault();

				if (winner != null)
				{
					winner.IncrementStat("GameWin");
				}

				foreach (var player in players.Where(p => p != winner))
				{
					player.IncrementStat("GameLose");
				}
			}
			else
			{
				int ctScore = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager")
					.Where(team => team.Teamname == "CT")
					.Select(team => team.Score)
					.FirstOrDefault();

				int tScore = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager")
					.Where(team => team.Teamname == "TERRORIST")
					.Select(team => team.Score)
					.FirstOrDefault();

				CsTeam winnerTeam = ctScore > tScore ? CsTeam.CounterTerrorist : tScore > ctScore ? CsTeam.Terrorist : CsTeam.None;

				if (winnerTeam > CsTeam.Spectator)
				{
					foreach (var player in players.Where(p => p.ZenithPlayer.Controller.Team > CsTeam.Spectator))
					{
						if (player.ZenithPlayer.Controller.Team == winnerTeam)
						{
							player.IncrementStat("GameWin");
						}
						else
						{
							player.IncrementStat("GameLose");
						}
					}
				}
			}
		}
	}

	public class PlayerStats
	{
		private readonly Plugin _plugin;
		public IPlayerServices ZenithPlayer { get; }
		public MapStats CurrentMapStats { get; private set; }
		public CounterStrikeSharp.API.Modules.Timers.Timer? SpawnMessageTimer = null;

		private readonly Dictionary<string, DateTime> _lastShotTime = [];
		private const double HIT_COOLDOWN = 0.1;

		private string _steamId;
		private string _currentMapName;

		public PlayerStats(IPlayerServices player, Plugin plugin)
		{
			ZenithPlayer = player;
			_plugin = plugin;
			_steamId = player.SteamID.ToString();
			_currentMapName = Server.MapName;
			CurrentMapStats = new MapStats { MapName = _currentMapName };

			if (_plugin._moduleServices == null)
				return;

			Task.Run(async () =>
			{
				await LoadCurrentMapStats(_plugin._moduleServices);
				await LoadWeaponStats(_plugin._moduleServices);
			});
		}

		private async Task LoadWeaponStats(IModuleServices _moduleServices)
		{
			if (!_plugin._coreAccessor.GetValue<bool>("Config", "EnableWeaponStats"))
				return;

			string connectionString = _moduleServices.GetConnectionString();
			using var connection = new MySqlConnection(connectionString);
			await connection.OpenAsync();

			string query = $@"
				SELECT `weapon`, `kills`, `shots`, `hits`, `headshots`, `chest_hits`, `stomach_hits`, `left_arm_hits`, `right_arm_hits`, `left_leg_hits`, `right_leg_hits`, `neck_hits`, `gear_hits`
				FROM `{_plugin._coreAccessor.GetValue<string>("Database", "TablePrefix")}zenith_weapon_stats`
				WHERE `steam_id` = @SteamId";

			try
			{
				var results = await connection.QueryAsync<dynamic>(query, new { SteamId = _steamId });
				foreach (var result in results)
				{
					WeaponStats[result.weapon] = new WeaponStats
					{
						Weapon = result.weapon,
						Kills = result.kills,
						Shots = result.shots,
						Hits = result.hits,
						Headshots = result.headshots,
						ChestHits = result.chest_hits,
						StomachHits = result.stomach_hits,
						LeftArmHits = result.left_arm_hits,
						RightArmHits = result.right_arm_hits,
						LeftLegHits = result.left_leg_hits,
						RightLegHits = result.right_leg_hits,
						NeckHits = result.neck_hits,
						GearHits = result.gear_hits
					};
				}
			}
			catch (Exception ex)
			{
				_plugin.Logger.LogError($"Error loading weapon stats: {ex.Message}");
			}
		}

		private async Task LoadCurrentMapStats(IModuleServices _moduleServices)
		{
			if (!_plugin._coreAccessor.GetValue<bool>("Config", "EnableMapStats"))
				return;

			string connectionString = _moduleServices.GetConnectionString();

			using var connection = new MySqlConnection(connectionString);
			await connection.OpenAsync();

			string query = $@"
				SELECT * FROM `{_plugin._coreAccessor.GetValue<string>("Database", "TablePrefix")}zenith_map_stats`
				WHERE `steam_id` = @SteamId AND `map_name` = @MapName";

			try
			{
				var result = await connection.QueryFirstOrDefaultAsync<dynamic>(query, new
				{
					SteamId = _steamId,
					MapName = _currentMapName
				});

				if (result != null)
				{
					CurrentMapStats = new MapStats
					{
						SteamId = result.steam_id,
						MapName = result.map_name,
						Kills = result.kills,
						FirstBlood = result.first_blood,
						Deaths = result.deaths,
						Assists = result.assists,
						Shoots = result.shoots,
						HitsTaken = result.hits_taken,
						HitsGiven = result.hits_given,
						Headshots = result.headshots,
						HeadHits = result.head_hits,
						ChestHits = result.chest_hits,
						StomachHits = result.stomach_hits,
						LeftArmHits = result.left_arm_hits,
						RightArmHits = result.right_arm_hits,
						LeftLegHits = result.left_leg_hits,
						RightLegHits = result.right_leg_hits,
						NeckHits = result.neck_hits,
						GearHits = result.gear_hits,
						Grenades = result.grenades,
						MVP = result.mvp,
						RoundWin = result.round_win,
						RoundLose = result.round_lose,
						GameWin = result.game_win,
						GameLose = result.game_lose,
						RoundsOverall = result.rounds_overall,
						RoundsCT = result.rounds_ct,
						RoundsT = result.rounds_t,
						BombPlanted = result.bomb_planted,
						BombDefused = result.bomb_defused,
						HostageRescued = result.hostage_rescued,
						HostageKilled = result.hostage_killed,
						NoScopeKill = result.no_scope_kill,
						PenetratedKill = result.penetrated_kill,
						ThruSmokeKill = result.thru_smoke_kill,
						FlashedKill = result.flashed_kill,
						DominatedKill = result.dominated_kill,
						RevengeKill = result.revenge_kill,
						AssistFlash = result.assist_flash
					};
				}
				else
				{
					CurrentMapStats = new MapStats { SteamId = _steamId, MapName = _currentMapName };
				}
			}
			catch
			{
				CurrentMapStats = new MapStats { SteamId = _steamId, MapName = _currentMapName };
			}
		}

		public Dictionary<string, WeaponStats> WeaponStats { get; private set; } = [];

		public int GetGlobalStat(string statName)
		{
			return ZenithPlayer.GetStorage<int>(statName);
		}

		public void SetGlobalStat(string statName, int value)
		{
			ZenithPlayer.SetStorage(statName, value);
		}

		public int GetMapStat(string statName)
		{
			var property = typeof(MapStats).GetProperty(statName);
			return property != null ? (int)property.GetValue(CurrentMapStats)! : 0;
		}

		public void SetMapStat(string statName, int value)
		{
			var property = typeof(MapStats).GetProperty(statName);
			if (property != null)
			{
				property.SetValue(CurrentMapStats, value);
			}
		}

		public void IncrementStat(string statName)
		{
			SetGlobalStat(statName, GetGlobalStat(statName) + 1);
			SetMapStat(statName, GetMapStat(statName) + 1);
		}

		public void AddWeaponKill(EventPlayerDeath @event)
		{
			string weapon = @event.Weapon;

			if (weapon.Contains("knife") || weapon.Contains("bayonet"))
				weapon = "knife";

			if (new List<string> { "world" }.Contains(weapon))
				return;

			if (!WeaponStats.ContainsKey(weapon))
				WeaponStats[weapon] = new WeaponStats { Weapon = weapon };

			WeaponStats[weapon].Kills++;

			if (@event.Headshot)
				WeaponStats[weapon].Headshots++;
		}

		public void AddWeaponShot(string weapon)
		{
			weapon = NormalizeWeaponName(weapon);

			if (weapon == "world")
				return;

			if (!WeaponStats.ContainsKey(weapon))
			{
				WeaponStats[weapon] = new WeaponStats { Weapon = weapon };
			}
			WeaponStats[weapon].Shots++;
			IncrementStat("Shoots");
		}

		public void AddWeaponHit(string weapon, int hitgroup)
		{
			weapon = NormalizeWeaponName(weapon);

			if (weapon == "world")
				return;

			if (!WeaponStats.ContainsKey(weapon))
			{
				WeaponStats[weapon] = new WeaponStats { Weapon = weapon };
			}

			if (!_lastShotTime.TryGetValue(weapon, out DateTime value) || (DateTime.Now - value).TotalSeconds >= HIT_COOLDOWN)
			{
				WeaponStats[weapon].Hits++;
				IncrementStat("HitsGiven");

				switch ((HitGroup_t)hitgroup)
				{
					case HitGroup_t.HITGROUP_HEAD:
						WeaponStats[weapon].HeadHits++;
						IncrementStat("HeadHits");
						break;
					case HitGroup_t.HITGROUP_CHEST:
						WeaponStats[weapon].ChestHits++;
						IncrementStat("ChestHits");
						break;
					case HitGroup_t.HITGROUP_STOMACH:
						WeaponStats[weapon].StomachHits++;
						IncrementStat("StomachHits");
						break;
					case HitGroup_t.HITGROUP_LEFTARM:
						WeaponStats[weapon].LeftArmHits++;
						IncrementStat("LeftArmHits");
						break;
					case HitGroup_t.HITGROUP_RIGHTARM:
						WeaponStats[weapon].RightArmHits++;
						IncrementStat("RightArmHits");
						break;
					case HitGroup_t.HITGROUP_LEFTLEG:
						WeaponStats[weapon].LeftLegHits++;
						IncrementStat("LeftLegHits");
						break;
					case HitGroup_t.HITGROUP_RIGHTLEG:
						WeaponStats[weapon].RightLegHits++;
						IncrementStat("RightLegHits");
						break;
					case HitGroup_t.HITGROUP_NECK:
						WeaponStats[weapon].NeckHits++;
						IncrementStat("NeckHits");
						break;
					case HitGroup_t.HITGROUP_GEAR:
						WeaponStats[weapon].GearHits++;
						IncrementStat("GearHits");
						break;
				}

				_lastShotTime[weapon] = DateTime.Now;
			}
		}

		private string NormalizeWeaponName(string weapon)
		{
			weapon = weapon.Replace("weapon_", string.Empty);

			if (weapon.Contains("knife") || weapon.Contains("bayonet"))
				return "knife";

			// ? From some event for usp returned hkp2000 and m4a1s as m4a1
			if (new List<string> { "m4a1", "hkp2000", "usp_silencer", "weapon_m4a1_silencer", "weapon_mp7", "weapon_mp5sd", "deagle", "revolver" }.Contains(weapon))
			{
				var activeWeapon = ZenithPlayer.Controller.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
				if (activeWeapon?.AttributeManager.Item.ItemDefinitionIndex == 60)
					return "m4a1-s";

				if (activeWeapon?.AttributeManager.Item.ItemDefinitionIndex == 61)
					return "usp-s";

				if (activeWeapon?.AttributeManager.Item.ItemDefinitionIndex == 23)
					return "mp5-sd";

				if (activeWeapon?.AttributeManager.Item.ItemDefinitionIndex == 64)
					return "revolver";

				if (activeWeapon?.AttributeManager.Item.ItemDefinitionIndex == 63)
					return "cz75-auto";
			}

			return weapon;
		}

		public async Task SaveWeaponStats()
		{
			if (!_plugin._coreAccessor.GetValue<bool>("Config", "EnableWeaponStats"))
				return;

			if (_plugin._moduleServices == null)
				return;

			try
			{
				string connectionString = _plugin._moduleServices?.GetConnectionString()!;
				using var connection = new MySqlConnection(connectionString);
				await connection.OpenAsync();

				foreach (var weaponStat in WeaponStats.Values)
				{
					string query = $@"
					INSERT INTO `{_plugin._coreAccessor.GetValue<string>("Database", "TablePrefix")}zenith_weapon_stats` (`steam_id`, `weapon`, `kills`, `shots`, `hits`, `headshots`, `chest_hits`, `stomach_hits`, `left_arm_hits`, `right_arm_hits`, `left_leg_hits`, `right_leg_hits`, `neck_hits`, `gear_hits`)
					VALUES (@SteamId, @Weapon, @Kills, @Shots, @Hits, @Headshots, @ChestHits, @StomachHits, @LeftArmHits, @RightArmHits, @LeftLegHits, @RightLegHits, @NeckHits, @GearHits)
					ON DUPLICATE KEY UPDATE
						`kills` = @Kills,
						`shots` = @Shots,
						`hits` = @Hits,
						`headshots` = @Headshots,
						`chest_hits` = @ChestHits,
						`stomach_hits` = @StomachHits,
						`left_arm_hits` = @LeftArmHits,
						`right_arm_hits` = @RightArmHits,
						`left_leg_hits` = @LeftLegHits,
						`right_leg_hits` = @RightLegHits,
						`neck_hits` = @NeckHits,
						`gear_hits` = @GearHits";

					await connection.ExecuteAsync(query, new
					{
						SteamId = _steamId,
						weaponStat.Weapon,
						weaponStat.Kills,
						weaponStat.Shots,
						weaponStat.Hits,
						weaponStat.Headshots,
						weaponStat.ChestHits,
						weaponStat.StomachHits,
						weaponStat.LeftArmHits,
						weaponStat.RightArmHits,
						weaponStat.LeftLegHits,
						weaponStat.RightLegHits,
						weaponStat.NeckHits,
						weaponStat.GearHits
					});
				}
			}
			catch (Exception ex)
			{
				_plugin.Logger.LogError($"Error saving weapon stats for {_steamId}: {ex.Message}");
			}

			WeaponStats.Clear();
		}

		public async Task SaveMapStats()
		{
			if (!_plugin._coreAccessor.GetValue<bool>("Config", "EnableMapStats"))
				return;

			if (_plugin._moduleServices == null)
				return;

			try
			{
				string connectionString = _plugin._moduleServices?.GetConnectionString()!;
				using var connection = new MySqlConnection(connectionString);
				await connection.OpenAsync();

				string query = $@"
				INSERT INTO `{_plugin._coreAccessor.GetValue<string>("Database", "TablePrefix")}zenith_map_stats` (
					`steam_id`, `map_name`, `kills`, `first_blood`, `deaths`, `assists`, `shoots`, `hits_taken`, `hits_given`,
					`headshots`, `head_hits`, `chest_hits`, `stomach_hits`, `left_arm_hits`, `right_arm_hits`, `left_leg_hits`, `right_leg_hits`,
					`neck_hits`, `gear_hits`, `grenades`, `mvp`, `round_win`, `round_lose`, `game_win`, `game_lose`,
					`rounds_overall`, `rounds_ct`, `rounds_t`, `bomb_planted`, `bomb_defused`, `hostage_rescued`, `hostage_killed`, `no_scope_kill`,
					`penetrated_kill`, `thru_smoke_kill`, `flashed_kill`, `dominated_kill`, `revenge_kill`, `assist_flash`
				) VALUES (
					@SteamId, @MapName, @Kills, @FirstBlood, @Deaths, @Assists, @Shoots, @HitsTaken, @HitsGiven,
					@Headshots, @HeadHits, @ChestHits, @StomachHits, @LeftArmHits, @RightArmHits, @LeftLegHits, @RightLegHits,
					@NeckHits, @GearHits, @Grenades, @MVP, @RoundWin, @RoundLose, @GameWin, @GameLose,
					@RoundsOverall, @RoundsCT, @RoundsT, @BombPlanted, @BombDefused, @HostageRescued, @HostageKilled, @NoScopeKill,
					@PenetratedKill, @ThruSmokeKill, @FlashedKill, @DominatedKill, @RevengeKill, @AssistFlash
				) ON DUPLICATE KEY UPDATE
					`kills` = @Kills,
					`first_blood` = @FirstBlood,
					`deaths` = @Deaths,
					`assists` = @Assists,
					`shoots` = @Shoots,
					`hits_taken` = @HitsTaken,
					`hits_given` = @HitsGiven,
					`headshots` = @Headshots,
					`head_hits` = @HeadHits,
					`chest_hits` = @ChestHits,
					`stomach_hits` = @StomachHits,
					`left_arm_hits` = @LeftArmHits,
					`right_arm_hits` = @RightArmHits,
					`left_leg_hits` = @LeftLegHits,
					`right_leg_hits` = @RightLegHits,
					`neck_hits` = @NeckHits,
					`gear_hits` = @GearHits,
					`grenades` = @Grenades,
					`mvp` = @MVP,
					`round_win` = @RoundWin,
					`round_lose` = @RoundLose,
					`game_win` = @GameWin,
					`game_lose` = @GameLose,
					`rounds_overall` = @RoundsOverall,
					`rounds_ct` = @RoundsCT,
					`rounds_t` = @RoundsT,
					`bomb_planted` = @BombPlanted,
					`bomb_defused` = @BombDefused,
					`hostage_rescued` = @HostageRescued,
					`hostage_killed` = @HostageKilled,
					`no_scope_kill` = @NoScopeKill,
					`penetrated_kill` = @PenetratedKill,
					`thru_smoke_kill` = @ThruSmokeKill,
					`flashed_kill` = @FlashedKill,
					`dominated_kill` = @DominatedKill,
					`revenge_kill` = @RevengeKill,
					`assist_flash` = @AssistFlash";

				await connection.ExecuteAsync(query, new
				{
					SteamId = _steamId,
					MapName = _currentMapName,
					Kills = GetMapStat("Kills"),
					FirstBlood = GetMapStat("FirstBlood"),
					Deaths = GetMapStat("Deaths"),
					Assists = GetMapStat("Assists"),
					Shoots = GetMapStat("Shoots"),
					HitsTaken = GetMapStat("HitsTaken"),
					HitsGiven = GetMapStat("HitsGiven"),
					Headshots = GetMapStat("Headshots"),
					HeadHits = GetMapStat("HeadHits"),
					ChestHits = GetMapStat("ChestHits"),
					StomachHits = GetMapStat("StomachHits"),
					LeftArmHits = GetMapStat("LeftArmHits"),
					RightArmHits = GetMapStat("RightArmHits"),
					LeftLegHits = GetMapStat("LeftLegHits"),
					RightLegHits = GetMapStat("RightLegHits"),
					NeckHits = GetMapStat("NeckHits"),
					GearHits = GetMapStat("GearHits"),
					Grenades = GetMapStat("Grenades"),
					MVP = GetMapStat("MVP"),
					RoundWin = GetMapStat("RoundWin"),
					RoundLose = GetMapStat("RoundLose"),
					GameWin = GetMapStat("GameWin"),
					GameLose = GetMapStat("GameLose"),
					RoundsOverall = GetMapStat("RoundsOverall"),
					RoundsCT = GetMapStat("RoundsCT"),
					RoundsT = GetMapStat("RoundsT"),
					BombPlanted = GetMapStat("BombPlanted"),
					BombDefused = GetMapStat("BombDefused"),
					HostageRescued = GetMapStat("HostageRescued"),
					HostageKilled = GetMapStat("HostageKilled"),
					NoScopeKill = GetMapStat("NoScopeKill"),
					PenetratedKill = GetMapStat("PenetratedKill"),
					ThruSmokeKill = GetMapStat("ThruSmokeKill"),
					FlashedKill = GetMapStat("FlashedKill"),
					DominatedKill = GetMapStat("DominatedKill"),
					RevengeKill = GetMapStat("RevengeKill"),
					AssistFlash = GetMapStat("AssistFlash")
				});
			}
			catch (Exception ex)
			{
				_plugin.Logger.LogError($"Error saving map stats for {_steamId}: {ex.Message}");
			}
		}

		public void ResetStats()
		{
			foreach (var prop in typeof(PlayerStats).GetProperties())
			{
				if (prop.PropertyType == typeof(int) && prop.Name != nameof(ZenithPlayer))
				{
					prop.SetValue(this, 0);
				}
			}
			WeaponStats.Clear();
			CurrentMapStats = new MapStats { MapName = _currentMapName };
		}
	}


	public class WeaponStats
	{
		public required string Weapon { get; set; }
		public int Kills { get; set; }
		public int Shots { get; set; }
		public int Hits { get; set; }
		public int Headshots { get; set; }
		public int HeadHits { get; set; }
		public int ChestHits { get; set; }
		public int StomachHits { get; set; }
		public int LeftArmHits { get; set; }
		public int RightArmHits { get; set; }
		public int LeftLegHits { get; set; }
		public int RightLegHits { get; set; }
		public int NeckHits { get; set; }
		public int GearHits { get; set; }
	}


	public class MapStats
	{
		[Column("steam_id")]
		public string SteamId { get; set; } = string.Empty;

		[Column("map_name")]
		public string MapName { get; set; } = string.Empty;

		public int Kills { get; set; }

		[Column("first_blood")]
		public int FirstBlood { get; set; }

		public int Deaths { get; set; }
		public int Assists { get; set; }
		public int Shoots { get; set; }

		[Column("hits_taken")]
		public int HitsTaken { get; set; }

		[Column("hits_given")]
		public int HitsGiven { get; set; }

		public int Headshots { get; set; }

		[Column("head_hits")]
		public int HeadHits { get; set; }

		[Column("chest_hits")]
		public int ChestHits { get; set; }

		[Column("stomach_hits")]
		public int StomachHits { get; set; }

		[Column("left_arm_hits")]
		public int LeftArmHits { get; set; }

		[Column("right_arm_hits")]
		public int RightArmHits { get; set; }

		[Column("left_leg_hits")]
		public int LeftLegHits { get; set; }

		[Column("right_leg_hits")]
		public int RightLegHits { get; set; }

		[Column("neck_hits")]
		public int NeckHits { get; set; }

		[Column("unused_hits")]
		public int UnusedHits { get; set; }

		[Column("gear_hits")]
		public int GearHits { get; set; }

		[Column("special_hits")]
		public int SpecialHits { get; set; }

		public int Grenades { get; set; }
		public int MVP { get; set; }

		[Column("round_win")]
		public int RoundWin { get; set; }

		[Column("round_lose")]
		public int RoundLose { get; set; }

		[Column("game_win")]
		public int GameWin { get; set; }

		[Column("game_lose")]
		public int GameLose { get; set; }

		[Column("rounds_overall")]
		public int RoundsOverall { get; set; }

		[Column("rounds_ct")]
		public int RoundsCT { get; set; }

		[Column("rounds_t")]
		public int RoundsT { get; set; }

		[Column("bomb_planted")]
		public int BombPlanted { get; set; }

		[Column("bomb_defused")]
		public int BombDefused { get; set; }

		[Column("hostage_rescued")]
		public int HostageRescued { get; set; }

		[Column("hostage_killed")]
		public int HostageKilled { get; set; }

		[Column("no_scope_kill")]
		public int NoScopeKill { get; set; }

		[Column("penetrated_kill")]
		public int PenetratedKill { get; set; }

		[Column("thru_smoke_kill")]
		public int ThruSmokeKill { get; set; }

		[Column("flashed_kill")]
		public int FlashedKill { get; set; }

		[Column("dominated_kill")]
		public int DominatedKill { get; set; }

		[Column("revenge_kill")]
		public int RevengeKill { get; set; }

		[Column("assist_flash")]
		public int AssistFlash { get; set; }
	}
}
