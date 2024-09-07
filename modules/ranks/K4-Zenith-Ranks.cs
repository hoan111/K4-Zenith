using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using ZenithAPI;

namespace Zenith_Ranks;

[MinimumApiVersion(260)]
public sealed partial class Plugin : BasePlugin
{
	private const string MODULE_ID = "Ranks";

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.3";

	private PlayerCapability<IPlayerServices>? _playerServicesCapability;
	private PluginCapability<IModuleServices>? _moduleServicesCapability;
	private DateTime _lastPlaytimeCheck = DateTime.UtcNow;

	public CCSGameRules? GameRules { get; private set; }
	private IZenithEvents? _zenithEvents;
	private IModuleServices? _moduleServices;
	private readonly HashSet<CCSPlayerController> _playerSpawned = [];
	private readonly Dictionary<CCSPlayerController, IPlayerServices> _playerCache = [];
	private bool _isGameEnd;

	public override void OnAllPluginsLoaded(bool hotReload)
	{
		if (!InitializeZenithAPI())
			return;

		RegisterConfigs(_moduleServices!);
		RegisterModuleSettings();
		RegisterModuleStorage();
		RegisterPlaceholders();
		RegisterCommands();

		Initialize_Ranks();
		Initialize_Events();

		SetupZenithEvents();
		SetupGameRules(hotReload);

		if (hotReload)
		{
			_moduleServices!.LoadAllOnlinePlayerData(); var players = Utilities.GetPlayers();
			foreach (var player in players)
			{
				if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
					OnZenithPlayerLoaded(player);
			}
		}

		AddTimer(60.0f, CheckPlaytime, TimerFlags.REPEAT);

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	private bool InitializeZenithAPI()
	{
		try
		{
			_playerServicesCapability = new("zenith:player-services");
			_moduleServicesCapability = new("zenith:module-services");
			_moduleServices = _moduleServicesCapability.Get();

			if (_moduleServices == null)
				throw new InvalidOperationException("Failed to get Module-Services API for Zenith.");

			return true;
		}
		catch (Exception ex)
		{
			Logger.LogError($"Failed to initialize Zenith API: {ex.Message}");
			Logger.LogInformation("Please check if Zenith is installed, configured and loaded correctly.");
			UnloadPlugin();
			return false;
		}
	}

	private void RegisterModuleSettings()
	{
		_moduleServices!.RegisterModuleSettings(new Dictionary<string, object?>
		{
			{ "ShowRankChanges", true },
		}, Localizer);
	}

	private void RegisterModuleStorage()
	{
		_moduleServices!.RegisterModuleStorage(new Dictionary<string, object?>
		{
			{ "Points", _configAccessor.GetValue<long>("Settings", "StartPoints") },
			{ "Rank", null }
		});
	}

	private void RegisterPlaceholders()
	{
		_moduleServices!.RegisterModulePlayerPlaceholder("rank_color", GetRankColor);
		_moduleServices.RegisterModulePlayerPlaceholder("rank", GetRankName);
		_moduleServices.RegisterModulePlayerPlaceholder("points", GetPlayerPoints);
	}

	private void RegisterCommands()
	{
		_moduleServices!.RegisterModuleCommands(_configAccessor.GetValue<List<string>>("Commands", "RankCommands"), "Show the rank informations.", OnRankCommand, CommandUsage.CLIENT_ONLY);
	}

	private void SetupZenithEvents()
	{
		_zenithEvents = _moduleServices!.GetEventHandler();
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
	}

	private void SetupGameRules(bool hotReload)
	{
		if (hotReload)
			GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
	}

	private void CheckPlaytime()
	{
		int interval = _configAccessor.GetValue<int>("Points", "PlaytimeInterval");
		if (interval <= 0) return;

		if ((DateTime.UtcNow - _lastPlaytimeCheck).TotalMinutes >= interval)
		{
			int playtimePoints = _configAccessor.GetValue<int>("Points", "PlaytimePoints");
			foreach (var player in GetValidPlayers())
			{
				ModifyPlayerPoints(player, playtimePoints, "k4.events.playtime");
			}
			_lastPlaytimeCheck = DateTime.UtcNow;
		}
	}

	private void OnZenithPlayerLoaded(CCSPlayerController player)
	{
		var handler = GetZenithPlayer(player);
		if (handler == null)
		{
			Logger.LogError($"Failed to get player services for {player.PlayerName}");
			return;
		}

		_playerCache[player] = handler;
		_playerSpawned.Add(player);
	}

	private void OnZenithPlayerUnloaded(CCSPlayerController player)
	{
		_playerCache.Remove(player);
		_playerSpawned.Remove(player);
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

	public override void Unload(bool hotReload)
	{
		_moduleServicesCapability?.Get()?.DisposeModule(this.GetType().Assembly);
	}

	public IPlayerServices? GetZenithPlayer(CCSPlayerController? player)
	{
		if (player == null) return null;
		try { return _playerServicesCapability?.Get(player); }
		catch { return null; }
	}

	private void UnloadPlugin()
	{
		Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
	}

	private string GetRankColor(CCSPlayerController p)
	{
		if (_playerCache.TryGetValue(p, out var player))
		{
			var (determinedRank, _) = DetermineRanks(player.GetStorage<long>("Points"));
			return determinedRank?.ChatColor.ToString() ?? ChatColors.Default.ToString();
		}

		return ChatColors.Default.ToString();
	}

	private string GetRankName(CCSPlayerController p)
	{
		if (_playerCache.TryGetValue(p, out var player))
		{
			var (determinedRank, _) = DetermineRanks(player.GetStorage<long>("Points"));
			return determinedRank?.Name ?? Localizer["k4.phrases.rank.none"];
		}

		return Localizer["k4.phrases.rank.none"];
	}

	private string GetPlayerPoints(CCSPlayerController p)
	{
		if (_playerCache.TryGetValue(p, out var player))
			return player.GetStorage<long>("Points").ToString();

		return "0";
	}
}
