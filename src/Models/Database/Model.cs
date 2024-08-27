using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Zenith.Models;

public partial class Database
{
	private readonly Plugin _plugin;

	public Database(Plugin plugin)
	{
		_plugin = plugin;
	}

	public MySqlConnection CreateConnection()
	{
		MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder
		{
			Server = _plugin.GetCoreConfig<string>("Database", "Hostname"),
			UserID = _plugin.GetCoreConfig<string>("Database", "Username"),
			Password = _plugin.GetCoreConfig<string>("Database", "Password"),
			Database = _plugin.GetCoreConfig<string>("Database", "Database"),
			Port = _plugin.GetCoreConfig<uint>("Database", "Port"),
			SslMode = Enum.Parse<MySqlSslMode>(_plugin.GetCoreConfig<string>("Database", "Sslmode"), true),
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
			_plugin.Logger.LogInformation($"Purged {affectedRows} rows older than {TablePurgeDays} days from {table} table.");
		}
	}

	public void ScheduleMidnightPurge()
	{
		DateTime now = DateTime.Now;
		TimeSpan timeUntilMidnight = now.Date.AddDays(1) - now;

		_plugin.AddTimer((float)timeUntilMidnight.TotalSeconds, () =>
		{
			Task.Run(async () =>
			{
				try
				{
					await PurgeOldData();
				}
				catch (Exception ex)
				{
					_plugin.Logger.LogError($"Midnight data purge failed: {ex.Message}");
				}
				finally
				{
					ScheduleMidnightPurge();
				}
			});
		});
	}

	public string TablePrefix => _plugin.GetCoreConfig<string>("Database", "TablePrefix");
	public int TablePurgeDays => _plugin.GetCoreConfig<int>("Database", "TablePurgeDays");
	public string GetConnectionString() => CreateConnection().ConnectionString;
}