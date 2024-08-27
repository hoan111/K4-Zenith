
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using ZenithAPI;

namespace Zenith_Ranks;

public sealed partial class Plugin : BasePlugin
{
	public void OnRankCommand(CCSPlayerController? player, CommandInfo info)
	{
		IPlayerServices? playerServices = GetZenithPlayer(player!);

		if (playerServices == null)
		{
			info.ReplyToCommand($" {Localizer["k4.general.prefix"]} {Localizer["k4.general.loading"]}");
			return;
		}

		long points = playerServices.GetStorage<long>("Points");
		var (currentRank, nextRank) = DetermineRanks(points);

		long pointsToNextRank = nextRank != null ? nextRank.Point - points : 0;

		string htmlMessage = $@"
		<font color='#ff3333' class='fontSize-m'>{Localizer["k4.ranks.info.title"]}</font><br>
		<font color='#FF6666' class='fontSize-sm'>{Localizer["k4.ranks.info.current"]}</font> <font color='{currentRank?.HexColor ?? "#FFFFFF"}' class='fontSize-s'>{currentRank?.Name ?? Localizer["k4.phrases.rank.none"]}</font><br>
		<font color='#FF6666' class='fontSize-sm'>{Localizer["k4.ranks.info.points"]}</font> <font color='#FFFFFF' class='fontSize-s'>{points}</font>";

		if (nextRank != null)
		{
			htmlMessage += $@"
			<br><font color='#FF6666' class='fontSize-sm'>{Localizer["k4.ranks.info.next"]}</font> <font color='{nextRank.HexColor}' class='fontSize-s'>{nextRank.Name}</font><br>
			<font color='#FF6666' class='fontSize-sm'>{Localizer["k4.ranks.info.pointstonext"]}</font> <font color='#FFFFFF' class='fontSize-s'>{pointsToNextRank}</font>";
		}

		playerServices.PrintToCenter(htmlMessage, 10, ActionPriority.Low);
	}
}