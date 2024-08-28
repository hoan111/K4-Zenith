using System.Reflection;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Localization;

namespace ZenithAPI
{
	/// <summary>
	/// Provides services for managing player-specific data.
	/// </summary>
	public interface IPlayerServices // ! zenith:player-services
	{
		/// <summary>
		/// The player's controller.
		/// </summary>
		CCSPlayerController Controller { get; }

		/// <summary>
		/// The SteamID of the player.
		/// </summary>
		ulong SteamID { get; }

		/// <summary>
		/// The name of the player.
		/// </summary>
		string Name { get; }

		/// <summary>
		/// Checks if the player is valid and connected.
		/// </summary>
		bool IsValid { get; }

		/// <summary>
		/// Checks if the entity is a real player (not a bot or HLTV).
		/// </summary>
		bool IsPlayer { get; }

		/// <summary>
		/// Checks if the player is alive.
		/// </summary>
		bool IsAlive { get; }

		/// <summary>
		/// Checks if the player is a VIP.
		/// </summary>
		bool IsVIP { get; }

		/// <summary>
		/// Checks if the player is an admin.
		/// </summary>
		bool IsAdmin { get; }

		/// <summary>
		/// Prints a message to the player's chat.
		/// </summary>
		/// <param name="message">The message to print.</param>
		void Print(string message);

		/// <summary>
		/// Prints a message to the center of the player's screen.
		/// </summary>
		/// <param name="message">The message to print.</param>
		/// <param name="duration">The duration to display the message, in seconds.</param>
		/// <remarks>Duration defaults to 3 seconds if not specified.</remarks>
		void PrintToCenter(string message, int duration = 3, ActionPriority priority = ActionPriority.Low, bool showCloseCounter = false);

		/// <summary>
		/// Sets the player's clan tag.
		/// </summary>
		/// <param name="tag">The tag to set, or null to clear the tag.</param>
		/// <param name="priority">The priority of the action.</param>
		void SetClanTag(string? tag, ActionPriority priority = ActionPriority.Low);

		/// <summary>
		/// Sets the player's name tag.
		/// </summary>
		/// <param name="tag">The tag to set, or null to clear the tag.</param>
		/// <param name="priority">The priority of the action.</param>
		void SetNameTag(string? tag, ActionPriority priority = ActionPriority.Low);

		/// <summary>
		/// Sets the player's name color.
		/// </summary>
		/// <param name="color">The color to set, or null to clear the color.</param>
		/// <param name="priority">The priority of the action.</param>
		void SetNameColor(char? color, ActionPriority priority = ActionPriority.Low);

		/// <summary>
		/// Sets the player's chat color.
		/// </summary>
		/// <param name="color">The color to set, or null to clear the color.</param>
		/// <param name="priority">The priority of the action.</param>
		void SetChatColor(char? color, ActionPriority priority = ActionPriority.Low);

		/// <summary>
		/// Retrieves a setting value for a specific module and key.
		/// </summary>
		/// <param name="key">The key of the setting.</param>
		/// <returns>The value of the setting, or null if not found.</returns>
		T? GetSetting<T>(string key);

		/// <summary>
		/// Sets a setting value for a specific module and key.
		/// </summary>
		/// <param name="key">The key of the setting.</param>
		/// <param name="value">The value to set.</param>
		/// <param name="saveImmediately">If true, saves the setting to the database immediately.</param>
		void SetSetting(string key, object? value, bool saveImmediately = false);

		/// <summary>
		/// Retrieves a storage value for a specific module and key.
		/// </summary>
		/// <param name="key">The key of the storage item.</param>
		/// <returns>The value of the storage item, or null if not found.</returns>
		T? GetStorage<T>(string key);

		/// <summary>
		/// Sets a storage value for a specific module and key.
		/// </summary>
		/// <param name="key">The key of the storage item.</param>
		/// <param name="value">The value to set.</param>
		/// <param name="saveImmediately">If true, saves the storage item to the database immediately.</param>
		void SetStorage(string key, object? value, bool saveImmediately = false);

		/// <summary>
		/// Retrieves a setting value for a specific module and key.
		/// </summary>
		/// <param name="module">The module to retrieve the setting from.</param>
		/// <param name="key">The key of the setting.</param>
		T? GetModuleStorage<T>(string module, string key);

		/// <summary>
		/// Sets a setting value for a specific module and key.
		/// </summary>
		/// <param name="module">The module to set the setting for.</param>
		/// <param name="key">The key of the setting.</param>
		/// <param name="value">The value to set.</param>
		/// <param name="saveImmediately">If true, saves the setting to the database immediately.</param>
		void SetModuleStorage(string module, string key, object? value, bool saveImmediately = false);

		/// <summary>
		/// Saves all settings or settings for a specific module.
		/// </summary>
		void SaveSettings();

		/// <summary>
		/// Saves all storage items or storage items for a specific module.
		/// </summary>
		void SaveStorage();

		/// <summary>
		/// Saves all settings and storage items, or those for a specific module.
		/// </summary>
		void SaveAll();

		/// <summary>
		/// Loads all player data from the database.
		/// </summary>
		void LoadPlayerData();

		/// <summary>
		/// Resets the settings for a specific module to their default values.
		/// </summary>
		void ResetModuleSettings();

		/// <summary>
		/// Resets the storage items for a specific module to their default values.
		/// </summary>
		void ResetModuleStorage();
	}

	/// <summary>
	/// Provides services for managing module-specific data.
	/// </summary>
	public interface IModuleServices : IZenithEvents // ! zenith:module-services
	{
		string GetConnectionString();

		/// <summary>
		/// Registers default settings for a module.
		/// </summary>
		/// <param name="defaultSettings">A dictionary of default settings.</param>
		void RegisterModuleSettings(Dictionary<string, object?> defaultSettings, IStringLocalizer? localizer = null);

		/// <summary>
		/// Registers default storage items for a module.
		/// </summary>
		/// <param name="defaultStorage">A dictionary of default storage items.</param>
		void RegisterModuleStorage(Dictionary<string, object?> defaultStorage);

		/// <summary>
		/// Registers a command for a module.
		/// </summary>
		/// <param name="command">The command to register.</param>
		/// <param name="description">The description of the command.</param>
		/// <param name="handler">The callback function to execute when the command is invoked.</param>
		/// <param name="usage">The usage type of the command.</param>
		/// <param name="argCount">The number of arguments required for the command.</param>
		/// <param name="helpText">The help text to display when the command is used incorrectly.</param>
		/// <param name="permission">The permission required to use the command.</param>
		void RegisterModuleCommand(string command, string description, CommandInfo.CommandCallback handler, CommandUsage usage = CommandUsage.CLIENT_AND_SERVER, int argCount = 0, string? helpText = null, string? permission = null);

		/// <summary>
		/// Registers multiple commands for a module.
		/// </summary>
		/// <param name="commands">The commands to register.</param>
		/// <param name="description">The description of the commands.</param>
		/// <param name="handler">The callback function to execute when the commands are invoked.</param>
		/// <param name="usage">The usage type of the commands.</param>
		/// <param name="argCount">The number of arguments required for the commands.</param>
		/// <param name="helpText">The help text to display when the commands are used incorrectly.</param>
		/// <param name="permission">The permission required to use the commands.</param>
		void RegisterModuleCommands(List<string> commands, string description, CommandInfo.CommandCallback handler, CommandUsage usage = CommandUsage.CLIENT_AND_SERVER, int argCount = 0, string? helpText = null, string? permission = null);

		/// <summary>
		/// Registers a placeholder for a player-specific value.
		/// </summary>
		/// <param name="key">The key of the placeholder.</param>
		/// <param name="valueFunc">The function to retrieve the value.</param>
		void RegisterModulePlayerPlaceholder(string key, Func<CCSPlayerController, string> valueFunc);

		/// <summary>
		/// Registers a placeholder for a server-specific value.
		/// </summary>
		/// <param name="key">The key of the placeholder.</param>
		/// <param name="valueFunc">The function to retrieve the value.</param>
		void RegisterModuleServerPlaceholder(string key, Func<string> valueFunc);

		/// <summary>
		/// Registers a module configuration setting.
		/// </summary>
		/// <typeparam name="T">The type of the setting.</typeparam>
		/// <param name="groupName">The group name of the setting.</param>
		/// <param name="configName">The name of the setting.</param>
		/// <param name="description">The description of the setting.</param>
		/// <param name="defaultValue">The default value of the setting.</param>
		/// <param name="flags">The flags of the setting.</param>
		void RegisterModuleConfig<T>(string groupName, string configName, string description, T defaultValue, ConfigFlag flags = ConfigFlag.None) where T : notnull;

		/// <summary>
		/// Checks if a module configuration setting exists.
		/// </summary>
		/// <param name="groupName">The group name of the setting.</param>
		/// <param name="configName">The name of the setting.</param>
		bool HasModuleConfigValue(string groupName, string configName);

		/// <summary>
		/// Retrieves a module configuration setting.
		/// </summary>
		/// <typeparam name="T">The type of the setting.</typeparam>
		/// <param name="groupName">The group name of the setting.</param>
		/// <param name="configName">The name of the setting.</param>
		T GetModuleConfigValue<T>(string groupName, string configName) where T : notnull;

		/// <summary>
		/// Sets a module configuration setting.
		/// </summary>
		/// <typeparam name="T">The type of the setting.</typeparam>
		/// <param name="groupName">The group name of the setting.</param>
		/// <param name="configName">The name of the setting.</param>
		/// <param name="value">The value to set.</param>
		void SetModuleConfigValue<T>(string groupName, string configName, T value) where T : notnull;

		/// <summary>
		/// Retrieves a module configuration setting.
		/// </summary>
		IModuleConfigAccessor GetModuleConfigAccessor();

		/// <summary>
		/// Retrieves the event handler for the module.
		/// </summary>
		IZenithEvents GetEventHandler();

		/// <summary>
		/// Loads all player data from the database.
		/// </summary>
		void LoadAllOnlinePlayerData();

		/// <summary>
		/// Saves all player data to the database.
		/// </summary>
		void SaveAllOnlinePlayerData();

		/// <summary>
		/// Dispose the module's Zenith based resources such as commands, configs, and player datas.
		/// </summary>
		void DisposeModule();

		/// <summary>
		/// Dispose the module's Zenith based resources such as commands, configs, and player datas.
		/// </summary>
		/// <param name="assembly">The assembly to dispose.</param>
		void DisposeModule(Assembly assembly); // ! Recommended to use, if you see the regular not working
	}

	public interface IModuleConfigAccessor
	{
		/// <summary>
		///  Retrieves a configuration value.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="groupName"></param>
		/// <param name="configName"></param>
		/// <returns></returns>
		T GetValue<T>(string groupName, string configName) where T : notnull;

		/// <summary>
		/// Sets a configuration value.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="groupName"></param>
		/// <param name="configName"></param>
		/// <param name="value"></param>
		void SetValue<T>(string groupName, string configName, T value) where T : notnull;
	}

	public interface IZenithEvents
	{
		/// <summary>
		/// Occurs when a player is loaded and their data is downloaded.
		/// </summary>
		event Action<CCSPlayerController> OnZenithPlayerLoaded;

		/// <summary>
		/// Occurs when a player is unloaded and their data is saved.
		/// </summary>
		event Action<CCSPlayerController> OnZenithPlayerUnloaded;
	}

	public enum ActionPriority
	{
		Low = 0,
		Normal = 1,
		High = 2
	}

	[Flags]
	public enum ConfigFlag
	{
		None = 0,
		Global = 1, // Allow all other modules to access this config value
		Protected = 2, // Prevent this config value from retrieving the value (hidden)
		Locked = 4 // Prevent this config value from being changed (read-only)
	}
}
