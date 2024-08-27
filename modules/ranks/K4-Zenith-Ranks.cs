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

	public static PlayerCapability<IPlayerServices> Capability_PlayerServices { get; } = new("zenith:player-services");
	public static PluginCapability<IModuleServices> Capability_ModuleServices { get; } = new("zenith:module-services");

	public CCSGameRules? GameRules = null;
	private IZenithEvents? _zenithEvents;

	public override void OnAllPluginsLoaded(bool hotReload)
	{
		IModuleServices? moduleServices = Capability_ModuleServices.Get();
		if (moduleServices == null)
		{
			Logger.LogError("Failed to get Module-Services API for Zenith.");
			Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
			return;
		}

		RegisterConfigs(moduleServices);

		moduleServices.RegisterModuleSettings(new Dictionary<string, object?>
		{
			{ "ShowRankChanges", true },
		}, Localizer);

		moduleServices.RegisterModuleStorage(new Dictionary<string, object?>
		{
			{ "Points", _configAccessor.GetValue<long>("Settings", "StartPoints") },
			{ "Rank", null }
		});

		moduleServices.RegisterModulePlayerPlaceholder("rank", p => GetZenithPlayer(p)?.GetStorage<string>("Rank") ?? Localizer["k4.phrases.rank.none"]);
		moduleServices.RegisterModulePlayerPlaceholder("points", p => GetZenithPlayer(p)?.GetStorage<long>("Points").ToString() ?? "0");

		moduleServices.RegisterModuleCommands(_configAccessor.GetValue<List<string>>("Commands", "RankCommands"), "Show the rank informations.", OnRankCommand, CommandUsage.CLIENT_ONLY);

		Initialize_Ranks();
		Initialize_Events();

		_zenithEvents = moduleServices.GetEventHandler();
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
			GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules;

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	public override void Unload(bool hotReload)
	{
		IModuleServices? moduleServices = Capability_ModuleServices.Get();
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
		try { return Capability_PlayerServices.Get(player); }
		catch { return null; }
	}
}