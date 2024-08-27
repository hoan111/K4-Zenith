namespace Zenith
{
	using System.Reflection;
	using CounterStrikeSharp.API;
	using CounterStrikeSharp.API.Core;
	using CounterStrikeSharp.API.Core.Attributes.Registration;
	using CounterStrikeSharp.API.Core.Translations;
	using CounterStrikeSharp.API.Modules.UserMessages;
	using CounterStrikeSharp.API.Modules.Utils;
	using Zenith.Models;

	public sealed partial class Plugin : BasePlugin
	{
		public void Initialize_Events()
		{
			HookUserMessage(118, OnMessage, HookMode.Pre);
		}

		public HookResult OnMessage(UserMessage um)
		{
			int entity = um.ReadInt("entityindex");

			Player? player = Player.Find(Utilities.GetPlayerFromIndex(entity));
			if (player == null || !player.IsValid)
				return HookResult.Continue;

			bool enabledChatModifier = player.GetSetting<bool>("ShowChatTags");

			string msgT = um.ReadString("messagename");
			string playername = um.ReadString("param1");
			string message = um.ReadString("param2");

			string dead = player.IsAlive ? string.Empty : Localizer["k4.tag.dead"];
			string team = msgT.Contains("All") ? Localizer["k4.tag.all"] : TeamLocalizer(player.Controller!.Team);
			string tag = enabledChatModifier ? player.GetNameTag() : string.Empty;

			char namecolor = enabledChatModifier ? player.GetNameColor() : ChatColors.ForTeam(player.Controller!.Team);
			char chatcolor = enabledChatModifier ? player.GetChatColor() : ChatColors.Default;

			string formattedMessage = FormatMessage(dead, team, tag, namecolor, chatcolor, playername, message, player.Controller!.Team);
			um.SetString("messagename", formattedMessage);
			return HookResult.Changed;

			static string ReplaceTags(string message, CsTeam team)
			{
				string modifiedValue = StringExtensions.ReplaceColorTags(message)
					.Replace("{team}", ChatColors.ForTeam(team).ToString());

				return modifiedValue;
			}

			string TeamLocalizer(CsTeam team)
			{
				return team switch
				{
					CsTeam.Spectator => Localizer["k4.tag.team.spectator"],
					CsTeam.Terrorist => Localizer["k4.tag.team.t"],
					CsTeam.CounterTerrorist => Localizer["k4.tag.team.ct"],
					_ => Localizer["k4.tag.team.unassigned"],
				};
			}

			static string FormatMessage(string deadIcon, string teamname, string tag, char namecolor, char chatcolor, string playername, string message, CsTeam team)
			{
				return ReplaceTags($" {deadIcon}{teamname}{tag}{namecolor}{playername}{ChatColors.Default}: {chatcolor}{message}", team);
			}
		}

		[GameEventHandler]
		public HookResult OnPlayerActivate(EventPlayerActivate @event, GameEventInfo info)
		{
			CCSPlayerController? player = @event.Userid;
			if (player is null || !player.IsValid)
				return HookResult.Continue;

			if (player.IsHLTV || player.IsBot)
				return HookResult.Continue;

			_ = new Player(this, player);
			return HookResult.Continue;
		}

		[GameEventHandler]
		public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
		{
			if (@event.Reason != 1)
				Player.Find(@event.Userid)?.Dispose();
			return HookResult.Continue;
		}

		[GameEventHandler]
		public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
		{
			if (GetCoreConfig<bool>("Database", "SaveOnRoundEnd"))
				Player.SaveAllOnlinePlayerData(this);
			return HookResult.Continue;
		}
	}
}