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
			var commands = _coreAccessor.GetValue<List<string>>("Commands", "SettingsCommands");

			foreach (var command in commands)
			{
				RegisterZenithCommand($"css_{command}", "Change player Zenith settings and storage", (CCSPlayerController? player, CommandInfo commandInfo) =>
				{
					if (player == null) return;

					var zenithPlayer = Player.Find(player);
					if (zenithPlayer == null) return;

					var items = new List<MenuItem>();
					var defaultValues = new Dictionary<int, object>();
					var dataMap = new Dictionary<int, (string ModuleID, string Key, bool IsStorage)>();

					int index = 0;
					foreach (var isStorage in booleanArray)
					{
						var moduleData = isStorage ? Player.moduleDefaultStorage : Player.moduleDefaultSettings;

						foreach (var moduleItem in moduleData)
						{
							string moduleID = moduleItem.Key;
							var moduleLocalizer = Player.GetModuleLocalizer(moduleID);

							foreach (var setting in moduleItem.Value.Settings)
							{
								string key = setting.Key;
								var defaultValue = setting.Value;
								var currentValue = zenithPlayer.GetData<object>(key, isStorage ? zenithPlayer.Storage : zenithPlayer.Settings, moduleID);
								currentValue ??= defaultValue;

								var displayName = moduleLocalizer != null ? $"{moduleLocalizer[$"{(isStorage ? "storage" : "settings")}.{key}"]}: " : $"{moduleID}.{key}: ";

								switch (currentValue)
								{
									case bool boolValue:
										items.Add(new MenuItem(MenuItemType.Bool, new MenuValue(displayName)));
										defaultValues[index] = boolValue;
										break;
									case int intValue:
										items.Add(new MenuItem(MenuItemType.Input, new MenuValue(displayName)));
										defaultValues[index] = intValue.ToString();
										break;
									case string stringValue:
										items.Add(new MenuItem(MenuItemType.Input, new MenuValue(displayName)));
										defaultValues[index] = stringValue;
										break;
									case JsonElement jsonElement:
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
										break;
									default:
										Logger.LogWarning($"Unknown setting type for {moduleID}.{key} ({currentValue?.GetType().Name ?? "null"})");
										continue;
								}

								dataMap[index] = (moduleID, key, isStorage);
								index++;
							}
						}
					}

					Menu?.ShowScrollableMenu(player, Localizer["k4.settings.title"], items, (buttons, menu, selected) =>
					{
						if (selected == null) return;

						if (dataMap.TryGetValue(menu.Option, out var dataInfo))
						{
							string moduleID = dataInfo.ModuleID;
							string key = dataInfo.Key;
							bool isStorage = dataInfo.IsStorage;
							var moduleLocalizer = Player.GetModuleLocalizer(moduleID);

							switch (buttons)
							{
								case MenuButtons.Select:
									if (selected.Type == MenuItemType.Bool)
									{
										bool newBoolValue = selected.Data[0] == 1;
										zenithPlayer.SetData(key, newBoolValue, isStorage ? zenithPlayer.Storage : zenithPlayer.Settings, true, moduleID);
										string localizedValue = Localizer[newBoolValue ? "k4.settings.enabled" : "k4.settings.disabled"];
										localizedValue = newBoolValue ? $"{ChatColors.Lime}{localizedValue}" : $"{ChatColors.LightRed}{localizedValue}";
										zenithPlayer.Print($"{moduleLocalizer?[$"{(isStorage ? "storage" : "settings")}.{key}"] ?? key}: {localizedValue}");
									}
									break;
								case MenuButtons.Input:
									string newValue = selected.DataString ?? string.Empty;
									var currentValue = zenithPlayer.GetData<object>(key, isStorage ? zenithPlayer.Storage : zenithPlayer.Settings, moduleID);

									switch (currentValue)
									{
										case JsonElement jsonElement:
											switch (jsonElement.ValueKind)
											{
												case JsonValueKind.Number:
													if (int.TryParse(newValue, out int parsedValue))
													{
														zenithPlayer.SetData(key, parsedValue, isStorage ? zenithPlayer.Storage : zenithPlayer.Settings, true, moduleID);
														zenithPlayer.Print($"{moduleLocalizer?[$"{(isStorage ? "storage" : "settings")}.{key}"] ?? key}: {parsedValue}");
													}
													else
													{
														zenithPlayer.Print(Localizer["k4.settings.invalid-input"]);
														selected.DataString = jsonElement.GetInt32().ToString();
													}
													break;
												case JsonValueKind.String:
													zenithPlayer.SetData(key, newValue, isStorage ? zenithPlayer.Storage : zenithPlayer.Settings, true, moduleID);
													zenithPlayer.Print($"{moduleLocalizer?[$"{(isStorage ? "storage" : "settings")}.{key}"] ?? key}: {newValue}");
													break;
											}
											break;
										case int:
											if (int.TryParse(newValue, out int intValue))
											{
												zenithPlayer.SetData(key, intValue, isStorage ? zenithPlayer.Storage : zenithPlayer.Settings, true, moduleID);
												zenithPlayer.Print($"{moduleLocalizer?[$"{(isStorage ? "storage" : "settings")}.{key}"] ?? key}: {intValue}");
											}
											else
											{
												zenithPlayer.Print(Localizer["k4.settings.invalid-input"]);
												selected.DataString = currentValue.ToString()!;
											}
											break;
										case string:
											zenithPlayer.SetData(key, newValue, isStorage ? zenithPlayer.Storage : zenithPlayer.Settings, true, moduleID);
											zenithPlayer.Print($"{moduleLocalizer?[$"{(isStorage ? "storage" : "settings")}.{key}"] ?? key}: {newValue}");
											break;
									}
									break;
							}
						}
					}, false, GetCoreConfig<bool>("Core", "FreezeInMenu"), 5, defaultValues, !GetCoreConfig<bool>("Core", "ShowDevelopers"));
				});
			}
		}

		private static readonly bool[] booleanArray = new[] { false, true };
	}
}
