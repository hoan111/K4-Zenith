using CounterStrikeSharp.API.Core;
using MySqlConnector;

namespace Zenith_Bans
{
	public sealed partial class Plugin : BasePlugin
	{
		public class PlayerDataRaw
		{
			public ulong SteamId { get; set; }
			public string Name { get; set; } = "";
			public string IpAddresses { get; set; } = "[]";
			public DateTime LastOnline { get; set; }
			public string GroupsJson { get; set; } = "[]";
			public string PermissionsJson { get; set; } = "[]";
			public int? Immunity { get; set; }
			public MySqlDateTime? RankExpiry { get; set; }
			public string? GroupPermissions { get; set; }
		}

		public class PlayerData
		{
			public ulong SteamId { get; set; }
			public string Name { get; set; } = "";
			public string IpAddress { get; set; } = "";
			public List<string> Groups { get; set; } = [];
			public List<string> Permissions { get; set; } = [];
			public int? Immunity { get; set; }
			public MySqlDateTime? RankExpiry { get; set; }
			public List<Punishment> Punishments { get; set; } = [];
		}

		public class Punishment
		{
			public int Id { get; set; }
			public PunishmentType Type { get; set; }
			public int? Duration { get; set; }
			public MySqlDateTime? ExpiresAt { get; set; } = null;
			public string PunisherName { get; set; } = "Console";
			public ulong? AdminSteamId { get; set; }
			public string Reason { get; set; } = "";
		}

		public enum PunishmentType
		{
			Mute,
			Gag,
			Silence,
			Ban,
			Warn,
			Kick
		}

		public enum TargetFailureReason
		{
			TargetNotFound,
			TargetImmunity
		}

		public class DisconnectedPlayer
		{
			public ulong SteamId { get; set; }
			public required string PlayerName { get; set; }
			public DateTime DisconnectedAt { get; set; }
		}
	}
}