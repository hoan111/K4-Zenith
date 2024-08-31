using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using ZenithAPI;
using CounterStrikeSharp.API.Modules.Timers;

namespace Zenith_TimeStats;

[MinimumApiVersion(250)]
public class Plugin : BasePlugin
{
	private IModuleConfigAccessor _coreAccessor = null!;
	private const string MODULE_ID = "TimeStats";

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.0";

	private PlayerCapability<IPlayerServices>? _playerServicesCapability;
	private PluginCapability<IModuleServices>? _moduleServicesCapability;

	private IZenithEvents? _zenithEvents;
	private IModuleServices? _moduleServices;

	private Dictionary<ulong, PlayerTimeData> _playerTimes = new Dictionary<ulong, PlayerTimeData>();

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

		_coreAccessor = _moduleServices.GetModuleConfigAccessor();

		_moduleServices.RegisterModuleConfig("Config", "PlaytimeCommands", "List of commands that shows player time statistics", new List<string> { "playtime", "mytime" });
		_moduleServices.RegisterModuleConfig("Config", "NotificationInterval", "Interval in seconds between playtime notifications", 300);

		_moduleServices.RegisterModuleSettings(new Dictionary<string, object?>
		{
			{ "ShowPlaytime", true }
		}, Localizer);

		_moduleServices.RegisterModuleStorage(new Dictionary<string, object?>
		{
			{ "TotalPlaytime", 0.0 },
			{ "TerroristPlaytime", 0.0 },
			{ "CounterTerroristPlaytime", 0.0 },
			{ "SpectatorPlaytime", 0.0 },
			{ "AlivePlaytime", 0.0 },
			{ "DeadPlaytime", 0.0 },
			{ "LastNotification", 0L }
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

		RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
		RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
		RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);

		AddTimer(10.0f, OnTimerElapsed, TimerFlags.REPEAT);

		_moduleServices.RegisterModuleCommands(_coreAccessor.GetValue<List<string>>("Config", "PlaytimeCommands"), "Show the playtime informations.", OnPlaytimeCommand, CommandUsage.CLIENT_ONLY);

		AddTimer(3.0f, () =>
		{
			_moduleServices.LoadAllOnlinePlayerData();
			Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).ToList().ForEach(player =>
			{
				_playerTimes[player.SteamID] = new PlayerTimeData
				{
					LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
					CurrentTeam = player.Team,
					IsAlive = player.PlayerPawn.Value?.Health > 0
				};
			});
		});

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	private void OnZenithPlayerLoaded(CCSPlayerController player)
	{
		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer is null) return;

		_playerTimes[zenithPlayer.SteamID] = new PlayerTimeData
		{
			LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
			CurrentTeam = CsTeam.Spectator,
			IsAlive = false
		};
	}

	private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
	{
		var player = GetZenithPlayer(@event.Userid);
		if (player is null) return HookResult.Continue;

		UpdatePlaytime(player);
		if (_playerTimes.TryGetValue(player.SteamID, out var timeData))
		{
			timeData.IsAlive = true;
		}
		return HookResult.Continue;
	}

	private void OnZenithPlayerUnloaded(CCSPlayerController player)
	{
		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer is null) return;

		UpdatePlaytime(zenithPlayer);
		_playerTimes.Remove(zenithPlayer.SteamID);
	}

	private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
	{
		var player = GetZenithPlayer(@event.Userid);
		if (player is null) return HookResult.Continue;

		UpdatePlaytime(player);
		if (_playerTimes.TryGetValue(player.SteamID, out var timeData))
		{
			timeData.IsAlive = false;
		}
		return HookResult.Continue;
	}

	private HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
	{
		var player = GetZenithPlayer(@event.Userid);
		if (player is null) return HookResult.Continue;

		UpdatePlaytime(player);
		if (_playerTimes.TryGetValue(player.SteamID, out var timeData))
		{
			timeData.CurrentTeam = (CsTeam)@event.Team;
			timeData.IsAlive = @event.Team != (int)CsTeam.Spectator;
		}
		return HookResult.Continue;
	}

	private void OnTimerElapsed()
	{
		foreach (var player in Utilities.GetPlayers())
		{
			var playerServices = GetZenithPlayer(player);
			if (playerServices is null)
				continue;

			UpdatePlaytime(playerServices);
			CheckAndSendNotification(playerServices);
		}
	}

	private void UpdatePlaytime(IPlayerServices playerServices)
	{
		if (!_playerTimes.TryGetValue(playerServices.SteamID, out var timeData))
			return;

		long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		double sessionDurationMinutes = Math.Round((currentTime - timeData.LastUpdateTime) / 60.0, 1);

		double totalPlaytime = playerServices.GetStorage<double>("TotalPlaytime");
		totalPlaytime += sessionDurationMinutes;
		playerServices.SetStorage("TotalPlaytime", totalPlaytime);

		string teamKey = timeData.CurrentTeam switch
		{
			CsTeam.Terrorist => "TerroristPlaytime",
			CsTeam.CounterTerrorist => "CounterTerroristPlaytime",
			_ => "SpectatorPlaytime"
		};
		double teamPlaytime = playerServices.GetStorage<double>(teamKey);
		teamPlaytime += sessionDurationMinutes;
		playerServices.SetStorage(teamKey, teamPlaytime);

		string lifeStatusKey = timeData.IsAlive ? "AlivePlaytime" : "DeadPlaytime";
		double lifeStatusPlaytime = playerServices.GetStorage<double>(lifeStatusKey);
		lifeStatusPlaytime += sessionDurationMinutes;
		playerServices.SetStorage(lifeStatusKey, lifeStatusPlaytime);

		timeData.LastUpdateTime = currentTime;
	}

	private void CheckAndSendNotification(IPlayerServices playerServices)
	{
		int interval = _coreAccessor.GetValue<int>("Config", "NotificationInterval");
		if (interval <= 0)
			return;

		bool showPlaytime = playerServices.GetSetting<bool>("ShowPlaytime");
		if (!showPlaytime) return;

		long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		long lastNotification = playerServices.GetStorage<long>("LastNotification");

		if (currentTime - lastNotification >= interval)
		{
			SendPlaytimeNotification(playerServices);
			playerServices.SetStorage("LastNotification", currentTime);
		}
	}

	private void SendPlaytimeNotification(IPlayerServices playerServices)
	{
		double totalPlaytime = playerServices.GetStorage<double>("TotalPlaytime");
		string formattedTime = FormatTime(totalPlaytime);

		string message = Localizer["timestats.notification", formattedTime];

		playerServices.Print(message);
	}

	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void OnPlaytimeCommand(CCSPlayerController? player, CommandInfo command)
	{
		var playerServices = GetZenithPlayer(player);
		if (playerServices is null) return;

		UpdatePlaytime(playerServices);
		SendDetailedPlaytimeStats(playerServices);
	}

	private void SendDetailedPlaytimeStats(IPlayerServices playerServices)
	{
		double totalPlaytime = playerServices.GetStorage<double>("TotalPlaytime");
		double terroristPlaytime = playerServices.GetStorage<double>("TerroristPlaytime");
		double ctPlaytime = playerServices.GetStorage<double>("CounterTerroristPlaytime");
		double spectatorPlaytime = playerServices.GetStorage<double>("SpectatorPlaytime");
		double alivePlaytime = playerServices.GetStorage<double>("AlivePlaytime");
		double deadPlaytime = playerServices.GetStorage<double>("DeadPlaytime");

		string htmlMessage = $@"
		<font color='#ff3333' class='fontSize-m'>{Localizer["timestats.center.title"]}</font><br>
		<font color='#FF6666' class='fontSize-sm'>{Localizer["timestats.center.total.label"]}</font> <font color='#FFFFFF' class='fontSize-s'>{FormatTime(totalPlaytime)}</font><br>
		<font color='#FF6666' class='fontSize-sm'>{Localizer["timestats.center.teams.label"]}</font> <font color='#FFFFFF' class='fontSize-s'>{Localizer["timestats.center.teams.value", FormatTime(terroristPlaytime), FormatTime(ctPlaytime)]}</font><br>
		<font color='#FF6666' class='fontSize-sm'>{Localizer["timestats.center.spectator.label"]}</font> <font color='#FFFFFF' class='fontSize-s'>{FormatTime(spectatorPlaytime)}</font><br>
		<font color='#FF6666' class='fontSize-sm'>{Localizer["timestats.center.status.label"]}</font> <font color='#FFFFFF' class='fontSize-s'>{Localizer["timestats.center.status.value", FormatTime(alivePlaytime), FormatTime(deadPlaytime)]}</font>";

		playerServices.PrintToCenter(htmlMessage, _coreAccessor.GetValue<int>("Core", "CenterMessageTime"), ActionPriority.Low);
	}

	private string FormatTime(double minutes)
	{
		int totalMinutes = (int)Math.Floor(minutes);
		int days = totalMinutes / 1440;
		int hours = (totalMinutes % 1440) / 60;
		int mins = totalMinutes % 60;

		return Localizer["timestats.time.format", days, hours, mins];
	}

	public override void Unload(bool hotReload)
	{
		foreach (var player in Utilities.GetPlayers())
		{
			var playerServices = GetZenithPlayer(player);
			if (playerServices is null) continue;

			UpdatePlaytime(playerServices);
		}
		_playerTimes.Clear();

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

public class PlayerTimeData
{
	public long LastUpdateTime { get; set; }
	public CsTeam CurrentTeam { get; set; }
	public bool IsAlive { get; set; }
}
