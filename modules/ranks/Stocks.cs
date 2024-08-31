using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using ZenithAPI;

namespace Zenith_Ranks;

public sealed partial class Plugin : BasePlugin
{
	private readonly Dictionary<CCSPlayerController, int> _roundPoints = [];

	public string ApplyPrefixColors(string msg)
	{
		var chatColors = typeof(ChatColors).GetFields()
			.Select(f => new { f.Name, Value = f.GetValue(null)?.ToString() })
			.OrderByDescending(c => c.Name.Length);

		foreach (var color in chatColors)
		{
			if (color.Value != null)
			{
				msg = Regex.Replace(msg, $@"\b{color.Name}\b", color.Value, RegexOptions.IgnoreCase);
			}
		}

		return msg;
	}

	public IEnumerable<IPlayerServices> GetValidPlayers()
	{
		foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
		{
			var zenithPlayer = GetZenithPlayer(player);
			if (zenithPlayer != null)
			{
				yield return zenithPlayer;
			}
		}
	}

	public void ModifyPlayerPoints(IPlayerServices player, int points, string eventKey, string? extraInfo = null)
	{
		if (points == 0)
			return;

		if (_configAccessor.GetValue<List<string>>("Settings", "VIPFlags").Any(f => AdminManager.PlayerHasPermissions(player.Controller, f)) && points > 0)
			points = (int)(points * (decimal)_configAccessor.GetValue<double>("Settings", "VipMultiplier"));

		long currentPoints = player.GetStorage<long>("Points");
		long newPoints = currentPoints + points;

		if (newPoints < 0)
			newPoints = 0;

		player.SetStorage("Points", newPoints);

		if (_configAccessor.GetValue<bool>("Settings", "ScoreboardScoreSync"))
			player.Controller.Score = (int)newPoints;

		UpdatePlayerRank(player, newPoints);

		if (!_configAccessor.GetValue<bool>("Settings", "PointSummaries") && player.GetSetting<bool>("ShowRankChanges"))
		{
			string pointChangePhrase = points >= 0 ? "k4.phrases.gain" : "k4.phrases.loss";
			string eventReason = Localizer[eventKey];
			string message = Localizer[pointChangePhrase, $"{newPoints:N0}", Math.Abs(points), extraInfo ?? eventReason];

			Server.NextFrame(() => player.Print(message));
		}
		else
		{
			if (!_roundPoints.ContainsKey(player.Controller))
				_roundPoints[player.Controller] = 0;

			_roundPoints[player.Controller] += points;
		}
	}

	private void UpdatePlayerRank(IPlayerServices player, long points)
	{
		string? currentRank = player.GetStorage<string>("Rank");
		var (determinedRank, _) = DetermineRanks(points);
		string? newRank = determinedRank?.Name;

		if (newRank != currentRank)
		{
			player.SetStorage("Rank", newRank);

			if (_configAccessor.GetValue<bool>("Settings", "UseChatRanks"))
				player.SetNameTag($"{determinedRank?.ChatColor}[{determinedRank?.Name}] ");

			string messageKey;
			string colorCode;
			if (string.IsNullOrEmpty(currentRank) || CompareRanks(newRank, currentRank) > 0)
			{
				messageKey = "k4.phrases.rankup";
				colorCode = "#00FF00";
			}
			else
			{
				messageKey = "k4.phrases.rankdown";
				colorCode = "#FF0000";
			}

			string rankName = newRank ?? Localizer["k4.phrases.rank.none"];

			string htmlMessage = $@"
			<font color='{colorCode}' class='fontSize-m'>{Localizer[messageKey]}</font><br>
			<font color='#FFFFFF' class='fontSize-m'>{Localizer["k4.phrases.newrank", rankName]}</font>";

			player.PrintToCenter(htmlMessage, _configAccessor.GetValue<int>("Core", "CenterAlertTime"), ActionPriority.Normal);
		}
	}

	private int CompareRanks(string? rank1Name, string? rank2Name)
	{
		if (rank1Name == null && rank2Name == null) return 0;
		if (rank1Name == null) return -1;
		if (rank2Name == null) return 1;

		Rank? rank1 = Ranks.FirstOrDefault(r => r.Name == rank1Name);
		Rank? rank2 = Ranks.FirstOrDefault(r => r.Name == rank2Name);

		if (rank1 == null && rank2 == null) return 0;
		if (rank1 == null) return -1;
		if (rank2 == null) return 1;

		return rank1.Point.CompareTo(rank2.Point);
	}

	private (Rank? CurrentRank, Rank? NextRank) DetermineRanks(long points)
	{
		if (Ranks == null || Ranks.Count == 0)
			return (null, null);

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

		if (!attacker.IsPlayer || !victim.IsPlayer)
			return basePoints;

		long attackerPoints = attacker.GetStorage<long>("Points");
		long victimPoints = victim.GetStorage<long>("Points");

		if (attackerPoints <= 0 || victimPoints <= 0)
			return basePoints;

		double pointsRatio = Math.Clamp(victimPoints / attackerPoints, _configAccessor.GetValue<double>("Settings", "DynamicDeathPointsMinMultiplier"), _configAccessor.GetValue<double>("Settings", "DynamicDeathPointsMaxMultiplier"));
		double result = pointsRatio * basePoints;
		return (int)Math.Round(result);
	}
}