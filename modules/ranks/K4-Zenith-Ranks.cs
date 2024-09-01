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
	public override string ModuleVersion => "1.0.0";

	private PlayerCapability<IPlayerServices>? _playerServicesCapability;
	private PluginCapability<IModuleServices>? _moduleServicesCapability;
	private float _lastPlaytimeCheck = 0;

	public CCSGameRules? GameRules = null;
	private IZenithEvents? _zenithEvents;
	private IModuleServices? _moduleServices;
	private readonly List<CCSPlayerController> playerSpawned = [];
	private bool _isGameEnd = false;

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

		RegisterConfigs(_moduleServices);

		_moduleServices.RegisterModuleSettings(new Dictionary<string, object?>
		{
			{ "ShowRankChanges", true },
		}, Localizer);

		_moduleServices.RegisterModuleStorage(new Dictionary<string, object?>
		{
			{ "Points", _configAccessor.GetValue<long>("Settings", "StartPoints") },
			{ "Rank", null }
		});

		_moduleServices.RegisterModulePlayerPlaceholder("rank_color", p =>
		{
			var player = GetZenithPlayer(p);
			if (player == null) return ChatColors.Default.ToString();

			var (determinedRank, _) = DetermineRanks(player.GetStorage<long>("Points"));
			return determinedRank?.ChatColor.ToString() ?? ChatColors.Default.ToString();
		});
		_moduleServices.RegisterModulePlayerPlaceholder("rank", p =>
		{
			var player = GetZenithPlayer(p);
			if (player == null) return "Unranked";

			var (determinedRank, _) = DetermineRanks(player.GetStorage<long>("Points"));
			return determinedRank?.Name ?? "Unranked";
		});
		_moduleServices.RegisterModulePlayerPlaceholder("points", p => GetZenithPlayer(p)?.GetStorage<long>("Points").ToString() ?? "0");

		_moduleServices.RegisterModuleCommands(_configAccessor.GetValue<List<string>>("Commands", "RankCommands"), "Show the rank informations.", OnRankCommand, CommandUsage.CLIENT_ONLY);

		Initialize_Ranks();
		Initialize_Events();

		_zenithEvents = _moduleServices.GetEventHandler();
		if (_zenithEvents != null)
		{
			_zenithEvents.OnZenithCoreUnload += OnZenithCoreUnload;
		}
		else
		{
			Logger.LogError("Failed to get Zenith event handler.");
		}

		if (hotReload)
			GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules;

		AddTimer(3.0f, () =>
		{
			_moduleServices.LoadAllOnlinePlayerData();
		});

		AddTimer(1, () =>
		{
			int interval = _configAccessor.GetValue<int>("Points", "PlaytimeInterval");
			if (interval <= 0) return;

			if (_lastPlaytimeCheck + (interval * 60) > Server.CurrentTime) return;

			foreach (var player in GetValidPlayers())
			{
				ModifyPlayerPoints(player, _configAccessor.GetValue<int>("Points", "PlaytimePoints"), "k4.events.playtime");
			}

			_lastPlaytimeCheck = Server.CurrentTime;
		}, TimerFlags.REPEAT);

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

	public IPlayerServices? GetZenithPlayer(CCSPlayerController? player)
	{
		if (player == null) return null;
		try { return _playerServicesCapability?.Get(player); }
		catch { return null; }
	}
}