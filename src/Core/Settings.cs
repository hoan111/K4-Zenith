namespace Zenith
{
	using System.Text.Json;
	using CounterStrikeSharp.API.Core;
	using CounterStrikeSharp.API.Modules.Commands;
	using CounterStrikeSharp.API.Modules.Utils;
	using Menu;
	using Menu.Enums;
	using Microsoft.Extensions.Logging;
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

					List<MenuItem> items = [];
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
							object? currentValue = zenithPlayer.GetData<object>(key, zenithPlayer.Settings, moduleID);

							currentValue ??= defaultValue;

							string displayName = moduleLocalizer != null ? $"{moduleLocalizer[$"settings.{key}"]}: " : $"{moduleID}.{key}: ";

							if (currentValue is JsonElement jsonElement)
							{
								switch (jsonElement.ValueKind)
								{
									case JsonValueKind.True:
									case JsonValueKind.False:
										items.Add(new MenuItem(MenuItemType.Bool, new MenuValue(displayName)));
										defaultValues[index] = jsonElement.GetBoolean();
										break;
									case JsonValueKind.Number:
										items.Add(new MenuItem(MenuItemType.Input, new MenuValue(displayName)));
										defaultValues[index] = jsonElement.GetInt32().ToString();
										break;
									case JsonValueKind.String:
										items.Add(new MenuItem(MenuItemType.Input, new MenuValue(displayName)));
										defaultValues[index] = jsonElement.GetString() ?? string.Empty;
										break;
									default:
										Logger.LogWarning($"Unsupported JsonElement type for {moduleID}.{key}: {jsonElement.ValueKind}");
										continue;
								}
							}
							else if (currentValue is bool boolValue)
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
							else
							{
								Logger.LogWarning($"Unknown setting type for {moduleID}.{key} ({currentValue?.GetType().Name ?? "null"})");
								continue;
							}

							settingsMap[index] = (moduleID, key);
							index++;
						}
					}

					Menu?.ShowScrollableMenu(player, Localizer["k4.settings.title"], items, (buttons, menu, selected) =>
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
										zenithPlayer.SetData(key, newBoolValue, zenithPlayer.Settings, true, moduleID);
										string localizedValue = Localizer[newBoolValue ? "k4.settings.enabled" : "k4.settings.disabled"];
										localizedValue = newBoolValue ? $"{ChatColors.Lime}{localizedValue}" : $"{ChatColors.LightRed}{localizedValue}";
										zenithPlayer.Print($"{moduleLocalizer?[$"settings.{key}"] ?? key}: {localizedValue}");
									}
									break;
								case MenuButtons.Input:
									string newValue = selected.DataString ?? string.Empty;
									object? currentValue = zenithPlayer.GetSetting<object>(key);

									if (currentValue is JsonElement jsonElement)
									{
										switch (jsonElement.ValueKind)
										{
											case JsonValueKind.Number:
												if (int.TryParse(newValue, out int intValue))
												{
													zenithPlayer.SetSetting(key, intValue, true);
													zenithPlayer.Print($"{moduleLocalizer?[$"settings.{key}"] ?? key}: {intValue}");
												}
												else
												{
													zenithPlayer.Print(Localizer["k4.settings.invalid-input"]);
													selected.DataString = jsonElement.GetInt32().ToString();
												}
												break;
											case JsonValueKind.String:
												zenithPlayer.SetSetting(key, newValue, true);
												zenithPlayer.Print($"{moduleLocalizer?[$"settings.{key}"] ?? key}: {newValue}");
												break;
										}
									}
									else if (currentValue is int)
									{
										if (int.TryParse(newValue, out int intValue))
										{
											zenithPlayer.SetSetting(key, intValue, true);
											zenithPlayer.Print($"{moduleLocalizer?[$"settings.{key}"] ?? key}: {intValue}");
										}
										else
										{
											zenithPlayer.Print(Localizer["k4.settings.invalid-input"]);
											selected.DataString = currentValue.ToString()!;
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
				});
			}
		}
	}
}