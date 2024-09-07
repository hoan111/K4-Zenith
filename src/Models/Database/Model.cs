using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Zenith.Models;

public partial class Database(Plugin plugin)
{
	public MySqlConnection CreateConnection()
	{
		MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder
		{
			Server = plugin.GetCoreConfig<string>("Database", "Hostname"),
			UserID = plugin.GetCoreConfig<string>("Database", "Username"),
			Password = plugin.GetCoreConfig<string>("Database", "Password"),
			Database = plugin.GetCoreConfig<string>("Database", "Database"),
			Port = plugin.GetCoreConfig<uint>("Database", "Port"),
			SslMode = Enum.Parse<MySqlSslMode>(plugin.GetCoreConfig<string>("Database", "Sslmode"), true),
			AllowZeroDateTime = true,
			ConvertZeroDateTime = true,
			TreatTinyAsBoolean = true,
			OldGuids = true
		};

		return new MySqlConnection(builder.ToString());
	}

	public bool TestConnection()
	{
		try
		{
			using var connection = CreateConnection();
			connection.Open();
			return true;
		}
		catch
		{
			return false;
		}
	}

	public async Task PurgeOldData()
	{
		if (TablePurgeDays <= 0) return;

		string tablePrefix = TablePrefix;
		using var connection = CreateConnection();
		await connection.OpenAsync();

		var tables = new[] { Player.TABLE_PLAYER_SETTINGS, Player.TABLE_PLAYER_STORAGE };

		foreach (var table in tables)
		{
			var query = $@"
                DELETE FROM `{tablePrefix}{table}`
                WHERE `last_online` < DATE_SUB(NOW(), INTERVAL @PurgeDays DAY);";

			var affectedRows = await connection.ExecuteAsync(query, new { PurgeDays = TablePurgeDays });

			if (affectedRows > 0)
				plugin.Logger.LogInformation($"Purged {affectedRows} rows older than {TablePurgeDays} days from {table} table.");
		}
	}

	public void ScheduleMidnightPurge()
	{
		DateTime now = DateTime.Now;
		TimeSpan timeUntilMidnight = now.Date.AddDays(1) - now;

		plugin.AddTimer((float)timeUntilMidnight.TotalSeconds, () =>
		{
			_ = Task.Run(async () =>
			{
				try
				{
					await PurgeOldData();
				}
				catch (Exception ex)
				{
					plugin.Logger.LogError($"Midnight data purge failed: {ex.Message}");
				}
				finally
				{
					ScheduleMidnightPurge();
				}
			});
		});
	}

	public string TablePrefix => plugin.GetCoreConfig<string>("Database", "TablePrefix");
	public int TablePurgeDays => plugin.GetCoreConfig<int>("Database", "TablePurgeDays");
	public string GetConnectionString() => CreateConnection().ConnectionString;
}