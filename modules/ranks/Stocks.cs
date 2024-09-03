using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using ZenithAPI;

namespace Zenith_Ranks;

public sealed partial class Plugin : BasePlugin
{
	private readonly Dictionary<CCSPlayerController, int> _roundPoints = new();

	private static readonly Dictionary<string, string> _chatColors = typeof(ChatColors).GetFields()
		.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
		.Select(g => g.First())
		.ToDictionary(f => f.Name, f => f.GetValue(null)?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

	private static readonly Regex _colorRegex = new(@"\b(?<color>\w+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	public string ApplyPrefixColors(string msg)
	{
		return _colorRegex.Replace(msg, match =>
		{
			string colorKey = match.Groups["color"].Value;
			return _chatColors.TryGetValue(colorKey, out string? colorValue) ? colorValue : match.Value;
		});
	}

	public IEnumerable<IPlayerServices> GetValidPlayers()
	{
		foreach (var player in Utilities.GetPlayers())
		{
			if (player.IsValid && !player.IsBot && !player.IsHLTV)
			{
				var zenithPlayer = GetZenithPlayer(player);
				if (zenithPlayer != null)
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
		var (determinedRank, _) = DetermineRanks(currentPoints + points);

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
            <font color='{determinedRank?.HexColor}' class='fontSize-m'>{Localizer["k4.phrases.newrank", $"{rankName}"]}</font>";

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
		if (!_configAccessor.GetValue<bool>("Settings", "DynamicDeathPoints") || !attacker.IsPlayer || !victim.IsPlayer)
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
}
