using System.Collections.Concurrent;
using CounterStrikeSharp.API.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using System.Reflection;
using ZenithAPI;
using System.Collections;
using CounterStrikeSharp.API.Modules.Cvars;
using System.Globalization;

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

		public static bool IsModuleConfigPrimitive(string groupName, string configName)
		{
			string callerPlugin = CallerIdentifier.GetCallingPluginName();
			return ConfigManager.IsPrimitiveConfig(callerPlugin, groupName, configName);
		}
	}

	public class ModuleConfigAccessor : IModuleConfigAccessor
	{
		private readonly string _moduleName;
		private readonly ConfigManager _configManager;

		internal ModuleConfigAccessor(string moduleName, ConfigManager configManager)
		{
			_moduleName = moduleName;
			_configManager = configManager;
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
		[YamlMember(Alias = "type")]
		public required string AllowedType { get; set; }
		[YamlIgnore]
		public ConfigFlag Flags { get; set; }
	}

	public class ConfigGroup
	{
		public required string Name { get; set; }
		public List<ConfigItem> Items { get; set; } = new List<ConfigItem>();
	}

	public class ModuleConfig
	{
		public required string ModuleName { get; set; }
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
		public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
		public List<ConfigGroup> Groups { get; set; } = new List<ConfigGroup>();
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
			return new ModuleConfigAccessor(moduleName, this);
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

			if (!force)
				Logger.LogInformation($"Reloading config for module {moduleName}");

			var newConfig = LoadModuleConfig(moduleName);
			_moduleConfigs[moduleName] = newConfig;

			if (!force)
				Logger.LogInformation($"Config reloaded for module {moduleName}");
		}

		public static void ReloadAllConfigs()
		{
			Logger.LogWarning("Reloading all Zenith configurations...");

			foreach (var moduleName in _moduleConfigs.Keys)
			{
				ReloadModuleConfig(moduleName, true);
			}

			Logger.LogInformation("All Zenith configurations reloaded.");
		}

		public static void RegisterConfig<T>(string moduleName, string groupName, string configName, string description, T defaultValue, ConfigFlag flags) where T : notnull
		{
			var moduleConfig = _moduleConfigs.GetOrAdd(moduleName, k => LoadModuleConfig(moduleName));

			var group = moduleConfig.Groups.FirstOrDefault(g => g.Name == groupName);
			if (group == null)
			{
				group = new ConfigGroup { Name = groupName };
				moduleConfig.Groups.Add(group);
			}

			var existingConfig = group.Items.FirstOrDefault(c => c.Name == configName);
			if (existingConfig != null)
			{
				existingConfig.Description = description;
				existingConfig.DefaultValue = defaultValue;
				existingConfig.AllowedType = typeof(T).FullName ?? typeof(T).Name;
				existingConfig.Flags = flags;

				if (existingConfig.CurrentValue == null || existingConfig.CurrentValue.GetType() != typeof(T))
					existingConfig.CurrentValue = defaultValue;
			}
			else
			{
				group.Items.Add(new ConfigItem
				{
					Name = configName,
					Description = description,
					DefaultValue = defaultValue,
					CurrentValue = defaultValue,
					AllowedType = typeof(T).FullName ?? typeof(T).Name,
					Flags = flags,
				});
			}

			SaveModuleConfig(moduleName);
		}

		public static bool HasConfigValue(string callerModule, string groupName, string configName)
		{
			if (_moduleConfigs.TryGetValue(callerModule, out var moduleConfig))
			{
				var group = moduleConfig.Groups.FirstOrDefault(g => g.Name == groupName);
				return group?.Items.Any(c => c.Name == configName) ?? false;
			}
			return false;
		}

		public static T GetConfigValue<T>(string callerModule, string groupName, string configName) where T : notnull
		{
			if (_moduleConfigs.TryGetValue(callerModule, out var moduleConfig))
			{
				var result = TryGetConfigValue<T>(moduleConfig, groupName, configName, callerModule);
				if (result.found)
				{
					return result.value;
				}
			}

			foreach (var config in _moduleConfigs.Values)
			{
				if (config.ModuleName != callerModule)
				{
					var result = TryGetConfigValue<T>(config, groupName, configName, callerModule, checkGlobalOnly: true);
					if (result.found)
					{
						return result.value;
					}
				}
			}

			Logger.LogWarning($"Configuration '{groupName}.{configName}' not found for module '{callerModule}'");
			throw new KeyNotFoundException($"Configuration '{groupName}.{configName}' not found for module '{callerModule}'");
		}

		private static (bool found, T value) TryGetConfigValue<T>(ModuleConfig config, string groupName, string configName, string callerModule, bool checkGlobalOnly = false) where T : notnull
		{
			var group = config.Groups.FirstOrDefault(g => g.Name == groupName);
			var configItem = group?.Items.FirstOrDefault(c => c.Name == configName);

			if (configItem != null)
			{
				if (checkGlobalOnly && !configItem.Flags.HasFlag(ConfigFlag.Global))
				{
					return (false, default!);
				}

				if (callerModule != CoreModuleName && callerModule != config.ModuleName && !configItem.Flags.HasFlag(ConfigFlag.Global))
				{
					Logger.LogWarning($"Attempt to access non-global config '{groupName}.{configName}' from module '{callerModule}'");
					return (false, default!);
				}

				if (configItem.CurrentValue == null)
				{
					throw new InvalidOperationException($"Configuration '{groupName}.{configName}' has null value for module '{config.ModuleName}'");
				}

				try
				{
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
			var group = moduleConfig.Groups.FirstOrDefault(g => g.Name == groupName);
			var config = group?.Items.FirstOrDefault(c => c.Name == configName);

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

				if (!IsValidType(value, config.AllowedType))
				{
					Logger.LogWarning($"Attempt to set invalid type for config '{groupName}.{configName}' in module '{moduleConfig.ModuleName}'. Expected {config.AllowedType}, got {value.GetType().Name}");
					return false;
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

		private static bool IsValidType(object value, string allowedType)
		{
			Type valueType = value.GetType();
			Type? allowedTypeType = Type.GetType(allowedType);

			if (allowedTypeType == null)
			{
				allowedTypeType = AppDomain.CurrentDomain.GetAssemblies()
					.SelectMany(a => a.GetTypes())
					.FirstOrDefault(t => t.FullName == allowedType || t.Name == allowedType);
			}

			if (allowedTypeType != null)
			{
				if (allowedTypeType.IsGenericType && allowedTypeType.GetGenericTypeDefinition() == typeof(List<>))
				{
					var elementType = allowedTypeType.GetGenericArguments()[0];
					return valueType.IsGenericType &&
						   valueType.GetGenericTypeDefinition() == typeof(List<>) &&
						   valueType.GetGenericArguments()[0] == elementType;
				}

				return allowedTypeType.IsAssignableFrom(valueType);
			}

			return false;
		}

		private static ModuleConfig LoadModuleConfig(string moduleName)
		{
			string filePath = moduleName == CoreModuleName
				? Path.Combine(_baseConfigDirectory, "core.yaml")
				: Path.Combine(_baseConfigDirectory, "modules", $"{moduleName}.yaml");

			ModuleConfig config = new ModuleConfig { ModuleName = moduleName };

			if (File.Exists(filePath))
			{
				try
				{
					var deserializer = new DeserializerBuilder()
						.WithNamingConvention(CamelCaseNamingConvention.Instance)
						.IgnoreUnmatchedProperties()
						.Build();

					var yaml = File.ReadAllText(filePath);
					config = deserializer.Deserialize<ModuleConfig>(yaml) ?? config;

					config.ModuleName = moduleName;

					foreach (var group in config.Groups)
					{
						foreach (var item in group.Items)
						{
							if (item.CurrentValue != null)
							{
								Type targetType = Type.GetType(item.AllowedType) ??
									throw new InvalidOperationException($"Unknown type: {item.AllowedType}");

								try
								{
									item.CurrentValue = ConvertValue(item.CurrentValue, targetType);
								}
								catch
								{
									Logger.LogWarning($"Invalid type for config '{group.Name}.{item.Name}' in module '{moduleName}'. Expected {item.AllowedType}, got {item.CurrentValue.GetType().Name}. Using default value.");
									item.CurrentValue = item.DefaultValue;
								}
							}
							else
							{
								item.CurrentValue = item.DefaultValue;
							}
						}
					}
				}
				catch (Exception ex)
				{
					Logger.LogError($"Error loading config for module {moduleName}: {ex.Message}");
				}
			}

			return config;
		}

		private static object ConvertValue(object value, Type targetType)
		{
			if (targetType == typeof(bool))
			{
				return value is string boolStr ? bool.Parse(boolStr) : Convert.ToBoolean(value);
			}
			else if (targetType == typeof(int))
			{
				return value is string intStr ? int.Parse(intStr, CultureInfo.InvariantCulture) : Convert.ToInt32(value);
			}
			else if (targetType == typeof(double))
			{
				return value is string doubleStr ? double.Parse(doubleStr, CultureInfo.InvariantCulture) : Convert.ToDouble(value, CultureInfo.InvariantCulture);
			}
			else if (targetType == typeof(float))
			{
				return value is string floatStr ? float.Parse(floatStr, CultureInfo.InvariantCulture) : Convert.ToSingle(value, CultureInfo.InvariantCulture);
			}
			else if (targetType == typeof(decimal))
			{
				return value is string decimalStr ? decimal.Parse(decimalStr, CultureInfo.InvariantCulture) : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
			}
			else if (targetType == typeof(string))
			{
				return value.ToString()!;
			}
			else if (targetType == typeof(DateTime))
			{
				return value is string dateStr ? DateTime.Parse(dateStr) : Convert.ToDateTime(value);
			}
			else if (targetType == typeof(TimeSpan))
			{
				return value is string timeSpanStr ? TimeSpan.Parse(timeSpanStr) : (TimeSpan)value;
			}
			else if (targetType == typeof(Guid))
			{
				return value is string guidStr ? Guid.Parse(guidStr) : (Guid)value;
			}
			else if (targetType.IsEnum)
			{
				return Enum.Parse(targetType, value.ToString()!);
			}
			else if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
			{
				if (value == null)
					return null!;
				var underlyingType = Nullable.GetUnderlyingType(targetType);
				return ConvertValue(value, underlyingType!);
			}
			else if (targetType == typeof(Uri))
			{
				return new Uri(value.ToString()!);
			}
			else if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
			{
				var elementType = targetType.GetGenericArguments()[0];
				var listType = typeof(List<>).MakeGenericType(elementType);
				var list = Activator.CreateInstance(listType);

				if (value is IEnumerable enumerable)
				{
					var addMethod = listType.GetMethod("Add");
					foreach (var item in enumerable)
					{
						var convertedItem = ConvertValue(item, elementType);
						addMethod?.Invoke(list, new[] { convertedItem });
					}
				}
				else
				{
					var convertedItem = ConvertValue(value, elementType);
					var addMethod = listType.GetMethod("Add");
					addMethod?.Invoke(list, new[] { convertedItem });
				}

				return list!;
			}
			else if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
			{
				var keyType = targetType.GetGenericArguments()[0];
				var valueType = targetType.GetGenericArguments()[1];
				var dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
				var dict = Activator.CreateInstance(dictType);

				if (value is IDictionary sourceDictionary)
				{
					var addMethod = dictType.GetMethod("Add");
					foreach (DictionaryEntry entry in sourceDictionary)
					{
						var convertedKey = ConvertValue(entry.Key, keyType);
						var convertedValue = ConvertValue(entry.Value!, valueType);
						addMethod?.Invoke(dict, new[] { convertedKey, convertedValue });
					}
				}

				return dict!;
			}
			else
			{
				return Convert.ChangeType(value, targetType);
			}
		}

		private static void SaveModuleConfig(string moduleName)
		{
			if (_moduleConfigs.TryGetValue(moduleName, out var moduleConfig))
			{
				CleanupUnusedConfigs(moduleName);

				var serializer = new SerializerBuilder()
					.WithNamingConvention(CamelCaseNamingConvention.Instance)
					.WithTypeConverter(new SystemTypeConverter())
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
				foreach (var group in moduleConfig.Groups)
				{
					group.Items.RemoveAll(c => c.CurrentValue == null);
				}

				moduleConfig.Groups.RemoveAll(g => g.Items.Count == 0);
			}
		}

		public static bool IsPrimitiveConfig(string callerModule, string groupName, string configName)
		{
			if (_moduleConfigs.TryGetValue(callerModule, out var moduleConfig))
			{
				var group = moduleConfig.Groups.FirstOrDefault(g => g.Name == groupName);
				var config = group?.Items.FirstOrDefault(c => c.Name == configName);

				if (config != null)
				{
					if (callerModule != CoreModuleName && !config.Flags.HasFlag(ConfigFlag.Global) && callerModule != moduleConfig.ModuleName)
					{
						Logger.LogWarning($"Attempt to access non-global config '{groupName}.{configName}' from module '{callerModule}'");
						return false;
					}

					Type? configType = Type.GetType(config.AllowedType);
					if (configType != null)
					{
						return IsPrimitiveType(configType);
					}
				}
			}

			Logger.LogWarning($"Configuration '{groupName}.{configName}' not found for module '{callerModule}'");
			return false;
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

	public class SystemTypeConverter : IYamlTypeConverter
	{
		public bool Accepts(Type type) => type == typeof(Type);

		public object? ReadYaml(IParser parser, Type type, ObjectDeserializer nestedObjectDeserializer)
		{
			var scalar = parser.Consume<Scalar>();
			var typeName = scalar.Value;

			if (typeName.StartsWith("System.Collections.Generic.List`1"))
			{
				var elementTypeName = typeName.Substring(typeName.IndexOf("[[") + 2, typeName.IndexOf("]]") - typeName.IndexOf("[[") - 2);
				var elementType = Type.GetType(elementTypeName);
				return typeof(List<>).MakeGenericType(elementType!);
			}
			else if (typeName.StartsWith("System.Collections.Generic.Dictionary`2"))
			{
				var typeNames = typeName.Substring(typeName.IndexOf("[[") + 2, typeName.IndexOf("]]") - typeName.IndexOf("[[") - 2).Split(',');
				var keyType = Type.GetType(typeNames[0].Trim());
				var valueType = Type.GetType(typeNames[1].Trim());
				return typeof(Dictionary<,>).MakeGenericType(keyType!, valueType!);
			}
			else if (typeName.StartsWith("System.Nullable`1"))
			{
				var underlyingTypeName = typeName.Substring(typeName.IndexOf("[[") + 2, typeName.IndexOf("]]") - typeName.IndexOf("[[") - 2);
				var underlyingType = Type.GetType(underlyingTypeName);
				return typeof(Nullable<>).MakeGenericType(underlyingType!);
			}
			else if (typeName == "System.DateTime" || typeName == "System.TimeSpan" || typeName == "System.Guid" || typeName == "System.Uri")
			{
				return Type.GetType(typeName);
			}
			else
			{
				var customType = AppDomain.CurrentDomain.GetAssemblies()
					.SelectMany(a => a.GetTypes())
					.FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);

				if (customType != null && customType.IsEnum)
				{
					return customType;
				}

				return Type.GetType(typeName);
			}
		}

		public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nestedObjectSerializer)
		{
			if (value is Type typeValue)
			{
				string typeName;
				if (typeValue.IsGenericType)
				{
					if (typeValue.GetGenericTypeDefinition() == typeof(List<>))
					{
						var elementType = typeValue.GetGenericArguments()[0];
						typeName = $"System.Collections.Generic.List`1[[{elementType.FullName}]]";
					}
					else if (typeValue.GetGenericTypeDefinition() == typeof(Dictionary<,>))
					{
						var arguments = typeValue.GetGenericArguments();
						typeName = $"System.Collections.Generic.Dictionary`2[[{arguments[0].FullName}],[{arguments[1].FullName}]]";
					}
					else if (typeValue.GetGenericTypeDefinition() == typeof(Nullable<>))
					{
						var underlyingType = typeValue.GetGenericArguments()[0];
						typeName = $"System.Nullable`1[[{underlyingType.FullName}]]";
					}
					else
					{
						typeName = typeValue.FullName ?? typeValue.Name;
					}
				}
				else
				{
					typeName = typeValue.FullName ?? typeValue.Name;
				}
				emitter.Emit(new Scalar(typeName));
			}
			else
			{
				emitter.Emit(new Scalar(""));
			}
		}
	}
}
