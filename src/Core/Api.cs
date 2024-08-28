namespace Zenith
{
	using System.Reflection;
	using CounterStrikeSharp.API.Core;
	using CounterStrikeSharp.API.Core.Capabilities;
	using CounterStrikeSharp.API.Modules.Commands;
	using Microsoft.Extensions.Localization;
	using Microsoft.Extensions.Logging;
	using Zenith.Models;
	using ZenithAPI;

	public sealed partial class Plugin : BasePlugin
	{
		public ModuleServices? _moduleServices;

		public static PlayerCapability<IPlayerServices> Capability_PlayerServices { get; } = new("zenith:player-services");
		public static PluginCapability<IModuleServices> Capability_ModuleServices { get; } = new("zenith:module-services");

		public void Initialize_API()
		{
			Capabilities.RegisterPlayerCapability(Capability_PlayerServices, player => new PlayerServices(player, this));

			_moduleServices = new ModuleServices(this);
			Capabilities.RegisterPluginCapability(Capability_ModuleServices, () => _moduleServices);
		}

		public class PlayerServices : IPlayerServices
		{
			private readonly Player _player;
			private readonly Plugin _plugin;

			public PlayerServices(CCSPlayerController player, Plugin plugin)
			{
				Player? zenithPlayer = Player.Find(player);
				if (zenithPlayer == null)
					throw new Exception("Player is not yet loaded to the system. Handle this with a try-catch block.");

				_plugin = plugin;
				_player = zenithPlayer;
			}

			public CCSPlayerController Controller
				=> _player.Controller!;

			public ulong SteamID
				=> _player.SteamID;

			public string Name
				=> _player.Name;

			public bool IsValid
				=> _player.IsValid;
			public bool IsPlayer
				=> _player.IsPlayer;
			public bool IsAlive
				=> _player.IsAlive;

			public bool IsVIP
				=> _player.IsVIP;

			public bool IsAdmin
				=> _player.IsAdmin;

			public void Print(string message)
				=> _player.Print(message);

			public void PrintToCenter(string message, int duration = 3, ActionPriority priority = ActionPriority.Low, bool showCloseCounter = false)
				=> _player.PrintToCenter(message, duration, priority, showCloseCounter);

			public void SetClanTag(string? tag, ActionPriority priority = ActionPriority.Low)
				=> _player.SetClanTag(tag, priority);

			public void SetNameTag(string? tag, ActionPriority priority = ActionPriority.Low)
				=> _player.SetNameTag(tag, priority);

			public void SetChatColor(char? color, ActionPriority priority = ActionPriority.Low)
				=> _player.SetChatColor(color, priority);

			public void SetNameColor(char? color, ActionPriority priority = ActionPriority.Low)
				=> _player.SetNameColor(color, priority);

			public T? GetSetting<T>(string key)
				=> _player.GetSetting<T>(key);

			public void SetSetting(string key, object? value, bool saveImmediately = false)
				=> _player.SetSetting(key, value, saveImmediately);

			public T? GetStorage<T>(string key)
				=> _player.GetStorage<T>(key);

			public void SetStorage(string key, object? value, bool saveImmediately = false)
				=> _player.SetStorage(key, value, saveImmediately);

			public T? GetModuleStorage<T>(string module, string key)
				=> _player.GetModuleStorage<T>(module, key);

			public void SetModuleStorage(string module, string key, object? value, bool saveImmediately = false)
				=> _player.SetModuleStorage(module, key, value, saveImmediately);

			public void SaveSettings()
				=> _player.SaveSettings();

			public void SaveStorage()
				=> _player.SaveStorage();

			public void SaveAll()
				=> _player.SaveAll();

			public void LoadPlayerData()
				=> Task.Run(_player.LoadPlayerData);

			public void ResetModuleSettings()
				=> _player.ResetModuleSettings();

			public void ResetModuleStorage()
				=> _player.ResetModuleStorage();
		}

		public class ModuleServices : IModuleServices
		{
			private readonly Plugin _plugin;

			public ModuleServices(Plugin plugin)
			{
				_plugin = plugin;
			}

			public event Action<CCSPlayerController>? OnZenithPlayerLoaded;
			public event Action<CCSPlayerController>? OnZenithPlayerUnloaded;

			public IZenithEvents GetEventHandler() => this;

			internal void InvokeZenithPlayerLoaded(CCSPlayerController player)
				=> OnZenithPlayerLoaded?.Invoke(player);

			internal void InvokeZenithPlayerUnloaded(CCSPlayerController player)
				=> OnZenithPlayerUnloaded?.Invoke(player);

			public string GetConnectionString()
				=> _plugin.Database.GetConnectionString();

			public void RegisterModuleSettings(Dictionary<string, object?> defaultSettings, IStringLocalizer? localizer = null)
				=> Player.RegisterModuleSettings(_plugin, defaultSettings, localizer);

			public void RegisterModuleStorage(Dictionary<string, object?> defaultStorage)
				=> Player.RegisterModuleStorage(_plugin, defaultStorage);

			public void RegisterModuleCommand(string command, string description, CommandInfo.CommandCallback handler, CommandUsage usage = CommandUsage.CLIENT_AND_SERVER, int argCount = 0, string? helpText = null, string? permission = null)
				=> _plugin.RegisterZenithCommand(command, description, handler, usage, argCount, helpText, permission);

			public void RegisterModuleCommands(List<string> commands, string description, CommandInfo.CommandCallback handler, CommandUsage usage = CommandUsage.CLIENT_AND_SERVER, int argCount = 0, string? helpText = null, string? permission = null)
				=> _plugin.RegisterZenithCommand(commands, description, handler, usage, argCount, helpText, permission);

			public void RegisterModulePlayerPlaceholder(string key, Func<CCSPlayerController, string> valueFunc)
				=> _plugin.RegisterZenithPlayerPlaceholder(key, valueFunc);

			public void RegisterModuleServerPlaceholder(string key, Func<string> valueFunc)
				=> _plugin.RegisterZenithServerPlaceholder(key, valueFunc);

			public void RegisterModuleConfig<T>(string groupName, string configName, string description, T defaultValue, ConfigFlag flags = ConfigFlag.None) where T : notnull
				=> Plugin.RegisterModuleConfig(groupName, configName, description, defaultValue, flags);

			public bool HasModuleConfigValue(string groupName, string configName)
				=> Plugin.HasModuleConfigValue(groupName, configName);

			public T GetModuleConfigValue<T>(string groupName, string configName) where T : notnull
				=> Plugin.GetModuleConfigValue<T>(groupName, configName);

			public void SetModuleConfigValue<T>(string groupName, string configName, T value) where T : notnull
				=> Plugin.SetModuleConfigValue(groupName, configName, value);

			public IModuleConfigAccessor GetModuleConfigAccessor()
				=> _plugin.GetModuleConfigAccessor();

			public void LoadAllOnlinePlayerData()
				=> Player.LoadAllOnlinePlayerData(_plugin, true);

			public void SaveAllOnlinePlayerData()
				=> Player.SaveAllOnlinePlayerData(_plugin, false);

			public void DisposeModule()
				=> _plugin.DisposeModule();

			public void DisposeModule(Assembly assembly)
				=> _plugin.DisposeModule(assembly);
		}
	}
}
