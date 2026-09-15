using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>What the bot does with the sides of a blow and with a rage meter it can see is full.</summary>
    public class BotAITests
    {
        private static (GladiatorInstance me, GladiatorInstance opp) Pair()
        {
            var me = new GladiatorInstance(GladiatorDef.Scutarius) { Pos = new Vector2(0f, -200f), Facing = Vector2.up };
            var opp = new GladiatorInstance(GladiatorDef.Brutius) { Pos = Vector2.zero, Facing = Vector2.up };
            me.EquipTrainedWeapon();
            opp.EquipTrainedWeapon();
            return (me, opp);
        }

        [Test]
        public void FacingAReadyAbility_TheBotGuardsMoreOftenThanNot()
        {
            var (me, opp) = Pair();
            opp.Rage = GameConstants.RageMax;
            Assert.IsTrue(opp.CanActivateAbility);

            int guards = 0;
            for (int seed = 0; seed < 200; seed++)
                if (BotAI.Decide(me, opp, (ArenaPickup)null, new System.Random(seed)).Action == ActionType.Defend) guards++;
            Assert.Greater(guards, 100, "with the other man's ability ready, the bot should guard more often than not");

            opp.Rage = 0f;
            guards = 0;
            for (int seed = 0; seed < 200; seed++)
                if (BotAI.Decide(me, opp, (ArenaPickup)null, new System.Random(seed)).Action == ActionType.Defend) guards++;
            Assert.Less(guards, 60, "and only now and then otherwise");
        }

        [Test]
        public void SomeOfTheTime_TheBotGoesForTheSideOrTheBack_AndMostlyStraightAtHim()
        {
            var (me, opp) = Pair();
            int round = 0, straight = 0, runs = 0;
            for (int seed = 0; seed < 300; seed++)
            {
                var d = BotAI.Decide(me, opp, (ArenaPickup)null, new System.Random(seed));
                if (d.Action != ActionType.Move) continue;
                runs++;
                var end = me.Pos + d.AimDirection * (d.Power * me.DashReach());
                if (opp.SectorHitFrom(end) == HitSector.Front) straight++; else round++;
            }
            Assert.Greater(runs, 200);
            Assert.Greater(round, runs * 0.15f, "some runs should end beside or behind him");
            Assert.Less(round, runs * 0.5f, "but not most: the bot still mostly comes straight on");
            Assert.Greater(straight, runs * 0.5f);
        }
    }
}
