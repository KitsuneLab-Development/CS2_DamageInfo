using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace K4ryuuDamageInfo
{
	public sealed class PluginConfig : BasePluginConfig
	{
		[JsonPropertyName("round-end-summary")]
		public bool RoundEndSummary { get; set; } = true;

		[JsonPropertyName("round-end-summary-allow-death-print")]
		public bool AllowDeathPrint { get; set; } = true;

		[JsonPropertyName("round-end-summary-show-only-killer")]
		public bool ShowOnlyKiller { get; set; } = false;

		[JsonPropertyName("round-end-summary-show-friendlyfire")]
		public bool ShowFriendlyFire { get; set; } = false;

		[JsonPropertyName("round-end-summary-show-all-damages")]
		public bool ShowAllDamages { get; set; } = false;

		[JsonPropertyName("center-damage-info")]
		public bool CenterDamageInfo { get; set; } = true;

		[JsonPropertyName("console-damage-info")]
		public bool ConsoleDamageInfo { get; set; } = true;

		[JsonPropertyName("ffa-mode")]
		public bool FFAMode { get; set; } = false;

		[JsonPropertyName("norounds-mode")]
		public bool NoRoundsMode { get; set; } = false;

		[JsonPropertyName("center-info-timeout")]
		public int CenterInfoTimeout { get; set; } = 3;

		[JsonPropertyName("center-info-flags")]
		public List<string> CenterInfoFlags { get; set; } =
		[
			"@myplugin/can-see-permission",
			"#myplugin/can-see-group",
			"can-see-override",
			"leave-empty-so-everyone-see-it"
		];

		[JsonPropertyName("ConfigVersion")]
		public override int Version { get; set; } = 5;
	}

	[MinimumApiVersion(300)]
	public class DamageInfoPlugin : BasePlugin, IPluginConfig<PluginConfig>
	{
		public override string ModuleName => "Damage Informations";
		public override string ModuleVersion => "2.4.0";
		public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
		public required PluginConfig Config { get; set; } = new PluginConfig();
		public CCSGameRules? GameRules;
		private readonly Dictionary<int, PlayerData> _playerData = [];

		private class PlayerData
		{
			public bool IsDataShown { get; set; }
			public int VictimKiller { get; set; } = -1;
			public string? CenterMessage { get; set; }
			public Timer? CenterTimer { get; set; }
			public PlayerDamageInfo DamageInfo { get; } = new PlayerDamageInfo();
			public Dictionary<int, RecentDamage> RecentDamages { get; } = [];
		}

		private class PlayerDamageInfo
		{
			public Dictionary<int, DamageInfo> GivenDamage { get; } = [];
			public Dictionary<int, DamageInfo> TakenDamage { get; } = [];
		}

		private class DamageInfo
		{
			public int TotalDamage { get; set; }
			public int Hits { get; set; }
		}

		private class RecentDamage
		{
			public int TotalDamage { get; set; }
			public DateTime LastDamageTime { get; set; }
		}

		public void OnConfigParsed(PluginConfig config)
		{
			if (config.Version < Config.Version)
				Logger.LogWarning("Configuration version mismatch (Expected: {Expected} | Current: {Current})", Config.Version, config.Version);

			Config = config;

			if (config.CenterInfoFlags.Contains("leave-empty-so-everyone-see-it"))
			{
				Logger.LogWarning("Please configure the CenterInfoFlags in the configuration file by removing the example values.");
				Logger.LogWarning("Damage center messages won't be displayed until the configuration is properly set.");
			}
		}

		public override void Load(bool hotReload)
		{
			OnMapStart();

			RegisterListener<Listeners.OnMapStart>(OnMapStart);
			RegisterListener<Listeners.OnTick>(OnTick);

			RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
			RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
			RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
			RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
			RegisterEventHandler<EventRoundStart>(OnRoundStart);
		}

		private PlayerData GetPlayerData(int slot)
		{
			if (!_playerData.TryGetValue(slot, out var data))
			{
				data = new PlayerData();
				_playerData[slot] = data;
			}

			return data;
		}

		private void OnTick()
		{
			var players = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV && p.Connected == PlayerConnectedState.PlayerConnected && p.Team > CsTeam.Spectator);

			if (!players.Any())
				return;

			foreach (var player in players)
			{
				var data = GetPlayerData(player.Slot);

				if (!string.IsNullOrEmpty(data.CenterMessage))
					player.PrintToCenterHtml(data.CenterMessage);
			}
		}

		private void OnMapStart(string? mapName = null)
		{
			AddTimer(1.0f, () =>
			{
				GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
			});
		}

		private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
		{
			if (!IsValidPlayer(@event.Userid))
				return HookResult.Continue;

			GetPlayerData(@event.Userid!.Slot).IsDataShown = false;
			return HookResult.Continue;
		}

		private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
		{
			var victim = @event.Userid;
			if (GameRules == null || GameRules.WarmupPeriod || !Config.AllowDeathPrint || !IsValidPlayer(victim) || victim!.Connected == PlayerConnectedState.PlayerDisconnecting)
				return HookResult.Continue;

			var attacker = @event.Attacker;
			GetPlayerData(victim.Slot).VictimKiller = (attacker != null && IsValidPlayer(attacker)) ? attacker.Slot : -1;
			if (!victim.IsBot)
				DisplayDamageInfo(victim);

			if (Config.NoRoundsMode && GetPlayerData(victim.Slot).DamageInfo != null)
			{
				GetPlayerData(victim.Slot).DamageInfo.GivenDamage.Clear();
				GetPlayerData(victim.Slot).DamageInfo.TakenDamage.Clear();
			}

			return HookResult.Continue;
		}

		private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
		{
			var victim = @event.Userid;
			var attacker = @event.Attacker;

			if (!IsValidPlayer(victim) || !IsValidPlayer(attacker))
				return HookResult.Continue;

			if (victim!.TeamNum == attacker!.TeamNum && !Config.ShowFriendlyFire)
				return HookResult.Continue;

			int damageToHealth = @event.DmgHealth;
			int damageToArmor = @event.DmgArmor;

			if (!attacker.IsBot)
			{
				string hitgroup = Localizer.ForPlayer(attacker, $"phrases.hitgroup.{@event.Hitgroup}");

				if (!attacker.IsBot && (victim.TeamNum != attacker.TeamNum || Config.FFAMode))
				{
					if (Config.ConsoleDamageInfo)
					{
						attacker.PrintToConsole(Localizer.ForPlayer(attacker, "phrases.console.normal", victim.PlayerName, damageToHealth, damageToArmor, hitgroup));

						if (!victim.IsBot)
							victim.PrintToConsole(Localizer.ForPlayer(victim, "phrases.console.inverse", attacker.PlayerName, damageToHealth, damageToArmor, hitgroup));
					}

					if (Config.CenterDamageInfo && PlayerHasPermissions(attacker))
					{
						var attackerData = GetPlayerData(attacker.Slot);
						if (!attackerData.RecentDamages.TryGetValue(victim.Slot, out var recentDamage))
						{
							recentDamage = new RecentDamage();
							attackerData.RecentDamages[victim.Slot] = recentDamage;
						}

						if (DateTime.Now - recentDamage.LastDamageTime <= TimeSpan.FromSeconds(5))
							recentDamage.TotalDamage += damageToHealth;
						else
							recentDamage.TotalDamage = damageToHealth;

						recentDamage.LastDamageTime = DateTime.Now;
						ShowTemporaryCenterMessage(attacker, damageToArmor, hitgroup, recentDamage.TotalDamage);
					}
				}
			}

			if (GameRules != null && !GameRules.WarmupPeriod && Config.RoundEndSummary)
			{
				var victimData = GetPlayerData(victim.Slot);
				var attackerData = GetPlayerData(attacker.Slot);

				if (!victimData.DamageInfo.TakenDamage.TryGetValue(attacker.Slot, out var takenDamage))
				{
					takenDamage = new DamageInfo();
					victimData.DamageInfo.TakenDamage[attacker.Slot] = takenDamage;
				}

				if (!attackerData.DamageInfo.GivenDamage.TryGetValue(victim.Slot, out var givenDamage))
				{
					givenDamage = new DamageInfo();
					attackerData.DamageInfo.GivenDamage[victim.Slot] = givenDamage;
				}

				takenDamage.TotalDamage += damageToHealth;
				takenDamage.Hits++;

				givenDamage.TotalDamage += damageToHealth;
				givenDamage.Hits++;
			}

			return HookResult.Continue;
		}

		private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
		{
			_playerData.Clear();
			return HookResult.Continue;
		}

		private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
		{
			if (!Config.RoundEndSummary)
				return HookResult.Continue;

			var players = Utilities.GetPlayers().Where(p => IsValidPlayer(p) && !p.IsBot && !p.IsHLTV && p.Connected == PlayerConnectedState.PlayerConnected && p.Team > CsTeam.Spectator).ToList();
			if (players.Count == 0)
				return HookResult.Continue;

			foreach (var player in players)
				DisplayDamageInfo(player);

			_playerData.Clear();
			return HookResult.Continue;
		}

		private void DisplayDamageInfo(CCSPlayerController player)
		{
			if (player.IsBot)
				return;

			var data = GetPlayerData(player.Slot);
			if (data.IsDataShown)
				return;

			if (Config.ShowAllDamages)
			{
				var allSummaries = _playerData.Values.Select(pd => SummarizePlayerDamage(pd.DamageInfo)).ToList();
				if (allSummaries.Count == 0)
					return;

				data.IsDataShown = true;
				player.PrintToChat($" {Localizer.ForPlayer(player, "phrases.summary.startline")}");

				foreach (var (given, taken) in allSummaries)
				{
					string dataLine = Localizer.ForPlayer(player, "phrases.summary.dataline", taken.TotalDamage, taken.Hits, given.TotalDamage, given.Hits, "Unknown", Localizer.ForPlayer(player, "phrases.dead"));
					player.PrintToChat($" {dataLine}");
				}

				player.PrintToChat($" {Localizer.ForPlayer(player, "phrases.summary.endline")}");
			}
			else
			{
				if (GetPlayerData(player.Slot).DamageInfo.GivenDamage.Count == 0)
					return;

				data.IsDataShown = true;
				DisplayPlayerDamageInfo(player, data.DamageInfo);
			}
		}

		private static (DamageInfo given, DamageInfo taken) SummarizePlayerDamage(PlayerDamageInfo info)
		{
			var totalGiven = new DamageInfo();
			var totalTaken = new DamageInfo();

			foreach (var entry in info.GivenDamage)
			{
				totalGiven.TotalDamage += entry.Value.TotalDamage;
				totalGiven.Hits += entry.Value.Hits;
			}

			foreach (var entry in info.TakenDamage)
			{
				totalTaken.TotalDamage += entry.Value.TotalDamage;
				totalTaken.Hits += entry.Value.Hits;
			}

			return (totalGiven, totalTaken);
		}

		private void DisplayPlayerDamageInfo(CCSPlayerController player, PlayerDamageInfo info)
		{
			if (player.IsBot)
				return;

			bool headerPrinted = false;
			var processedSlots = new HashSet<int>();

			foreach (var entry in info.GivenDamage)
			{
				int otherSlot = entry.Key;
				if (Config.ShowOnlyKiller && GetPlayerData(player.Slot).VictimKiller != otherSlot)
					continue;

				if (!headerPrinted)
				{
					player.PrintToChat($" {Localizer.ForPlayer(player, "phrases.summary.startline")}");
					headerPrinted = true;
				}

				var given = entry.Value;
				info.TakenDamage.TryGetValue(otherSlot, out var taken);

				taken ??= new DamageInfo();

				processedSlots.Add(otherSlot);
				PrintDataLine(player, otherSlot, given, taken);
			}

			foreach (var entry in info.TakenDamage)
			{
				int otherSlot = entry.Key;
				if (Config.ShowOnlyKiller && GetPlayerData(player.Slot).VictimKiller != otherSlot)
					continue;

				if (processedSlots.Contains(otherSlot))
					continue;

				if (!headerPrinted)
				{
					player.PrintToChat($" {Localizer.ForPlayer(player, "phrases.summary.startline")}");
					headerPrinted = true;
				}

				var taken = entry.Value;
				PrintDataLine(player, otherSlot, new DamageInfo(), taken);
			}

			if (headerPrinted)
				player.PrintToChat($" {Localizer.ForPlayer(player, "phrases.summary.endline")}");
		}

		private void PrintDataLine(CCSPlayerController player, int otherSlot, DamageInfo given, DamageInfo taken)
		{
			if (player.IsBot)
				return;

			var other = Utilities.GetPlayerFromSlot(otherSlot);
			string otherName = (other != null && other.IsValid) ? other.PlayerName : "Unknown";
			int otherHealth = (other?.PlayerPawn is { IsValid: true } pawn) ? pawn.Value!.Health : 0;
			string healthString = otherHealth > 0 ? $"{otherHealth}HP" : Localizer.ForPlayer(player, "phrases.dead");

			string dataLine = Localizer.ForPlayer(player, "phrases.summary.dataline",
				given.TotalDamage, given.Hits, taken.TotalDamage, taken.Hits, otherName, healthString);

			player.PrintToChat($" {dataLine}");
		}

		private static bool IsValidPlayer(CCSPlayerController? player) =>
			player != null && player.IsValid && player.PlayerPawn != null && player.PlayerPawn.IsValid;

		public bool PlayerHasPermissions(CCSPlayerController player)
		{
			if (Config.CenterInfoFlags.Count == 0)
				return true;

			foreach (string flag in Config.CenterInfoFlags)
			{
				if (flag[0] == '@' && AdminManager.PlayerHasPermissions(player, flag))
					return true;

				if (flag[0] == '#' && AdminManager.PlayerInGroup(player, flag))
					return true;

				if (AdminManager.PlayerHasCommandOverride(player, flag))
					return true;
			}

			return false;
		}

		private void ShowTemporaryCenterMessage(CCSPlayerController player, int armorDamage, string hitgroup, int healthDamage)
		{
			if (player.IsBot)
				return;

			var data = GetPlayerData(player.Slot);
			if (data.CenterTimer != null)
			{
				data.CenterTimer.Kill();
				data.CenterTimer = null;
			}

			data.CenterMessage = Localizer.ForPlayer(player, "phrases.center.html", hitgroup, armorDamage, healthDamage);

			data.CenterTimer = AddTimer(Config.CenterInfoTimeout, () =>
			{
				data.CenterMessage = null;
				data.CenterTimer = null;
			});
		}
	}

	public static class DictionaryExtensions
	{
		public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func<TValue> factory)
			where TKey : notnull
		{
			if (!dict.TryGetValue(key, out var value))
			{
				value = factory();
				dict[key] = value;
			}

			return value;
		}
	}
}