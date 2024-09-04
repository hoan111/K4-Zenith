using System.Collections.Concurrent;
using CounterStrikeSharp.API.Core;

namespace Zenith.Models;

public sealed partial class Player
{
	public static ConcurrentDictionary<ulong, Player> List { get; } = new ConcurrentDictionary<ulong, Player>();

	public static Player? Find(ulong steamID)
	{
		if (List.TryGetValue(steamID, out var player))
		{
			if (!player.IsValid)
			{
				player.Dispose();
				return null;
			}
			return player;
		}
		return null;
	}

	public static Player? Find(CCSPlayerController? controller)
	{
		if (controller == null) return null;

		var player = List.Values.FirstOrDefault(p => p.Controller == controller);

		if (player != null && !player.IsValid)
		{
			player.Dispose();
			return null;
		}

		return player;
	}

	public static void AddToList(Player player)
	{
		List[player.SteamID] = player;
	}

	public static void RemoveFromList(Player playerToRemove)
	{
		List.TryRemove(playerToRemove.SteamID, out _);
	}
}
