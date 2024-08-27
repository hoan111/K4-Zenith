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
using System.Text.Json;

namespace Zenith_Stats;

[MinimumApiVersion(250)]
public class Plugin : BasePlugin
{
	public CCSGameRules? GameRules = null;
	private IModuleConfigAccessor _coreAccessor = null!;
	private const string MODULE_ID = "Stats";

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.0";

	public Menu.KitsuneMenu Menu { get; private set; } = null!;
	public static PlayerCapability<IPlayerServices> Capability_PlayerServices { get; } = new("zenith:player-services");
	public static PluginCapability<IModuleServices> Capability_ModuleServices { get; } = new("zenith:module-services");

	private IZenithEvents? _zenithEvents;
	private EventManager? _eventManager;
	private Dictionary<ulong, PlayerStats> _playerStats = new Dictionary<ulong, PlayerStats>();

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
		IModuleServices? _moduleServices = Capability_ModuleServices.Get();
		if (_moduleServices == null)
		{
			Logger.LogError("Failed to get Module-Services API for Zenith.");
			Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
			return;
		}

		Menu = new Menu.KitsuneMenu(this);

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
		}
		else
		{
			Logger.LogError("Failed to get Zenith event handler.");
		}

		_moduleServices.RegisterModuleCommands(_coreAccessor.GetValue<List<string>>("Config", "StatisticCommands"), "Show the player statistics.", OnStatsCommand, CommandUsage.CLIENT_ONLY);
		_moduleServices.RegisterModuleCommands(_coreAccessor.GetValue<List<string>>("Config", "WeaponStatisticCommands"), "Show the player statistics for weapons.", OnWeaponStatsCommand, CommandUsage.CLIENT_ONLY);
		_moduleServices.RegisterModuleCommands(_coreAccessor.GetValue<List<string>>("Config", "MapStatisticCommands"), "Show the player statistics for maps.", OnMapStatsCommand, CommandUsage.CLIENT_ONLY);

		_moduleServices.RegisterModulePlayerPlaceholder("kda", p => CalculateKDA(GetZenithPlayer(p)));
		_moduleServices.RegisterModulePlayerPlaceholder("kdr", p => CalculateKDR(GetZenithPlayer(p)));
		_moduleServices.RegisterModulePlayerPlaceholder("accuracy", p => CalculateAccuracy(GetZenithPlayer(p)));

		Initialize_Events();
		InitializeDatabaseTables();

		if (hotReload)
			GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules;

		RegisterListener<Listeners.OnMapStart>((mapName) =>
		{
			AddTimer(1.0f, () =>
			{
				GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules;

				foreach (var playerStats in _playerStats.Values)
				{
					Task.Run(async () =>
					{
						await playerStats.SaveWeaponStats();
						await playerStats.SaveMapStats();
						playerStats.ResetStats();
					});
				}
			});
		});

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	public override void Unload(bool hotReload)
	{
		IModuleServices? moduleServices = Capability_ModuleServices.Get();
		if (moduleServices == null)
			return;

		moduleServices.DisposeModule(this.GetType().Assembly);
	}

	private string CalculateKDA(IPlayerServices? player)
	{
		if (player == null) return "N/A";
		var stats = _playerStats.GetValueOrDefault(player.Controller.SteamID);
		if (stats == null) return "N/A";

		double kda = (stats.Kills + stats.Assists) / (double)(stats.Deaths == 0 ? 1 : stats.Deaths);
		return kda.ToString("F2");
	}

	private string CalculateKDR(IPlayerServices? player)
	{
		if (player == null) return "N/A";
		var stats = _playerStats.GetValueOrDefault(player.Controller.SteamID);
		if (stats == null) return "N/A";

		double kdr = stats.Kills / (double)(stats.Deaths == 0 ? 1 : stats.Deaths);
		return kdr.ToString("F2");
	}

	private string CalculateAccuracy(IPlayerServices? player)
	{
		if (player == null) return "N/A";
		var stats = _playerStats.GetValueOrDefault(player.Controller.SteamID);
		if (stats == null) return "N/A";

		double accuracy = (stats.Shoots == 0) ? 0 : stats.HitsGiven / (double)stats.Shoots * 100;
		if (accuracy > 100) // ? This is just to prevent shotguns making them over 100%
		{
			accuracy = 100;
		}

		return accuracy.ToString("F2") + "%";
	}

	private async void InitializeDatabaseTables()
	{
		IModuleServices? _moduleServices = Capability_ModuleServices.Get();
		if (_moduleServices == null)
		{
			Logger.LogError("Failed to get Module-Services API for Zenith.");
			return;
		}

		string createWeaponStatsTable = @"
		CREATE TABLE IF NOT EXISTS zenith_weapon_stats (
			steam_id VARCHAR(32) NOT NULL,
			weapon VARCHAR(64) NOT NULL,
			kills INT NOT NULL DEFAULT 0,
			shots INT NOT NULL DEFAULT 0,
			hits INT NOT NULL DEFAULT 0,
			headshots INT NOT NULL DEFAULT 0,
			PRIMARY KEY (steam_id, weapon)
		)";

		string createMapStatsTable = @"
		CREATE TABLE IF NOT EXISTS zenith_map_stats (
			steam_id VARCHAR(32) NOT NULL,
			map_name VARCHAR(64) NOT NULL,
			kills INT NOT NULL DEFAULT 0,
			first_blood INT NOT NULL DEFAULT 0,
			deaths INT NOT NULL DEFAULT 0,
			assists INT NOT NULL DEFAULT 0,
			shoots INT NOT NULL DEFAULT 0,
			hits_taken INT NOT NULL DEFAULT 0,
			hits_given INT NOT NULL DEFAULT 0,
			headshots INT NOT NULL DEFAULT 0,
			head_hits INT NOT NULL DEFAULT 0,
			chest_hits INT NOT NULL DEFAULT 0,
			stomach_hits INT NOT NULL DEFAULT 0,
			left_arm_hits INT NOT NULL DEFAULT 0,
			right_arm_hits INT NOT NULL DEFAULT 0,
			left_leg_hits INT NOT NULL DEFAULT 0,
			right_leg_hits INT NOT NULL DEFAULT 0,
			neck_hits INT NOT NULL DEFAULT 0,
			unused_hits INT NOT NULL DEFAULT 0,
			gear_hits INT NOT NULL DEFAULT 0,
			special_hits INT NOT NULL DEFAULT 0,
			grenades INT NOT NULL DEFAULT 0,
			mvp INT NOT NULL DEFAULT 0,
			round_win INT NOT NULL DEFAULT 0,
			round_lose INT NOT NULL DEFAULT 0,
			game_win INT NOT NULL DEFAULT 0,
			game_lose INT NOT NULL DEFAULT 0,
			rounds_overall INT NOT NULL DEFAULT 0,
			rounds_ct INT NOT NULL DEFAULT 0,
			rounds_t INT NOT NULL DEFAULT 0,
			bomb_planted INT NOT NULL DEFAULT 0,
			bomb_defused INT NOT NULL DEFAULT 0,
			hostage_rescued INT NOT NULL DEFAULT 0,
			hostage_killed INT NOT NULL DEFAULT 0,
			no_scope_kill INT NOT NULL DEFAULT 0,
			penetrated_kill INT NOT NULL DEFAULT 0,
			thru_smoke_kill INT NOT NULL DEFAULT 0,
			flashed_kill INT NOT NULL DEFAULT 0,
			dominated_kill INT NOT NULL DEFAULT 0,
			revenge_kill INT NOT NULL DEFAULT 0,
			assist_flash INT NOT NULL DEFAULT 0,
			PRIMARY KEY (steam_id, map_name)
		)";

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

	private void OnZenithPlayerLoaded(object? sender, CCSPlayerController player)
	{
		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer != null)
		{
			var stats = new PlayerStats(zenithPlayer, this);
			_playerStats[player.SteamID] = stats;
		}
	}

	private async void OnZenithPlayerUnloaded(object? sender, CCSPlayerController player)
	{
		if (_playerStats.TryGetValue(player.SteamID, out var stats))
		{
			await stats.SaveWeaponStats();
			await stats.SaveMapStats();
			_playerStats.Remove(player.SteamID);
		}
	}

	public IPlayerServices? GetZenithPlayer(CCSPlayerController? player)
	{
		if (player == null) return null;
		try { return Capability_PlayerServices.Get(player); }
		catch { return null; }
	}

	private void OnStatsCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null) return;

		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer == null) return;

		if (_playerStats.TryGetValue(player.SteamID, out var stats))
		{
			List<MenuItem> items = new List<MenuItem>();

			foreach (var property in typeof(PlayerStats).GetProperties())
			{
				// Skip non-statistic properties
				if (property.Name == nameof(PlayerStats.ZenithPlayer) ||
					property.Name == nameof(PlayerStats.WeaponStats) ||
					property.Name == nameof(PlayerStats.CurrentMapStats))
					continue;

				// Only process integer properties
				if (property.PropertyType == typeof(int))
				{
					var value = (int)property.GetValue(stats)!;
					if (value != 0)
					{
						string localizedName = Localizer[$"k4.stats.{property.Name.ToLower()}"];
						items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{localizedName}:</font> {value}")));
					}
				}
			}

			if (items.Count == 0)
			{
				items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.no_stats"]}</font>")));
			}

			Menu?.ShowPaginatedMenu(player, Localizer["k4.stats.title"], items, (buttons, menu, selected) =>
			{
				// No selection handle as all items are just for display
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu"), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}
	}

	private void OnWeaponStatsCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null) return;

		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer == null) return;

		if (!_coreAccessor.GetValue<bool>("Config", "EnableWeaponStats"))
		{
			zenithPlayer.Print(Localizer["k4.stats.weapon-disabled"]);
			return;
		}

		if (_playerStats.TryGetValue(player.SteamID, out var stats))
		{
			List<MenuItem> items = new List<MenuItem>();
			var safeWeaponStats = new Dictionary<string, WeaponStats>(stats.WeaponStats);

			foreach (var weaponStat in safeWeaponStats.Values)
			{
				var safeWeaponStat = new WeaponStats
				{
					Weapon = weaponStat.Weapon,
					Kills = weaponStat.Kills,
					Shots = weaponStat.Shots,
					Hits = weaponStat.Hits,
					Headshots = weaponStat.Headshots
				};

				float accuracy = safeWeaponStat.Shots > 0 ? (float)safeWeaponStat.Hits / safeWeaponStat.Shots * 100 : 0;
				if (accuracy > 100)
				{
					accuracy = 100;
				}

				string localizedKills = Localizer["k4.stats.kills"];
				string localizedAccuracy = Localizer["k4.stats.accuracy"];
				items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{safeWeaponStat.Weapon.ToUpper()}:</font> {localizedKills}: {safeWeaponStat.Kills}, {localizedAccuracy}: {accuracy:F2}%")));
			}

			if (items.Count == 0)
			{
				items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.no_stats"]}</font>")));
			}

			Menu?.ShowPaginatedMenu(player, Localizer["k4.weaponstats.title"], items, (buttons, menu, selected) => { }, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu"), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}
	}

	private void OnMapStatsCommand(CCSPlayerController? player, CommandInfo command)
	{
		try
		{
			if (player == null)
			{
				Logger.LogWarning("OnMapStatsCommand called with null player");
				return;
			}

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
				ShowMapStats(player, stats.CurrentMapStats);
			}
			else
			{
				Logger.LogWarning($"No stats found for player {player.PlayerName}");
				zenithPlayer.Print(Localizer["k4.stats.no_stats"]);
			}
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error in OnMapStatsCommand: {ex.Message}\n{ex.StackTrace}");
		}
	}

	private void ShowMapStats(CCSPlayerController player, MapStats mapStat)
	{
		try
		{
			List<MenuItem> items = new List<MenuItem>();

			foreach (var property in typeof(MapStats).GetProperties())
			{
				if (property.Name == nameof(MapStats.MapName)) continue;

				var value = property.GetValue(mapStat);
				if (value is int intValue && intValue != 0)
				{
					string displayValue = intValue.ToString();
					string localizedName = Localizer[$"k4.stats.{property.Name.ToLower()}"];
					items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{localizedName}:</font> {displayValue}")));
				}
			}

			if (items.Count == 0)
			{
				items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer["k4.stats.no_stats"]}</font>")));
			}

			Menu?.ShowPaginatedMenu(player, mapStat.MapName.ToUpper(), items, (buttons, menu, selected) =>
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
			int notBots = Utilities.GetPlayers().Count(player => !player.IsBot);
			bool warmupStats = _plugin._coreAccessor.GetValue<bool>("Config", "WarmupStats");
			int minPlayers = _plugin._coreAccessor.GetValue<int>("Config", "MinPlayers");

			return _plugin.GameRules != null &&
				   (!_plugin.GameRules.WarmupPeriod || warmupStats) &&
				   (minPlayers <= notBots);
		}

		private void HandlePlayerDeath(EventPlayerDeath? @event)
		{
			if (@event == null) return;

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
					victimStats.Deaths++;
				}
			}

			if (!isAttackerBot && attacker != victim && _plugin._playerStats.TryGetValue(@event.Attacker!.SteamID, out var attackerStats))
			{
				if (statsForBots || !isVictimBot)
				{
					attackerStats.Kills++;
					if (!FirstBlood)
					{
						FirstBlood = true;
						attackerStats.FirstBlood++;
					}
					if (@event.Noscope) attackerStats.NoScopeKill++;
					if (@event.Penetrated > 0) attackerStats.PenetratedKill++;
					if (@event.Thrusmoke) attackerStats.ThruSmokeKill++;
					if (@event.Attackerblind) attackerStats.FlashedKill++;
					if (@event.Dominated > 0) attackerStats.DominatedKill++;
					if (@event.Revenge > 0) attackerStats.RevengeKill++;
					if (@event.Headshot) attackerStats.Headshots++;

					attackerStats.AddWeaponKill(@event.Weapon);
				}
			}

			if (!isAssisterBot && _plugin._playerStats.TryGetValue(@event.Assister!.SteamID, out var assisterStats))
			{
				if (statsForBots || (!isVictimBot && !isAttackerBot))
				{
					assisterStats.Assists++;
					if (@event.Assistedflash) assisterStats.AssistFlash++;
				}
			}
		}

		private void HandleGrenadeThrown(EventGrenadeThrown? @event)
		{
			if (@event == null) return;

			var player = _plugin.GetZenithPlayer(@event.Userid);
			if (player != null && _plugin._playerStats.TryGetValue(player.Controller.SteamID, out var stats))
			{
				stats.Grenades++;
			}
		}

		private void HandlePlayerHurt(EventPlayerHurt? @event)
		{
			if (@event == null) return;

			var victim = _plugin.GetZenithPlayer(@event.Userid);
			var attacker = _plugin.GetZenithPlayer(@event.Attacker);

			if (victim != null && _plugin._playerStats.TryGetValue(victim.Controller.SteamID, out var victimStats))
			{
				victimStats.HitsTaken++;
			}

			if (attacker != null && attacker != victim && _plugin._playerStats.TryGetValue(attacker.Controller.SteamID, out var attackerStats))
			{
				attackerStats.HitsGiven++;

				switch (@event.Hitgroup)
				{
					case (int)HitGroup_t.HITGROUP_HEAD:
						attackerStats.HeadHits++;
						break;
					case (int)HitGroup_t.HITGROUP_CHEST:
						attackerStats.ChestHits++;
						break;
					case (int)HitGroup_t.HITGROUP_STOMACH:
						attackerStats.StomachHits++;
						break;
					case (int)HitGroup_t.HITGROUP_LEFTARM:
						attackerStats.LeftArmHits++;
						break;
					case (int)HitGroup_t.HITGROUP_RIGHTARM:
						attackerStats.RightArmHits++;
						break;
					case (int)HitGroup_t.HITGROUP_LEFTLEG:
						attackerStats.LeftLegHits++;
						break;
					case (int)HitGroup_t.HITGROUP_RIGHTLEG:
						attackerStats.RightLegHits++;
						break;
					case (int)HitGroup_t.HITGROUP_NECK:
						attackerStats.NeckHits++;
						break;
					case (int)HitGroup_t.HITGROUP_GEAR:
						attackerStats.GearHits++;
						break;
				}

				attackerStats.AddWeaponHit(@event.Weapon);
			}
		}

		private void HandleBombPlanted(EventBombPlanted? @event)
		{
			if (@event == null) return;

			var player = _plugin.GetZenithPlayer(@event.Userid);
			if (player != null && _plugin._playerStats.TryGetValue(player.Controller.SteamID, out var stats))
			{
				stats.BombPlanted++;
			}
		}

		private void HandleHostageRescued(EventHostageRescued? @event)
		{
			if (@event == null) return;

			var player = _plugin.GetZenithPlayer(@event.Userid);
			if (player != null && _plugin._playerStats.TryGetValue(player.Controller.SteamID, out var stats))
			{
				stats.HostageRescued++;
			}
		}

		private void HandleHostageKilled(EventHostageKilled? @event)
		{
			if (@event == null) return;

			var player = _plugin.GetZenithPlayer(@event.Userid);
			if (player != null && _plugin._playerStats.TryGetValue(player.Controller.SteamID, out var stats))
			{
				stats.HostageKilled++;
			}
		}

		private void HandleBombDefused(EventBombDefused? @event)
		{
			if (@event == null) return;

			var player = _plugin.GetZenithPlayer(@event.Userid);
			if (player != null && _plugin._playerStats.TryGetValue(player.Controller.SteamID, out var stats))
			{
				stats.BombDefused++;
			}
		}

		private void HandleRoundEnd(EventRoundEnd? @event)
		{
			if (@event == null) return;

			foreach (var playerStats in _plugin._playerStats.Values)
			{
				playerStats.RoundsOverall++;
				if (playerStats.ZenithPlayer.Controller.Team == CsTeam.Terrorist)
					playerStats.RoundsT++;
				else if (playerStats.ZenithPlayer.Controller.Team == CsTeam.CounterTerrorist)
					playerStats.RoundsCT++;

				if (playerStats.ZenithPlayer.Controller.TeamNum == @event.Winner)
					playerStats.RoundWin++;
				else
					playerStats.RoundLose++;
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
					stats.Shoots++;
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
				stats.MVP++;
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
					winner.GameWin++;
				}

				foreach (var player in players.Where(p => p != winner))
				{
					player.GameLose++;
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
							player.GameWin++;
						}
						else
						{
							player.GameLose++;
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

		private string _steamId;
		private string _currentMapName;

		public PlayerStats(IPlayerServices player, Plugin plugin)
		{
			ZenithPlayer = player;
			_plugin = plugin;
			_steamId = player.SteamID.ToString();
			_currentMapName = Server.MapName;
			_plugin.Logger.LogInformation($"Initializing PlayerStats for {_steamId} on map {_currentMapName}");
			CurrentMapStats = new MapStats { MapName = _currentMapName };

			Task.Run(LoadCurrentMapStats);
		}

		private async Task LoadCurrentMapStats()
		{
			if (!_plugin._coreAccessor.GetValue<bool>("Config", "EnableMapStats"))
			{
				_plugin.Logger.LogInformation("Map stats are disabled.");
				return;
			}

			IModuleServices? _moduleServices = Capability_ModuleServices.Get();
			if (_moduleServices == null)
			{
				_plugin.Logger.LogError("Failed to get Module-Services API for Zenith.");
				return;
			}

			string connectionString = _moduleServices.GetConnectionString();

			using var connection = new MySqlConnection(connectionString);
			await connection.OpenAsync();

			string query = @"
			SELECT * FROM zenith_map_stats
			WHERE steam_id = @SteamId AND map_name = @MapName";

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

			UpdatePropertiesFromMapStats(CurrentMapStats);
		}

		private void UpdatePropertiesFromMapStats(MapStats mapStats)
		{
			Kills = mapStats.Kills;
			FirstBlood = mapStats.FirstBlood;
			Deaths = mapStats.Deaths;
			Assists = mapStats.Assists;
			Shoots = mapStats.Shoots;
			HitsTaken = mapStats.HitsTaken;
			HitsGiven = mapStats.HitsGiven;
			Headshots = mapStats.Headshots;
			HeadHits = mapStats.HeadHits;
			ChestHits = mapStats.ChestHits;
			StomachHits = mapStats.StomachHits;
			LeftArmHits = mapStats.LeftArmHits;
			RightArmHits = mapStats.RightArmHits;
			LeftLegHits = mapStats.LeftLegHits;
			RightLegHits = mapStats.RightLegHits;
			NeckHits = mapStats.NeckHits;
			GearHits = mapStats.GearHits;
			Grenades = mapStats.Grenades;
			MVP = mapStats.MVP;
			RoundWin = mapStats.RoundWin;
			RoundLose = mapStats.RoundLose;
			GameWin = mapStats.GameWin;
			GameLose = mapStats.GameLose;
			RoundsOverall = mapStats.RoundsOverall;
			RoundsCT = mapStats.RoundsCT;
			RoundsT = mapStats.RoundsT;
			BombPlanted = mapStats.BombPlanted;
			BombDefused = mapStats.BombDefused;
			HostageRescued = mapStats.HostageRescued;
			HostageKilled = mapStats.HostageKilled;
			NoScopeKill = mapStats.NoScopeKill;
			PenetratedKill = mapStats.PenetratedKill;
			ThruSmokeKill = mapStats.ThruSmokeKill;
			FlashedKill = mapStats.FlashedKill;
			DominatedKill = mapStats.DominatedKill;
			RevengeKill = mapStats.RevengeKill;
			AssistFlash = mapStats.AssistFlash;
		}

		public Dictionary<string, WeaponStats> WeaponStats { get; private set; } = new Dictionary<string, WeaponStats>();

		public int Kills
		{
			get => ZenithPlayer.GetStorage<int>("Kills");
			set
			{
				ZenithPlayer.SetStorage("Kills", value);
				CurrentMapStats.Kills = value;
			}
		}
		public int FirstBlood
		{
			get => ZenithPlayer.GetStorage<int>("FirstBlood");
			set
			{
				ZenithPlayer.SetStorage("FirstBlood", value);
				CurrentMapStats.FirstBlood = value;
			}
		}
		public int Deaths
		{
			get => ZenithPlayer.GetStorage<int>("Deaths");
			set
			{
				ZenithPlayer.SetStorage("Deaths", value);
				CurrentMapStats.Deaths = value;
			}
		}
		public int Assists
		{
			get => ZenithPlayer.GetStorage<int>("Assists");
			set
			{
				ZenithPlayer.SetStorage("Assists", value);
				CurrentMapStats.Assists = value;
			}
		}

		public int Shoots
		{
			get => ZenithPlayer.GetStorage<int>("Shoots");
			set
			{
				ZenithPlayer.SetStorage("Shoots", value);
				CurrentMapStats.Shoots = value;
			}
		}
		public int HitsTaken
		{
			get => ZenithPlayer.GetStorage<int>("HitsTaken");
			set
			{
				ZenithPlayer.SetStorage("HitsTaken", value);
				CurrentMapStats.HitsTaken = value;
			}
		}
		public int HitsGiven
		{
			get => ZenithPlayer.GetStorage<int>("HitsGiven");
			set
			{
				ZenithPlayer.SetStorage("HitsGiven", value);
				CurrentMapStats.HitsGiven = value;
			}
		}
		public int Headshots
		{
			get => ZenithPlayer.GetStorage<int>("Headshots");
			set
			{
				ZenithPlayer.SetStorage("Headshots", value);
				CurrentMapStats.Headshots = value;
			}
		}
		public int HeadHits
		{
			get => ZenithPlayer.GetStorage<int>("HeadHits");
			set
			{
				ZenithPlayer.SetStorage("HeadHits", value);
				CurrentMapStats.HeadHits = value;
			}
		}
		public int ChestHits
		{
			get => ZenithPlayer.GetStorage<int>("ChestHits");
			set
			{
				ZenithPlayer.SetStorage("ChestHits", value);
				CurrentMapStats.ChestHits = value;
			}
		}
		public int StomachHits
		{
			get => ZenithPlayer.GetStorage<int>("StomachHits");
			set
			{
				ZenithPlayer.SetStorage("StomachHits", value);
				CurrentMapStats.StomachHits = value;
			}
		}
		public int LeftArmHits
		{
			get => ZenithPlayer.GetStorage<int>("LeftArmHits");
			set
			{
				ZenithPlayer.SetStorage("LeftArmHits", value);
				CurrentMapStats.LeftArmHits = value;
			}
		}
		public int RightArmHits
		{
			get => ZenithPlayer.GetStorage<int>("RightArmHits");
			set
			{
				ZenithPlayer.SetStorage("RightArmHits", value);
				CurrentMapStats.RightArmHits = value;
			}
		}
		public int LeftLegHits
		{
			get => ZenithPlayer.GetStorage<int>("LeftLegHits");
			set
			{
				ZenithPlayer.SetStorage("LeftLegHits", value);
				CurrentMapStats.LeftLegHits = value;
			}
		}
		public int RightLegHits
		{
			get => ZenithPlayer.GetStorage<int>("RightLegHits");
			set
			{
				ZenithPlayer.SetStorage("RightLegHits", value);
				CurrentMapStats.RightLegHits = value;
			}
		}
		public int NeckHits
		{
			get => ZenithPlayer.GetStorage<int>("NeckHits");
			set
			{
				ZenithPlayer.SetStorage("NeckHits", value);
				CurrentMapStats.NeckHits = value;
			}
		}
		public int GearHits
		{
			get => ZenithPlayer.GetStorage<int>("GearHits");
			set
			{
				ZenithPlayer.SetStorage("GearHits", value);
				CurrentMapStats.GearHits = value;
			}
		}
		public int Grenades
		{
			get => ZenithPlayer.GetStorage<int>("Grenades");
			set
			{
				ZenithPlayer.SetStorage("Grenades", value);
				CurrentMapStats.Grenades = value;
			}
		}
		public int MVP
		{
			get => ZenithPlayer.GetStorage<int>("MVP");
			set
			{
				ZenithPlayer.SetStorage("MVP", value);
				CurrentMapStats.MVP = value;
			}
		}
		public int RoundWin
		{
			get => ZenithPlayer.GetStorage<int>("RoundWin");
			set
			{
				ZenithPlayer.SetStorage("RoundWin", value);
				CurrentMapStats.RoundWin = value;
			}
		}
		public int RoundLose
		{
			get => ZenithPlayer.GetStorage<int>("RoundLose");
			set
			{
				ZenithPlayer.SetStorage("RoundLose", value);
				CurrentMapStats.RoundLose = value;
			}
		}
		public int GameWin
		{
			get => ZenithPlayer.GetStorage<int>("GameWin");
			set
			{
				ZenithPlayer.SetStorage("GameWin", value);
				CurrentMapStats.GameWin = value;
			}
		}
		public int GameLose
		{
			get => ZenithPlayer.GetStorage<int>("GameLose");
			set
			{
				ZenithPlayer.SetStorage("GameLose", value);
				CurrentMapStats.GameLose = value;
			}
		}
		public int RoundsOverall
		{
			get => ZenithPlayer.GetStorage<int>("RoundsOverall");
			set
			{
				ZenithPlayer.SetStorage("RoundsOverall", value);
				CurrentMapStats.RoundsOverall = value;
			}
		}
		public int RoundsCT
		{
			get => ZenithPlayer.GetStorage<int>("RoundsCT");
			set
			{
				ZenithPlayer.SetStorage("RoundsCT", value);
				CurrentMapStats.RoundsCT = value;
			}
		}
		public int RoundsT
		{
			get => ZenithPlayer.GetStorage<int>("RoundsT");
			set
			{
				ZenithPlayer.SetStorage("RoundsT", value);
				CurrentMapStats.RoundsT = value;
			}
		}
		public int BombPlanted
		{
			get => ZenithPlayer.GetStorage<int>("BombPlanted");
			set
			{
				ZenithPlayer.SetStorage("BombPlanted", value);
				CurrentMapStats.BombPlanted = value;
			}
		}
		public int BombDefused
		{
			get => ZenithPlayer.GetStorage<int>("BombDefused");
			set
			{
				ZenithPlayer.SetStorage("BombDefused", value);
				CurrentMapStats.BombDefused = value;
			}
		}
		public int HostageRescued
		{
			get => ZenithPlayer.GetStorage<int>("HostageRescued");
			set
			{
				ZenithPlayer.SetStorage("HostageRescued", value);
				CurrentMapStats.HostageRescued = value;
			}
		}
		public int HostageKilled
		{
			get => ZenithPlayer.GetStorage<int>("HostageKilled");
			set
			{
				ZenithPlayer.SetStorage("HostageKilled", value);
				CurrentMapStats.HostageKilled = value;
			}
		}
		public int NoScopeKill
		{
			get => ZenithPlayer.GetStorage<int>("NoScopeKill");
			set
			{
				ZenithPlayer.SetStorage("NoScopeKill", value);
				CurrentMapStats.NoScopeKill = value;
			}
		}
		public int PenetratedKill
		{
			get => ZenithPlayer.GetStorage<int>("PenetratedKill");
			set
			{
				ZenithPlayer.SetStorage("PenetratedKill", value);
				CurrentMapStats.PenetratedKill = value;
			}
		}
		public int ThruSmokeKill
		{
			get => ZenithPlayer.GetStorage<int>("ThruSmokeKill");
			set
			{
				ZenithPlayer.SetStorage("ThruSmokeKill", value);
				CurrentMapStats.ThruSmokeKill = value;
			}
		}
		public int FlashedKill
		{
			get => ZenithPlayer.GetStorage<int>("FlashedKill");
			set
			{
				ZenithPlayer.SetStorage("FlashedKill", value);
				CurrentMapStats.FlashedKill = value;
			}
		}
		public int DominatedKill
		{
			get => ZenithPlayer.GetStorage<int>("DominatedKill");
			set
			{
				ZenithPlayer.SetStorage("DominatedKill", value);
				CurrentMapStats.DominatedKill = value;
			}
		}
		public int RevengeKill
		{
			get => ZenithPlayer.GetStorage<int>("RevengeKill");
			set
			{
				ZenithPlayer.SetStorage("RevengeKill", value);
				CurrentMapStats.RevengeKill = value;
			}
		}
		public int AssistFlash
		{
			get => ZenithPlayer.GetStorage<int>("AssistFlash");
			set
			{
				ZenithPlayer.SetStorage("AssistFlash", value);
				CurrentMapStats.AssistFlash = value;
			}
		}

		public void AddWeaponKill(string weapon)
		{
			if (!WeaponStats.ContainsKey(weapon))
			{
				WeaponStats[weapon] = new WeaponStats { Weapon = weapon };
			}
			WeaponStats[weapon].Kills++;
		}

		public void AddWeaponShot(string weapon)
		{
			weapon = weapon.Replace("weapon_", string.Empty);

			if (!WeaponStats.ContainsKey(weapon))
			{
				WeaponStats[weapon] = new WeaponStats { Weapon = weapon };
			}
			WeaponStats[weapon].Shots++;
		}

		public void AddWeaponHit(string weapon)
		{
			weapon = weapon.Replace("weapon_", string.Empty);

			if (!WeaponStats.ContainsKey(weapon))
			{
				WeaponStats[weapon] = new WeaponStats { Weapon = weapon };
			}
			WeaponStats[weapon].Hits++;
		}

		public async Task SaveWeaponStats()
		{
			if (!_plugin._coreAccessor.GetValue<bool>("Config", "EnableWeaponStats"))
				return;

			IModuleServices? _moduleServices = Capability_ModuleServices.Get();
			if (_moduleServices == null)
			{
				return;
			}

			string connectionString = _moduleServices?.GetConnectionString()!;
			using var connection = new MySqlConnection(connectionString);
			await connection.OpenAsync();

			foreach (var weaponStat in WeaponStats.Values)
			{
				string query = @"
				INSERT INTO zenith_weapon_stats (steam_id, weapon, kills, shots, hits, headshots)
				VALUES (@SteamId, @Weapon, @Kills, @Shots, @Hits, @Headshots)
				ON DUPLICATE KEY UPDATE
					kills = kills + @Kills,
					shots = shots + @Shots,
					hits = hits + @Hits,
					headshots = headshots + @Headshots";

				await connection.ExecuteAsync(query, new
				{
					SteamId = _steamId,
					weaponStat.Weapon,
					weaponStat.Kills,
					weaponStat.Shots,
					weaponStat.Hits,
					weaponStat.Headshots
				});
			}

			WeaponStats.Clear();
		}

		public async Task SaveMapStats()
		{
			if (!_plugin._coreAccessor.GetValue<bool>("Config", "EnableMapStats"))
				return;

			IModuleServices? _moduleServices = Capability_ModuleServices.Get();
			if (_moduleServices == null)
			{
				return;
			}

			string connectionString = _moduleServices?.GetConnectionString()!;
			using var connection = new MySqlConnection(connectionString);
			await connection.OpenAsync();

			string query = @"
        INSERT INTO zenith_map_stats (steam_id, map_name, kills, first_blood, deaths, assists, shoots, hits_taken, hits_given,
            headshots, head_hits, chest_hits, stomach_hits, left_arm_hits, right_arm_hits, left_leg_hits, right_leg_hits,
            neck_hits, gear_hits, grenades, mvp, round_win, round_lose, game_win, game_lose,
            rounds_overall, rounds_ct, rounds_t, bomb_planted, bomb_defused, hostage_rescued, hostage_killed, no_scope_kill,
            penetrated_kill, thru_smoke_kill, flashed_kill, dominated_kill, revenge_kill, assist_flash)
        VALUES (@SteamId, @MapName, @Kills, @FirstBlood, @Deaths, @Assists, @Shoots, @HitsTaken, @HitsGiven,
            @Headshots, @HeadHits, @ChestHits, @StomachHits, @LeftArmHits, @RightArmHits, @LeftLegHits, @RightLegHits,
            @NeckHits, @GearHits, @Grenades, @MVP, @RoundWin, @RoundLose, @GameWin, @GameLose,
            @RoundsOverall, @RoundsCT, @RoundsT, @BombPlanted, @BombDefused, @HostageRescued, @HostageKilled, @NoScopeKill,
            @PenetratedKill, @ThruSmokeKill, @FlashedKill, @DominatedKill, @RevengeKill, @AssistFlash)
        ON DUPLICATE KEY UPDATE
            kills = kills + @Kills,
            first_blood = first_blood + @FirstBlood,
            deaths = deaths + @Deaths,
            assists = assists + @Assists,
            shoots = shoots + @Shoots,
            hits_taken = hits_taken + @HitsTaken,
            hits_given = hits_given + @HitsGiven,
            headshots = headshots + @Headshots,
            head_hits = head_hits + @HeadHits,
            chest_hits = chest_hits + @ChestHits,
            stomach_hits = stomach_hits + @StomachHits,
            left_arm_hits = left_arm_hits + @LeftArmHits,
            right_arm_hits = right_arm_hits + @RightArmHits,
            left_leg_hits = left_leg_hits + @LeftLegHits,
            right_leg_hits = right_leg_hits + @RightLegHits,
            neck_hits = neck_hits + @NeckHits,
            gear_hits = gear_hits + @GearHits,
            grenades = grenades + @Grenades,
            mvp = mvp + @MVP,
            round_win = round_win + @RoundWin,
            round_lose = round_lose + @RoundLose,
            game_win = game_win + @GameWin,
            game_lose = game_lose + @GameLose,
            rounds_overall = rounds_overall + @RoundsOverall,
            rounds_ct = rounds_ct + @RoundsCT,
            rounds_t = rounds_t + @RoundsT,
            bomb_planted = bomb_planted + @BombPlanted,
            bomb_defused = bomb_defused + @BombDefused,
            hostage_rescued = hostage_rescued + @HostageRescued,
            hostage_killed = hostage_killed + @HostageKilled,
            no_scope_kill = no_scope_kill + @NoScopeKill,
            penetrated_kill = penetrated_kill + @PenetratedKill,
            thru_smoke_kill = thru_smoke_kill + @ThruSmokeKill,
            flashed_kill = flashed_kill + @FlashedKill,
            dominated_kill = dominated_kill + @DominatedKill,
            revenge_kill = revenge_kill + @RevengeKill,
            assist_flash = assist_flash + @AssistFlash";

			await connection.ExecuteAsync(query, new
			{
				SteamId = _steamId,
				MapName = _currentMapName,
				Kills,
				FirstBlood,
				Deaths,
				Assists,
				Shoots,
				HitsTaken,
				HitsGiven,
				Headshots,
				HeadHits,
				ChestHits,
				StomachHits,
				LeftArmHits,
				RightArmHits,
				LeftLegHits,
				RightLegHits,
				NeckHits,
				GearHits,
				Grenades,
				MVP,
				RoundWin,
				RoundLose,
				GameWin,
				GameLose,
				RoundsOverall,
				RoundsCT,
				RoundsT,
				BombPlanted,
				BombDefused,
				HostageRescued,
				HostageKilled,
				NoScopeKill,
				PenetratedKill,
				ThruSmokeKill,
				FlashedKill,
				DominatedKill,
				RevengeKill,
				AssistFlash
			});
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
