
using CounterStrikeSharp.API.Core;
using ZenithAPI;

namespace Zenith_Ranks;

public sealed partial class Plugin : BasePlugin
{
	public IModuleConfigAccessor _configAccessor = null!;

	private void RegisterConfigs(IModuleServices _moduleServices)
	{
		// Register Commands
		_moduleServices.RegisterModuleConfig("Commands", "RankCommands", "Commands to show rank", new List<string> { "rank", "level" });

		// Register Settings
		_moduleServices.RegisterModuleConfig("Settings", "StartPoints", "Starting points for new players", 0);
		_moduleServices.RegisterModuleConfig("Settings", "WarmupPoints", "Allow points during warmup", false);
		_moduleServices.RegisterModuleConfig("Settings", "PointSummaries", "Show point summaries", false);
		_moduleServices.RegisterModuleConfig("Settings", "MinPlayers", "Minimum players for points", 4);
		_moduleServices.RegisterModuleConfig("Settings", "PointsForBots", "Allow points for bot kills", false);
		_moduleServices.RegisterModuleConfig("Settings", "FFAMode", "Free-for-all mode", false);
		_moduleServices.RegisterModuleConfig("Settings", "ScoreboardScoreSync", "Sync scoreboard score", false);
		_moduleServices.RegisterModuleConfig("Settings", "VipMultiplier", "VIP point multiplier", 1.25);
		_moduleServices.RegisterModuleConfig("Settings", "DynamicDeathPoints", "Use dynamic death points", true);
		_moduleServices.RegisterModuleConfig("Settings", "DynamicDeathPointsMaxMultiplier", "Max multiplier for dynamic death points", 3.00);
		_moduleServices.RegisterModuleConfig("Settings", "DynamicDeathPointsMinMultiplier", "Min multiplier for dynamic death points", 0.5);
		_moduleServices.RegisterModuleConfig("Settings", "UseScoreboardRanks", "Use of ranks on scoreboard", true);
		_moduleServices.RegisterModuleConfig("Settings", "ShowRankChanges", "Globally enable or disable rank change center messages", true);
		_moduleServices.RegisterModuleConfig("Settings", "ScoreboardMode", "Scoreboard mode (1 - premier, 2 - competitive, 3 - wingman, 4 - danger zone, 0 - custom)", 1);
		_moduleServices.RegisterModuleConfig("Settings", "RankBase", "Base rank value for custom ranks", 0);
		_moduleServices.RegisterModuleConfig("Settings", "RankMax", "Maximum rank value for custom ranks", 0);
		_moduleServices.RegisterModuleConfig("Settings", "RankMargin", "Rank margin value for custom ranks", 0);
		_moduleServices.RegisterModuleConfig("Settings", "ExtendedDeathMessages", "Use extended death messages including enemy name and points", true);
		_moduleServices.RegisterModuleConfig("Settings", "VIPFlags", "VIP flags for multipliers", new List<string> { "@zenith-ranks/vip" });

		// Register Points
		_moduleServices.RegisterModuleConfig("Points", "Death", "Points for death", -5);
		_moduleServices.RegisterModuleConfig("Points", "Kill", "Points for kill", 8);
		_moduleServices.RegisterModuleConfig("Points", "Headshot", "Extra points for headshot", 5);
		_moduleServices.RegisterModuleConfig("Points", "Penetrated", "Extra points for penetration kill", 3);
		_moduleServices.RegisterModuleConfig("Points", "NoScope", "Extra points for no-scope kill", 15);
		_moduleServices.RegisterModuleConfig("Points", "Thrusmoke", "Extra points for kill through smoke", 15);
		_moduleServices.RegisterModuleConfig("Points", "BlindKill", "Extra points for blind kill", 5);
		_moduleServices.RegisterModuleConfig("Points", "TeamKill", "Points for team kill", -10);
		_moduleServices.RegisterModuleConfig("Points", "Suicide", "Points for suicide", -5);
		_moduleServices.RegisterModuleConfig("Points", "Assist", "Points for assist", 5);
		_moduleServices.RegisterModuleConfig("Points", "AssistFlash", "Points for flash assist", 7);
		_moduleServices.RegisterModuleConfig("Points", "TeamKillAssist", "Points for team kill assist", -4);
		_moduleServices.RegisterModuleConfig("Points", "TeamKillAssistFlash", "Points for team kill flash assist", -2);
		_moduleServices.RegisterModuleConfig("Points", "RoundWin", "Points for round win", 5);
		_moduleServices.RegisterModuleConfig("Points", "RoundLose", "Points for round loss", -2);
		_moduleServices.RegisterModuleConfig("Points", "MVP", "Points for MVP", 10);
		_moduleServices.RegisterModuleConfig("Points", "BombDrop", "Points for dropping the bomb", -2);
		_moduleServices.RegisterModuleConfig("Points", "BombPickup", "Points for picking up the bomb", 2);
		_moduleServices.RegisterModuleConfig("Points", "BombDefused", "Points for defusing the bomb", 10);
		_moduleServices.RegisterModuleConfig("Points", "BombDefusedOthers", "Points for others when bomb is defused", 3);
		_moduleServices.RegisterModuleConfig("Points", "BombPlant", "Points for planting the bomb", 10);
		_moduleServices.RegisterModuleConfig("Points", "BombExploded", "Points for bomb explosion", 10);
		_moduleServices.RegisterModuleConfig("Points", "HostageHurt", "Points for hurting a hostage", -2);
		_moduleServices.RegisterModuleConfig("Points", "HostageKill", "Points for killing a hostage", -20);
		_moduleServices.RegisterModuleConfig("Points", "HostageRescue", "Points for rescuing a hostage", 15);
		_moduleServices.RegisterModuleConfig("Points", "HostageRescueAll", "Extra points for rescuing all hostages", 10);
		_moduleServices.RegisterModuleConfig("Points", "LongDistanceKill", "Extra points for long-distance kill", 8);
		_moduleServices.RegisterModuleConfig("Points", "LongDistance", "Distance for long-distance kill (units)", 30);
		_moduleServices.RegisterModuleConfig("Points", "SecondsBetweenKills", "Seconds between kills for multi-kill bonuses", 0);
		_moduleServices.RegisterModuleConfig("Points", "RoundEndKillStreakReset", "Reset kill streak on round end", true);
		_moduleServices.RegisterModuleConfig("Points", "DoubleKill", "Points for double kill", 5);
		_moduleServices.RegisterModuleConfig("Points", "TripleKill", "Points for triple kill", 10);
		_moduleServices.RegisterModuleConfig("Points", "Domination", "Points for domination (4 kills)", 15);
		_moduleServices.RegisterModuleConfig("Points", "Rampage", "Points for rampage (5 kills)", 20);
		_moduleServices.RegisterModuleConfig("Points", "MegaKill", "Points for mega kill (6 kills)", 25);
		_moduleServices.RegisterModuleConfig("Points", "Ownage", "Points for ownage (7 kills)", 30);
		_moduleServices.RegisterModuleConfig("Points", "UltraKill", "Points for ultra kill (8 kills)", 35);
		_moduleServices.RegisterModuleConfig("Points", "KillingSpree", "Points for killing spree (9 kills)", 40);
		_moduleServices.RegisterModuleConfig("Points", "MonsterKill", "Points for monster kill (10 kills)", 45);
		_moduleServices.RegisterModuleConfig("Points", "Unstoppable", "Points for unstoppable (11 kills)", 50);
		_moduleServices.RegisterModuleConfig("Points", "GodLike", "Points for godlike (12+ kills)", 60);
		_moduleServices.RegisterModuleConfig("Points", "GrenadeKill", "Points for grenade kill", 30);
		_moduleServices.RegisterModuleConfig("Points", "InfernoKill", "Points for inferno (molotov/incendiary) kill", 30);
		_moduleServices.RegisterModuleConfig("Points", "ImpactKill", "Points for impact kill", 100);
		_moduleServices.RegisterModuleConfig("Points", "TaserKill", "Points for taser kill", 20);
		_moduleServices.RegisterModuleConfig("Points", "KnifeKill", "Points for knife kill", 15);
		_moduleServices.RegisterModuleConfig("Points", "PlaytimeInterval", "Interval for playtime points (in minutes), or 0 to disable", 5);
		_moduleServices.RegisterModuleConfig("Points", "PlaytimePoints", "Points for playtime interval", 20);

		// Get the config accessor
		_configAccessor = _moduleServices.GetModuleConfigAccessor();
	}
}