using System.Collections.Concurrent;
using System.Drawing;
using CounterStrikeSharp.API.Core;

namespace Zenith.Models;

public sealed partial class Player
{
	public static readonly ConcurrentBag<Player> List = [];

	public static Player? Find(ulong steamID)
	{
		var player = List.FirstOrDefault(player => player.SteamID == steamID);

		if (player != null && !player.IsValid)
		{
			player.Dispose();
			return null;
		}

		return player;
	}

	public static Player? Find(CCSPlayerController? controller)
	{
		if (controller is null)
			return null;

		var player = List.FirstOrDefault(player => player.Controller == controller);

		if (player != null && !player.IsValid)
		{
			player.Dispose();
			return null;
		}

		return player;
	}
}