using System.Collections.Generic;
using System.Linq;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The six archetypes, each with a weapon of his own and three abilities to choose from - and
    /// every one of the eighteen doing what its card says.
    /// </summary>
    public class RosterTests
    {
        private const float Tol = 0.001f;
        private const float Dt = 1f / 60f;

        [Test]
        public void SixArchetypes_EachWithAWeaponAndThreeAbilitiesNobodyElseHas()
        {
            Assert.AreEqual(6, GladiatorDef.All.Count);
            CollectionAssert.AllItemsAreUnique(GladiatorDef.All.Select(d => d.Id));
            CollectionAssert.AllItemsAreUnique(GladiatorDef.All.Select(d => d.SkilledWith),
                "two archetypes share a weapon - each is meant to walk in with his own");

            var every = GladiatorDef.All.SelectMany(d => d.Abilities).ToList();
            Assert.AreEqual(18, every.Count, "three abilities each");
            CollectionAssert.AllItemsAreUnique(every, "an ability is offered to two archetypes");
            CollectionAssert.AreEquivalent(System.Enum.GetValues(typeof(AbilityKey)), every,
                "every ability belongs to somebody, and nobody offers one that does not exist");

            foreach (var def in GladiatorDef.All)
            {
                Assert.AreSame(def, GladiatorDef.Get(def.Id), $"{def.Name} is not what Get hands back");
                var weapon = WeaponDef.Get(def.SkilledWith);
                Assert.AreEqual(def.SkilledWith, weapon.Kind, $"{def.Name}'s weapon comes back as {weapon.Name}");
                Assert.GreaterOrEqual(weapon.Reach, GameConstants.CollideDistance,
                    $"{weapon.Name} cannot reach a man pressed against him");
                Assert.AreEqual(def.SkilledWith, new GladiatorInstance(def).Weapon,
                    $"{def.Name} should walk in holding his own weapon");

                foreach (var key in def.Abilities)
                {
                    var card = AbilityDef.Get(key);
                    Assert.IsFalse(string.IsNullOrEmpty(card.Name), $"{key} has no name");
                    Assert.IsFalse(string.IsNullOrEmpty(card.Description), $"{key} says nothing about what it does");
                }
            }
        }

        [Test]
        public void AManTakesTheFirstOfHisThree_UnlessTheMatchIsToldOtherwise()
        {
            Assert.AreEqual(AbilityKey.Earthshaker, new GladiatorInstance(GladiatorDef.Brutius).Ability);

            var m = new GameManager(new System.Random(1));
            m.StartMatch(new[] { GladiatorDef.Brutius }, new[] { GladiatorDef.Retiarius }, false,
                new Dictionary<GladiatorId, AbilityKey> { { GladiatorId.Brutius, AbilityKey.StoneSkin } },
                new Dictionary<GladiatorId, AbilityKey> { { GladiatorId.Retiarius, AbilityKey.Shackles } });
            Assert.AreEqual(AbilityKey.StoneSkin, m.State.P1.Roster[0].Ability);
            Assert.AreEqual(AbilityKey.Shackles, m.State.Bot.Roster[0].Ability);

            // Somebody else's ability is not his to take.
            m.StartMatch(new[] { GladiatorDef.Brutius }, new[] { GladiatorDef.Retiarius }, false,
                new Dictionary<GladiatorId, AbilityKey> { { GladiatorId.Brutius, AbilityKey.Net } });
            Assert.AreEqual(AbilityKey.Earthshaker, m.State.P1.Roster[0].Ability);
        }

        [Test]
        public void AnAbilityLastsTheRoundsOnItsCard()
        {
            foreach (var card in AbilityDef.All)
            {
                var owner = GladiatorDef.All.First(d => d.Abilities.Contains(card.Key));
                var g = new GladiatorInstance(owner) { Ability = card.Key, Rage = GameConstants.RageMax };
                g.ActivateAbility();

                int rounds = System.Math.Max(1, card.Rounds);
                for (int round = 1; round < rounds; round++)
                {
                    g.BeginRound();
                    Assert.IsTrue(g.Has(card.Key), $"{card.Name} ended after {round} of its {rounds} rounds");
                }
                g.BeginRound();
                Assert.IsFalse(g.Has(card.Key), $"{card.Name} outlasted its {rounds} rounds");
            }
        }

        // --- Brutius -----------------------------------------------------------------------

        [Test]
        public void Rampage_RunsHalfAsFarAgain_AndHitsForLess()
        {
            var g = Running(GladiatorDef.Brutius, AbilityKey.Rampage);
            var plain = new GladiatorInstance(GladiatorDef.Brutius);
            Assert.AreEqual(plain.DashReach() * GameConstants.RampageSpeedMult, g.DashReach(), Tol);
            Assert.AreEqual(CombatResolver.ComputeAttackDamage(plain) * GameConstants.RampageDamageMult,
                CombatResolver.ComputeAttackDamage(g), Tol);
        }

        [Test]
        public void StoneSkin_TakesLess()
        {
            var stone = Running(GladiatorDef.Brutius, AbilityKey.StoneSkin);
            var plain = new GladiatorInstance(GladiatorDef.Brutius);
            var striker = new GladiatorInstance(GladiatorDef.Hilius);
            Assert.AreEqual(CombatResolver.ApplyMitigation(plain, 10f) * GameConstants.StoneSkinTakenMult,
                CombatResolver.ApplyMitigation(stone, 10f), Tol);
            Assert.Greater(CombatResolver.DealDamage(striker, stone), 0f);
        }

        [Test]
        public void Earthshaker_ThrowsTwiceAsFar_AndRootsTheManItLandsOn()
        {
            var m = Duel(GladiatorDef.Brutius, GladiatorDef.Hastarius, out var me, out var them);
            me.Buff = new ActiveBuff { Key = AbilityKey.Earthshaker, RoundsLeft = 1 };
            FaceOff(me, them, 60f);

            SubmitStandAgainstACharge(m);
            RunIntoAction(m, 0.2f);

            Assert.IsTrue(them.IsStaggered, "the man the blow landed on should be rooted");
            Assert.Greater(Vector2.Distance(me.Pos, them.Pos),
                60f + GameConstants.MaceKnockback * GameConstants.EarthshakerKnockbackMult * 0.9f,
                "he should have been thrown twice the mace's distance");

            them.BeginRound();
            Assert.AreEqual(0f, them.DashReach(), Tol, "rooted through the next round as well");
            them.BeginRound();
            Assert.Greater(them.DashReach(), 0f, "and free after that");
        }

        // --- Barbarius ---------------------------------------------------------------------

        [Test]
        public void Bloodlust_HealsHalfOfWhatHeDeals()
        {
            var g = Running(GladiatorDef.Barbarius, AbilityKey.Bloodlust);
            g.Hp = 40f;
            var target = new GladiatorInstance(GladiatorDef.Brutius);
            float dealt = CombatResolver.DealDamage(g, target);
            Assert.AreEqual(40f + dealt * GameConstants.BloodlustHeal, g.Hp, Tol);
        }

        [Test]
        public void Frenzy_OpensWoundsTwiceAsDeep_ForARoundLonger()
        {
            var plainTarget = new GladiatorInstance(GladiatorDef.Brutius);
            CombatResolver.DealDamage(new GladiatorInstance(GladiatorDef.Barbarius), plainTarget);

            var frenzied = Running(GladiatorDef.Barbarius, AbilityKey.Frenzy);
            var target = new GladiatorInstance(GladiatorDef.Brutius);
            CombatResolver.DealDamage(frenzied, target);

            Assert.AreEqual(plainTarget.BleedPerRound * GameConstants.FrenzyBleedMult, target.BleedPerRound, Tol);
            Assert.AreEqual(GameConstants.FrenzyBleedRounds, target.BleedRoundsLeft);
        }

        [Test]
        public void Berserk_HitsHarder_AndTakesMore()
        {
            var g = Running(GladiatorDef.Barbarius, AbilityKey.Berserk);
            var plain = new GladiatorInstance(GladiatorDef.Barbarius);
            Assert.AreEqual(CombatResolver.ComputeAttackDamage(plain) * GameConstants.BerserkDamageMult,
                CombatResolver.ComputeAttackDamage(g), Tol);
            Assert.AreEqual(CombatResolver.ApplyMitigation(plain, 10f) * GameConstants.BerserkTakenMult,
                CombatResolver.ApplyMitigation(g, 10f), Tol);
        }

        // --- Hilius ------------------------------------------------------------------------

        [Test]
        public void Backstab_LandsFromTheFrontAsIfFromBehind_AndGoesRoundAShieldWall()
        {
            var g = Running(GladiatorDef.Hilius, AbilityKey.Backstab);
            g.Pos = new Vector2(0f, 30f);
            var plain = new GladiatorInstance(GladiatorDef.Hilius) { Pos = new Vector2(0f, 30f) };

            var target = new GladiatorInstance(GladiatorDef.Brutius) { Pos = Vector2.zero, Facing = Vector2.up };
            var other = new GladiatorInstance(GladiatorDef.Brutius) { Pos = Vector2.zero, Facing = Vector2.up };
            Assert.AreEqual(CombatResolver.DealDamage(plain, other) * GameConstants.BackAttackMult,
                CombatResolver.DealDamage(g, target), Tol);

            var wall = new GladiatorInstance(GladiatorDef.Scutarius)
            {
                Pos = Vector2.zero, Facing = Vector2.up,
                Buff = new ActiveBuff { Key = AbilityKey.Bulwark, RoundsLeft = 2 },
            };
            Assert.Greater(CombatResolver.DealDamage(g, wall), 0f, "a blow from behind goes round Bulwark");
        }

        [Test]
        public void Riposte_SendsHalfOfAFrontalBlowBack_AndNothingFromBehind()
        {
            var riposte = Running(GladiatorDef.Hilius, AbilityKey.Riposte);
            riposte.Pos = Vector2.zero;
            riposte.Facing = Vector2.up;

            var front = new GladiatorInstance(GladiatorDef.Brutius) { Pos = new Vector2(0f, 30f) };
            float hp = front.Hp;
            float dealt = CombatResolver.DealDamage(front, riposte, out float returned);
            Assert.AreEqual(dealt * GameConstants.RiposteReturn, returned, Tol);
            Assert.AreEqual(hp - returned, front.Hp, Tol, "and it should have cost him");

            var behind = new GladiatorInstance(GladiatorDef.Brutius) { Pos = new Vector2(0f, -30f) };
            CombatResolver.DealDamage(behind, riposte, out float none);
            Assert.AreEqual(0f, none, Tol);
        }

        [Test]
        public void Mongoose_DoublesHisAttacks()
        {
            var g = Running(GladiatorDef.Hilius, AbilityKey.Mongoose);
            Assert.AreEqual(2 * WeaponDef.Get(GladiatorDef.Hilius.SkilledWith).Attacks, g.AttacksPerRound);
        }

        // --- Scutarius ---------------------------------------------------------------------

        [Test]
        public void Bulwark_TurnsAsideEveryBlowFromTheFront_AndNoneFromBehind()
        {
            var scutarius = Running(GladiatorDef.Scutarius, AbilityKey.Bulwark);
            scutarius.Pos = Vector2.zero;
            scutarius.Facing = Vector2.up;
            var front = new GladiatorInstance(GladiatorDef.Brutius) { Pos = new Vector2(0f, 30f) };
            var behind = new GladiatorInstance(GladiatorDef.Brutius) { Pos = new Vector2(0f, -30f) };

            float hp = scutarius.Hp;
            Assert.AreEqual(0f, CombatResolver.DealDamage(front, scutarius), Tol, "a blow into the shield wall landed");
            Assert.AreEqual(hp, scutarius.Hp, Tol);
            Assert.Greater(CombatResolver.DealDamage(behind, scutarius), 0f, "Bulwark covers the front, not his back");
        }

        [Test]
        public void Testudo_TakesFarLess_AndMovesAtHalfPace()
        {
            var g = Running(GladiatorDef.Scutarius, AbilityKey.Testudo);
            var plain = new GladiatorInstance(GladiatorDef.Scutarius);
            Assert.AreEqual(CombatResolver.ApplyMitigation(plain, 10f) * GameConstants.TestudoTakenMult,
                CombatResolver.ApplyMitigation(g, 10f), Tol);
            Assert.AreEqual(plain.DashReach() * GameConstants.TestudoSpeedMult, g.DashReach(), Tol);
        }

        [Test]
        public void ShieldBash_ThrowsThreeTimesAsFar_AndStopsTheRun()
        {
            var m = Duel(GladiatorDef.Scutarius, GladiatorDef.Hastarius, out var me, out var them);
            me.Buff = new ActiveBuff { Key = AbilityKey.ShieldBash, RoundsLeft = 2 };
            FaceOff(me, them, 50f);

            SubmitStandAgainstACharge(m);
            RunIntoAction(m, 0.2f);

            Assert.Greater(Vector2.Distance(me.Pos, them.Pos),
                50f + GameConstants.ShieldBashKnockback * GameConstants.ShieldBashKnockbackMult * 0.9f,
                "a bash should throw him three times the scutum's distance");
            Assert.IsFalse(them.IsRunning, "and leave him standing where he landed");
        }

        // --- Hastarius ---------------------------------------------------------------------

        [Test]
        public void Lunge_AndTridentThrow_LengthenTheReach()
        {
            var lunge = Running(GladiatorDef.Hastarius, AbilityKey.Lunge);
            Assert.AreEqual(lunge.WeaponDef.Reach * GameConstants.LungeReachMult, lunge.Reach, Tol);

            var thrown = Running(GladiatorDef.Retiarius, AbilityKey.TridentThrow);
            Assert.AreEqual(thrown.WeaponDef.Reach * GameConstants.TridentThrowReachMult, thrown.Reach, Tol);

            // And the swing test reads it: a man past the weapon's own reach and inside the thrown one.
            thrown.Pos = Vector2.zero;
            thrown.Facing = Vector2.up;
            var target = new Vector2(0f, thrown.WeaponDef.Reach * 1.5f);
            Assert.IsTrue(thrown.CanStrikeFrom(thrown.Pos, target));
            Assert.IsFalse(new GladiatorInstance(GladiatorDef.Retiarius) { Facing = Vector2.up }
                .CanStrikeFrom(Vector2.zero, target));
        }

        [Test]
        public void Brace_StrikesTheManChargingIntoHisFront_AndStopsHim()
        {
            var m = Duel(GladiatorDef.Hastarius, GladiatorDef.Scutarius, out var me, out var them);
            me.Buff = new ActiveBuff { Key = AbilityKey.Brace, RoundsLeft = 2 };
            FaceOff(me, them, 90f);

            float hp = them.Hp;
            Assert.IsTrue(m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false));
            Assert.IsTrue(m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.down, 1f, false));
            RunIntoAction(m, 1.2f);

            float spear = CombatResolver.ComputeAttackDamage(me) * them.WeaponDef.IncomingDamageMultiplier;
            Assert.Greater(hp - them.Hp, spear * 1.5f,
                "he should have taken the brace blow on top of the one the spear lands by reach");
        }

        [Test]
        public void SecondWind_HealsItsShareOfHisHealth_AndNotPastFull()
        {
            var h = new GladiatorInstance(GladiatorDef.Hastarius)
            {
                Ability = AbilityKey.SecondWind, Hp = 50f, Rage = GameConstants.RageMax,
            };
            h.ActivateAbility();
            Assert.AreEqual(50f + GladiatorDef.Hastarius.MaxHp * GameConstants.SecondWindHeal, h.Hp, Tol);

            var nearlyWhole = new GladiatorInstance(GladiatorDef.Hastarius)
            {
                Ability = AbilityKey.SecondWind, Hp = GladiatorDef.Hastarius.MaxHp - 5f, Rage = GameConstants.RageMax,
            };
            nearlyWhole.ActivateAbility();
            Assert.AreEqual(GladiatorDef.Hastarius.MaxHp, nearlyWhole.Hp, Tol, "healed past his own health");
        }

        // --- Retiarius ---------------------------------------------------------------------

        [Test]
        public void ANet_HoldsHimStill_ForTheRoundItLandsAndTheNext()
        {
            var g = new GladiatorInstance(GladiatorDef.Brutius);
            float full = g.DashReach();

            g.Ensnare();
            Assert.AreEqual(0f, g.DashReach(), Tol, "the round it lands");
            g.BeginRound();
            Assert.AreEqual(0f, g.DashReach(), Tol, "the round after");
            g.BeginRound();
            Assert.AreEqual(full, g.DashReach(), Tol, "and free again after that");

            g.Ensnare();
            g.ResetForNewClash();
            Assert.AreEqual(full, g.DashReach(), Tol, "a net does not outlast the clash");
        }

        [Test]
        public void ANetThrownThisPhaseStopsTheRunTheOtherManOrderedThisPhase()
        {
            // The abilities go off before either run is laid out, so a net thrown by the player
            // holds the bot on the very phase it was thrown in, though the player's side is always
            // laid out first.
            var m = Duel(GladiatorDef.Retiarius, GladiatorDef.Brutius, out var me, out var them);
            me.Ability = AbilityKey.Net;
            me.Rage = GameConstants.RageMax;
            Assert.IsTrue(m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, true));
            Assert.IsTrue(m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move,
                (me.Pos - them.Pos).normalized, 1f, false));

            AdvanceUntil(m, MatchPhase.Action);

            Assert.IsTrue(them.IsEnsnared, "the net did not land");
            Assert.AreEqual(0f, ObstacleField.Length(them.Path), 0.5f, "he ran anyway");
        }

        [Test]
        public void Shackles_BurnHisRage_AndKeepHisAbilityFromHimForTwoRounds()
        {
            var m = Duel(GladiatorDef.Retiarius, GladiatorDef.Brutius, out var me, out var them);
            me.Ability = AbilityKey.Shackles;
            me.Rage = GameConstants.RageMax;
            them.Rage = GameConstants.RageMax;
            Assert.IsTrue(m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, true));
            Assert.IsTrue(m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false));
            AdvanceUntil(m, MatchPhase.Action);

            Assert.AreEqual(0f, them.Rage, Tol, "his rage should have burned away");

            them.Rage = GameConstants.RageMax;
            them.BeginRound();
            Assert.IsFalse(them.CanActivateAbility, "the first round after");
            them.BeginRound();
            Assert.IsFalse(them.CanActivateAbility, "the second round after");
            them.BeginRound();
            Assert.IsTrue(them.CanActivateAbility, "and his again after that");
        }

        // --- helpers -----------------------------------------------------------------------

        /// <summary>A man of this archetype with this ability already running.</summary>
        private static GladiatorInstance Running(GladiatorDef def, AbilityKey key)
            => new GladiatorInstance(def)
            {
                Ability = key,
                Buff = new ActiveBuff { Key = key, RoundsLeft = System.Math.Max(1, AbilityDef.Get(key).Rounds) },
            };

        /// <summary>One man a side, in the first planning phase of the first clash.</summary>
        private static GameManager Duel(GladiatorDef p1, GladiatorDef bot,
            out GladiatorInstance me, out GladiatorInstance them)
        {
            var m = new GameManager(new System.Random(5));
            m.StartMatch(new[] { p1 }, new[] { bot });
            m.SubmitPick(PlayerSide.P1, p1.Id);
            AdvanceUntil(m, MatchPhase.Planning);
            me = m.State.P1.Active;
            them = m.State.Bot.Active;
            return m;
        }

        /// <summary>The two on open sand in the middle, this far apart, looking at each other.</summary>
        private static void FaceOff(GladiatorInstance me, GladiatorInstance them, float gap)
        {
            me.Pos = new Vector2(0f, -gap * 0.5f);
            them.Pos = new Vector2(0f, gap * 0.5f);
            me.Facing = Vector2.up;
            them.Facing = Vector2.down;
        }

        /// <summary>He stands his ground; the other man comes at him rather than guarding - a guard set
        /// against a blow from the front is not thrown (see GameManager.Shove).</summary>
        private static void SubmitStandAgainstACharge(GameManager m)
        {
            Assert.IsTrue(m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false));
            Assert.IsTrue(m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.down, 0.02f, false));
        }

        private static void SubmitStand(GameManager m)
        {
            Assert.IsTrue(m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false));
            Assert.IsTrue(m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false));
        }

        private static void RunIntoAction(GameManager m, float seconds)
        {
            AdvanceUntil(m, MatchPhase.Action);
            for (float t = 0f; t < seconds && m.State.Phase == MatchPhase.Action; t += Dt) m.Tick(Dt);
        }

        private static void AdvanceUntil(GameManager m, MatchPhase phase)
        {
            for (float t = 0f; t < 30f && m.State.Phase != phase; t += Dt) m.Tick(Dt);
            Assert.AreEqual(phase, m.State.Phase, $"never reached {phase}");
        }
    }
}
