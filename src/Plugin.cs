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
					await MigrateOldData();
				}
				catch (Exception ex)
				{
					Logger.LogError($"Database migration failed: {ex.Message}");
				}
			}).Wait();

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
				foreach (var player in Player.List.Values)
				{
					if (player.IsValid)
						player.ShowCenterMessage();
				}
			});

			if (hotReload)
			{
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

				var players = Utilities.GetPlayers();

				foreach (var player in players)
				{
					if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
					{
						_ = new Player(this, player, true);
					}
				}

				Player.LoadAllOnlinePlayerDataWithSingleQuery(this);
			}

			AddTimer(3.0f, () =>
			{
				string coreFormat = GetCoreConfig<string>("Modular", "PlayerClantagFormat");
				foreach (var player in Player.List.Values)
				{
					if (player.IsValid)
						player.EnforcePluginValues(coreFormat);
				}
			}, TimerFlags.REPEAT);

			AddTimer(60.0f, () =>
			{
				int interval = GetCoreConfig<int>("Database", "AutoSaveInterval");
				if (interval <= 0)
					return;

				if ((DateTime.UtcNow - _lastStorageSave).TotalMinutes >= interval)
				{
					_lastStorageSave = DateTime.UtcNow;
					_ = Task.Run(() => Player.SaveAllOnlinePlayerDataWithTransaction(this));
				}
			}, TimerFlags.REPEAT);
		}

		public override void Unload(bool hotReload)
		{
			_moduleServices?.InvokeZenithCoreUnload(hotReload);

			ConfigManager.Dispose();
			Player.Dispose(this);
			RemoveAllCommands();
			RemoveModulePlaceholders();
		}
	}
}