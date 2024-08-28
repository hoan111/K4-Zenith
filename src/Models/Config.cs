using CounterStrikeSharp.API.Core;
using ZenithAPI;

namespace Zenith
{
	public sealed partial class Plugin : BasePlugin
	{
		private ModuleConfigAccessor _coreAccessor = null!;

		private void RegisterCoreConfigs()
		{
			_coreAccessor = GetModuleConfigAccessor();

			// Database settings
			RegisterModuleConfig("Database", "Hostname", "The IP address or hostname of the database", "localhost", ConfigFlag.Protected | ConfigFlag.Locked);
			RegisterModuleConfig("Database", "Port", "The port number of the database", 3306, ConfigFlag.Protected | ConfigFlag.Locked);
			RegisterModuleConfig("Database", "Username", "The username for accessing the database", "root", ConfigFlag.Protected | ConfigFlag.Locked);
			RegisterModuleConfig("Database", "Password", "The password for accessing the database", "password", ConfigFlag.Protected | ConfigFlag.Locked);
			RegisterModuleConfig("Database", "Database", "The name of the database", "database", ConfigFlag.Protected | ConfigFlag.Locked);
			RegisterModuleConfig("Database", "Sslmode", "The SSL mode for the database connection (none, preferred, required, verifyca, verifyfull)", "preferred", ConfigFlag.Locked);
			RegisterModuleConfig("Database", "TablePrefix", "The prefix for the database tables to support multiple servers on the same database with different tables", "", ConfigFlag.Locked);
			RegisterModuleConfig("Database", "TablePurgeDays", "The number of days of inactivity after which unused data is automatically purged", 30, ConfigFlag.Locked);
			RegisterModuleConfig("Database", "SaveOnRoundEnd", "Whether to save every player setting and storage change on round ends", true, ConfigFlag.Locked | ConfigFlag.Global);

			// Commands settings
			RegisterModuleConfig("Commands", "SettingsCommands", "Open the settings menu for players", new List<string> { "settings", "preferences", "prefs" });

			// Modular settings
			RegisterModuleConfig("Modular", "PlayerClantagFormat", "The format for displaying the player clantag. css_placeholderlist for list", "{country_short} | {rank} |");
			RegisterModuleConfig("Modular", "VIPClantagFormat", "The format for displaying the VIP clantag. css_placeholderlist for list", "");
			RegisterModuleConfig("Modular", "AdminClantagFormat", "The format for displaying the admin clantag. css_placeholderlist for list", "");

			// Core settings
			RegisterModuleConfig("Core", "GlobalChangeTracking", "Whether to enable global change tracking. When you change config values through commands, they are saved to files.", true);
			RegisterModuleConfig("Core", "AutoReload", "Whether to enable auto-reload of configurations. When config values are changed in a file, they are automatically changed on the server.", true);
			RegisterModuleConfig("Core", "FreezeInMenu", "Whether to freeze the player when opening the menu.", true, ConfigFlag.Global);
			RegisterModuleConfig("Core", "ShowDevelopers", "Support the developers by showing their names in the menu.", true, ConfigFlag.Global);
			RegisterModuleConfig("Core", "CenterMessageTime", "The time in seconds for how long the center message is displayed by default.", 10, ConfigFlag.Global);
			RegisterModuleConfig("Core", "CenterAlertTime", "The time in seconds for how long the center alert is displayed by default.", 5, ConfigFlag.Global);

			// Apply global settings
			ConfigManager.GlobalChangeTracking = GetModuleConfigValue<bool>("Core", "GlobalChangeTracking");
			ConfigManager.SetGlobalAutoReload(GetModuleConfigValue<bool>("Core", "AutoReload"));
		}

		public T GetCoreConfig<T>(string groupName, string configName) where T : notnull
		{
			return _coreAccessor.GetValue<T>(groupName, configName);
		}

		// Setter metódus a core konfigurációkhoz
		public void SetCoreConfig<T>(string groupName, string configName, T value) where T : notnull
		{
			_coreAccessor.SetValue(groupName, configName, value);
		}
	}
}