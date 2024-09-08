
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using ZenithAPI;

namespace Zenith_Ranks;

public sealed partial class Plugin : BasePlugin
{
	public void OnRankCommand(CCSPlayerController? player, CommandInfo info)
	{
		if (!_playerCache.TryGetValue(player!, out var playerServices))
		{
			info.ReplyToCommand($" {Localizer["k4.general.prefix"]} {Localizer["k4.general.loading"]}");
			return;
		}

		var playerData = GetOrUpdatePlayerRankInfo(playerServices);

		long pointsToNextRank = playerData.NextRank != null ? playerData.NextRank.Point - playerData.Points : 0;

		string htmlMessage = $@"
		<font color='#ff3333' class='fontSize-m'>{Localizer["k4.ranks.info.title"]}</font><br>
		<font color='#FF6666' class='fontSize-sm'>{Localizer["k4.ranks.info.current"]}</font> <font color='{playerData.Rank?.HexColor ?? "#FFFFFF"}' class='fontSize-s'>{playerData.Rank?.Name ?? Localizer["k4.phrases.rank.none"]}</font><br>
		<font color='#FF6666' class='fontSize-sm'>{Localizer["k4.ranks.info.points"]}</font> <font color='#FFFFFF' class='fontSize-s'>{playerData.Points:N0}</font>";

		if (playerData.NextRank != null)
		{
			htmlMessage += $@"
			<br><font color='#FF6666' class='fontSize-sm'>{Localizer["k4.ranks.info.next"]}</font> <font color='{playerData.NextRank.HexColor}' class='fontSize-s'>{playerData.NextRank.Name}</font><br>
			<font color='#FF6666' class='fontSize-sm'>{Localizer["k4.ranks.info.pointstonext"]}</font> <font color='#FFFFFF' class='fontSize-s'>{pointsToNextRank:N0}</font>";
		}

		playerServices.PrintToCenter(htmlMessage, _configAccessor.GetValue<int>("Core", "CenterMessageTime"), ActionPriority.Low);
	}
}