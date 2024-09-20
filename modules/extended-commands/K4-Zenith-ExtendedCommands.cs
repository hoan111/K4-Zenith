using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Eventing.Reader;
using ZenithAPI;

namespace Zenith_ExtendedCommands;

[MinimumApiVersion(250)]
public sealed partial class Plugin : BasePlugin
{
	private const string MODULE_ID = "ExtendedCommands";

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab | edited by HoanNK";
	public override string ModuleVersion => "1.0.4.1";

	private IModuleConfigAccessor _coreAccessor = null!;

	private PluginCapability<IModuleServices>? _moduleServicesCapability;
	private PluginCapability<IDamageManagementAPI>? _damageManagementCapability;

	private IZenithEvents? _zenithEvents;
	private IModuleServices? _moduleServices;
	private IDamageManagementAPI damageManagementAPI;
	public static bool EnableOriginalOnTakeDamageMethod = false;


    public override void OnAllPluginsLoaded(bool hotReload)
	{
		string pluginDirectory = Path.GetDirectoryName(ModuleDirectory)!;
		List<string> blockPlugins = ["CS2-SimpleAdmin"];
		foreach (var p in blockPlugins)
		{
			if (Directory.GetDirectories(pluginDirectory, p).Any())
			{
				Logger.LogCritical($"This module is not compatible with {p}. You can use only one of them. Unloading...");
				Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
				return;
			}
		}

		try
		{
			_moduleServicesCapability = new("zenith:module-services");
		}
		catch (Exception ex)
		{
			Logger.LogError($"Failed to initialize Zenith API: {ex.Message}");
			Logger.LogInformation("Please check if Zenith is installed, configured and loaded correctly.");

			Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
			return;
		}

		_moduleServices = _moduleServicesCapability.Get();
		if (_moduleServices == null)
		{
			Logger.LogError("Failed to get Module-Services API for Zenith.");
			Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
			return;
		}

        _coreAccessor = _moduleServices.GetModuleConfigAccessor();

		_zenithEvents = _moduleServices.GetEventHandler();
		if (_zenithEvents != null)
		{
			_zenithEvents.OnZenithCoreUnload += OnZenithCoreUnload;
		}
		else
		{
			Logger.LogError("Failed to get Zenith event handler.");
		}

        //Init CS2-DamageManagement plugin API (optional)
        Logger.LogInformation("Loading CS2-DamageManagement API (optional)");
        try
        {
            _damageManagementCapability = new("damagemanagement:api");
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to initialize CS2-DamageManagement API: {@msg}\nIgnore this message if you are not using CS2-DamagementPlugin", ex.Message);
        }
        damageManagementAPI = _damageManagementCapability.Get();
        if (damageManagementAPI != null)
        {
            damageManagementAPI.Hook_OnTakeDamage(CallOriginalOnTakeDamageMethod);
            Logger.LogInformation("Successfully get DamageManagementAPI instance and hook OnTakeDamage");
        }

        Initialize_Commands();
		Initialize_Events();

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	private void OnZenithCoreUnload(bool hotReload)
	{
		if (hotReload)
		{
			AddTimer(3.0f, () =>
			{
				try { File.SetLastWriteTime(Path.Combine(ModulePath), DateTime.Now); }
				catch (Exception ex) { Logger.LogError($"Failed to update file: {ex.Message}"); }
			});
		}
	}

	public override void Unload(bool hotReload)
	{
		_damageManagementCapability?.Get()?.Unhook_OnTakeDamage(CallOriginalOnTakeDamageMethod);
		_moduleServicesCapability?.Get()?.DisposeModule(this.GetType().Assembly);
	}

	public bool CallOriginalOnTakeDamageMethod()
	{
		return EnableOriginalOnTakeDamageMethod;
    }
}