using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using ZenithAPI;

namespace Zenith.Models;

public sealed partial class Player
{
	// +--------------------+
	// | HELPER VARIABLES   |
	// +--------------------+

	private readonly Plugin _plugin;

	// +--------------------+
	// | PLAYER VARIABLES   |
	// +--------------------+

	public readonly CCSPlayerController? Controller;
	public readonly ulong SteamID;
	public readonly string Name;

	// +--------------------+
	// | PLAYER PROPERTIES  |
	// +--------------------+

	private readonly List<CenterMessage> _centerMessages = new List<CenterMessage>();
	private Tuple<string, ActionPriority>? _clanTag = null;
	private Tuple<string, ActionPriority>? _nameTag = null;
	private Tuple<char, ActionPriority>? _nameColor = null;
	private Tuple<char, ActionPriority>? _chatColor = null;

	// +--------------------+
	// | PLAYER CONSTRUCTOR |
	// +--------------------+

	public Player(Plugin plugin, CCSPlayerController? controller, bool noLoad = false)
	{
		_plugin = plugin;

		Controller = controller;
		SteamID = controller?.SteamID ?? 0;
		Name = controller?.PlayerName ?? "Unknown";

		if (List.Any(player => player.Controller == controller))
			return;

		List.Add(this);

		Task.Run(async () =>
		{
			try
			{
				if (!noLoad)
					await this.LoadPlayerData();
			}
			catch (Exception ex)
			{
				_plugin.Logger.LogError($"Error loading data for player {Name} (SteamID: {SteamID}): {ex.Message}");
			}
		});
	}

	// +--------------------+
	// | PLAYER VALIDATION  |
	// +--------------------+

	public bool IsValid
		=> Controller?.IsValid == true && Controller.PlayerPawn?.IsValid == true;
	public bool IsPlayer
		=> Controller?.IsBot == false && Controller.IsHLTV == false;
	public bool IsAlive
		=> Controller?.PlayerPawn.Value?.Health > 0;

	public bool IsVIP
		=> AdminManager.PlayerHasPermissions(Controller, "@zenith/vip");

	public bool IsAdmin
		=> AdminManager.PlayerHasPermissions(Controller, "@zenith/admin");

	// +--------------------+
	// | PLAYER FUNCTIONS   |
	// +--------------------+

	public void Print(string message)
		=> Controller?.PrintToChat($" {_plugin.Localizer["k4.general.prefix"]} {message}");

	public void PrintToCenter(string message, int duration = 3, ActionPriority priority = ActionPriority.Low, bool showCloseCounter = false)
	{
		_centerMessages.Add(new CenterMessage(message, Server.CurrentTime + duration, priority, showCloseCounter));
	}

	public void ShowCenterMessage()
	{
		if (_centerMessages.Count == 0)
			return;

		var orderedMessages = _centerMessages
			.OrderByDescending(m => m.Priority)
			.ThenBy(m => m.Duration);

		var topMessage = orderedMessages.First();

		string finalMessage = topMessage.Message;

		if (topMessage.ShowCloseCounter)
			finalMessage += "<br><br><font class=\"fontSize-s\"><font color=\"#f5a142\">The message will disappear in <font color=\"#ff3333\">" + (int)(topMessage.Duration - Server.CurrentTime) + "</font> seconds.</font></font>";

		Controller?.PrintToCenterHtml(finalMessage);

		_centerMessages.RemoveAll(m => m.Duration <= Server.CurrentTime);
	}

	public void SetClanTag(string? tag, ActionPriority priority)
	{
		if (tag == null)
		{
			_nameTag = null;
			return;
		}

		if (priority < _clanTag?.Item2)
			return;

		_clanTag = new Tuple<string, ActionPriority>(tag, priority);

		Controller!.Clan = tag;
		Utilities.SetStateChanged(Controller, "CCSPlayerController", "m_szClan");
	}

	public string GetClanTag()
		=> _clanTag?.Item1 ?? Controller?.Clan ?? string.Empty;

	public void SetNameTag(string? tag, ActionPriority priority)
	{
		if (tag == null)
		{
			_nameTag = null;
			return;
		}

		if (priority < _nameTag?.Item2)
			return;

		_nameTag = new Tuple<string, ActionPriority>(tag, priority);
	}

	public string GetNameTag()
		=> _nameTag?.Item1 ?? string.Empty;

	public void SetNameColor(char? color, ActionPriority priority)
	{
		if (color == null)
		{
			_nameColor = null;
			return;
		}

		if (priority < _nameColor?.Item2)
			return;

		_nameColor = new Tuple<char, ActionPriority>((char)color, priority);
	}

	public char GetNameColor()
			=> _nameColor?.Item1 ?? ChatColors.ForTeam(Controller!.Team);

	public void SetChatColor(char? color, ActionPriority priority)
	{
		if (color == null)
		{
			_chatColor = null;
			return;
		}

		if (priority < _chatColor?.Item2)
			return;

		_chatColor = new Tuple<char, ActionPriority>((char)color, priority);
	}

	public char GetChatColor()
		=> _chatColor?.Item1 ?? ChatColors.Default;

	public void EnforcePluginValues()
	{
		if (!GetSetting<bool>("ShowClanTags"))
		{
			return;
		}

		string? clanTag = _clanTag?.Item1;
		if (clanTag == null)
		{
			clanTag = IsAdmin ? _plugin.GetCoreConfig<string>("Modular", "AdminClantagFormat") : IsVIP ? _plugin.GetCoreConfig<string>("Modular", "VIPClantagFormat") : _plugin.GetCoreConfig<string>("Modular", "PlayerClantagFormat");
		}

		if (clanTag != null)
		{
			Controller!.Clan = _plugin.ReplacePlayerPlaceholders(Controller, clanTag);
			Utilities.SetStateChanged(Controller, "CCSPlayerController", "m_szClan");
		}
	}

	// +--------------------+
	// | PLAYER DISPOSER    |
	// +--------------------+

	public void Dispose()
	{
		_plugin._moduleServices?.InvokeZenithPlayerUnloaded(Controller!);

		Task.Run(async () =>
		{
			try
			{
				await this.SaveAllDataAsync();
			}
			catch (Exception ex)
			{
				_plugin.Logger.LogError($"Error saving data for player {Name} (SteamID: {SteamID}) during disposal: {ex.Message}");
			}
			finally
			{
				List.Remove(this);
			}
		});
	}
}

public struct CenterMessage
{
	public string Message { get; set; }
	public float Duration { get; set; }
	public ActionPriority Priority { get; set; }
	public bool ShowCloseCounter { get; set; } = false;

	public CenterMessage(string message, float duration, ActionPriority priority, bool showCloseCounter = false)
	{
		Message = message;
		Duration = duration;
		Priority = priority;
		ShowCloseCounter = showCloseCounter;
	}
}