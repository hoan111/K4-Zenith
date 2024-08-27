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
		// Add your versioned migrations here...
		// { "1.1", new List<string> { "SQL_STATEMENT1;", "SQL_STATEMENT2;" } },
		// { "1.2", new List<string> { "SQL_STATEMENT3;" } },
	};

	private async Task<string> GetCurrentDatabaseVersionAsync(MySqlConnection connection)
	{
		await EnsureVersionTableExistsAsync(connection);
		var result = await connection.QueryFirstOrDefaultAsync<string>(
			$"SELECT value FROM {TablePrefix}{INFO_TABLE} WHERE `key` = @Key",
			new { Key = VERSION_KEY }
		);
		return result ?? "1.0";
	}

	private async Task SetDatabaseVersionAsync(MySqlConnection connection, string version)
	{
		await connection.ExecuteAsync(
			$"INSERT INTO {TablePrefix}{INFO_TABLE} (`key`, value) VALUES (@Key, @Value) " +
			"ON DUPLICATE KEY UPDATE value = @Value",
			new { Key = VERSION_KEY, Value = version }
		);
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

	private async Task CreateDatabaseBackupAsync(MySqlConnection connection)
	{
		var backupFilePath = Path.Combine(_plugin.ModuleDirectory, $"db_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.sql");
		_plugin.Logger.LogInformation($"Creating database backup: {backupFilePath}");

		using var cmd = new MySqlCommand("SHOW TABLES", connection);
		using var reader = await cmd.ExecuteReaderAsync();
		using var writer = new StreamWriter(backupFilePath);

		while (await reader.ReadAsync())
		{
			var tableName = reader.GetString(0);
			await BackupTableAsync(connection, tableName, writer);
		}

		_plugin.Logger.LogInformation("Database backup completed successfully.");
	}

	private async Task BackupTableAsync(MySqlConnection connection, string tableName, StreamWriter writer)
	{
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
		using var connection = CreateConnection();
		await connection.OpenAsync();

		var currentVersion = await GetCurrentDatabaseVersionAsync(connection);

		var pendingMigrations = _migrations
			.Where(m => string.Compare(m.Key, currentVersion, StringComparison.Ordinal) > 0)
			.OrderBy(m => m.Key)
			.ToList();

		if (!pendingMigrations.Any())
			return;

		_plugin.Logger.LogInformation($"Starting database migration to version {_plugin.ModuleVersion} from {currentVersion}");

		await CreateDatabaseBackupAsync(connection);

		foreach (var migration in pendingMigrations)
		{
			_plugin.Logger.LogInformation($"Applying migration: {migration.Key}");

			using var transaction = await connection.BeginTransactionAsync();
			try
			{
				foreach (var sql in migration.Value)
				{
					var formattedSql = string.Format(sql, TablePrefix);
					await connection.ExecuteAsync(formattedSql, transaction: transaction);
				}

				await SetDatabaseVersionAsync(connection, migration.Key);
				await transaction.CommitAsync();
				_plugin.Logger.LogInformation($"Migration {migration.Key} applied successfully.");
			}
			catch (Exception ex)
			{
				_plugin.Logger.LogError($"Migration {migration.Key} failed: {ex.Message}");
				await transaction.RollbackAsync();
				throw;
			}
		}

		_plugin.Logger.LogInformation($"Database migration completed. Current version: {_plugin.ModuleVersion}");
	}
}
