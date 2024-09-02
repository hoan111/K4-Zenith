using System.Text.Json;
using Microsoft.Extensions.Logging;
using Dapper;
using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using Microsoft.Extensions.Localization;
using MySqlConnector;
using System.Reflection;

namespace Zenith.Models;

public sealed partial class Player
{
	public static readonly string TABLE_PLAYER_SETTINGS = "zenith_player_settings";
	public static readonly string TABLE_PLAYER_STORAGE = "zenith_player_storage";

	public ConcurrentDictionary<string, object?> Settings = new();
	public ConcurrentDictionary<string, object?> Storage = new();
	public static readonly ConcurrentDictionary<string, (ConcurrentDictionary<string, object?> Settings, IStringLocalizer? Localizer)> moduleDefaultSettings = new();
	public static readonly ConcurrentDictionary<string, (ConcurrentDictionary<string, object?> Settings, IStringLocalizer? Localizer)> moduleDefaultStorage = new();

	public static async Task CreateTablesAsync(Plugin plugin)
	{
		try
		{
			string tablePrefix = plugin.Database.TablePrefix;
			using var connection = plugin.Database.CreateConnection();
			await connection.OpenAsync();

			var createSettingsTableQuery = $@"
				CREATE TABLE IF NOT EXISTS `{MySqlHelper.EscapeString(tablePrefix)}{TABLE_PLAYER_SETTINGS}` (
					`steam_id` VARCHAR(32) NOT NULL,
					`last_online` TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
					PRIMARY KEY (`steam_id`)
				) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";

			await connection.ExecuteAsync(createSettingsTableQuery);

			var createStorageTableQuery = $@"
				CREATE TABLE IF NOT EXISTS `{MySqlHelper.EscapeString(tablePrefix)}{TABLE_PLAYER_STORAGE}` (
					`steam_id` VARCHAR(32) NOT NULL,
					`last_online` TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
					PRIMARY KEY (`steam_id`)
				) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";

			await connection.ExecuteAsync(createStorageTableQuery);
		}
		catch (Exception ex)
		{
			plugin.Logger.LogError($"Failed to create player data tables: {ex.Message}");
		}
	}

	public static void RegisterModuleSettings(Plugin plugin, Dictionary<string, object?> defaultSettings, IStringLocalizer? localizer = null)
	{
		string callerPlugin = CallerIdentifier.GetCallingPluginName();

		Task.Run(async () =>
		{
			await RegisterModuleDataAsync(plugin, callerPlugin, TABLE_PLAYER_SETTINGS);

			Server.NextFrame(() =>
			{
				moduleDefaultSettings[callerPlugin] = (new ConcurrentDictionary<string, object?>(defaultSettings), localizer);
			});
		});
	}

	public static void RegisterModuleStorage(Plugin plugin, Dictionary<string, object?> defaultStorage)
	{
		string callerPlugin = CallerIdentifier.GetCallingPluginName();

		Task.Run(async () =>
		{
			await RegisterModuleDataAsync(plugin, callerPlugin, TABLE_PLAYER_STORAGE);

			Server.NextFrame(() =>
			{
				moduleDefaultStorage[callerPlugin] = (new ConcurrentDictionary<string, object?>(defaultStorage), null);
			});
		});
	}

	private static async Task RegisterModuleDataAsync(Plugin plugin, string moduleID, string tableName)
	{
		try
		{
			string tablePrefix = plugin.Database.TablePrefix;
			string columnName = tableName == TABLE_PLAYER_SETTINGS ? $"{moduleID}.settings" : $"{moduleID}.storage";

			using var connection = plugin.Database.CreateConnection();
			await connection.OpenAsync();

			var columnExistsQuery = $@"
				SELECT COUNT(*)
				FROM INFORMATION_SCHEMA.COLUMNS
				WHERE TABLE_SCHEMA = DATABASE()
				AND TABLE_NAME = '{MySqlHelper.EscapeString(tablePrefix)}{tableName}'
				AND COLUMN_NAME = '{MySqlHelper.EscapeString(columnName)}'";

			var columnExists = await connection.ExecuteScalarAsync<int>(columnExistsQuery) > 0;

			if (!columnExists)
			{
				var addColumnQuery = $@"
					ALTER TABLE `{MySqlHelper.EscapeString(tablePrefix)}{tableName}`
					ADD COLUMN `{MySqlHelper.EscapeString(columnName)}` JSON NULL;";

				await connection.ExecuteAsync(addColumnQuery);
			}
		}
		catch (Exception ex)
		{
			plugin.Logger.LogError($"Failed to register module data for {moduleID}: {ex.Message}");
		}
	}

	public void SetSetting(string key, object? value, bool saveImmediately = false)
		=> SetData(key, value, Settings, saveImmediately);

	public void SetStorage(string key, object? value, bool saveImmediately = false)
		=> SetData(key, value, Storage, saveImmediately);

	public void SetData(string key, object? value, ConcurrentDictionary<string, object?> targetDict, bool saveImmediately, string? caller = null)
	{
		string callerPlugin = caller ?? CallerIdentifier.GetCallingPluginName();

		var fullKey = $"{callerPlugin}.{key}";
		if (targetDict.ContainsKey(fullKey))
		{
			targetDict[fullKey] = value;
		}
		else
		{
			// Search across all plugins
			var existingKey = targetDict.Keys.FirstOrDefault(k => k.EndsWith($".{key}"));
			if (existingKey != null)
			{
				targetDict[existingKey] = value;
				fullKey = existingKey;
			}
			else
			{
				targetDict[fullKey] = value;
			}
		}

		if (saveImmediately)
		{
			Task.Run(async () =>
			{
				await SavePlayerDataAsync(fullKey.Split('.')[0], targetDict == Storage);
			});
		}
	}

	public T? GetModuleStorage<T>(string module, string key)
		=> GetData<T>(key, Storage, module);

	public void SetModuleStorage(string module, string key, object? value, bool saveImmediately = false)
		=> SetData(key, value, Storage, saveImmediately, module);

	public T? GetSetting<T>(string key)
		=> GetData<T>(key, Settings);
	public T? GetStorage<T>(string key)
		=> GetData<T>(key, Storage);

	public T? GetData<T>(string key, ConcurrentDictionary<string, object?> targetDict, string? caller = null)
	{
		string callerPlugin = caller ?? CallerIdentifier.GetCallingPluginName();

		var fullKey = $"{callerPlugin}.{key}";
		if (!targetDict.TryGetValue(fullKey, out var value))
		{
			// Search across all plugins
			var existingKey = targetDict.Keys.FirstOrDefault(k => k.EndsWith($".{key}"));
			if (existingKey != null)
			{
				fullKey = existingKey;
				value = targetDict[existingKey];
			}
			else
			{
				_plugin.Logger.LogWarning($"Key '{key}' not found for any plugin.");
				return default;
			}
		}

		try
		{
			if (value is JsonElement jsonElement)
			{
				return DeserializeJsonElement<T>(jsonElement);
			}
			else if (value is T typedValue)
			{
				return typedValue;
			}
			else
			{
				return (T?)Convert.ChangeType(value, typeof(T));
			}
		}
		catch (Exception ex)
		{
			_plugin.Logger.LogError($"Failed to convert setting value for key '{fullKey}' to type '{typeof(T).Name}'. Error: {ex.Message}");
			return default;
		}
	}

	private T? DeserializeJsonElement<T>(JsonElement element)
	{
		Type type = typeof(T);

		if (Nullable.GetUnderlyingType(type) != null)
		{
			type = Nullable.GetUnderlyingType(type)!;
		}

		switch (Type.GetTypeCode(type))
		{
			case TypeCode.Boolean:
				if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
					return (T)(object)element.GetBoolean();
				else if (element.ValueKind == JsonValueKind.Number)
					return (T)(object)(element.GetInt32() != 0);
				break;
			case TypeCode.Int32:
				if (element.ValueKind == JsonValueKind.Number)
					return (T)(object)element.GetInt32();
				else if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
					return (T)(object)(element.GetBoolean() ? 1 : 0);
				break;
			case TypeCode.String:
				if (element.ValueKind == JsonValueKind.String)
					return (T)(object)element.GetString()!;
				break;
			case TypeCode.Double:
				if (element.ValueKind == JsonValueKind.Number)
					return (T)(object)element.GetDouble();
				break;
			case TypeCode.Single:
				if (element.ValueKind == JsonValueKind.Number)
					return (T)(object)element.GetSingle();
				break;
			case TypeCode.Int64:
				if (element.ValueKind == JsonValueKind.Number)
					return (T)(object)element.GetInt64();
				break;
			case TypeCode.DateTime:
				if (element.ValueKind == JsonValueKind.String)
					return (T)(object)element.GetDateTime();
				break;
			case TypeCode.Object:
				if (type == typeof(Guid) && element.ValueKind == JsonValueKind.String)
					return (T)(object)element.GetGuid();
				else if (type == typeof(TimeSpan) && element.ValueKind == JsonValueKind.String)
					return (T)(object)TimeSpan.Parse(element.GetString()!);
				else if (type.IsGenericType)
				{
					if (type.GetGenericTypeDefinition() == typeof(List<>))
					{
						var listType = type.GetGenericArguments()[0];
						var list = Activator.CreateInstance(type) as System.Collections.IList;
						foreach (var item in element.EnumerateArray())
						{
							list!.Add(DeserializeJsonElement(item, listType));
						}
						return (T)list!;
					}
					else if (type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
					{
						var keyType = type.GetGenericArguments()[0];
						var valueType = type.GetGenericArguments()[1];
						var dict = Activator.CreateInstance(type) as System.Collections.IDictionary;
						foreach (var item in element.EnumerateObject())
						{
							var key = Convert.ChangeType(item.Name, keyType);
							var value = DeserializeJsonElement(item.Value, valueType);
							dict!.Add(key, value);
						}
						return (T)dict!;
					}
				}
				// For other complex types, use JsonSerializer
				return JsonSerializer.Deserialize<T>(element.GetRawText());
		}

		return default;
	}

	private object? DeserializeJsonElement(JsonElement element, Type type)
	{
		var method = typeof(Player).GetMethod(nameof(DeserializeJsonElement), BindingFlags.NonPublic | BindingFlags.Instance);
		var genericMethod = method!.MakeGenericMethod(type);
		return genericMethod.Invoke(this, [element]);
	}

	public static IStringLocalizer? GetModuleLocalizer(string moduleID)
	{
		if (moduleDefaultSettings.TryGetValue(moduleID, out var moduleInfo))
		{
			return moduleInfo.Localizer;
		}
		return null;
	}

	public async Task LoadPlayerData()
	{
		await LoadDataAsync(Settings, TABLE_PLAYER_SETTINGS, moduleDefaultSettings);
		await LoadDataAsync(Storage, TABLE_PLAYER_STORAGE, moduleDefaultStorage);

		Server.NextFrame(() =>
		{
			_plugin._moduleServices?.InvokeZenithPlayerLoaded(Controller!);
		});
	}

	private async Task LoadDataAsync(ConcurrentDictionary<string, object?> targetDict, string tableName, ConcurrentDictionary<string, (ConcurrentDictionary<string, object?> Settings, IStringLocalizer? Localizer)> defaults)
	{
		try
		{
			string tablePrefix = _plugin.Database.TablePrefix;
			using var connection = _plugin.Database.CreateConnection();
			await connection.OpenAsync();
			var query = $@"
				SELECT * FROM `{MySqlHelper.EscapeString(tablePrefix)}{tableName}`
				WHERE `steam_id` = @SteamID;";
			var result = await connection.QueryFirstOrDefaultAsync(query, new { SteamID = SteamID.ToString() });

			await UpdateLastOnline();

			Server.NextFrame(() =>
			{
				if (result != null)
				{
					foreach (var property in result)
					{
						if (property.Value != null && (property.Key.EndsWith(".settings") || property.Key.EndsWith(".storage")))
						{
							var moduleID = property.Key.Split('.')[0];
							var data = JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.ToString());
							foreach (var item in data)
							{
								targetDict[$"{moduleID}.{item.Key}"] = item.Value;
							}
						}
					}
				}

				ApplyDefaultValues(defaults, targetDict);
			});
		}
		catch (Exception ex)
		{
			_plugin.Logger.LogError($"Error loading player data for player {SteamID}: {ex.Message}");
		}
	}

	private async Task UpdateLastOnline()
	{
		try
		{
			string tablePrefix = _plugin.Database.TablePrefix;
			using var connection = _plugin.Database.CreateConnection();
			await connection.OpenAsync();
			var query = $@"
				INSERT INTO `{MySqlHelper.EscapeString(tablePrefix)}{TABLE_PLAYER_SETTINGS}` (`steam_id`, `last_online`)
				VALUES (@SteamID, NOW())
				ON DUPLICATE KEY UPDATE `last_online` = NOW();

				INSERT INTO `{MySqlHelper.EscapeString(tablePrefix)}{TABLE_PLAYER_STORAGE}` (`steam_id`, `last_online`)
				VALUES (@SteamID, NOW())
				ON DUPLICATE KEY UPDATE `last_online` = NOW();";
			await connection.ExecuteAsync(query, new { SteamID = SteamID.ToString() });
		}
		catch (Exception ex)
		{
			_plugin.Logger.LogError($"Error updating last online for player {SteamID}: {ex.Message}");
		}
	}

	private static void ApplyDefaultValues(ConcurrentDictionary<string, (ConcurrentDictionary<string, object?> Settings, IStringLocalizer? Localizer)> defaults, ConcurrentDictionary<string, object?> target)
	{
		foreach (var module in defaults)
		{
			foreach (var item in module.Value.Settings)
			{
				var fullKey = $"{module.Key}.{item.Key}";
				if (!target.ContainsKey(fullKey))
				{
					target[fullKey] = item.Value;
				}
			}
		}
	}

	public void SaveSettings(string? moduleID = null)
		=> SavePlayerData(moduleID, false);
	public void SaveStorage(string? moduleID = null)
		=> SavePlayerData(moduleID, true);
	public void SaveAll(string? moduleID = null)
		=> SavePlayerData(moduleID, null);

	private void SavePlayerData(string? moduleID, bool? isStorage)
	{
		Task.Run(async () =>
		{
			if (moduleID != null)
			{
				if (isStorage.HasValue)
				{
					await SavePlayerDataAsync(moduleID, isStorage.Value);
				}
				else
				{
					await SavePlayerDataAsync(moduleID, false);
					await SavePlayerDataAsync(moduleID, true);
				}
			}
			else
			{
				if (isStorage.HasValue)
				{
					await SaveAllPlayerDataAsync(isStorage.Value);
				}
				else
				{
					await SaveAllPlayerDataAsync(false);
					await SaveAllPlayerDataAsync(true);
				}
			}
		});
	}

	private async Task SaveAllDataAsync()
	{
		await SaveAllPlayerDataAsync(false);
		await SaveAllPlayerDataAsync(true);
	}

	private async Task SavePlayerDataAsync(string moduleID, bool isStorage)
	{
		try
		{
			string tablePrefix = _plugin.Database.TablePrefix;
			string tableName = isStorage ? TABLE_PLAYER_STORAGE : TABLE_PLAYER_SETTINGS;
			using var connection = _plugin.Database.CreateConnection();
			await connection.OpenAsync();

			var columnName = isStorage ? $"{moduleID}.storage" : $"{moduleID}.settings";
			var targetDict = isStorage ? Storage : Settings;
			var moduleData = targetDict
				.Where(kvp => kvp.Key.StartsWith($"{moduleID}."))
				.ToDictionary(kvp => kvp.Key.Split('.')[1], kvp => kvp.Value);

			var jsonValue = JsonSerializer.Serialize(moduleData);

			var query = $@"
				INSERT INTO `{MySqlHelper.EscapeString(tablePrefix)}{tableName}` (`steam_id`, `{MySqlHelper.EscapeString(columnName)}`)
				VALUES (@SteamID, @JsonValue)
				ON DUPLICATE KEY UPDATE `{MySqlHelper.EscapeString(columnName)}` = @JsonValue;";

			await connection.ExecuteAsync(query, new { SteamID = SteamID.ToString(), JsonValue = jsonValue });
		}
		catch (Exception ex)
		{
			_plugin.Logger.LogError($"Error saving player data for player {SteamID}: {ex.Message}");
		}
	}

	private async Task SaveAllPlayerDataAsync(bool isStorage)
	{
		try
		{
			string tablePrefix = _plugin.Database.TablePrefix;
			string tableName = isStorage ? TABLE_PLAYER_STORAGE : TABLE_PLAYER_SETTINGS;
			using var connection = _plugin.Database.CreateConnection();
			await connection.OpenAsync();

			var targetDict = isStorage ? Storage : Settings;
			var dataToSave = new Dictionary<string, string>();

			foreach (var moduleGroup in targetDict.GroupBy(kvp => kvp.Key.Split('.')[0]))
			{
				var moduleID = moduleGroup.Key;
				var moduleData = moduleGroup.ToDictionary(kvp => kvp.Key.Split('.')[1], kvp => kvp.Value);
				if (moduleData.Count > 0) // Only add if there's data to save
				{
					dataToSave[$"{moduleID}.{(isStorage ? "storage" : "settings")}"] = JsonSerializer.Serialize(moduleData);
				}
			}

			if (dataToSave.Count == 0)
			{
				return; // No data to save
			}

			var columns = string.Join(", ", dataToSave.Keys.Select(k => $"`{MySqlHelper.EscapeString(k)}`"));
			var parameters = string.Join(", ", dataToSave.Keys.Select(k => $"@p_{k.Replace("-", "_")}"));
			var updateStatements = string.Join(", ", dataToSave.Keys.Select(k => $"`{MySqlHelper.EscapeString(k)}` = @p_{k.Replace("-", "_")}"));

			var query = $@"
				INSERT INTO `{MySqlHelper.EscapeString(tablePrefix)}{tableName}` (`steam_id`, {columns})
				VALUES (@p_SteamID, {parameters})
				ON DUPLICATE KEY UPDATE {updateStatements};";

			var queryParams = new DynamicParameters();
			queryParams.Add("@p_SteamID", SteamID.ToString());
			foreach (var item in dataToSave)
			{
				queryParams.Add($"@p_{item.Key.Replace("-", "_")}", item.Value);
			}

			await connection.ExecuteAsync(query, queryParams);
		}
		catch (Exception ex)
		{
			_plugin.Logger.LogError($"Error saving player data for player {SteamID}: {ex.Message}");
		}
	}

	public void ResetModuleSettings()
		=> ResetModuleData(false);
	public void ResetModuleStorage()
		=> ResetModuleData(true);

	private void ResetModuleData(bool isStorage)
	{
		string callerPlugin = CallerIdentifier.GetCallingPluginName();

		var defaults = isStorage ? moduleDefaultStorage : moduleDefaultSettings;
		var targetDict = isStorage ? Storage : Settings;

		if (defaults.TryGetValue(callerPlugin, out var defaultData))
		{
			foreach (var item in defaultData.Settings)
			{
				targetDict[$"{callerPlugin}.{item.Key}"] = item.Value;
			}
			SavePlayerData(callerPlugin, isStorage);
		}
		else
		{
			_plugin.Logger.LogWarning($"Attempted to reset non-existent module data: {callerPlugin}");
		}
	}

	public static void LoadAllOnlinePlayerData(Plugin plugin, bool blockEvent = false)
	{
		string tablePrefix = plugin.Database.TablePrefix;
		List<string> steamIds = [];

		var playerList = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV);

		steamIds = playerList.Select(p => p.SteamID.ToString()).ToList();

		Task.Run(async () =>
		{
			if (steamIds.Count == 0)
				return;
			try
			{
				using var connection = plugin.Database.CreateConnection();
				await connection.OpenAsync();

				var settingsQuery = $@"
					SELECT * FROM `{MySqlHelper.EscapeString(tablePrefix)}{TABLE_PLAYER_SETTINGS}`
					WHERE `steam_id` IN @SteamIDs;";
				var settingsResults = await connection.QueryAsync(settingsQuery, new { SteamIDs = steamIds });

				var storageQuery = $@"
					SELECT * FROM `{MySqlHelper.EscapeString(tablePrefix)}{TABLE_PLAYER_STORAGE}`
					WHERE `steam_id` IN @SteamIDs;";
				var storageResults = await connection.QueryAsync(storageQuery, new { SteamIDs = steamIds });

				Server.NextFrame(() =>
				{
					foreach (var controller in playerList)
					{
						var player = Player.Find(controller);
						if (player == null)
							continue;

						try
						{
							var playerSettings = settingsResults.FirstOrDefault(r => r.steam_id == player.SteamID.ToString());
							var playerStorage = storageResults.FirstOrDefault(r => r.steam_id == player.SteamID.ToString());

							if (playerSettings != null)
								LoadPlayerData(player.Settings, playerSettings, moduleDefaultSettings, plugin);

							if (playerStorage != null)
								LoadPlayerData(player.Storage, playerStorage, moduleDefaultStorage, plugin);
						}
						catch (Exception ex)
						{
							plugin.Logger.LogError($"Error loading player data for player {player.SteamID}: {ex.Message}");
						}

						if (!blockEvent)
							plugin._moduleServices?.InvokeZenithPlayerLoaded(player.Controller!);
					}
				});
			}
			catch (Exception ex)
			{
				plugin.Logger.LogError($"An error occurred while querying the database: {ex.Message}");
			}
		});
	}

	public static void SaveAllOnlinePlayerData(Plugin plugin, bool dipose)
	{
		string tablePrefix = plugin.Database.TablePrefix;
		var playerDataToSave = new ConcurrentDictionary<string, (Dictionary<string, string> Settings, Dictionary<string, string> Storage)>();

		if (!List.Any(p => p.IsValid))
			return;

		foreach (var player in List.Where(p => p.IsValid))
		{
			var settingsData = new Dictionary<string, string>();
			var storageData = new Dictionary<string, string>();

			foreach (var isStorage in new[] { false, true })
			{
				var targetDict = isStorage ? player.Storage : player.Settings;
				var dataDict = isStorage ? storageData : settingsData;

				foreach (var moduleGroup in targetDict.GroupBy(kvp => kvp.Key.Split('.')[0]))
				{
					var moduleID = moduleGroup.Key;
					var moduleData = moduleGroup.ToDictionary(kvp => kvp.Key.Split('.')[1], kvp => kvp.Value);
					dataDict[$"{moduleID}.{(isStorage ? "storage" : "settings")}"] = JsonSerializer.Serialize(moduleData);
				}
			}

			playerDataToSave[player.SteamID.ToString()] = (settingsData, storageData);
		}

		Task.Run(async () =>
		{
			if (playerDataToSave.IsEmpty)
				return;

			try
			{
				using var connection = plugin.Database.CreateConnection();
				await connection.OpenAsync();

				foreach (var isStorage in new[] { false, true })
				{
					string tableName = isStorage ? TABLE_PLAYER_STORAGE : TABLE_PLAYER_SETTINGS;

					foreach (var playerData in playerDataToSave)
					{
						var steamId = playerData.Key;
						var data = isStorage ? playerData.Value.Storage : playerData.Value.Settings;

						if (data.Count == 0)
							continue;

						var columns = string.Join(", ", data.Keys.Select(k => $"`{k}`"));
						var parameters = string.Join(", ", data.Keys.Select((k, i) => $"@param{i}"));
						var updateStatements = string.Join(", ", data.Keys.Select((k, i) => $"`{k}` = @param{i}"));

						var query = $@"
                        INSERT INTO `{MySqlHelper.EscapeString(tablePrefix)}{tableName}` (`steam_id`, {columns})
                        VALUES (@steamId, {parameters})
                        ON DUPLICATE KEY UPDATE {updateStatements};";

						var queryParams = new DynamicParameters();
						queryParams.Add("@steamId", steamId);
						int i = 0;
						foreach (var item in data)
						{
							queryParams.Add($"@param{i}", item.Value);
							i++;
						}

						await connection.ExecuteAsync(query, queryParams);
					}
				}

				if (dipose)
				{
					foreach (var player in List)
					{
						player.Settings.Clear();
						player.Storage.Clear();
					}

					moduleDefaultSettings.Clear();
					moduleDefaultStorage.Clear();
				}
			}
			catch (Exception ex)
			{
				plugin.Logger.LogError($"An error occurred while saving player data: {ex.Message}");
			}
		});
	}

	private static void LoadPlayerData(ConcurrentDictionary<string, object?> targetDict, dynamic data, ConcurrentDictionary<string, (ConcurrentDictionary<string, object?> Settings, IStringLocalizer? Localizer)> defaults, Plugin plugin)
	{
		foreach (var property in (IDictionary<string, object>)data)
		{
			if ((property.Key.EndsWith(".settings") || property.Key.EndsWith(".storage")) && property.Value != null)
			{
				var moduleID = property.Key.Split('.')[0];
				try
				{
					string? jsonString = property.Value?.ToString();
					if (string.IsNullOrEmpty(jsonString))
					{
						plugin.Logger.LogWarning($"Empty or null JSON string for module {moduleID}.");
						continue;
					}

					var moduleData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
					if (moduleData != null)
					{
						foreach (var item in moduleData)
						{
							targetDict[$"{moduleID}.{item.Key}"] = item.Value;
						}
					}
					else
					{
						plugin.Logger.LogWarning($"Deserialized data for module {moduleID} is null.");
					}
				}
				catch (JsonException ex)
				{
					plugin.Logger.LogError($"Error deserializing data for module {moduleID}: {ex.Message}");
				}
			}
		}

		ApplyDefaultValues(defaults, targetDict);
	}

	public static void DisposeModuleData(Plugin plugin, string callerPlugin)
	{
		SaveAllOnlinePlayerData(plugin, false);

		foreach (var player in List.Where(p => p.IsValid))
		{
			player.Storage.TryRemove(callerPlugin, out _);
			player.Settings.TryRemove(callerPlugin, out _);
		}

		moduleDefaultSettings.TryRemove(callerPlugin, out _);
		moduleDefaultStorage.TryRemove(callerPlugin, out _);
	}

	public static void Dispose(Plugin plugin)
	{
		SaveAllOnlinePlayerData(plugin, true);
	}
}
