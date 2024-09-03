using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Zenith_Bans
{
	public sealed partial class Plugin : BasePlugin
	{
		private readonly Dictionary<ulong, PlayerData> _playerCache = [];

		private void Initialize_Database()
		{
			string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");

			using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
			connection.Open();

			// Create players table
			connection.Execute($@"
				CREATE TABLE IF NOT EXISTS `{prefix}zenith_bans_players` (
					`id` INT AUTO_INCREMENT PRIMARY KEY,
					`steam_id` BIGINT UNSIGNED UNIQUE,
					`name` VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci,
					`current_server` VARCHAR(50),
					`ip_addresses` JSON,
					`last_online` DATETIME
				) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

			// Create player_ranks table
			connection.Execute($@"
				CREATE TABLE IF NOT EXISTS `{prefix}zenith_bans_player_ranks` (
					`id` INT AUTO_INCREMENT PRIMARY KEY,
					`steam_id` BIGINT UNSIGNED,
					`server_ip` VARCHAR(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
					`groups` JSON,
					`permissions` JSON,
					`immunity` INT,
					`rank_expiry` DATETIME,
					UNIQUE KEY `unique_player_server` (`steam_id`, `server_ip`),
					FOREIGN KEY (`steam_id`) REFERENCES `{prefix}zenith_bans_players`(`steam_id`)
				) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

			// Create admin_groups table
			connection.Execute($@"
				CREATE TABLE IF NOT EXISTS `{prefix}zenith_bans_admin_groups` (
					`id` INT AUTO_INCREMENT PRIMARY KEY,
					`name` VARCHAR(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci UNIQUE,
					`permissions` JSON,
					`immunity` INT
				) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

			// Create punishments table
			connection.Execute($@"
				CREATE TABLE IF NOT EXISTS `{prefix}zenith_bans_punishments` (
					`id` INT AUTO_INCREMENT PRIMARY KEY,
					`status` ENUM('active', 'expired', 'removed', 'removed_console') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'active',
					`steam_id` BIGINT UNSIGNED,
					`type` ENUM('mute', 'gag', 'silence', 'ban', 'warn', 'kick') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci,
					`duration` INT,
					`created_at` DATETIME,
					`expires_at` DATETIME,
					`admin_steam_id` BIGINT UNSIGNED NULL,
					`removed_at` DATETIME,
					`remove_admin_steam_id` BIGINT UNSIGNED NULL,
					`server_ip` VARCHAR(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'all',
					`reason` TEXT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci,
					FOREIGN KEY (`steam_id`) REFERENCES `{prefix}zenith_bans_players`(`steam_id`),
					FOREIGN KEY (`admin_steam_id`) REFERENCES `{prefix}zenith_bans_players`(`steam_id`),
					FOREIGN KEY (`remove_admin_steam_id`) REFERENCES `{prefix}zenith_bans_players`(`steam_id`)
				) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
		}

		private async Task<PlayerData> LoadOrUpdatePlayerDataAsync(ulong steamId, string playerName, string ipAddress)
		{
			try
			{
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");

				// First query: Insert or update player data
				var updateQuery = $@"
					INSERT INTO `{prefix}zenith_bans_players` (`steam_id`, `name`, `ip_addresses`, `last_online`, `current_server`)
					VALUES (@SteamId, @PlayerName, JSON_ARRAY(@IpAddress), NOW(), @ServerIp)
					ON DUPLICATE KEY UPDATE
						`name` = @PlayerName,
						`ip_addresses` =
							CASE
								WHEN `ip_addresses` IS NULL THEN JSON_ARRAY(@IpAddress)
								WHEN JSON_CONTAINS(`ip_addresses`, JSON_QUOTE(@IpAddress)) = 0
								THEN JSON_ARRAY_APPEND(`ip_addresses`, '$', @IpAddress)
								ELSE `ip_addresses`
							END,
						`last_online` = NOW(),
						`current_server` = @ServerIp;";

				await connection.ExecuteAsync(updateQuery, new { SteamId = steamId, PlayerName = playerName, IpAddress = ipAddress, ServerIp = _serverIp });

				// Second query: Select player data with ranks
				var selectPlayerQuery = $@"
					SELECT p.*,
						pr.`groups` AS GroupsJson,
						pr.`permissions` AS PermissionsJson,
						pr.`immunity`,
						pr.`rank_expiry` AS RankExpiry,
						ag.`permissions` AS GroupPermissions
					FROM `{prefix}zenith_bans_players` p
					LEFT JOIN `{prefix}zenith_bans_player_ranks` pr ON p.`steam_id` = pr.`steam_id`
						AND (pr.`server_ip` = @ServerIp OR pr.`server_ip` = 'all')
					LEFT JOIN `{prefix}zenith_bans_admin_groups` ag ON JSON_CONTAINS(COALESCE(pr.`groups`, '[]'), CONCAT('""', ag.`name`, '""'), '$')
					WHERE p.`steam_id` = @SteamId
					ORDER BY CASE
						WHEN pr.`server_ip` = @ServerIp THEN 0
						WHEN pr.`server_ip` = 'all' THEN 1
						ELSE 2 END
					LIMIT 1;";

				var playerDataRaw = await connection.QuerySingleOrDefaultAsync<PlayerDataRaw>(selectPlayerQuery, new { SteamId = steamId, ServerIp = _serverIp });

				// Third query: Select punishments
				var selectPunishmentsQuery = $@"
					SELECT
						pun.`id`,
						pun.`type`,
						pun.`duration`,
						pun.`expires_at` AS ExpiresAt,
						COALESCE(admin.`name`, 'Console') AS PunisherName,
						pun.`admin_steam_id` AS AdminSteamId,
						pun.`reason` AS Reason
					FROM `{prefix}zenith_bans_punishments` pun
					LEFT JOIN `{prefix}zenith_bans_players` admin ON pun.`admin_steam_id` = admin.`steam_id`
					WHERE pun.`steam_id` = @SteamId
					AND (pun.`server_ip` = 'all' OR pun.`server_ip` = @ServerIp)
					AND pun.`status` = 'active'
					AND (
						pun.`type` = 'warn'
						OR (pun.`type` IN ('mute', 'gag', 'silence', 'ban') AND (pun.`expires_at` > NOW() OR pun.`expires_at` IS NULL))
					);";

				var punishments = await connection.QueryAsync<Punishment>(selectPunishmentsQuery, new { SteamId = steamId, ServerIp = _serverIp });

				var playerData = new PlayerData
				{
					SteamId = steamId,
					Name = playerName,
					IpAddress = ipAddress,
					Immunity = playerDataRaw?.Immunity,
					RankExpiry = playerDataRaw?.RankExpiry,
					Punishments = punishments.ToList()
				};

				if (playerDataRaw != null)
				{
					// Deserialize the groups JSON
					if (!string.IsNullOrEmpty(playerDataRaw.GroupsJson))
					{
						try
						{
							playerData.Groups = JsonSerializer.Deserialize<List<string>>(playerDataRaw.GroupsJson) ?? new List<string>();
						}
						catch (JsonException ex)
						{
							Logger.LogError(ex, "Error deserializing groups for SteamID: {SteamId}", steamId);
						}
					}

					// Deserialize the permissions JSON
					if (!string.IsNullOrEmpty(playerDataRaw.PermissionsJson))
					{
						try
						{
							playerData.Permissions = JsonSerializer.Deserialize<List<string>>(playerDataRaw.PermissionsJson) ?? new List<string>();
						}
						catch (JsonException ex)
						{
							Logger.LogError(ex, "Error deserializing permissions for SteamID: {SteamId}", steamId);
						}
					}

					DateTime? rankExpiry = playerData.RankExpiry?.IsValidDateTime == true ? (DateTime?)playerData.RankExpiry : null;

					if (rankExpiry <= DateTime.UtcNow)
					{
						playerData.Groups = new List<string>();
						playerData.Permissions = new List<string>();
						playerData.Immunity = null;
						await UpdatePlayerRankAsync(steamId, null, null, null, null, _serverIp);
					}
					else
					{
						playerData.Permissions = MergePermissions(playerData.Permissions, playerDataRaw.GroupPermissions);
					}
				}
				else
				{
					playerData.Groups = [];
					playerData.Permissions = [];
				}

				return playerData;
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "An error occurred while loading or updating player data for SteamID: {SteamId}", steamId);
				return new PlayerData
				{
					SteamId = steamId,
					Name = playerName,
					IpAddress = ipAddress,
					Groups = [],
					Permissions = [],
					Punishments = []
				};
			}
		}

		private async Task HandlePlayerDisconnectAsync(ulong steamId)
		{
			try
			{
				string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				var query = $@"
					UPDATE `{prefix}zenith_bans_players`
					SET `current_server` = NULL
					WHERE `steam_id` = @SteamId;";

				await connection.ExecuteAsync(query, new { SteamId = steamId });

				_playerCache.Remove(steamId);
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Error handling player disconnect for SteamID: {SteamId}", steamId);
			}
		}

		private async Task<bool> IsIpBannedAsync(string ipAddress)
		{
			try
			{
				string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				var query = $@"
					SELECT COUNT(*)
					FROM `{prefix}zenith_bans_punishments` p
					JOIN `{prefix}zenith_bans_players` pl ON p.`steam_id` = pl.`steam_id`
					WHERE JSON_CONTAINS(pl.`ip_addresses`, JSON_QUOTE(@IpAddress))
					AND p.`type` = 'ban'
					AND (p.`expires_at` > NOW() OR p.`expires_at` IS NULL)
					AND p.`removed_at` IS NULL";

				int count = await connection.ExecuteScalarAsync<int>(query, new { IpAddress = ipAddress });
				return count > 0;
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "An error occurred while checking if IP address is banned: {IpAddress}", ipAddress);
				return false;
			}
		}

		private async Task UpdatePlayerRankAsync(ulong steamId, List<string>? groups, List<string>? permissions, int? immunity, MySqlDateTime? expiry, string serverIp = "all")
		{
			try
			{
				string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				var query = $@"
					INSERT INTO `{prefix}zenith_bans_player_ranks`
					(`steam_id`, `server_ip`, `groups`, `permissions`, `immunity`, `rank_expiry`)
					VALUES (@SteamId, @ServerIp, @Groups, @Permissions, @Immunity, @Expiry)
					ON DUPLICATE KEY UPDATE
					`groups` = @Groups,
					`permissions` = @Permissions,
					`immunity` = @Immunity,
					`rank_expiry` = @Expiry";

				await connection.ExecuteAsync(query, new
				{
					SteamId = steamId,
					ServerIp = serverIp,
					Groups = groups != null ? JsonSerializer.Serialize(groups) : null,
					Permissions = permissions != null ? JsonSerializer.Serialize(permissions) : null,
					Immunity = immunity,
					Expiry = expiry?.GetDateTime()
				});
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "An error occurred while updating player rank for SteamID: {SteamId}", steamId);
			}
		}

		private List<string> MergePermissions(List<string>? userPermissions, string? groupPermissionsJson)
		{
			var permissions = new HashSet<string>(userPermissions ?? []);

			if (!string.IsNullOrEmpty(groupPermissionsJson))
			{
				try
				{
					var groupPermissions = JsonSerializer.Deserialize<List<string>>(groupPermissionsJson);
					if (groupPermissions != null)
					{
						permissions.UnionWith(groupPermissions);
					}
				}
				catch
				{
					try
					{
						var nestedGroupPermissions = JsonSerializer.Deserialize<List<List<string>>>(groupPermissionsJson);
						if (nestedGroupPermissions != null)
						{
							foreach (var permList in nestedGroupPermissions)
							{
								permissions.UnionWith(permList);
							}
						}
						else
							Logger.LogWarning("Deserialized nested group permissions is null");
					}
					catch (JsonException nestedEx)
					{
						Logger.LogError(nestedEx, "Error deserializing nested group permissions JSON");
					}
				}
			}

			return [.. permissions];
		}

		private async Task<int> AddPunishmentAsync(ulong targetSteamId, PunishmentType type, int? duration, string reason, ulong? adminSteamId)
		{
			string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");
			using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
			await connection.OpenAsync();

			var query = $@"
				INSERT INTO `{prefix}zenith_bans_punishments`
				(`steam_id`, `type`, `status`, `duration`, `created_at`, `expires_at`, `admin_steam_id`, `reason`, `server_ip`)
				VALUES
				(@SteamId, @Type, @Status, @Duration, NOW(),
					CASE
						WHEN @Type = 'ban' AND (@Duration IS NULL OR @Duration = 0) THEN NULL
						WHEN @Duration IS NULL THEN NULL
						ELSE DATE_ADD(NOW(), INTERVAL @Duration MINUTE)
					END,
				@AdminSteamId, @Reason, @ServerIp);
				SELECT LAST_INSERT_ID();";

			return await connection.ExecuteScalarAsync<int>(query, new
			{
				SteamId = targetSteamId,
				Type = type.ToString().ToLower(),
				Status = type == PunishmentType.Kick ? "removed" : "active",
				Duration = duration,
				AdminSteamId = adminSteamId,
				Reason = reason,
				ServerIp = _coreAccessor.GetValue<bool>("Config", "GlobalPunishments") ? "all" : _serverIp
			});
		}

		private async Task<bool> RemovePunishmentAsync(ulong targetSteamId, PunishmentType type, ulong? removerSteamId)
		{
			string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");
			using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
			await connection.OpenAsync();

			var query = $@"
				UPDATE `{prefix}zenith_bans_punishments`
				SET `status` = CASE WHEN @RemoverSteamId IS NULL THEN 'removed_console' ELSE 'removed' END,
					`removed_at` = NOW(),
					`remove_admin_steam_id` = @RemoverSteamId
				WHERE `steam_id` = @TargetSteamId AND `type` = @Type
				AND (`server_ip` = 'all' OR `server_ip` = @ServerIp)
				AND `status` = 'active'";

			int affectedRows = await connection.ExecuteAsync(query, new
			{
				TargetSteamId = targetSteamId,
				Type = type.ToString().ToLower(),
				RemoverSteamId = removerSteamId,
				ServerIp = _serverIp
			});
			return affectedRows > 0;
		}

		private async Task<List<Punishment>> GetActivePunishmentsAsync(ulong steamId)
		{
			string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");
			using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
			await connection.OpenAsync();

			var query = $@"
				SELECT p.id, p.type, p.duration, p.expires_at AS ExpiresAt,
					COALESCE(admin.name, 'Console') AS PunisherName, p.admin_steam_id AS AdminSteamId, p.reason
				FROM `{prefix}zenith_bans_punishments` p
				LEFT JOIN `{prefix}zenith_bans_players` admin ON p.admin_steam_id = admin.steam_id
				WHERE p.steam_id = @SteamId
				AND (p.server_ip = 'all' OR p.server_ip = @ServerIp)
				AND p.status = 'active'
				AND (p.type = 'warn' OR (p.expires_at > NOW() OR p.expires_at IS NULL))";

			var punishments = await connection.QueryAsync<Punishment>(query, new { SteamId = steamId, ServerIp = _serverIp });

			return punishments.ToList();
		}

		private async Task<string> GetPlayerNameAsync(ulong steamId)
		{
			string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");
			using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
			await connection.OpenAsync();

			var query = $"SELECT `name` FROM `{prefix}zenith_bans_players` WHERE `steam_id` = @SteamId";
			return await connection.ExecuteScalarAsync<string>(query, new { SteamId = steamId }) ?? "Unknown";
		}

		private async Task RemoveOfflinePlayersFromServerAsync(List<ulong> onlineSteamIds)
		{
			try
			{
				string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				var query = $@"
					UPDATE `{prefix}zenith_bans_players`
					SET `current_server` = NULL
					WHERE `current_server` = @ServerIp
					AND `steam_id` NOT IN @OnlineSteamIds;";

				await connection.ExecuteAsync(query, new { ServerIp = _serverIp, OnlineSteamIds = onlineSteamIds });
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Error removing offline players from server.");
			}
		}

		private async Task RemoveExpiredPunishmentsAsync(List<ulong> onlineSteamIds)
		{
			try
			{
				string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				var query = $@"
					UPDATE `{prefix}zenith_bans_punishments` p
					JOIN `{prefix}zenith_bans_players` pl ON p.`steam_id` = pl.`steam_id`
					SET p.`status` = 'expired',
						p.`removed_at` = NOW(),
						p.`remove_admin_steam_id` = NULL
					WHERE p.`expires_at` <= NOW()
					AND p.`expires_at` IS NOT NULL
					AND p.`status` = 'active'
					AND p.`type` IN ('mute', 'gag', 'silence', 'ban');

					SELECT p.`steam_id`, p.`type`, pl.`name` AS `player_name`, pl.`current_server`
					FROM `{prefix}zenith_bans_punishments` p
					JOIN `{prefix}zenith_bans_players` pl ON p.`steam_id` = pl.`steam_id`
					WHERE p.`expires_at` <= NOW()
					AND p.`expires_at` IS NOT NULL
					AND p.`status` = 'expired'
					AND p.`removed_at` = NOW()
					AND p.`type` IN ('mute', 'gag', 'silence', 'ban');";

				using var multi = await connection.QueryMultipleAsync(query);
				var removedPunishments = await multi.ReadAsync<(ulong SteamId, string Type, string PlayerName, string CurrentServer)>();

				Server.NextWorldUpdate(async () =>
				{
					foreach (var (steamId, type, playerName, currentServer) in removedPunishments)
					{
						var player = Utilities.GetPlayerFromSteamId(steamId);
						if (player != null && _playerCache.TryGetValue(steamId, out var playerData))
						{
							playerData.Punishments.RemoveAll(p => p.Type.ToString().Equals(type, StringComparison.CurrentCultureIgnoreCase) && p.ExpiresAt?.GetDateTime() <= DateTime.UtcNow);
							RemovePunishmentEffect(player, Enum.Parse<PunishmentType>(type, true));

							_moduleServices?.PrintForPlayer(player, Localizer[$"k4.punishment.expired.{type.ToLower()}"]);
						}
					}

					await RemoveOfflinePlayersFromServerAsync(onlineSteamIds);
				});
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error in RemoveExpiredPunishmentsAsync: {ex.Message}");
			}
		}

		private async Task<(List<string> Permissions, int? Immunity)> GetGroupDetailsAsync(string groupName)
		{
			string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");
			using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
			await connection.OpenAsync();

			var query = $"SELECT `permissions`, `immunity` FROM `{prefix}zenith_bans_admin_groups` WHERE `name` = @GroupName";
			var result = await connection.QuerySingleOrDefaultAsync<dynamic>(query, new { GroupName = groupName });

			if (result == null)
				return ([], null);

			var permissions = string.IsNullOrEmpty(result.permissions)
				? new List<string>()
				: JsonSerializer.Deserialize<List<string>>(result.permissions) ?? new List<string>();

			return (permissions, (int?)result.immunity);
		}

		private async Task<List<string>> GetAdminGroupsAsync()
		{
			string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");
			using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
			await connection.OpenAsync();

			var query = $"SELECT `name` FROM `{prefix}zenith_bans_admin_groups`";
			var groups = await connection.QueryAsync<string>(query);
			return groups.ToList();
		}

		private async Task AddAdminAsync(ulong steamId, string groupName, string serverIp = "all")
		{
			await UpdatePlayerRankAsync(steamId, [groupName], null, null, null, serverIp);
		}

		private async Task RemoveAdminAsync(ulong steamId, string serverIp = "all")
		{
			await UpdatePlayerRankAsync(steamId, [], null, null, null, serverIp);
		}

		private async Task ImportAdminGroupsFromJsonAsync(string directory)
		{
			string adminGroupsPath = Path.Combine(directory, "csgo", "addons", "counterstrikesharp", "configs", "admin_groups.json");

			if (!File.Exists(adminGroupsPath))
				return;

			try
			{
				string jsonContent = await File.ReadAllTextAsync(adminGroupsPath);
				var adminGroups = JsonSerializer.Deserialize<Dictionary<string, AdminGroupInfo>>(jsonContent);

				if (adminGroups == null || adminGroups.Count == 0)
					return;

				string prefix = _coreAccessor.GetValue<string>("Database", "TablePrefix");
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				foreach (var group in adminGroups)
				{
					string groupName = group.Key;
					var groupInfo = group.Value;

					var query = $@"
						INSERT INTO `{prefix}zenith_bans_admin_groups` (`name`, `permissions`, `immunity`)
						VALUES (@Name, @Permissions, @Immunity)
						ON DUPLICATE KEY UPDATE
						`permissions` = @Permissions,
						`immunity` = @Immunity";

					await connection.ExecuteAsync(query, new
					{
						Name = groupName,
						Permissions = JsonSerializer.Serialize(groupInfo.Flags),
						Immunity = groupInfo.Immunity
					});
				}

				Logger.LogInformation($"Succesfully imported {adminGroups.Count} admin groups from local JSON. You can disable this feature in the config.");
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Error importing admin groups from JSON.");
			}
		}

		private class AdminGroupInfo
		{
			[JsonPropertyName("flags")]
			public List<string> Flags { get; set; } = [];

			[JsonPropertyName("immunity")]
			public int Immunity { get; set; }
		}
	}
}
