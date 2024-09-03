namespace Zenith
{
	using CounterStrikeSharp.API;
	using CounterStrikeSharp.API.Core;
	using CounterStrikeSharp.API.Core.Attributes;
	using CounterStrikeSharp.API.Modules.Timers;
	using Microsoft.Extensions.Logging;
	using Zenith.Models;

	[MinimumApiVersion(260)]
	public sealed partial class Plugin : BasePlugin
	{
		public Menu.KitsuneMenu Menu { get; private set; } = null!;
		public Database Database { get; private set; } = null!;
		public DateTime _lastStorageSave = DateTime.UtcNow;

		public override void Load(bool hotReload)
		{
			Initialize_Config();

			Database = new Database(this);

			if (!Database.TestConnection())
			{
				Logger.LogError("Failed to connect to the database. Please check your configuration.");
				Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
				return;
			}

			Task.Run(async () =>
			{
				try
				{
					await Player.CreateTablesAsync(this);
					await Database.MigrateDatabaseAsync();
					await Database.PurgeOldData();
				}
				catch (Exception ex)
				{
					Logger.LogError($"Database migration failed: {ex.Message}");
				}
			}).Wait();

			MigrateOldData();

			Menu = new Menu.KitsuneMenu(this);

			ValidateGeoLiteDatabase();

			Initialize_API();
			Initialize_Events();
			Initialize_Settings();
			Initialize_Commands();
			Initialize_Placeholders();

			Player.RegisterModuleSettings(this, new Dictionary<string, object?>
			{
				{ "ShowClanTags", true },
				{ "ShowChatTags", true },
			}, Localizer);

			RegisterListener<Listeners.OnTick>(() =>
			{
				foreach (var player in Player.List)
				{
					if (player.IsValid && player.IsPlayer)
						player.ShowCenterMessage();
				}
			});

			if (hotReload)
			{
				// log here an ascii art saying WARNING
				Logger.LogCritical(@"*");
				Logger.LogCritical(@"*");
				Logger.LogCritical(@"*    ██╗    ██╗ █████╗ ██████╗ ███╗   ██╗██╗███╗   ██╗ ██████╗");
				Logger.LogCritical(@"*    ██║    ██║██╔══██╗██╔══██╗████╗  ██║██║████╗  ██║██╔════╝");
				Logger.LogCritical(@"*    ██║ █╗ ██║███████║██████╔╝██╔██╗ ██║██║██╔██╗ ██║██║  ███╗");
				Logger.LogCritical(@"*    ██║███╗██║██╔══██║██╔══██╗██║╚██╗██║██║██║╚██╗██║██║   ██║");
				Logger.LogCritical(@"*    ╚███╔███╔╝██║  ██║██║  ██║██║ ╚████║██║██║ ╚████║╚██████╔╝");
				Logger.LogCritical(@"*     ╚══╝╚══╝ ╚═╝  ╚═╝╚═╝  ╚═╝╚═╝  ╚═══╝╚═╝╚═╝  ╚═══╝ ╚═════╝");
				Logger.LogCritical(@"*");
				Logger.LogCritical(@"*    WARNING: Hot reloading Zenith Core currently breaks the plugin. Please restart the server instead.");
				Logger.LogCritical(@"*    More information: https://github.com/roflmuffin/CounterStrikeSharp/issues/565");
				Logger.LogCritical(@"*");

				Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).ToList().ForEach(player => new Player(this, player, true));
				Player.LoadAllOnlinePlayerData(this);
			}

			AddTimer(3.0f, () =>
			{
				foreach (var player in Player.List)
				{
					if (player.IsValid && player.IsPlayer)
						player.EnforcePluginValues();
				}
			}, TimerFlags.REPEAT);

			AddTimer(1, () =>
			{
				if ((DateTime.UtcNow - _lastStorageSave).TotalMinutes >= GetCoreConfig<int>("Database", "AutoSaveInterval"))
				{
					_lastStorageSave = DateTime.UtcNow;
					Player.SaveAllOnlinePlayerData(this, false);
				}
			}, TimerFlags.REPEAT);
		}

		public override void Unload(bool hotReload)
		{
			_moduleServices?.InvokeZenithCoreUnload(hotReload);

			Player.Dispose(this);
			RemoveAllCommands();
			RemoveModulePlaceholders();
			ConfigManager.Dispose();
		}
	}
}