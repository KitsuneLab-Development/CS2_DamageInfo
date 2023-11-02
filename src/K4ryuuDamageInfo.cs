using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace K4ryuuDamageInfo
{
	public class DamageInfoPlugin : BasePlugin
	{
		public override string ModuleName => "Damage Info";
		public override string ModuleVersion => "1.0.0";
		private Dictionary<CCSPlayerController, DamageData> playerDamageData = new Dictionary<CCSPlayerController, DamageData>();
		public Dictionary<int, Dictionary<int, DamagePlayerInfo>> playerDamageInfo = new Dictionary<int, Dictionary<int, DamagePlayerInfo>>();

		public override void Load(bool hotReload)
		{
			new CFG().CheckConfig(ModuleDirectory);

			RegisterEventHandler<EventPlayerHurt>((@event, info) =>
			{
				CCSPlayerController attackerController = @event.Attacker;
				int targetId = @event.Userid.UserId ?? -1;

				if (attackerController.IsValid && !attackerController.IsBot)
				{
					if (CFG.config.RoundEndPrint)
					{
						int attackerId = attackerController.UserId ?? -1;

						if (!playerDamageInfo.ContainsKey(attackerId))
						{
							playerDamageInfo[attackerId] = new Dictionary<int, DamagePlayerInfo>();
						}

						if (!playerDamageInfo[attackerId].ContainsKey(targetId))
						{
							playerDamageInfo[attackerId][targetId] = new DamagePlayerInfo();
						}

						playerDamageInfo[attackerId][targetId].DamageHP += @event.DmgHealth;
						playerDamageInfo[attackerId][targetId].Hits++;
					}

					if (CFG.config.CenterPrint)
					{
						if (!playerDamageData.ContainsKey(attackerController))
						{
							playerDamageData[attackerController] = new DamageData();
						}

						playerDamageData[attackerController].UpdateDamage(attackerController, targetId, @event.DmgHealth, @event.DmgArmor, @event.Hitgroup);
					}
				}

				return HookResult.Continue;
			});

			RegisterEventHandler<EventRoundEnd>((@event, info) =>
			{
				if (CFG.config.RoundEndPrint)
				{
					HashSet<Tuple<int, int>> processedPairs = new HashSet<Tuple<int, int>>();

					foreach (var entry in playerDamageInfo)
					{
						int attackerId = entry.Key;
						foreach (var targetEntry in entry.Value)
						{
							int targetId = targetEntry.Key;
							if (processedPairs.Contains(new Tuple<int, int>(attackerId, targetId)) || processedPairs.Contains(new Tuple<int, int>(targetId, attackerId)))
							{
								// This pair has already been processed, so skip it.
								continue;
							}

							// Access and use the damage information as needed.
							int damageGiven = 0;
							int hitsGiven = 0;
							int damageTaken = 0;
							int hitsTaken = 0;

							if (playerDamageInfo.ContainsKey(attackerId) && playerDamageInfo[attackerId].ContainsKey(targetId))
							{
								damageGiven = playerDamageInfo[attackerId][targetId].DamageHP;
								hitsGiven = playerDamageInfo[attackerId][targetId].Hits;
							}

							if (playerDamageInfo.ContainsKey(targetId) && playerDamageInfo[targetId].ContainsKey(attackerId))
							{
								damageTaken = playerDamageInfo[targetId][attackerId].DamageHP;
								hitsTaken = playerDamageInfo[targetId][attackerId].Hits;
							}

							CCSPlayerController attackerController = Utilities.GetPlayerFromUserid(attackerId);
							CCSPlayerController targetController = Utilities.GetPlayerFromUserid(targetId);

							attackerController.PrintToChat($" {CFG.config.ChatPrefix} To: [{damageGiven} / {hitsGiven}] From: [{damageTaken} / {hitsTaken}] - {targetController.PlayerName} -- ({targetController.Health} hp)");
							targetController.PrintToChat($" {CFG.config.ChatPrefix} To: [{damageTaken} / {hitsTaken}] From: [{damageGiven} / {hitsGiven}] - {attackerController.PlayerName} -- ({attackerController.Health} hp)");

							// Mark this pair as processed to avoid duplicates.
							processedPairs.Add(new Tuple<int, int>(attackerId, targetId));
						}
					}

					playerDamageInfo.Clear();
				}

				return HookResult.Continue;
			});
		}
	}

	internal class DamageData
	{
		private Dictionary<int, DamageInfo> damageInfo = new Dictionary<int, DamageInfo>();
		private Dictionary<int, DateTime> lastUpdateTime = new Dictionary<int, DateTime>();


		public void UpdateDamage(CCSPlayerController attackerController, int targetId, int damageHP, int damageArmor, int Hitgroup)
		{
			if (!damageInfo.ContainsKey(targetId))
			{
				damageInfo[targetId] = new DamageInfo();
				lastUpdateTime[targetId] = DateTime.Now;
			}

			ClearOutdatedDamageInfo(targetId);

			damageInfo[targetId].DamageHP += damageHP;
			damageInfo[targetId].DamageArmor += damageArmor;

			if (damageHP > 350)
			{
				attackerController.PrintToCenter($"Damage Given:\nHP {damageInfo[targetId].DamageHP} | HitGroup: {HitGroupToString(Hitgroup)}");
			}
			else
				attackerController.PrintToCenter($"Damage Given:\nHP {damageInfo[targetId].DamageHP} | Armor {damageInfo[targetId].DamageArmor} | HitGroup: {HitGroupToString(Hitgroup)}");

			lastUpdateTime[targetId] = DateTime.Now;
		}

		private void ClearOutdatedDamageInfo(int targetId)
		{
			if (lastUpdateTime.ContainsKey(targetId))
			{
				TimeSpan elapsed = DateTime.Now - lastUpdateTime[targetId];
				if (elapsed.TotalSeconds > 5)
				{
					damageInfo.Remove(targetId);
					lastUpdateTime.Remove(targetId);
				}
			}
		}

		public string HitGroupToString(int hitGroup)
		{
			switch (hitGroup)
			{
				case 0:
					return "Body";
				case 1:
					return "Head";
				case 2:
					return "Chest";
				case 3:
					return "Stomach";
				case 4:
					return "Left Arm";
				case 5:
					return "Right Arm";
				case 6:
					return "Left Leg";
				case 7:
					return "Right Leg";
				case 10:
					return "Gear";
				default:
					return "Unknown";
			}
		}
	}

	public class DamageInfo
	{
		public int DamageHP { get; set; }
		public int DamageArmor { get; set; }
	}

	public class DamagePlayerInfo
	{
		public int DamageHP { get; set; }
		public int Hits { get; set; }
	}
}
