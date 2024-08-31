using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using Menu;
using Menu.Enums;

namespace Zenith_Bans
{
	public sealed partial class Plugin : BasePlugin
	{
		private void ShowPlayerSelectionMenu(CCSPlayerController? caller, Action<CCSPlayerController> callback, bool includeBots = false)
		{
			if (caller == null) return;

			List<MenuItem> items = [];
			var playerMap = new Dictionary<int, CCSPlayerController>();

			int index = 0;
			foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && (includeBots || (!p.IsBot && !p.IsHLTV)) && p != caller && AdminManager.CanPlayerTarget(caller, p)))
			{
				items.Add(new MenuItem(MenuItemType.Button, [new MenuValue($"#{player.UserId} | {player.PlayerName}")]));
				playerMap[index] = player;
				index++;
			}

			if (items.Count == 0)
			{
				items.Add(new MenuItem(MenuItemType.Text, new MenuValue(Localizer["k4.general.noplayersfound"]) { Prefix = "<font color='#FF6666'>", Suffix = "</font>" }));
			}

			Menu.ShowScrollableMenu(caller, Localizer["k4.menu.selectplayer"], items, (buttons, menu, selected) =>
			{
				if (selected == null) return;

				switch (buttons)
				{
					case MenuButtons.Select:
						if (playerMap.TryGetValue(menu.Option, out var targetPlayer))
						{
							callback(targetPlayer);
						}
						break;
				}
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu"), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}

		private void ShowLengthSelectionMenu(CCSPlayerController? caller, List<int> lengthList, Action<int> callback)
		{
			if (caller == null) return;

			List<MenuItem> items = [];

			int index = 0;
			foreach (var length in lengthList)
			{
				if (length == 0)
				{
					string permanent = Localizer["k4.general.permanent"];
					items.Add(new MenuItem(MenuItemType.Button, new List<MenuValue> { new MenuValue(permanent.ToUpper()) }));
				}
				else
				{
					items.Add(new MenuItem(MenuItemType.Button, new List<MenuValue> { new MenuValue(length.ToString()) }));
				}
				index++;
			}

			Menu.ShowScrollableMenu(caller, Localizer["k4.menu.selectlength"], items, (buttons, menu, selected) =>
			{
				if (selected == null) return;

				switch (buttons)
				{
					case MenuButtons.Select:
						callback(lengthList[menu.Option]);
						break;
				}
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu"), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}

		public void ShowReasonSelectionMenu(CCSPlayerController? caller, List<string> reasonList, Action<string> callback)
		{
			if (caller == null) return;

			List<MenuItem> items = new List<MenuItem>();

			int index = 0;
			foreach (var reason in reasonList)
			{
				items.Add(new MenuItem(MenuItemType.Button, new List<MenuValue> { new MenuValue(reason) }));
				index++;
			}

			Menu.ShowScrollableMenu(caller, "Select Reason", items, (buttons, menu, selected) =>
			{
				if (selected == null) return;

				switch (buttons)
				{
					case MenuButtons.Select:
						callback(reasonList[menu.Option]);
						break;
				}
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu"), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}

		private void ShowGroupSelectionMenu(CCSPlayerController controller, List<string> groups, Action<string> callback)
		{
			List<MenuItem> items = groups.Select(group => new MenuItem(MenuItemType.Button, new List<MenuValue> { new MenuValue(group) })).ToList();

			Menu.ShowScrollableMenu(controller, Localizer["k4.addadmin.select-group"], items, (buttons, menu, selected) =>
			{
				if (selected == null) return;

				switch (buttons)
				{
					case MenuButtons.Select:
						callback(groups[menu.Option]);
						break;
				}
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu"), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}
	}
}