using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using Menu;
using Microsoft.Extensions.Logging;
using ZenithAPI;

namespace Zenith_Bans;

[MinimumApiVersion(250)]
public sealed partial class Plugin : BasePlugin
{
	private IModuleConfigAccessor _coreAccessor = null!;
	private const string MODULE_ID = "Bans";

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.0";

	private PlayerCapability<IPlayerServices>? _playerServicesCapability;
	private PluginCapability<IModuleServices>? _moduleServicesCapability;

	private IZenithEvents? _zenithEvents;
	private IModuleServices? _moduleServices;

	public KitsuneMenu Menu { get; private set; } = null!;
	private string _serverIp = "all";
	private readonly HttpClient _httpClient = new HttpClient();
	private readonly List<DisconnectedPlayer> _disconnectedPlayers = [];

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

		_moduleServices = _moduleServicesCapability?.Get();
		if (_moduleServices == null)
		{
			Logger.LogError("Failed to get Module-Services API for Zenith.");
			Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
			return;
		}

		AddTimer(hotReload ? 3.0f : 0.01f, () => // ? Sometimes CS2 server use a default port at start, so we need to wait a bit
		{
			int port = ConVar.Find("hostport")!.GetPrimitiveValue<int>();
			Task.Run(async () => await InitializeServerIpAsync(port));
		});

		Menu = new KitsuneMenu(this);

		_coreAccessor = _moduleServices.GetModuleConfigAccessor();

		_moduleServices.RegisterModuleConfig("Config", "ForcePunishmentReasons", "Forcing admin to select a reason while punishing", true);

		_moduleServices.RegisterModuleConfig("Config", "BanReasons", "Reasons to select from while banning", new List<string> { "Cheating", "Abusive behavior", "Spamming", "Griefing", "Other" });
		_moduleServices.RegisterModuleConfig("Config", "BanDurations", "Durations to select from while banning (in minutes, 0 for permanent)", new List<int> { 10, 30, 60, 120, 1440, 10080, 43200, 525600, 0 });

		_moduleServices.RegisterModuleConfig("Config", "MuteReasons", "Reasons to select from while muting", new List<string> { "Abusive language", "Spamming", "Inappropriate content", "Other" });
		_moduleServices.RegisterModuleConfig("Config", "MuteDurations", "Durations to select from while muting (in minutes, 0 for permanent)", new List<int> { 5, 10, 30, 60, 120, 1440, 0 });

		_moduleServices.RegisterModuleConfig("Config", "GagReasons", "Reasons to select from while gagging", new List<string> { "Mic spam", "Abusive voice chat", "Inappropriate sounds", "Other" });
		_moduleServices.RegisterModuleConfig("Config", "GagDurations", "Durations to select from while gagging (in minutes, 0 for permanent)", new List<int> { 5, 10, 30, 60, 120, 1440, 0 });

		_moduleServices.RegisterModuleConfig("Config", "SilenceReasons", "Reasons to select from while silencing", new List<string> { "Abusive communication", "Spamming", "Disruptive behavior", "Other" });
		_moduleServices.RegisterModuleConfig("Config", "SilenceDurations", "Durations to select from while silencing (in minutes, 0 for permanent)", new List<int> { 5, 10, 30, 60, 120, 1440, 0 });

		_moduleServices.RegisterModuleConfig("Config", "WarnReasons", "Reasons to select from while warning", new List<string> { "Rule violation", "Inappropriate behavior", "Disruptive play", "Other" });
		_moduleServices.RegisterModuleConfig("Config", "WarnMax", "Maximum number of warnings before a ban is applied", 3);
		_moduleServices.RegisterModuleConfig("Config", "WarnBanLength", "Duration of the ban in minutes when maximum warnings are reached (0 for permanent)", 1440);

		_moduleServices.RegisterModuleConfig("Config", "KickReasons", "Reasons to select from while kicking", new List<string> { "Rule violation", "Inappropriate behavior", "Disruptive play", "Other" });

		_moduleServices.RegisterModuleConfig("Config", "DisconnectMaxPlayers", "Maximum number of disconnected players to store", 20);

		_moduleServices.RegisterModuleConfig("Config", "GlobalPunishments", "Whether to apply punishments globally on all your servers", false);
		_moduleServices.RegisterModuleConfig("Config", "ConnectAdminInfo", "Whether to show admin info on player connect (With @zenith-admin/admin permission)", true);
		_moduleServices.RegisterModuleConfig("Config", "ShowActivity", "Specifies how admin activity should be relayed to users (1: Show to non-admins, 2: Show admin names to non-admins, 4: Show to admins, 8: Show admin names to admins, 16: Always show admin names to root users (admins are @zenith-admin/admin))", 13, ConfigFlag.Global);
		_moduleServices.RegisterModuleConfig("Config", "ApplyIPBans", "(NOT RECOMMENDED )Whether to apply IP bans along with player bans. Not recommended due to Cloud Gaming, you ban everyone who use that data center.", false);
		_moduleServices.RegisterModuleConfig("Config", "DiscordWebhookUrl", "Discord webhook URL for sending notifications", "", ConfigFlag.Protected);
		_moduleServices.RegisterModuleConfig("Config", "DelayPlayerRemoval", "Delay in seconds before removing a player from the server on kick / ban to show a message about it. (0 - Instantly)", 5);

		_zenithEvents = _moduleServices.GetEventHandler();
		if (_zenithEvents != null)
		{
			_zenithEvents.OnZenithCoreUnload += OnZenithCoreUnload;
		}
		else
		{
			Logger.LogError("Failed to get Zenith event handler.");
		}

		Initialize_Events();
		Initialize_Commands();
		Initialize_Database();

		AddTimer(60.0f, () =>
		{
			Task.Run(async () =>
			{
				await RemoveExpiredPunishmentsAsync();
			});
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
}