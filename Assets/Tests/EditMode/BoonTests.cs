using System.Collections.Generic;
using System.Linq;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// Boons: three of a side's five offered each time a new man of it steps out, one kept for the
    /// rest of the match on every man of the side - and what each of the eight does.
    /// </summary>
    public class BoonTests
    {
        private const float Dt = 1f / 60f;
        private const float Tol = 0.0001f;

        private static readonly GladiatorDef[] Squad = { GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius };
        private static readonly GladiatorDef[] BotSquad = { GladiatorDef.Scutarius, GladiatorDef.Hastarius, GladiatorDef.Retiarius };

        private static GameManager Match(List<BoonKey> p1 = null, List<BoonKey> bot = null, int seed = 5)
        {
            var m = new GameManager(new System.Random(seed));
            m.StartMatch(Squad, BotSquad, false, null, null, p1 ?? BoonDef.DefaultLoadout(), bot ?? BoonDef.DefaultLoadout());
            return m;
        }

        [Test]
        public void AtTheStartEachSideIsOfferedThreeOfItsFive_AndTheBotChoosesAtOnce()
        {
            var m = Match();
            Assert.IsTrue(m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius));

            Assert.AreEqual(MatchPhase.BoonPick, m.State.Phase, "the clash should wait on the player's choice");
            var offer = m.State.P1.BoonOffer;
            Assert.AreEqual(BoonDef.OfferSize, offer.Count);
            CollectionAssert.AllItemsAreUnique(offer);
            CollectionAssert.IsSubsetOf(offer, m.State.P1.BoonLoadout, "only boons he brought");

            Assert.AreEqual(1, m.State.Bot.Boons.Count, "the bot takes its boon at once");
            Assert.IsFalse(m.State.Bot.NeedsBoonPick);
        }

        [Test]
        public void TakingOneAnnouncesTheClash_AndOnlyOneOnOfferCanBeTaken()
        {
            var m = Match();
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
            var offer = m.State.P1.BoonOffer.ToList();
            var notOffered = m.State.P1.BoonLoadout.First(k => !offer.Contains(k));

            Assert.IsFalse(m.SubmitBoon(PlayerSide.P1, notOffered), "one not on offer should be refused");
            Assert.IsTrue(m.SubmitBoon(PlayerSide.P1, offer[1]));

            CollectionAssert.Contains(m.State.P1.Boons, offer[1]);
            Assert.AreEqual(MatchPhase.Reveal, m.State.Phase, "with nobody left choosing the clash is announced");
            Assert.IsFalse(m.SubmitBoon(PlayerSide.P1, offer[0]), "one choice per man");
        }

        [Test]
        public void TenSecondsLaterOneIsChosenForHim()
        {
            var m = Match();
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
            for (float t = 0f; t < GameConstants.BoonPickTime - 0.5f; t += Dt) m.Tick(Dt);
            Assert.AreEqual(MatchPhase.BoonPick, m.State.Phase, "he still has time");

            for (float t = 0f; t < 1f; t += Dt) m.Tick(Dt);
            Assert.AreEqual(1, m.State.P1.Boons.Count, "his time ran out and one should have been chosen for him");
            Assert.AreNotEqual(MatchPhase.BoonPick, m.State.Phase);
        }

        [Test]
        public void OnAutoHisIsChosenAtOnce()
        {
            var m = Match();
            m.P1Auto = true;
            Assert.AreEqual(MatchPhase.Reveal, m.State.Phase, "on auto the pick and the boon are both taken for him");
            Assert.AreEqual(1, m.State.P1.Boons.Count);
        }

        [Test]
        public void WithoutFiveToChooseFromThereIsNoChoosing()
        {
            var m = new GameManager(new System.Random(5));
            m.StartMatch(Squad, BotSquad);
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
            Assert.AreEqual(MatchPhase.Reveal, m.State.Phase, "no boons, no pause");
            Assert.AreEqual(0, m.State.P1.Boons.Count);
            Assert.AreEqual(0, m.State.Bot.Boons.Count);
        }

        /// <summary>
        /// A man who falls earns his side another boon - of the ones it does not have yet - and the
        /// side whose man is still standing earns none: the comeback is the losing side's.
        /// </summary>
        [Test]
        public void AManWhoFallsEarnsHisSideAnother_AndTheSurvivorsSideNone()
        {
            var m = Match();
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
            var first = m.State.P1.BoonOffer[0];
            m.SubmitBoon(PlayerSide.P1, first);
            int botBoons = m.State.Bot.Boons.Count;

            for (int i = 0; i < 2000 && m.State.Phase != MatchPhase.Planning; i++) m.Tick(Dt);
            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f);
            for (int i = 0; i < 2000 && m.State.Phase != MatchPhase.Action; i++) m.Tick(Dt);

            // He falls in this action phase; the opponent does not.
            m.State.P1.Active.Hp = 0f;
            m.State.P1.Active.Alive = false;
            for (int i = 0; i < 5000 && m.State.Phase != MatchPhase.Pick; i++) m.Tick(Dt);
            Assert.AreEqual(MatchPhase.Pick, m.State.Phase, "his fall should have ended the clash");

            Assert.IsTrue(m.SubmitPick(PlayerSide.P1, GladiatorId.Barbarius));
            Assert.AreEqual(MatchPhase.BoonPick, m.State.Phase, "the man who stepped out earns his side a boon");
            var offer = m.State.P1.BoonOffer;
            Assert.AreEqual(BoonDef.OfferSize, offer.Count, "four left to draw three from");
            CollectionAssert.DoesNotContain(offer, first, "not one he already has");
            Assert.AreEqual(botBoons, m.State.Bot.Boons.Count, "the survivor's side earns nothing");
        }

        [Test]
        public void BattleSpirit_EachClashStartsWithRage()
        {
            var loadout = new List<BoonKey> { BoonKey.BattleSpirit };
            var m = Match(loadout, loadout);
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
            Assert.IsTrue(m.SubmitBoon(PlayerSide.P1, BoonKey.BattleSpirit));
            Assert.AreEqual(GameConstants.BattleSpiritRage, m.State.P1.Active.Rage, Tol);
        }

        // --- what each one does -------------------------------------------------------------------

        private static GladiatorInstance With(GladiatorDef def, params BoonKey[] boons)
            => new GladiatorInstance(def) { TeamBoons = new HashSet<BoonKey>(boons) };

        [Test]
        public void SharpenedSteel_HitsHarder()
        {
            float plain = CombatResolver.ComputeAttackDamage(With(GladiatorDef.Brutius));
            float sharp = CombatResolver.ComputeAttackDamage(With(GladiatorDef.Brutius, BoonKey.SharpenedSteel));
            Assert.AreEqual(plain * GameConstants.SharpenedSteelDamageMult, sharp, Tol);
        }

        [Test]
        public void IronHide_TakesLess()
        {
            Assert.AreEqual(10f * GameConstants.IronHideTakenMult,
                CombatResolver.ApplyMitigation(With(GladiatorDef.Brutius, BoonKey.IronHide), 10f), Tol);
        }

        [Test]
        public void Stalwart_AGuardTakesHalf()
        {
            var g = With(GladiatorDef.Brutius, BoonKey.Stalwart);
            g.PlannedAction = ActionType.Defend;
            Assert.AreEqual(10f * GameConstants.StalwartDefendMult, CombatResolver.ApplyMitigation(g, 10f), Tol);
        }

        [Test]
        public void FleetFoot_RunsFurther_AndLongReach_ReachesFurther()
        {
            var plain = With(GladiatorDef.Hilius);
            Assert.AreEqual(plain.DashReach() * GameConstants.FleetFootSpeedMult,
                With(GladiatorDef.Hilius, BoonKey.FleetFoot).DashReach(), Tol);
            Assert.AreEqual(plain.Reach * GameConstants.LongReachMult,
                With(GladiatorDef.Hilius, BoonKey.LongReach).Reach, Tol);
        }

        [Test]
        public void Flanker_HitsHarderFromBehind_AndNoHarderFromTheFront()
        {
            float Blow(bool flanker, Vector2 from)
            {
                var victim = new GladiatorInstance(GladiatorDef.Brutius) { Pos = Vector2.zero, Facing = Vector2.up };
                var striker = flanker ? With(GladiatorDef.Hilius, BoonKey.Flanker) : With(GladiatorDef.Hilius);
                striker.Pos = from;
                return CombatResolver.DealDamage(striker, victim);
            }

            var behind = new Vector2(0f, -30f);
            var front = new Vector2(0f, 30f);
            Assert.AreEqual(Blow(false, behind) * GameConstants.FlankerDamageMult, Blow(true, behind), 0.001f);
            Assert.AreEqual(Blow(false, front), Blow(true, front), 0.001f);
        }

        [Test]
        public void Quartermaster_MakesThePickupsStronger()
        {
            var g = With(GladiatorDef.Brutius, BoonKey.Quartermaster);
            g.Hp = 10f;
            var apple = new ArenaPickup(new System.Random(1), ObstacleField.Empty, PickupKind.Apple);
            apple.PlaceAt(g.Pos);
            Assert.IsTrue(apple.TryPickup(g, out float healed));
            Assert.AreEqual(g.Def.MaxHp * GameConstants.QuartermasterAppleHealFraction, healed, Tol);

            var horn = new ArenaPickup(new System.Random(1), ObstacleField.Empty, PickupKind.Horn);
            horn.PlaceAt(g.Pos);
            Assert.IsTrue(horn.TryPickup(g, out float gained));
            Assert.AreEqual(GameConstants.QuartermasterHornRage, gained, Tol);

            var blessing = new ArenaPickup(new System.Random(1), ObstacleField.Empty, PickupKind.Blessing);
            blessing.PlaceAt(g.Pos);
            Assert.IsTrue(blessing.TryPickup(g));
            Assert.AreEqual(GameConstants.WeaponBuffRounds + 1 + GameConstants.QuartermasterBlessingExtraRounds,
                g.WeaponBuffRoundsLeft);
        }
    }
}
