using CounterStrikeSharp.API.Core;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Zenith.Models;

namespace Zenith
{
	public sealed partial class Plugin : BasePlugin
	{
		private async Task MigrateOldData()
		{
			try
			{
				using var connection = Database.CreateConnection();
				await connection.OpenAsync();

				bool anyMigrationPerformed = false;

				anyMigrationPerformed |= await MigrateRanksData(connection);
				anyMigrationPerformed |= await MigrateTimesData(connection);
				anyMigrationPerformed |= await MigrateStatsData(connection);

				if (anyMigrationPerformed)
				{
					Logger.LogWarning("Data migration completed. Please manually delete the old k4ranks, k4times, and k4stats tables to prevent data duplication. We do not delete them automatically to prevent accidental data loss.");
				}
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error during data migration: {ex.Message}");
			}
		}

		private async Task<bool> MigrateRanksData(MySqlConnection connection)
		{
			var oldTableExists = await connection.ExecuteScalarAsync<bool>(
				$"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'k4ranks'");

			if (!oldTableExists) return false;

			Logger.LogInformation("Found old k4ranks table. Starting data migration.");

			var columnExists = await connection.ExecuteScalarAsync<bool>(
				$"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = '{Database.TablePrefix}{Player.TABLE_PLAYER_STORAGE}' AND column_name = 'K4-Zenith-Ranks.storage'");

			if (!columnExists)
			{
				await connection.ExecuteAsync(
					$"ALTER TABLE `{Database.TablePrefix}{Player.TABLE_PLAYER_STORAGE}` ADD COLUMN `K4-Zenith-Ranks.storage` JSON NULL");
			}

			var migrateQuery = $@"
                INSERT INTO `{Database.TablePrefix}{Player.TABLE_PLAYER_STORAGE}` (`steam_id`, `last_online`, `K4-Zenith-Ranks.storage`)
                SELECT
                    k.`steam_id`,
                    k.`lastseen`,
                    JSON_OBJECT('Points', k.`points`, 'Rank', k.`rank`)
                FROM
                    `k4ranks` k
                ON DUPLICATE KEY UPDATE
                    `K4-Zenith-Ranks.storage` = IF(`K4-Zenith-Ranks.storage` IS NULL,
                        VALUES(`K4-Zenith-Ranks.storage`),
                        `K4-Zenith-Ranks.storage`)";

			var affectedRows = await connection.ExecuteAsync(migrateQuery);

			if (affectedRows > 0)
			{
				Logger.LogInformation($"Migrated {affectedRows} rows from k4ranks table.");
				return true;
			}
			return false;
		}

		private async Task<bool> MigrateStatsData(MySqlConnection connection)
		{
			var oldTableExists = await connection.ExecuteScalarAsync<bool>(
				$"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'k4stats'");

			if (!oldTableExists) return false;

			Logger.LogInformation("Found old k4stats table. Starting data migration.");

			var columnExists = await connection.ExecuteScalarAsync<bool>(
				$"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = '{Database.TablePrefix}{Player.TABLE_PLAYER_STORAGE}' AND column_name = 'K4-Zenith-Stats.storage'");

			if (!columnExists)
			{
				await connection.ExecuteAsync(
					$"ALTER TABLE `{Database.TablePrefix}{Player.TABLE_PLAYER_STORAGE}` ADD COLUMN `K4-Zenith-Stats.storage` JSON NULL");
			}

			var migrateQuery = $@"
				INSERT INTO `{Database.TablePrefix}{Player.TABLE_PLAYER_STORAGE}` (`steam_id`, `last_online`, `K4-Zenith-Stats.storage`)
				SELECT
					s.`steam_id`,
					s.`lastseen`,
					JSON_OBJECT(
						'Kills', s.`kills`,
						'FirstBlood', s.`firstblood`,
						'Deaths', s.`deaths`,
						'Assists', s.`assists`,
						'Shoots', s.`shoots`,
						'HitsTaken', s.`hits_taken`,
						'HitsGiven', s.`hits_given`,
						'Headshots', s.`headshots`,
						'HeadHits', s.`headshots`,
						'ChestHits', s.`chest_hits`,
						'StomachHits', s.`stomach_hits`,
						'LeftArmHits', s.`left_arm_hits`,
						'RightArmHits', s.`right_arm_hits`,
						'LeftLegHits', s.`left_leg_hits`,
						'RightLegHits', s.`right_leg_hits`,
						'NeckHits', s.`neck_hits`,
						'UnusedHits', s.`unused_hits`,
						'GearHits', s.`gear_hits`,
						'SpecialHits', s.`special_hits`,
						'Grenades', s.`grenades`,
						'MVP', s.`mvp`,
						'RoundWin', s.`round_win`,
						'RoundLose', s.`round_lose`,
						'GameWin', s.`game_win`,
						'GameLose', s.`game_lose`,
						'RoundsOverall', s.`rounds_overall`,
						'RoundsCT', s.`rounds_ct`,
						'RoundsT', s.`rounds_t`,
						'BombPlanted', s.`bomb_planted`,
						'BombDefused', s.`bomb_defused`,
						'HostageRescued', s.`hostage_rescued`,
						'HostageKilled', s.`hostage_killed`,
						'NoScopeKill', s.`noscope_kill`,
						'PenetratedKill', s.`penetrated_kill`,
						'ThruSmokeKill', s.`thrusmoke_kill`,
						'FlashedKill', s.`flashed_kill`,
						'DominatedKill', s.`dominated_kill`,
						'RevengeKill', s.`revenge_kill`,
						'AssistFlash', s.`assist_flash`
					)
				FROM
					`k4stats` s
				ON DUPLICATE KEY UPDATE
					`K4-Zenith-Stats.storage` = IF(`K4-Zenith-Stats.storage` IS NULL,
						VALUES(`K4-Zenith-Stats.storage`),
						`K4-Zenith-Stats.storage`)";

			var affectedRows = await connection.ExecuteAsync(migrateQuery);

			if (affectedRows > 0)
			{
				Logger.LogInformation($"Migrated {affectedRows} rows from k4stats table.");
				return true;
			}
			return false;
		}

		private async Task<bool> MigrateTimesData(MySqlConnection connection)
		{
			var oldTableExists = await connection.ExecuteScalarAsync<bool>(
				$"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'k4times'");

			if (!oldTableExists) return false;

			Logger.LogInformation("Found old k4times table. Starting data migration.");

			var columnExists = await connection.ExecuteScalarAsync<bool>(
				$"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = '{Database.TablePrefix}{Player.TABLE_PLAYER_STORAGE}' AND column_name = 'K4-Zenith-TimeStats.storage'");

			if (!columnExists)
			{
				await connection.ExecuteAsync(
					$"ALTER TABLE `{Database.TablePrefix}{Player.TABLE_PLAYER_STORAGE}` ADD COLUMN `K4-Zenith-TimeStats.storage` JSON NULL");
			}

			var migrateQuery = $@"
				INSERT INTO `{Database.TablePrefix}{Player.TABLE_PLAYER_STORAGE}` (`steam_id`, `last_online`, `K4-Zenith-TimeStats.storage`)
				SELECT
					t.`steam_id`,
					t.`lastseen`,
					JSON_OBJECT(
						'TotalPlaytime', ROUND(t.`all` / 60, 1),
						'TerroristPlaytime', ROUND(t.`t` / 60, 1),
						'CounterTerroristPlaytime', ROUND(t.`ct` / 60, 1),
						'SpectatorPlaytime', ROUND(t.`spec` / 60, 1),
						'AlivePlaytime', ROUND(t.`alive` / 60, 1),
						'DeadPlaytime', ROUND(t.`dead` / 60, 1),
						'LastNotification', UNIX_TIMESTAMP(t.`lastseen`)
					)
				FROM
					`k4times` t
				ON DUPLICATE KEY UPDATE
					`K4-Zenith-TimeStats.storage` = IF(`K4-Zenith-TimeStats.storage` IS NULL,
						VALUES(`K4-Zenith-TimeStats.storage`),
						`K4-Zenith-TimeStats.storage`)";

			var affectedRows = await connection.ExecuteAsync(migrateQuery);

			if (affectedRows > 0)
			{
				Logger.LogInformation($"Migrated {affectedRows} rows from k4times table.");
				return true;
			}
			return false;
		}
	}
}
