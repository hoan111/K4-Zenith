using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.UserMessages;
using ZenithAPI;

namespace Zenith_Ranks;

public sealed partial class Plugin : BasePlugin
{
	private readonly Dictionary<CCSPlayerController, int> _roundPoints = new();

	public IEnumerable<IPlayerServices> GetValidPlayers()
	{
		foreach (var player in Utilities.GetPlayers())
		{
			if (player != null)
			{
				if (_playerCache.TryGetValue(player, out var zenithPlayer))
				{
					yield return zenithPlayer;
				}
			}
		}
	}

	public void ModifyPlayerPoints(IPlayerServices player, int points, string eventKey, string? extraInfo = null)
	{
		if (points == 0) return;

		long currentPoints = player.GetStorage<long>("Points");
		long newPoints = Math.Max(0, currentPoints + points);

		if (points > 0 && _configAccessor.GetValue<List<string>>("Settings", "VIPFlags").Any(f => AdminManager.PlayerHasPermissions(player.Controller, f)))
		{
			points = (int)(points * (decimal)_configAccessor.GetValue<double>("Settings", "VipMultiplier"));
		}

		player.SetStorage("Points", newPoints);

		if (_configAccessor.GetValue<bool>("Settings", "ScoreboardScoreSync"))
		{
			player.Controller.Score = (int)newPoints;
		}

		UpdatePlayerRank(player, newPoints);

		if (_configAccessor.GetValue<bool>("Settings", "PointSummaries") || !player.GetSetting<bool>("ShowRankChanges"))
		{
			_roundPoints[player.Controller] = _roundPoints.TryGetValue(player.Controller, out int existingPoints) ? existingPoints + points : points;
		}
		else
		{
			string message = Localizer[points >= 0 ? "k4.phrases.gain" : "k4.phrases.loss", $"{newPoints:N0}", Math.Abs(points), extraInfo ?? Localizer[eventKey]];
			Server.NextFrame(() => player.Print(message));
		}
	}

	private void UpdatePlayerRank(IPlayerServices player, long points)
	{
		long currentPoints = player.GetStorage<long>("Points");

		var (currentRank, _) = DetermineRanks(currentPoints);
		var (determinedRank, _) = DetermineRanks(points);

		if (determinedRank?.Id != currentRank?.Id)
		{
			string newRankName = determinedRank?.Name ?? Localizer["k4.phrases.rank.none"];
			player.SetStorage("Rank", newRankName);

			bool isRankUp = currentRank is null || CompareRanks(determinedRank, currentRank) > 0;
			string messageKey = isRankUp ? "k4.phrases.rankup" : "k4.phrases.rankdown";
			string colorCode = isRankUp ? "#00FF00" : "#FF0000";
			string rankName = newRankName;

			string htmlMessage = $@"
            <font color='{colorCode}' class='fontSize-m'>{Localizer[messageKey]}</font><br>
            <font color='{determinedRank?.HexColor}' class='fontSize-m'>{Localizer["k4.phrases.newrank", rankName]}</font>";

			player.PrintToCenter(htmlMessage, _configAccessor.GetValue<int>("Core", "CenterAlertTime"), ActionPriority.Normal);
		}
	}

	private static int CompareRanks(Rank? rank1, Rank? rank2)
	{
		if (rank1 == rank2) return 0;

		if (rank1 == null) return rank2 == null ? 0 : -1;
		if (rank2 == null) return 1;

		return rank1.Point.CompareTo(rank2.Point);
	}

	private (Rank? CurrentRank, Rank? NextRank) DetermineRanks(long points)
	{
		Rank? currentRank = null;
		Rank? nextRank = null;

		foreach (var rank in Ranks)
		{
			if (points >= rank.Point)
			{
				currentRank = rank;
			}
			else
			{
				nextRank = rank;
				break;
			}
		}

		return (currentRank, nextRank);
	}

	public int CalculateDynamicPoints(IPlayerServices attacker, IPlayerServices victim, int basePoints)
	{
		if (!_configAccessor.GetValue<bool>("Settings", "DynamicDeathPoints"))
			return basePoints;

		long attackerPoints = attacker.GetStorage<long>("Points");
		long victimPoints = victim.GetStorage<long>("Points");

		if (attackerPoints <= 0 || victimPoints <= 0)
			return basePoints;

		double minMultiplier = _configAccessor.GetValue<double>("Settings", "DynamicDeathPointsMinMultiplier");
		double maxMultiplier = _configAccessor.GetValue<double>("Settings", "DynamicDeathPointsMaxMultiplier");

		double pointsRatio = Math.Clamp(victimPoints / (double)attackerPoints, minMultiplier, maxMultiplier);
		return (int)Math.Round(pointsRatio * basePoints);
	}

	private readonly Dictionary<string, object> settingTickCache = new Dictionary<string, object>();
	private readonly Dictionary<ulong, (int rankId, long points, DateTime lastUpdate)> playerRankCache = [];
	private DateTime lastCacheUpdate = DateTime.MinValue;
	private readonly TimeSpan cacheDuration = TimeSpan.FromSeconds(3);

	public UserMessage? message;
	private void UpdateScoreboards()
	{
		message ??= UserMessage.FromId(350);
		message.Recipients.AddAllPlayers();
		message.Send();

		if ((DateTime.Now - lastCacheUpdate) >= cacheDuration)
		{
			UpdateSettingCache();
			lastCacheUpdate = DateTime.Now;
		}

		if (!settingTickCache.TryGetValue("UseScoreboardRanks", out var useScoreboardRanks) || !(bool)useScoreboardRanks)
			return;

		int mode = (int)settingTickCache["ScoreboardMode"];
		int rankMax = (int)settingTickCache["RankMax"];
		int rankBase = (int)settingTickCache["RankBase"];
		int rankMargin = (int)settingTickCache["RankMargin"];

		foreach (var player in GetValidPlayers())
		{
			ulong steamID = player.SteamID;
			if (playerRankCache.TryGetValue(steamID, out var cachedData) && (DateTime.Now - cachedData.lastUpdate) < cacheDuration)
			{
				SetCompetitiveRank(player, mode, cachedData.rankId, cachedData.points, rankMax, rankBase, rankMargin);
			}
			else
			{
				long currentPoints = player.GetStorage<long>("Points");
				var (determinedRank, _) = DetermineRanks(currentPoints);
				int rankId = determinedRank?.Id ?? 0;

				playerRankCache[steamID] = (rankId, currentPoints, DateTime.Now);

				SetCompetitiveRank(player, mode, rankId, currentPoints, rankMax, rankBase, rankMargin);
			}
		}
	}

	private void UpdateSettingCache()
	{
		settingTickCache["UseScoreboardRanks"] = _configAccessor.GetValue<bool>("Settings", "UseScoreboardRanks");
		settingTickCache["ScoreboardMode"] = _configAccessor.GetValue<int>("Settings", "ScoreboardMode");
		settingTickCache["RankMax"] = _configAccessor.GetValue<int>("Settings", "RankMax");
		settingTickCache["RankBase"] = _configAccessor.GetValue<int>("Settings", "RankBase");
		settingTickCache["RankMargin"] = _configAccessor.GetValue<int>("Settings", "RankMargin");
	}
}
