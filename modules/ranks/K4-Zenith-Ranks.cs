using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using ZenithAPI;

namespace Zenith_Ranks;

[MinimumApiVersion(250)]
public sealed partial class Plugin : BasePlugin
{
	private const string MODULE_ID = "Ranks";

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.0";

	private static PlayerCapability<IPlayerServices>? _playerServicesCapability;
	private static PluginCapability<IModuleServices>? _moduleServicesCapability;

	public CCSGameRules? GameRules = null;
	private IZenithEvents? _zenithEvents;
	private IModuleServices? _moduleServices;
	private readonly List<CCSPlayerController> playerSpawned = [];

	public override void OnAllPluginsLoaded(bool hotReload)
	{
		try
		{
			_playerServicesCapability = new PlayerCapability<IPlayerServices>("zenith:player-services");
			_moduleServicesCapability = new PluginCapability<IModuleServices>("zenith:module-services");
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

		_moduleServices.RegisterModulePlayerPlaceholder("rank", p => GetZenithPlayer(p)?.GetStorage<string>("Rank") ?? Localizer["k4.phrases.rank.none"]);
		_moduleServices.RegisterModulePlayerPlaceholder("points", p => GetZenithPlayer(p)?.GetStorage<long>("Points").ToString() ?? "0");

		_moduleServices.RegisterModuleCommands(_configAccessor.GetValue<List<string>>("Commands", "RankCommands"), "Show the rank informations.", OnRankCommand, CommandUsage.CLIENT_ONLY);

		Initialize_Ranks();
		Initialize_Events();

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

		if (hotReload)
		{
			GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules;

			if (_configAccessor.GetValue<bool>("Settings", "UseChatRanks"))
			{
				_moduleServices.LoadAllOnlinePlayerData();
				AddTimer(3.0f, () =>
				{
					Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).ToList().ForEach(player =>
					{
						IPlayerServices? playerServices = GetZenithPlayer(player);
						if (playerServices == null) return;

						var (determinedRank, _) = DetermineRanks(playerServices.GetStorage<int>("Points"));
						playerServices.SetStorage("Rank", determinedRank?.Name);

						playerServices.SetNameTag($"{determinedRank?.ChatColor}[{determinedRank?.Name}] ");
					});
				});
			}
		}

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	public override void Unload(bool hotReload)
	{
		IModuleServices? moduleServices = _moduleServicesCapability?.Get();
		if (moduleServices == null)
			return;

		moduleServices.DisposeModule(this.GetType().Assembly);
	}

	private void OnZenithPlayerLoaded(object? sender, CCSPlayerController player)
	{
		if (_configAccessor.GetValue<bool>("Settings", "UseChatRanks"))
		{
			IPlayerServices? playerServices = GetZenithPlayer(player);
			if (playerServices == null) return;

			var (determinedRank, _) = DetermineRanks(playerServices.GetStorage<int>("Points"));
			playerServices.SetStorage("Rank", determinedRank?.Name);

			playerServices.SetNameTag($"{determinedRank?.ChatColor}[{determinedRank?.Name}] ");
		}
	}

	private void OnZenithPlayerUnloaded(object? sender, CCSPlayerController player)
	{
		// Do anything if needed
	}

	public IPlayerServices? GetZenithPlayer(CCSPlayerController? player)
	{
		if (player == null) return null;
		try { return _playerServicesCapability?.Get(player); }
		catch { return null; }
	}
}