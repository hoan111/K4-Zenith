namespace Zenith
{
	using CounterStrikeSharp.API;
	using CounterStrikeSharp.API.Core;
	using CounterStrikeSharp.API.Core.Attributes;
	using CounterStrikeSharp.API.Modules.Timers;
	using Microsoft.Extensions.Logging;
	using Zenith.Models;

	[MinimumApiVersion(250)]
	public sealed partial class Plugin : BasePlugin
	{
		public Menu.KitsuneMenu Menu { get; private set; } = null!;
		public Database Database { get; private set; } = null!;

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
				Player.List.ForEach(player =>
				{
					if (player.IsValid && player.IsPlayer)
						player.ShowCenterMessage();
				});
			});

			if (hotReload)
			{
				string pluginDirectory = Path.GetDirectoryName(ModuleDirectory)!;
				string[] directories = Directory.EnumerateDirectories(pluginDirectory, "*", SearchOption.TopDirectoryOnly)
					.Where(d => Path.GetFileName(d).ToLower().Contains("zenith") && d != ModuleDirectory)
					.ToArray();

				if (directories.Length > 0)
					foreach (var directory in directories)
						Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(directory)}");

				Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).ToList().ForEach(player => new Player(this, player, true));
				Player.LoadAllOnlinePlayerData(this);

				AddTimer(2.0f, () =>
				{
					if (directories.Length > 0)
						foreach (var directory in directories)
							Server.ExecuteCommand($"css_plugins load {Path.GetFileNameWithoutExtension(directory)}");
				}, TimerFlags.STOP_ON_MAPCHANGE);

				AddTimer(3.0f, () =>
				{
					Player.List.ForEach(player =>
					{
						if (player.IsValid && player.IsPlayer)
							player.EnforcePluginValues();
					});
				}, TimerFlags.REPEAT);
			}
		}

		public override void Unload(bool hotReload)
		{
			Player.Dispose(this);
			RemoveAllCommands();
			ConfigManager.Dispose();
		}
	}
}