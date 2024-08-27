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

	public static PlayerCapability<IPlayerServices> Capability_PlayerServices { get; } = new("zenith:player-services");
	public static PluginCapability<IModuleServices> Capability_ModuleServices { get; } = new("zenith:module-services");

	private IZenithEvents? _zenithEvents;

	private Dictionary<ulong, PlayerTimeData> _playerTimes = new Dictionary<ulong, PlayerTimeData>();

	public override void OnAllPluginsLoaded(bool hotReload)
	{
		IModuleServices? moduleServices = Capability_ModuleServices.Get();
		if (moduleServices == null)
		{
			Logger.LogError("Failed to get Module-Services API for Zenith.");
			Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
			return;
		}

		_coreAccessor = moduleServices.GetModuleConfigAccessor();

		moduleServices.RegisterModuleConfig("Config", "PlaytimeCommands", "List of commands that shows player time statistics", new List<string> { "playtime", "mytime" });
		moduleServices.RegisterModuleConfig("Config", "NotificationInterval", "Interval in seconds between playtime notifications", 300);

		moduleServices.RegisterModuleSettings(new Dictionary<string, object?>
		{
			{ "ShowPlaytime", true }
		}, Localizer);

		moduleServices.RegisterModuleStorage(new Dictionary<string, object?>
		{
			{ "TotalPlaytime", 0L },
			{ "TerroristPlaytime", 0L },
			{ "CounterTerroristPlaytime", 0L },
			{ "SpectatorPlaytime", 0L },
			{ "AlivePlaytime", 0L },
			{ "DeadPlaytime", 0L },
			{ "LastNotification", 0L }
		});

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

		RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
		RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
		RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);

		AddTimer(10.0f, OnTimerElapsed, TimerFlags.REPEAT);

		moduleServices.RegisterModuleCommands(_coreAccessor.GetValue<List<string>>("Config", "PlaytimeCommands"), "Show the playtime informations.", OnPlaytimeCommand, CommandUsage.CLIENT_ONLY);

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	private void OnZenithPlayerLoaded(object? sender, CCSPlayerController player)
	{
		_playerTimes[player.SteamID] = new PlayerTimeData
		{
			LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
			CurrentTeam = CsTeam.Spectator,
			IsAlive = false
		};
	}

	private void OnZenithPlayerUnloaded(object? sender, CCSPlayerController player)
	{
		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer is null) return;

		UpdatePlaytime(zenithPlayer);
		_playerTimes.Remove(player.SteamID);
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
		long sessionDuration = currentTime - timeData.LastUpdateTime;

		long totalPlaytime = playerServices.GetStorage<long>("TotalPlaytime");
		totalPlaytime += sessionDuration;
		playerServices.SetStorage("TotalPlaytime", totalPlaytime);

		string teamKey = timeData.CurrentTeam switch
		{
			CsTeam.Terrorist => "TerroristPlaytime",
			CsTeam.CounterTerrorist => "CounterTerroristPlaytime",
			_ => "SpectatorPlaytime"
		};
		long teamPlaytime = playerServices.GetStorage<long>(teamKey);
		teamPlaytime += sessionDuration;
		playerServices.SetStorage(teamKey, teamPlaytime);

		string lifeStatusKey = timeData.IsAlive ? "AlivePlaytime" : "DeadPlaytime";
		long lifeStatusPlaytime = playerServices.GetStorage<long>(lifeStatusKey);
		lifeStatusPlaytime += sessionDuration;
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
		long totalPlaytime = playerServices.GetStorage<long>("TotalPlaytime");
		TimeSpan playtime = TimeSpan.FromSeconds(totalPlaytime);

		string message = Localizer["timestats.notification",
			playtime.Days,
			playtime.Hours,
			playtime.Minutes];

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
		long totalPlaytime = playerServices.GetStorage<long>("TotalPlaytime");
		long terroristPlaytime = playerServices.GetStorage<long>("TerroristPlaytime");
		long ctPlaytime = playerServices.GetStorage<long>("CounterTerroristPlaytime");
		long spectatorPlaytime = playerServices.GetStorage<long>("SpectatorPlaytime");
		long alivePlaytime = playerServices.GetStorage<long>("AlivePlaytime");
		long deadPlaytime = playerServices.GetStorage<long>("DeadPlaytime");

		string htmlMessage = $@"
		<font color='#ff3333' class='fontSize-m'>{Localizer["timestats.center.title"]}</font><br>
		<font color='#FF6666' class='fontSize-sm'>{Localizer["timestats.center.total.label"]}</font> <font color='#FFFFFF' class='fontSize-s'>{FormatTime(totalPlaytime)}</font><br>
		<font color='#FF6666' class='fontSize-sm'>{Localizer["timestats.center.teams.label"]}</font> <font color='#FFFFFF' class='fontSize-s'>{Localizer["timestats.center.teams.value", FormatTime(terroristPlaytime), FormatTime(ctPlaytime)]}</font><br>
		<font color='#FF6666' class='fontSize-sm'>{Localizer["timestats.center.spectator.label"]}</font> <font color='#FFFFFF' class='fontSize-s'>{FormatTime(spectatorPlaytime)}</font><br>
		<font color='#FF6666' class='fontSize-sm'>{Localizer["timestats.center.status.label"]}</font> <font color='#FFFFFF' class='fontSize-s'>{Localizer["timestats.center.status.value", FormatTime(alivePlaytime), FormatTime(deadPlaytime)]}</font>";

		playerServices.PrintToCenter(htmlMessage, _coreAccessor.GetValue<int>("Core", "CenterMessageTime"), ActionPriority.Low);
	}

	private string FormatTime(long seconds)
	{
		TimeSpan time = TimeSpan.FromSeconds(seconds);
		return Localizer["timestats.time.format", time.Days, time.Hours, time.Minutes];
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

		IModuleServices? moduleServices = Capability_ModuleServices.Get();
		if (moduleServices == null)
			return;

		moduleServices.DisposeModule(this.GetType().Assembly);
	}

	public IPlayerServices? GetZenithPlayer(CCSPlayerController? player)
	{
		if (player == null) return null;
		try { return Capability_PlayerServices.Get(player); }
		catch { return null; }
	}
}

public class PlayerTimeData
{
	public long LastUpdateTime { get; set; }
	public CsTeam CurrentTeam { get; set; }
	public bool IsAlive { get; set; }
}
