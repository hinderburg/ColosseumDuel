using System.Collections.Generic;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>BlowLanded: a blow announced with its side, its weapon, its guard and its weight.</summary>
    public class BlowEventTests
    {
        private const float Dt = 1f / 60f;

        private static GameManager StartedClash()
        {
            var m = new GameManager(new System.Random(3));
            m.StartMatch(new[] { GladiatorDef.Brutius }, new[] { GladiatorDef.Hilius });
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
            for (float t = 0f; t < 30f && m.State.Phase != MatchPhase.Planning; t += Dt) m.Tick(Dt);
            return m;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ABlowIsAnnouncedWithItsSideItsWeaponAndWhetherAGuardMetIt(bool guarding)
        {
            var m = StartedClash();
            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            var blows = new List<Blow>();
            m.BlowLanded += blows.Add;

            // The bot's man behind the player's, within the sword's reach; the player faces him or not.
            p1.Pos = Vector2.zero;
            bot.Pos = new Vector2(0f, 50f);
            bot.Facing = Vector2.down;
            p1.Facing = guarding ? Vector2.up : Vector2.down;
            p1.Hp = 1000f;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            for (float t = 0f; t < 30f && m.State.Phase != MatchPhase.Action; t += Dt) m.Tick(Dt);
            for (int i = 0; i < 30; i++) m.Tick(Dt);

            var onPlayer = blows.Find(b => b.Victim == PlayerSide.P1);
            Assert.AreEqual(PlayerSide.Bot, onPlayer.Striker);
            Assert.AreEqual(WeaponKind.SwordAndShield, onPlayer.Weapon, "Hilius fights with the sword and shield");
            Assert.AreEqual(guarding ? HitSector.Front : HitSector.Back, onPlayer.Sector);
            Assert.AreEqual(guarding, onPlayer.Blocked, "a guard set against the blow, or a back turned to it");
            Assert.Greater(onPlayer.Damage, 0f);
            Assert.AreEqual(GladiatorDef.Brutius.MaxHp, onPlayer.VictimMaxHp);
            Assert.IsFalse(onPlayer.Lethal);
            Assert.AreEqual(bot.Pos, onPlayer.From);
        }

        [Test]
        public void TheBlowThatDropsHimSaysSo()
        {
            var m = StartedClash();
            var p1 = m.State.P1.Active;
            var bot = m.State.Bot.Active;
            var blows = new List<Blow>();
            m.BlowLanded += blows.Add;

            p1.Pos = Vector2.zero;
            bot.Pos = new Vector2(0f, 50f);
            bot.Facing = Vector2.down;
            p1.Facing = Vector2.up;
            p1.Hp = 1f;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            for (float t = 0f; t < 30f && m.State.Phase != MatchPhase.Action; t += Dt) m.Tick(Dt);
            for (int i = 0; i < 30 && p1.Alive; i++) m.Tick(Dt);

            Assert.IsFalse(p1.Alive);
            Assert.IsTrue(blows.Exists(b => b.Victim == PlayerSide.P1 && b.Lethal), "the killing blow should be marked");
        }
    }
}
