namespace Zenith
{
	using CounterStrikeSharp.API.Core;
	using CounterStrikeSharp.API.Modules.Commands;
	using CounterStrikeSharp.API.Modules.Utils;
	using Menu;
	using Menu.Enums;
	using Zenith.Models;

	public sealed partial class Plugin : BasePlugin
	{
		public void Initialize_Settings()
		{
			foreach (string command in _coreAccessor.GetValue<List<string>>("Commands", "SettingsCommands"))
			{
				RegisterZenithCommand($"css_{command}", "Change player Zenith settings", (CCSPlayerController? player, CommandInfo command) =>
				{
					if (player == null) return;

					var zenithPlayer = Player.Find(player);
					if (zenithPlayer == null) return;

					List<MenuItem> items = new List<MenuItem>();
					var defaultValues = new Dictionary<int, object>();
					var settingsMap = new Dictionary<int, (string ModuleID, string Key)>();

					int index = 0;
					foreach (var moduleSettings in Player.moduleDefaultSettings)
					{
						string moduleID = moduleSettings.Key;
						var moduleLocalizer = Player.GetModuleLocalizer(moduleID);

						foreach (var setting in moduleSettings.Value.Settings)
						{
							string key = setting.Key;
							object? defaultValue = setting.Value;
							object? currentValue = zenithPlayer.GetSetting<object>(key);

							currentValue ??= defaultValue;

							string displayName = moduleLocalizer != null ? $"{moduleLocalizer[$"settings.{key}"]}: " : $"{moduleID}.{key}: ";

							if (currentValue is bool boolValue)
							{
								items.Add(new MenuItem(MenuItemType.Bool, new MenuValue(displayName)));
								defaultValues[index] = boolValue;
							}
							else if (currentValue is int intValue)
							{
								items.Add(new MenuItem(MenuItemType.Input, new MenuValue(displayName)));
								defaultValues[index] = intValue.ToString();
							}
							else if (currentValue is string stringValue)
							{
								items.Add(new MenuItem(MenuItemType.Input, new MenuValue(displayName)));
								defaultValues[index] = stringValue;
							}

							settingsMap[index] = (moduleID, key);
							index++;
						}
					}

					Menu?.ShowPaginatedMenu(player, Localizer["k4.settings.title"], items, (buttons, menu, selected) =>
					{
						if (selected == null) return;

						if (settingsMap.TryGetValue(menu.Option, out var settingInfo))
						{
							string moduleID = settingInfo.ModuleID;
							string key = settingInfo.Key;
							var moduleLocalizer = Player.GetModuleLocalizer(moduleID);

							switch (buttons)
							{
								case MenuButtons.Select:
									if (selected.Type == MenuItemType.Bool)
									{
										bool newBoolValue = selected.Data[0] == 1;
										zenithPlayer.SetSetting(key, newBoolValue, true);
										string localizedValue = Localizer[newBoolValue ? "k4.settings.enabled" : "k4.settings.disabled"];
										localizedValue = newBoolValue ? $"{ChatColors.Lime}{localizedValue}" : $"{ChatColors.LightRed}{localizedValue}";
										zenithPlayer.Print($"{moduleLocalizer?[$"settings.{key}"] ?? key}: {localizedValue}");
									}
									break;
								case MenuButtons.Input:
									string newValue = selected.DataString ?? string.Empty;
									object? currentValue = zenithPlayer.GetSetting<object>(key);

									if (currentValue is int)
									{
										if (int.TryParse(newValue, out int intValue))
										{
											zenithPlayer.SetSetting(key, intValue, true);
											zenithPlayer.Print($"{moduleLocalizer?[$"settings.{key}"] ?? key}: {intValue}");
										}
										else
										{
											zenithPlayer.Print(Localizer["k4.settings.invalid-input"]);
											selected.DataString = currentValue?.ToString() ?? string.Empty;
										}
									}
									else if (currentValue is string)
									{
										zenithPlayer.SetSetting(key, newValue, true);
										zenithPlayer.Print($"{moduleLocalizer?[$"settings.{key}"] ?? key}: {newValue}");
									}
									break;
							}
						}
					}, false, GetCoreConfig<bool>("Core", "FreezeInMenu"), 5, defaultValues, !GetCoreConfig<bool>("Core", "ShowDevelopers"));
				}, CommandUsage.CLIENT_ONLY);
			}
		}
	}
}