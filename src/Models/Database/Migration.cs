using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Zenith.Models;

public partial class Database
{
	private const string INFO_TABLE = "zenith_info";
	private const string VERSION_KEY = "db_version";

	private readonly Dictionary<string, List<string>> _migrations = new()
	{
		{ "1.2", new List<string>{"CREATE TABLE IF NOT EXISTS zenith_weapon_stats_temp (id INT); ALTER TABLE zenith_weapon_stats ADD COLUMN IF NOT EXISTS chest_hits INT NOT NULL DEFAULT 0, ADD COLUMN IF NOT EXISTS stomach_hits INT NOT NULL DEFAULT 0, ADD COLUMN IF NOT EXISTS left_arm_hits INT NOT NULL DEFAULT 0, ADD COLUMN IF NOT EXISTS right_arm_hits INT NOT NULL DEFAULT 0, ADD COLUMN IF NOT EXISTS left_leg_hits INT NOT NULL DEFAULT 0, ADD COLUMN IF NOT EXISTS right_leg_hits INT NOT NULL DEFAULT 0, ADD COLUMN IF NOT EXISTS neck_hits INT NOT NULL DEFAULT 0, ADD COLUMN IF NOT EXISTS gear_hits INT NOT NULL DEFAULT 0; DROP TABLE IF EXISTS zenith_weapon_stats_temp;"} },
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
            )
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
		using (var connection = CreateConnection())
		{
			await connection.OpenAsync();
			currentVersion = await GetCurrentDatabaseVersionAsync(connection);
		}

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

		await CreateDatabaseBackupAsync();

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
						foreach (var sql in migration.Value)
						{
							var formattedSql = string.Format(sql, TablePrefix);
							var statements = formattedSql.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
							foreach (var statement in statements)
							{
								if (!string.IsNullOrWhiteSpace(statement))
								{
									var trimmedStatement = statement.Trim();
									plugin.Logger.LogDebug($"Executing SQL: {trimmedStatement}");
									await connection.ExecuteAsync(trimmedStatement, transaction: transaction);
								}
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

	private async Task SetDatabaseVersionAsync(MySqlConnection connection, string version, MySqlTransaction? transaction = null)
	{
		var sql = $@"
			INSERT INTO {TablePrefix}{INFO_TABLE} (`key`, value)
			VALUES (@Key, @Value)
			ON DUPLICATE KEY UPDATE value = @Value";

		await connection.ExecuteAsync(sql, new { Key = VERSION_KEY, Value = version }, transaction);
		plugin.Logger.LogInformation($"Database version updated to: {version}");
	}

}
