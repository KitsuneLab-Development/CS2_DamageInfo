using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;

namespace K4ryuuDamageInfo
{
	[MinimumApiVersion(30)]
	public class DamageInfoPlugin : BasePlugin
	{
		public override string ModuleName => "Damage Info";
		public override string ModuleVersion => "1.3.2";
		public override string ModuleAuthor => "K4ryuu";
		private readonly Dictionary<CCSPlayerController, DamageData> playerDamageData = new Dictionary<CCSPlayerController, DamageData>();
		public Dictionary<int, Dictionary<int, DamagePlayerInfo>> playerDamageInfo = new Dictionary<int, Dictionary<int, DamagePlayerInfo>>();

		public override void Load(bool hotReload)
		{
			new CFG().CheckConfig(ModuleDirectory);

			RegisterEventHandler<EventPlayerHurt>((@event, info) =>
			{
				CCSPlayerController attacker = @event.Attacker;

				if (!attacker.IsValid || attacker.IsBot && !(@event.DmgHealth > 0 || @event.DmgArmor > 0))
					return HookResult.Continue;

				if (@event.Userid.TeamNum != attacker.TeamNum || CFG.config.FFAMode)
				{
					int targetId = (int)@event.Userid.UserId!;

					if (CFG.config.RoundEndPrint)
						UpdatePlayerDamageInfo(@event, targetId);

					if (CFG.config.CenterPrint)
						UpdatePlayerDamageData(attacker, targetId, @event.DmgHealth, @event.DmgArmor, @event.Hitgroup);
				}

				return HookResult.Continue;
			});

			RegisterEventHandler<EventRoundEnd>((@event, info) =>
			{
				if (!CFG.config.RoundEndPrint)
					return HookResult.Continue;

				ProcessRoundEnd();

				return HookResult.Continue;
			});
		}

		private void UpdatePlayerDamageInfo(EventPlayerHurt @event, int targetId)
		{
			int attackerId = (int)@event.Attacker.UserId!;
			if (!playerDamageInfo.TryGetValue(attackerId, out var attackerInfo))
				playerDamageInfo[attackerId] = attackerInfo = new Dictionary<int, DamagePlayerInfo>();

			if (!attackerInfo.TryGetValue(targetId, out var targetInfo))
				attackerInfo[targetId] = targetInfo = new DamagePlayerInfo();

			targetInfo.DamageHP += @event.DmgHealth;
			targetInfo.Hits++;
		}

		private void UpdatePlayerDamageData(CCSPlayerController attacker, int targetId, int damageHP, int damageArmor, int hitGroup)
		{
			if (!playerDamageData.TryGetValue(attacker, out var damageData))
				playerDamageData[attacker] = damageData = new DamageData();

			damageData.UpdateDamage(attacker, targetId, damageHP, damageArmor, hitGroup);
		}

		private void ProcessRoundEnd()
		{
			HashSet<(int, int)> processedPairs = new HashSet<(int, int)>();

			foreach (var entry in playerDamageInfo)
			{
				int attackerId = entry.Key;
				foreach (var (targetId, targetEntry) in entry.Value)
				{
					if (processedPairs.Contains((attackerId, targetId)) || processedPairs.Contains((targetId, attackerId)))
						continue;

					// Access and use the damage information as needed.
					int damageGiven = targetEntry.DamageHP;
					int hitsGiven = targetEntry.Hits;
					int damageTaken = 0;
					int hitsTaken = 0;

					if (playerDamageInfo.TryGetValue(targetId, out var targetInfo) && targetInfo.TryGetValue(attackerId, out var takenInfo))
					{
						damageTaken = takenInfo.DamageHP;
						hitsTaken = takenInfo.Hits;
					}

					var attackerController = Utilities.GetPlayerFromUserid(attackerId);
					var targetController = Utilities.GetPlayerFromUserid(targetId);

					if (attackerController != null && targetController != null)
					{
						int attackerHP = attackerController.Health < 0 ? 0 : attackerController.Health;
						string attackerName = attackerController.PlayerName;

						int targetHP = targetController.Health < 0 ? 0 : targetController.Health;
						string targetName = targetController.PlayerName;

						attackerController.PrintToChat($" {CFG.config.ChatPrefix} To: [{damageGiven} / {hitsGiven}] From: [{damageTaken} / {hitsTaken}] - {targetName} -- ({targetHP} hp)");
						targetController.PrintToChat($" {CFG.config.ChatPrefix} To: [{damageTaken} / {hitsTaken}] From: [{damageGiven} / {hitsGiven}] - {attackerName} -- ({attackerHP} hp)");
					}

					// Mark this pair as processed to avoid duplicates.
					processedPairs.Add((attackerId, targetId));
				}
			}

			playerDamageInfo.Clear();
		}
	}

	internal class DamageData
	{
		private readonly Dictionary<int, DamageInfo> damageInfo = new Dictionary<int, DamageInfo>();
		private readonly Dictionary<int, DateTime> lastUpdateTime = new Dictionary<int, DateTime>();

		public void UpdateDamage(CCSPlayerController attackerController, int targetId, int damageHP, int damageArmor, int hitGroup)
		{
			if (!damageInfo.TryGetValue(targetId, out var info))
				damageInfo[targetId] = info = new DamageInfo();

			ClearOutdatedDamageInfo(targetId);

			info.DamageHP += damageHP;
			info.DamageArmor += damageArmor;

			var printMessage = damageHP > 200
				? $"Damage Given:\nHP {info.DamageHP} | HitGroup: {HitGroupToString(hitGroup)}"
				: $"Damage Given:\nHP {info.DamageHP} | Armor {info.DamageArmor} | HitGroup: {HitGroupToString(hitGroup)}";

			attackerController.PrintToCenter(printMessage);

			lastUpdateTime[targetId] = DateTime.Now;
		}

		private void ClearOutdatedDamageInfo(int targetId)
		{
			if (lastUpdateTime.TryGetValue(targetId, out var updateTime))
			{
				var elapsed = DateTime.Now - updateTime;
				if (elapsed.TotalSeconds > 5)
				{
					damageInfo.Remove(targetId);
					lastUpdateTime.Remove(targetId);
				}
			}
		}

		public string HitGroupToString(int hitGroup)
		{
			return hitGroup switch
			{
				0 => "Body",
				1 => "Head",
				2 => "Chest",
				3 => "Stomach",
				4 => "Left Arm",
				5 => "Right Arm",
				6 => "Left Leg",
				7 => "Right Leg",
				10 => "Gear",
				_ => "Unknown"
			};
		}
	}

	public class DamageInfo
	{
		public int DamageHP { get; set; } = 0;
		public int DamageArmor { get; set; } = 0;
	}

	public class DamagePlayerInfo
	{
		public int DamageHP { get; set; } = 0;
		public int Hits { get; set; } = 0;
	}
}
