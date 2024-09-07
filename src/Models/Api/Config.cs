using System.Collections.Concurrent;
using CounterStrikeSharp.API.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API;
using System.Reflection;
using ZenithAPI;

namespace Zenith
{
	public sealed partial class Plugin : BasePlugin
	{
		private ConfigManager _configManager = null!;

		public void Initialize_Config()
		{
			try
			{
				string configDirectory = Path.Combine(Server.GameDirectory, "csgo", "addons", "counterstrikesharp", "configs", "zenith");
				_configManager = new ConfigManager(configDirectory, Logger);
				RegisterCoreConfigs();
			}
			catch (Exception ex)
			{
				Logger.LogError($"Failed to initialize config: {ex.Message}");
				throw;
			}
		}

		public ModuleConfigAccessor GetModuleConfigAccessor()
		{
			string callerPlugin = CallerIdentifier.GetCallingPluginName();
			Logger.LogInformation($"Module {callerPlugin} requested config accessor.");
			return _configManager.GetModuleAccessor(callerPlugin);
		}

		public static void RegisterModuleConfig<T>(string groupName, string configName, string description, T defaultValue, ConfigFlag flags = ConfigFlag.None) where T : notnull
		{
			string callerPlugin = CallerIdentifier.GetCallingPluginName();
			ConfigManager.RegisterConfig(callerPlugin, groupName, configName, description, defaultValue, flags);
		}

		public static bool HasModuleConfigValue(string groupName, string configName)
		{
			string callerPlugin = CallerIdentifier.GetCallingPluginName();
			return ConfigManager.HasConfigValue(callerPlugin, groupName, configName);
		}

		public static T GetModuleConfigValue<T>(string groupName, string configName) where T : notnull
		{
			string callerPlugin = CallerIdentifier.GetCallingPluginName();
			return ConfigManager.GetConfigValue<T>(callerPlugin, groupName, configName);
		}

		public static void SetModuleConfigValue<T>(string groupName, string configName, T value) where T : notnull
		{
			string callerPlugin = CallerIdentifier.GetCallingPluginName();
			ConfigManager.SetConfigValue(callerPlugin, groupName, configName, value);
		}
	}

	public class ModuleConfigAccessor : IModuleConfigAccessor
	{
		private readonly string _moduleName;

		internal ModuleConfigAccessor(string moduleName)
		{
			_moduleName = moduleName;
		}

		public T GetValue<T>(string groupName, string configName) where T : notnull
		{
			return ConfigManager.GetConfigValue<T>(_moduleName, groupName, configName);
		}

		public void SetValue<T>(string groupName, string configName, T value) where T : notnull
		{
			ConfigManager.SetConfigValue(_moduleName, groupName, configName, value);
		}

		public bool HasValue(string groupName, string configName)
		{
			return ConfigManager.HasConfigValue(_moduleName, groupName, configName);
		}
	}

	public class ConfigItem
	{
		public required string Name { get; set; }
		public required string Description { get; set; }
		public required object DefaultValue { get; set; }
		public required object CurrentValue { get; set; }
		[YamlIgnore]
		public ConfigFlag Flags { get; set; }
	}

	public class ConfigGroup
	{
		public required string Name { get; set; }
		public ConcurrentDictionary<string, ConfigItem> Items { get; set; } = new ConcurrentDictionary<string, ConfigItem>();
	}

	public class ModuleConfig
	{
		public required string ModuleName { get; set; }
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
		public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
		public ConcurrentDictionary<string, ConfigGroup> Groups { get; set; } = new ConcurrentDictionary<string, ConfigGroup>();
	}

	public class ConfigManager
	{
		private static string CoreModuleName => Assembly.GetEntryAssembly()?.GetName().Name ?? "K4-Zenith";
		private static string _baseConfigDirectory = string.Empty;
		private static readonly ConcurrentDictionary<string, ModuleConfig> _moduleConfigs = new ConcurrentDictionary<string, ModuleConfig>();
		private static ILogger Logger = null!;
		public static bool GlobalChangeTracking { get; set; } = false;
		public static bool GlobalAutoReloadEnabled { get; set; } = false;
		private static FileSystemWatcher? _watcher;

		public ConfigManager(string baseConfigDirectory, ILogger logger)
		{
			_baseConfigDirectory = baseConfigDirectory;
			Logger = logger;
			Directory.CreateDirectory(_baseConfigDirectory);
			Directory.CreateDirectory(Path.Combine(_baseConfigDirectory, "modules"));

			SetupFileWatcher();
		}

		public ModuleConfigAccessor GetModuleAccessor(string moduleName)
		{
			return new ModuleConfigAccessor(moduleName);
		}

		private static void SetupFileWatcher()
		{
			_watcher = new FileSystemWatcher(_baseConfigDirectory)
			{
				NotifyFilter = NotifyFilters.LastWrite,
				Filter = "*.yaml",
				IncludeSubdirectories = true,
				EnableRaisingEvents = GlobalAutoReloadEnabled
			};

			_watcher.Changed += OnConfigFileChanged;
			_watcher.Created += OnConfigFileChanged;
		}

		public static void Dispose()
		{
			_watcher?.Dispose();
		}

		public static void SetGlobalAutoReload(bool enabled)
		{
			GlobalAutoReloadEnabled = enabled;
			if (_watcher != null)
			{
				_watcher.EnableRaisingEvents = enabled;
			}
		}

		private static Timer? _debounceTimer;

		private static void OnConfigFileChanged(object sender, FileSystemEventArgs e)
		{
			if (!GlobalAutoReloadEnabled)
				return;

			_debounceTimer?.Dispose();
			_debounceTimer = new Timer(DebounceCallback, e.FullPath, 1000, Timeout.Infinite);
		}

		private static void DebounceCallback(object? state)
		{
			var fullPath = (string)state!;
			var relativePath = Path.GetRelativePath(_baseConfigDirectory, fullPath);
			var pathParts = relativePath.Split(Path.DirectorySeparatorChar);
			if (pathParts.Length >= 2 && pathParts[0] == "modules")
			{
				var moduleName = Path.GetFileNameWithoutExtension(pathParts[1]);
				ReloadModuleConfig(moduleName);
			}
			else if (pathParts.Length == 1 && pathParts[0] == "core.yaml")
			{
				ReloadModuleConfig(CoreModuleName);
			}
		}

		private static void ReloadModuleConfig(string moduleName, bool force = false)
		{
			if (!GlobalAutoReloadEnabled && !force)
				return;

			var newConfig = LoadModuleConfig(moduleName);
			_moduleConfigs[moduleName] = newConfig;

			if (!force)
				Logger.LogInformation($"Config reloaded for module {moduleName}");
		}

		public static void ReloadAllConfigs()
		{
			foreach (var moduleName in _moduleConfigs.Keys)
			{
				ReloadModuleConfig(moduleName, true);
			}

			Logger.LogInformation("All Zenith configurations reloaded.");
		}

		public static void RegisterConfig<T>(string moduleName, string groupName, string configName, string description, T defaultValue, ConfigFlag flags) where T : notnull
		{
			var moduleConfig = _moduleConfigs.GetOrAdd(moduleName, k => LoadModuleConfig(moduleName));

			var group = moduleConfig.Groups.GetOrAdd(groupName, new ConfigGroup { Name = groupName });

			var existingConfig = group.Items.GetOrAdd(configName, new ConfigItem
			{
				Name = configName,
				Description = description,
				DefaultValue = defaultValue,
				CurrentValue = defaultValue,
				Flags = flags
			});

			existingConfig.Flags = flags;

			SaveModuleConfig(moduleName);
		}

		public static bool HasConfigValue(string callerModule, string groupName, string configName)
		{
			if (_moduleConfigs.TryGetValue(callerModule, out var moduleConfig))
			{
				var group = moduleConfig.Groups.FirstOrDefault(g => g.Key == groupName);
				return group.Value?.Items.ContainsKey(configName) ?? false;
			}
			return false;
		}

		public static T GetConfigValue<T>(string callerModule, string groupName, string configName) where T : notnull
		{
			if (_moduleConfigs.TryGetValue(callerModule, out var moduleConfig))
			{
				var (found, value) = TryGetConfigValue<T>(moduleConfig, groupName, configName, callerModule);
				if (found)
					return value;
			}

			foreach (var config in _moduleConfigs.Values)
			{
				if (config.ModuleName != callerModule)
				{
					var (found, value) = TryGetConfigValue<T>(config, groupName, configName, callerModule, checkGlobalOnly: true);
					if (found)
						return value;
				}
			}

			throw new KeyNotFoundException($"Configuration '{groupName}.{configName}' not found for module '{callerModule}'");
		}

		private static (bool found, T value) TryGetConfigValue<T>(ModuleConfig config, string groupName, string configName, string callerModule, bool checkGlobalOnly = false) where T : notnull
		{
			var group = config.Groups.FirstOrDefault(g => g.Key == groupName);
			var configItem = group.Value?.Items.GetValueOrDefault(configName);

			if (configItem != null)
			{
				if (checkGlobalOnly && !configItem.Flags.HasFlag(ConfigFlag.Global))
				{
					Logger.LogWarning($"Config '{groupName}.{configName}' not allowed to be accessed globally");
					return (false, default!);
				}

				if (callerModule != CoreModuleName && callerModule != config.ModuleName && !configItem.Flags.HasFlag(ConfigFlag.Global))
				{
					Logger.LogWarning($"Attempt to access non-global config '{groupName}.{configName}' from module '{callerModule}'");
					return (false, default!);
				}

				if (configItem.CurrentValue == null)
				{
					Logger.LogWarning($"Config '{groupName}.{configName}' has a null value for module '{config.ModuleName}'");
					throw new InvalidOperationException($"Configuration '{groupName}.{configName}' has null value for module '{config.ModuleName}'");
				}

				try
				{
					if (typeof(T) == typeof(string) && configItem.CurrentValue is string currentString && string.IsNullOrEmpty(currentString))
					{
						return (true, (T)(object)"");
					}

					if (typeof(T) == typeof(List<string>) && configItem.CurrentValue is List<object> objectList)
					{
						var stringList = objectList.Select(o => o.ToString() ?? string.Empty).ToList();
						return (true, (T)(object)stringList);
					}

					return (true, (T)Convert.ChangeType(configItem.CurrentValue, typeof(T)));
				}
				catch (InvalidCastException ex)
				{
					Logger.LogError($"Failed to cast config value for '{groupName}.{configName}' to type {typeof(T)}. Stored type: {configItem.CurrentValue.GetType()}. Error: {ex.Message}");
					throw;
				}
			}

			return (false, default!);
		}

		public static void SetConfigValue<T>(string callerModule, string groupName, string configName, T value) where T : notnull
		{
			if (_moduleConfigs.TryGetValue(callerModule, out var moduleConfig))
			{
				if (TrySetConfigValue(moduleConfig, groupName, configName, value, callerModule))
				{
					return;
				}
			}

			foreach (var config in _moduleConfigs.Values)
			{
				if (config.ModuleName != callerModule)
				{
					if (TrySetConfigValue(config, groupName, configName, value, callerModule, checkGlobalOnly: true))
					{
						return;
					}
				}
			}

			throw new KeyNotFoundException($"Configuration '{groupName}.{configName}' not found for module '{callerModule}'");
		}

		private static bool TrySetConfigValue<T>(ModuleConfig moduleConfig, string groupName, string configName, T value, string callerModule, bool checkGlobalOnly = false) where T : notnull
		{
			var group = moduleConfig.Groups.FirstOrDefault(g => g.Key == groupName);
			var config = group.Value?.Items.GetValueOrDefault(configName);

			if (config != null)
			{
				if (checkGlobalOnly && !config.Flags.HasFlag(ConfigFlag.Global))
				{
					return false;
				}

				if (callerModule != CoreModuleName && callerModule != moduleConfig.ModuleName && !config.Flags.HasFlag(ConfigFlag.Global))
				{
					Logger.LogWarning($"Attempt to modify non-global config '{groupName}.{configName}' from module '{callerModule}'");
					return false;
				}

				if (callerModule != CoreModuleName && callerModule != moduleConfig.ModuleName)
				{
					if (config.Flags.HasFlag(ConfigFlag.Locked))
					{
						Logger.LogWarning($"Attempt to modify locked configuration '{groupName}.{configName}' for module '{callerModule}'");
						return false;
					}

					if (config.Flags.HasFlag(ConfigFlag.Protected))
					{
						throw new InvalidOperationException($"Cannot modify protected configuration '{groupName}.{configName}' for module '{callerModule}'");
					}
				}

				config.CurrentValue = value;

				if (GlobalChangeTracking || config.Flags.HasFlag(ConfigFlag.Global) || callerModule == CoreModuleName)
				{
					SaveModuleConfig(moduleConfig.ModuleName);
				}

				return true;
			}

			return false;
		}

		private static ModuleConfig LoadModuleConfig(string moduleName)
		{
			string filePath = moduleName == CoreModuleName
				? Path.Combine(_baseConfigDirectory, "core.yaml")
				: Path.Combine(_baseConfigDirectory, "modules", $"{moduleName}.yaml");

			var newConfig = new ModuleConfig { ModuleName = moduleName };

			if (File.Exists(filePath))
			{
				try
				{
					var deserializer = new DeserializerBuilder()
						.WithNamingConvention(CamelCaseNamingConvention.Instance)
						.IgnoreUnmatchedProperties()
						.Build();

					var yaml = File.ReadAllText(filePath);
					var loadedConfig = deserializer.Deserialize<ModuleConfig>(yaml);

					if (loadedConfig != null)
					{
						var existingConfig = _moduleConfigs.GetOrAdd(moduleName, k => new ModuleConfig { ModuleName = moduleName });

						foreach (var group in loadedConfig.Groups)
						{
							var configGroup = existingConfig.Groups.GetOrAdd(group.Key, new ConfigGroup { Name = group.Value.Name });

							foreach (var item in group.Value.Items)
							{
								var existingItem = configGroup.Items.GetOrAdd(item.Key, new ConfigItem
								{
									Name = item.Value.Name,
									Description = item.Value.Description,
									DefaultValue = item.Value.DefaultValue,
									CurrentValue = item.Value.CurrentValue ?? item.Value.DefaultValue,
									Flags = item.Value.Flags
								});

								existingItem.Flags |= item.Value.Flags;
								existingItem.CurrentValue = item.Value.CurrentValue ?? existingItem.CurrentValue;
							}
						}
					}
				}
				catch (Exception ex)
				{
					Logger.LogError($"Error loading config for module {moduleName}: {ex.Message}");
				}
			}

			return newConfig;
		}

		private static void SaveModuleConfig(string moduleName)
		{
			if (_moduleConfigs.TryGetValue(moduleName, out var moduleConfig))
			{
				CleanupUnusedConfigs(moduleName);

				var serializer = new SerializerBuilder()
					.WithNamingConvention(CamelCaseNamingConvention.Instance)
					.DisableAliases()
					.ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
					.Build();

				var header = $@"# This file was generated by Zenith Core.
#
# Developer: K4ryuu @ KitsuneLab
# Module: {moduleName}
#";

				var yaml = header + serializer.Serialize(moduleConfig);

				string filePath = moduleName == CoreModuleName
					? Path.Combine(_baseConfigDirectory, "core.yaml")
					: Path.Combine(_baseConfigDirectory, "modules", $"{moduleName}.yaml");

				File.WriteAllText(filePath, yaml);

				moduleConfig.LastUpdated = DateTime.Now;
			}
		}

		public static void CleanupUnusedConfigs(string moduleName)
		{
			if (_moduleConfigs.TryGetValue(moduleName, out var moduleConfig))
			{
				foreach (var key in moduleConfig.Groups.Keys.ToList())
				{
					if (moduleConfig.Groups[key].Items.IsEmpty)
					{
						moduleConfig.Groups.TryRemove(key, out _);
					}
				}
			}
		}

		private static bool IsPrimitiveType(Type type)
		{
			if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
			{
				return true;
			}

			var nullableType = Nullable.GetUnderlyingType(type);
			if (nullableType != null)
			{
				return IsPrimitiveType(nullableType);
			}

			if (type.IsEnum)
			{
				return true;
			}

			if (type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid))
			{
				return true;
			}

			return false;
		}
	}
}
