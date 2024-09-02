using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Zenith.Models
{
	public partial class Database
	{
		private const string INFO_TABLE = "zenith_info";
		private const string VERSION_KEY = "db_version";

		private class MigrationStep
		{
			public required string TableName { get; set; }
			public required string SqlQuery { get; set; }
		}

		private readonly Dictionary<string, List<MigrationStep>> _migrations = new()
		{
			{ "1.0.9", new List<MigrationStep>
				{
					new MigrationStep
					{
						TableName = "zenith_bans_punishments",
						SqlQuery = @"
                        ALTER TABLE `{prefix}zenith_bans_punishments`
                        ADD COLUMN IF NOT EXISTS `status` ENUM('active', 'expired', 'removed', 'removed_console')
                        CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
                        NOT NULL DEFAULT 'active';"
					},
					new MigrationStep
					{
						TableName = "zenith_bans_punishments",
						SqlQuery = @"
                        UPDATE `{prefix}zenith_bans_punishments`
                        SET `status` = CASE
                            WHEN `type` = 'kick' THEN 'removed'
                            WHEN `removed_at` IS NOT NULL THEN
                                CASE
                                    WHEN `remove_admin_steam_id` IS NULL THEN 'removed_console'
                                    ELSE 'removed'
                                END
                            WHEN `expires_at` IS NOT NULL AND `expires_at` <= NOW() THEN 'expired'
                            ELSE 'active'
                        END;"
					}
				}
			},
            // Add other migrations here as needed
        };

		private async Task<string> GetCurrentDatabaseVersionAsync(MySqlConnection connection)
		{
			await EnsureVersionTableExistsAsync(connection);
			var result = await connection.QueryFirstOrDefaultAsync<string>(
				$"SELECT value FROM {TablePrefix}{INFO_TABLE} WHERE `key` = @Key",
				new { Key = VERSION_KEY }
			);
			return result ?? "1.0.0";
		}

		private async Task EnsureVersionTableExistsAsync(MySqlConnection connection)
		{
			await connection.ExecuteAsync($@"
                CREATE TABLE IF NOT EXISTS {TablePrefix}{INFO_TABLE} (
                    `key` VARCHAR(50) PRIMARY KEY,
                    value VARCHAR(50) NOT NULL
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
            ");
		}

		private async Task CreateDatabaseBackupAsync()
		{
			var backupFilePath = Path.Combine(plugin.ModuleDirectory, $"db_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.sql");
			plugin.Logger.LogInformation($"Creating database backup: {backupFilePath}");

			using var writer = new StreamWriter(backupFilePath);

			using (var connection = CreateConnection())
			{
				await connection.OpenAsync();
				using var cmd = new MySqlCommand("SHOW TABLES", connection);
				using var reader = await cmd.ExecuteReaderAsync();

				var tables = new List<string>();
				while (await reader.ReadAsync())
				{
					tables.Add(reader.GetString(0));
				}

				foreach (var tableName in tables)
				{
					await BackupTableAsync(tableName, writer);
				}
			}

			plugin.Logger.LogInformation("Database backup completed successfully.");
		}

		private async Task BackupTableAsync(string tableName, StreamWriter writer)
		{
			using var connection = CreateConnection();
			await connection.OpenAsync();

			// Write table creation script
			var createTableCmd = new MySqlCommand($"SHOW CREATE TABLE `{tableName}`", connection);
			var createTableResult = (await createTableCmd.ExecuteScalarAsync())?.ToString();
			await writer.WriteLineAsync($"-- Table structure for {tableName}");
			await writer.WriteLineAsync($"{createTableResult};");
			await writer.WriteLineAsync();

			// Write table data
			var dataCmd = new MySqlCommand($"SELECT * FROM `{tableName}`", connection);
			using var dataReader = await dataCmd.ExecuteReaderAsync();
			await writer.WriteLineAsync($"-- Data for {tableName}");
			while (await dataReader.ReadAsync())
			{
				var values = new object[dataReader.FieldCount];
				dataReader.GetValues(values);
				var escapedValues = values.Select(v => $"'{MySqlHelper.EscapeString(v?.ToString() ?? "")}'");
				await writer.WriteLineAsync($"INSERT INTO `{tableName}` VALUES ({string.Join(", ", escapedValues)});");
			}
			await writer.WriteLineAsync();
		}

		public async Task MigrateDatabaseAsync()
		{
			string currentVersion;
			string serverType;
			using (var connection = CreateConnection())
			{
				await connection.OpenAsync();
				currentVersion = await GetCurrentDatabaseVersionAsync(connection);
				serverType = await GetServerTypeAsync(connection);
			}

			plugin.Logger.LogInformation($"Database server type: {serverType}");

			var pendingMigrations = _migrations
				.Where(m => string.Compare(m.Key, currentVersion, StringComparison.Ordinal) > 0)
				.OrderBy(m => m.Key)
				.ToList();

			if (!pendingMigrations.Any())
			{
				plugin.Logger.LogInformation("No pending migrations.");
				return;
			}

			plugin.Logger.LogInformation($"Starting database migration from version {currentVersion} to {plugin.ModuleVersion}");

			try
			{
				await CreateDatabaseBackupAsync();
			}
			catch (Exception ex)
			{
				plugin.Logger.LogError($"Failed to create database backup: {ex.Message}");
				// Consider whether to proceed with migration or not
			}

			string latestAppliedVersion = currentVersion;

			foreach (var migration in pendingMigrations)
			{
				const int maxRetries = 3;
				for (int retry = 0; retry < maxRetries; retry++)
				{
					using (var connection = CreateConnection())
					{
						await connection.OpenAsync();
						using var transaction = await connection.BeginTransactionAsync();
						try
						{
							foreach (var step in migration.Value)
							{
								if (await TableExistsAsync(connection, step.TableName, transaction))
								{
									var formattedSql = step.SqlQuery.Replace("{prefix}", TablePrefix);
									plugin.Logger.LogDebug($"Executing SQL on table {step.TableName}: {formattedSql}");
									await connection.ExecuteAsync(formattedSql, transaction: transaction);
								}
								else
								{
									plugin.Logger.LogWarning($"Table {step.TableName} does not exist. Skipping this migration step.");
								}
							}

							await SetDatabaseVersionAsync(connection, migration.Key, transaction);
							await transaction.CommitAsync();
							latestAppliedVersion = migration.Key;
							break; // Success, exit retry loop
						}
						catch (Exception ex)
						{
							await transaction.RollbackAsync();
							if (retry == maxRetries - 1)
							{
								plugin.Logger.LogError($"Migration {migration.Key} failed after {maxRetries} attempts: {ex.Message}");
								throw;
							}
							plugin.Logger.LogWarning($"Migration {migration.Key} failed (attempt {retry + 1}): {ex.Message}. Retrying...");
							await Task.Delay(1000 * (retry + 1)); // Exponential backoff
						}
					}
				}
			}

			plugin.Logger.LogInformation($"Database migration completed. Current version: {latestAppliedVersion}");
		}

		private async Task<bool> TableExistsAsync(MySqlConnection connection, string tableName, MySqlTransaction transaction)
		{
			var sql = @"
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = DATABASE()
                AND table_name = @TableName";

			var count = await connection.ExecuteScalarAsync<int>(sql, new { TableName = $"{TablePrefix}{tableName}" }, transaction);
			return count > 0;
		}

		private async Task SetDatabaseVersionAsync(MySqlConnection connection, string version, MySqlTransaction? transaction = null)
		{
			var sql = $@"
                INSERT INTO {TablePrefix}{INFO_TABLE} (`key`, value)
                VALUES (@Key, @Value)
                ON DUPLICATE KEY UPDATE value = @Value";

			await connection.ExecuteAsync(sql, new { Key = VERSION_KEY, Value = version }, transaction);
			plugin.Logger.LogInformation($"Database version updated to: {version}");
		}

		private async Task<string> GetServerTypeAsync(MySqlConnection connection)
		{
			var version = await connection.ExecuteScalarAsync<string>("SELECT VERSION()");
			return version!.Contains("mariadb", StringComparison.CurrentCultureIgnoreCase) ? "MariaDB" : "MySQL";
		}
	}
}
