using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace K4ryuuDamageInfo
{
	public class DamageInfoPlugin : BasePlugin
	{
		public override string ModuleName => "Damage Info";
		public override string ModuleVersion => "0.0.1";

		private Dictionary<CCSPlayerController, DamageData> playerDamageData = new Dictionary<CCSPlayerController, DamageData>();

		public override void Load(bool hotReload)
		{
			RegisterEventHandler<EventPlayerHurt>((@event, info) =>
			{
				CCSPlayerController attackerController = @event.Attacker;
				int targetId = @event.Userid.UserId ?? -1;

				if (attackerController.IsValid && !attackerController.IsBot)
				{
					if (!playerDamageData.ContainsKey(attackerController))
					{
						playerDamageData[attackerController] = new DamageData();
					}

					playerDamageData[attackerController].UpdateDamage(attackerController, targetId, @event.DmgHealth, @event.DmgArmor, @event.Hitgroup);
				}

				return HookResult.Continue;
			});
		}
	}

	public class DamageData
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
}
