
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using Microsoft.Extensions.Logging;
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

	private void ProcessTargetAction(CCSPlayerController? player, CommandInfo info, Func<IPlayerServices, long?, (string message, string logMessage)> action, bool requireAmount = true)
	{
		TargetResult targets = info.GetArgTargetResult(1);
		if (!targets.Any())
		{
			_moduleServices?.PrintForPlayer(player, Localizer["k4.phrases.no-target"]);
			return;
		}

		long? amount = null;
		if (requireAmount)
		{
			if (!int.TryParse(info.GetArg(2), out int parsedAmount) || parsedAmount <= 0)
			{
				_moduleServices?.PrintForPlayer(player, Localizer["k4.phrases.invalid-amount"]);
				return;
			}
			amount = parsedAmount;
		}

		foreach (var target in targets)
		{
			if (_playerCache.TryGetValue(target, out var zenithPlayer))
			{
				var (message, logMessage) = action(zenithPlayer, amount);
				if (player != null)
					_moduleServices?.PrintForPlayer(target, message);

				Logger.LogWarning(logMessage,
					player?.PlayerName ?? "CONSOLE", player?.SteamID ?? 0,
					target.PlayerName, target.SteamID, amount ?? 0);
			}
			else
			{
				_moduleServices?.PrintForPlayer(player, Localizer["k4.phrases.cant-target", target.PlayerName]);
			}
		}
	}

	public void OnGivePoints(CCSPlayerController? player, CommandInfo info)
	{
		ProcessTargetAction(player, info,
			(zenithPlayer, amount) =>
			{
				long newAmount = zenithPlayer.GetStorage<long>("Points") + amount!.Value;
				zenithPlayer.SetStorage("Points", newAmount);

				var playerData = GetOrUpdatePlayerRankInfo(zenithPlayer);
				playerData.Points = newAmount;
				playerData.LastUpdate = DateTime.Now;
				UpdatePlayerRank(zenithPlayer, playerData, newAmount);

				return (
					Localizer["k4.phrases.points-given", player?.PlayerName ?? "CONSOLE", amount],
					"{0} ({1}) gave {2} ({3}) {4} rank points."
				);
			}
		);
	}

	public void OnTakePoints(CCSPlayerController? player, CommandInfo info)
	{
		ProcessTargetAction(player, info,
			(zenithPlayer, amount) =>
			{
				long newAmount = zenithPlayer.GetStorage<long>("Points") - amount!.Value;
				zenithPlayer.SetStorage("Points", newAmount, true);

				var playerData = GetOrUpdatePlayerRankInfo(zenithPlayer);
				playerData.Points = newAmount;
				playerData.LastUpdate = DateTime.Now;
				UpdatePlayerRank(zenithPlayer, playerData, newAmount);

				return (
					Localizer["k4.phrases.points-taken", player?.PlayerName ?? "CONSOLE", amount],
					"{0} ({1}) taken {4} rank points from {2} ({3})."
				);
			}
		);
	}

	public void OnSetPoints(CCSPlayerController? player, CommandInfo info)
	{
		ProcessTargetAction(player, info,
			(zenithPlayer, amount) =>
			{
				zenithPlayer.SetStorage("Points", amount!.Value, true);

				var playerData = GetOrUpdatePlayerRankInfo(zenithPlayer);
				playerData.Points = amount!.Value;
				playerData.LastUpdate = DateTime.Now;
				UpdatePlayerRank(zenithPlayer, playerData, amount!.Value);

				return (
					Localizer["k4.phrases.points-set", player?.PlayerName ?? "CONSOLE", amount],
					"{0} ({1}) set {2} ({3}) rank points to {4}."
				);
			}
		);
	}

	public void OnResetPoints(CCSPlayerController? player, CommandInfo info)
	{
		ProcessTargetAction(player, info,
			(zenithPlayer, _) =>
			{
				long startPoints = _configAccessor.GetValue<long>("Settings", "StartPoints");
				zenithPlayer.SetStorage("Points", startPoints, true);

				var playerData = GetOrUpdatePlayerRankInfo(zenithPlayer);
				playerData.Points = startPoints;
				playerData.LastUpdate = DateTime.Now;
				UpdatePlayerRank(zenithPlayer, playerData, startPoints);

				return (
					Localizer["k4.phrases.points-reset", player?.PlayerName ?? "CONSOLE"],
					"{0} ({1}) reset {2} ({3}) rank points to {4}."
				);
			},
			requireAmount: false
		);
	}
}