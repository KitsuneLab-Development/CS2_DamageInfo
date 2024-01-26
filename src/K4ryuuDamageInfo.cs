using System.Data;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace K4ryuuDamageInfo
{
	public sealed class PluginConfig : BasePluginConfig
	{
		[JsonPropertyName("round-end-summary")]
		public bool RoundEndSummary { get; set; } = true;

		[JsonPropertyName("round-end-summary-allow-death-print")]
		public bool AlowDeathPrint { get; set; } = true;

		[JsonPropertyName("center-damage-info")]
		public bool CenterDamageInfo { get; set; } = true;

		[JsonPropertyName("console-damage-info")]
		public bool ConsoleDamageInfo { get; set; } = true;

		[JsonPropertyName("ffa-mode")]
		public bool FFAMode { get; set; } = false;

		[JsonPropertyName("ConfigVersion")]
		public override int Version { get; set; } = 2;
	}

	[MinimumApiVersion(153)]
	public class DamageInfoPlugin : BasePlugin, IPluginConfig<PluginConfig>
	{
		public override string ModuleName => "Damage Info";
		public override string ModuleVersion => "2.0.2";
		public override string ModuleAuthor => "K4ryuu";

		public required PluginConfig Config { get; set; } = new PluginConfig();
		public CCSGameRules? GameRules;
		public Dictionary<int, bool> IsDataShown = new Dictionary<int, bool>();

		public void OnConfigParsed(PluginConfig config)
		{
			if (config.Version < Config.Version)
			{
				base.Logger.LogWarning("Configuration version mismatch (Expected: {0} | Current: {1})", this.Config.Version, config.Version);
			}

			this.Config = config;
		}

		public override void Load(bool hotReload)
		{
			RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
			RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
			RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
			RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
			RegisterEventHandler<EventRoundStart>(OnRoundStart);

			RegisterListener<Listeners.OnMapStart>(OnMapStart);

			OnMapStart("");
		}

		private void OnMapStart(string mapName)
		{
			AddTimer(1.0f, () =>
			{
				GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules;
			});
		}

		private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
		{
			CCSPlayerController player = @event.Userid;

			if (player is null || !player.IsValid || !player.PlayerPawn.IsValid)
				return HookResult.Continue;

			IsDataShown[player.Slot] = false;
			return HookResult.Continue;
		}

		private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
		{
			if (GameRules is null || GameRules.WarmupPeriod)
				return HookResult.Continue;

			if (!Config.AlowDeathPrint)
				return HookResult.Continue;

			CCSPlayerController victim = @event.Userid;

			if (victim is null || !victim.IsValid || !victim.PlayerPawn.IsValid || victim.Connected == PlayerConnectedState.PlayerDisconnecting)
				return HookResult.Continue;

			DisplayDamageInfo(victim);

			return HookResult.Continue;
		}

		private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
		{
			CCSPlayerController victim = @event.Userid;

			if (victim is null || !victim.IsValid || !victim.PlayerPawn.IsValid)
				return HookResult.Continue;

			CCSPlayerController attacker = @event.Attacker;

			if (attacker is null || !attacker.IsValid || !attacker.PlayerPawn.IsValid)
				return HookResult.Continue;

			int damageToHeath = @event.DmgHealth;
			int damageToArmor = @event.DmgArmor;

			string hitgroup = Localizer[$"phrases.hitgroup.{@event.Hitgroup}"];

			if (!attacker.IsBot && (victim.TeamNum != attacker.TeamNum || Config.FFAMode))
			{
				if (Config.ConsoleDamageInfo)
				{
					attacker.PrintToConsole(Localizer["phrases.console.normal", victim.PlayerName, damageToHeath, damageToArmor, hitgroup]);
					victim.PrintToConsole(Localizer["phrases.console.inverse", attacker.PlayerName, damageToHeath, damageToArmor, hitgroup]);
				}

				if (Config.CenterDamageInfo)
				{

					if (!recentDamages.ContainsKey(attacker.Slot))
					{
						recentDamages[attacker.Slot] = new Dictionary<int, RecentDamage>();
					}

					if (!recentDamages[attacker.Slot].TryGetValue(victim.Slot, out RecentDamage? recentDamage))
					{
						recentDamage = new RecentDamage();
						recentDamages[attacker.Slot][victim.Slot] = recentDamage;
					}

					if (DateTime.Now - recentDamage.LastDamageTime <= TimeSpan.FromSeconds(5))
					{
						recentDamage.TotalDamage += damageToHeath;
					}
					else
					{
						recentDamage.TotalDamage = damageToHeath;
					}

					recentDamage.LastDamageTime = DateTime.Now;

					// This is because of a wierd bug, where if the damage is above 110, the "Armor: x - HitGroup:" gets replaced with ************** ._: idk why
					// If you wanna check it, remove this block, print always the normal and shoot a bot with awp to the head
					string printMessage = recentDamage.TotalDamage > 110
						? Localizer["phrases.center.deadly", recentDamage.TotalDamage, hitgroup]
						: Localizer["phrases.center.normal", recentDamage.TotalDamage, damageToArmor, hitgroup];

					attacker.PrintToCenter(printMessage);
				}
			}

			if (GameRules is null || GameRules.WarmupPeriod)
				return HookResult.Continue;

			if (Config.RoundEndSummary)
			{
				if (!playerDamageInfos.ContainsKey(victim.Slot))
					playerDamageInfos.Add(victim.Slot, new PlayerDamageInfo());

				if (!playerDamageInfos.ContainsKey(attacker.Slot))
					playerDamageInfos.Add(attacker.Slot, new PlayerDamageInfo());

				if (!playerDamageInfos[victim.Slot].TakenDamage.ContainsKey(attacker.Slot))
					playerDamageInfos[victim.Slot].TakenDamage.Add(attacker.Slot, new DamageInfo());

				if (!playerDamageInfos[attacker.Slot].GivenDamage.ContainsKey(victim.Slot))
					playerDamageInfos[attacker.Slot].GivenDamage.Add(victim.Slot, new DamageInfo());

				playerDamageInfos[victim.Slot].TakenDamage[attacker.Slot].TotalDamage += damageToHeath;
				playerDamageInfos[victim.Slot].TakenDamage[attacker.Slot].Hits++;

				playerDamageInfos[attacker.Slot].GivenDamage[victim.Slot].TotalDamage += damageToHeath;
				playerDamageInfos[attacker.Slot].GivenDamage[victim.Slot].Hits++;
			}

			return HookResult.Continue;
		}

		private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
		{
			IsDataShown.Clear();
			return HookResult.Continue;
		}

		private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
		{
			if (!Config.RoundEndSummary)
				return HookResult.Continue;

			List<CCSPlayerController> players = Utilities.GetPlayers();

			foreach (CCSPlayerController target in players)
			{
				if (target is null || !target.IsValid || !target.PlayerPawn.IsValid || target.IsBot || target.IsHLTV || !target.PawnIsAlive)
					continue;

				DisplayDamageInfo(target);
			}

			playerDamageInfos.Clear();
			recentDamages.Clear();

			return HookResult.Continue;
		}

		private void DisplayDamageInfo(CCSPlayerController player)
		{
			if (!playerDamageInfos.ContainsKey(player.Slot))
				return;

			if (IsDataShown.ContainsKey(player.Slot) && IsDataShown[player.Slot])
				return;

			IsDataShown[player.Slot] = true;

			PlayerDamageInfo playerInfo = playerDamageInfos[player.Slot];
			HashSet<int> processedPlayers = new HashSet<int>();

			player.PrintToChat($" {Localizer["phrases.summary.startline"]}");

			foreach (KeyValuePair<int, DamageInfo> entry in playerInfo.GivenDamage)
			{
				int otherPlayerId = entry.Key;
				DamageInfo givenDamageInfo = entry.Value;
				DamageInfo takenDamageInfo = playerInfo.TakenDamage.ContainsKey(otherPlayerId) ? playerInfo.TakenDamage[otherPlayerId] : new DamageInfo();
				processedPlayers.Add(otherPlayerId);

				CCSPlayerController otherPlayer = Utilities.GetPlayerFromSlot(otherPlayerId);
				int otherPlayerHealth = otherPlayer.PlayerPawn.Value!.Health;

				player.PrintToChat($" {Localizer["phrases.summary.dataline", givenDamageInfo.TotalDamage, givenDamageInfo.Hits, takenDamageInfo.TotalDamage, takenDamageInfo.Hits, otherPlayer.PlayerName, otherPlayerHealth > 0 ? $"{otherPlayerHealth}HP" : $"{Localizer["phrases.dead"]}"]}");
			}

			foreach (KeyValuePair<int, DamageInfo> entry in playerInfo.TakenDamage)
			{
				int otherPlayerId = entry.Key;

				if (processedPlayers.Contains(otherPlayerId))
					continue;

				DamageInfo takenDamageInfo = entry.Value;
				DamageInfo givenDamageInfo = new DamageInfo();

				CCSPlayerController otherPlayer = Utilities.GetPlayerFromSlot(otherPlayerId);

				int otherPlayerHealth = otherPlayer.PlayerPawn.Value!.Health;

				player.PrintToChat($" {Localizer["phrases.summary.dataline", givenDamageInfo.TotalDamage, givenDamageInfo.Hits, takenDamageInfo.TotalDamage, takenDamageInfo.Hits, otherPlayer.PlayerName, otherPlayerHealth > 0 ? $"{otherPlayerHealth}HP" : $"{Localizer["phrases.dead"]}"]}");
			}

			player.PrintToChat($" {Localizer["phrases.summary.endline"]}");
		}

		private Dictionary<int, PlayerDamageInfo> playerDamageInfos = new Dictionary<int, PlayerDamageInfo>();

		private class PlayerDamageInfo
		{
			public Dictionary<int, DamageInfo> GivenDamage = new Dictionary<int, DamageInfo>();
			public Dictionary<int, DamageInfo> TakenDamage = new Dictionary<int, DamageInfo>();
		}

		private class DamageInfo
		{
			public int TotalDamage = 0;
			public int Hits = 0;
		}

		private Dictionary<int, Dictionary<int, RecentDamage>> recentDamages = new Dictionary<int, Dictionary<int, RecentDamage>>();

		private class RecentDamage
		{
			public int TotalDamage;
			public DateTime LastDamageTime;
		}
	}
}