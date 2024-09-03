using System.Collections.Concurrent;
using CounterStrikeSharp.API.Core;

namespace Zenith.Models;

public sealed partial class Player
{
	public static readonly ConcurrentBag<Player> List = [];

	public static Player? Find(ulong steamID)
	{
		var player = List.FirstOrDefault(player => player.SteamID == steamID);

		if (player?.IsValid == false)
		{
			player.Dispose();
			return null;
		}

		return player;
	}

	public static Player? Find(CCSPlayerController? controller)
	{
		if (controller == null) return null;

		var player = List.FirstOrDefault(player => player.Controller == controller);

		if (player?.IsValid == false)
		{
			player.Dispose();
			return null;
		}

		return player;
	}
}
