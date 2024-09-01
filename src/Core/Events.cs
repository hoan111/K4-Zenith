namespace Zenith
{
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
			if (!GetCoreConfig<bool>("Core", "HookChatMessages"))
				return HookResult.Continue;

			int entity = um.ReadInt("entityindex");

			Player? player = Player.Find(Utilities.GetPlayerFromIndex(entity));
			if (player == null || !player.IsValid)
				return HookResult.Continue;

			if (player.IsGagged)
				return HookResult.Stop;

			bool enabledChatModifier = player.GetSetting<bool>("ShowChatTags");

			string dead = player.IsAlive ? string.Empty : Localizer["k4.tag.dead"];
			string team = um.ReadString("messagename").Contains("All") ? Localizer["k4.tag.all"] : TeamLocalizer(player.Controller!.Team);
			string tag = enabledChatModifier ? player.GetNameTag() : string.Empty;

			char namecolor = enabledChatModifier ? player.GetNameColor() : ChatColors.ForTeam(player.Controller!.Team);
			char chatcolor = enabledChatModifier ? player.GetChatColor() : ChatColors.Default;

			um.SetString("messagename", FormatMessage(player.Controller!, $" {dead}{team}{tag}{namecolor}{um.ReadString("param1")}{RemoveLeadingSpaceBeforeColorCode(Localizer["k4.tag.separator"])}{chatcolor}{um.ReadString("param2")}"));
			return HookResult.Changed;

			static string FormatMessage(CCSPlayerController player, string message)
			{
				string modifiedValue = StringExtensions.ReplaceColorTags(message)
					.Replace("{team}", ChatColors.ForPlayer(player).ToString());

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
		}

		[GameEventHandler]
		public HookResult OnPlayerActivate(EventPlayerActivate @event, GameEventInfo info)
		{
			CCSPlayerController? player = @event.Userid;
			if (player is null || !player.IsValid)
				return HookResult.Continue;

			if (player.IsHLTV || player.IsBot)
				return HookResult.Continue;

			new Player(this, player);
			return HookResult.Continue;
		}

		[GameEventHandler(HookMode.Post)]
		public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
		{
			Player.Find(@event.Userid)?.Dispose();
			return HookResult.Continue;
		}

		[GameEventHandler]
		public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
		{
			if (HasModuleConfigValue("Database", "SaveOnRoundEnd") && GetCoreConfig<bool>("Database", "SaveOnRoundEnd"))
				Player.SaveAllOnlinePlayerData(this, false);
			return HookResult.Continue;
		}
	}
}