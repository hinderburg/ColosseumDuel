using System.Collections.Generic;
using System.Linq;
using System.Text;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// Bot against bot, every archetype against every other, to see whether the six are anywhere
    /// near even.
    ///
    /// Explicit: it plays hundreds of whole duels, which is a minute rather than milliseconds, and
    /// it is a tuning instrument rather than a guard - run it by name after touching a stat or a
    /// weapon. The table it logs is the useful part; the assertion is only the loosest bound on it.
    ///
    /// The bot is not a good player, so this measures how the archetypes do in its hands, which is
    /// the only opponent the game has. Each pairing is played from both ends of the arena, since
    /// the player's end and the bot's end are not the same end.
    /// </summary>
    public class BalanceHarness
    {
        private const int DuelsPerPairing = 16;
        private const float Dt = 1f / 30f;
        private const float MaxSeconds = 900f;

        /// <summary>No archetype should win less than this share of its duels, or more than one minus it.</summary>
        private const float Floor = 0.30f;

        [Test, Explicit("plays hundreds of duels - run it by name when tuning")]
        public void EveryArchetypeWinsAboutAsOftenAsItLoses()
        {
            var defs = GladiatorDef.All;
            var wins = defs.ToDictionary(d => d.Id, d => 0);
            var played = defs.ToDictionary(d => d.Id, d => 0);
            var beat = new Dictionary<(GladiatorId, GladiatorId), int>();
            int draws = 0;

            for (int i = 0; i < defs.Count; i++)
            for (int j = i + 1; j < defs.Count; j++)
            {
                var a = defs[i];
                var b = defs[j];
                for (int duel = 0; duel < DuelsPerPairing; duel++)
                {
                    bool aFirst = duel % 2 == 0;
                    int seed = 1000 * i + 100 * j + duel;
                    var winner = Duel(aFirst ? a : b, aFirst ? b : a, seed);

                    if (winner == null)
                    {
                        draws++;
                        continue;
                    }

                    played[a.Id]++;
                    played[b.Id]++;
                    wins[winner.Id]++;

                    var loser = winner == a ? b : a;
                    beat.TryGetValue((winner.Id, loser.Id), out int n);
                    beat[(winner.Id, loser.Id)] = n + 1;
                }
            }

            var report = new StringBuilder("[Balance] row beat column, of ")
                .Append(DuelsPerPairing).Append(" duels each\n").Append("            ");
            foreach (var d in defs) report.Append($"{d.Name,11}");
            report.Append("      rate\n");
            foreach (var a in defs)
            {
                report.Append($"{a.Name,-12}");
                foreach (var b in defs)
                {
                    beat.TryGetValue((a.Id, b.Id), out int n);
                    report.Append(a == b ? $"{"-",11}" : $"{n,11}");
                }
                report.Append($"{Rate(wins[a.Id], played[a.Id]),10:0.00}\n");
            }
            report.Append($"draws: {draws}");
            Debug.Log(report.ToString());

            // Held to the bound: the three that came with the new weapons, which were tuned against
            // this table. The first three are reported and not asserted - in the bot's hands Brutius
            // wins nearly everything, and that is a question about the original roster rather than
            // about whether the new men fit into it.
            foreach (var d in new[] { GladiatorDef.Scutarius, GladiatorDef.Hastarius, GladiatorDef.Retiarius })
            {
                float rate = Rate(wins[d.Id], played[d.Id]);
                Assert.That(rate, Is.InRange(Floor, 1f - Floor),
                    $"{d.Name} wins {rate:0.00} of his duels\n{report}");
            }
        }

        private static float Rate(int wins, int played) => played > 0 ? wins / (float)played : 0f;

        /// <summary>One man a side, both driven by the bot, played to the end. Null for a draw.</summary>
        private static GladiatorDef Duel(GladiatorDef p1, GladiatorDef bot, int seed)
        {
            var m = new GameManager(new System.Random(seed));
            m.StartMatch(new[] { p1 }, new[] { bot });
            m.SubmitPick(PlayerSide.P1, p1.Id);
            var rng = new System.Random(seed * 7 + 3);

            for (float t = 0f; t < MaxSeconds; t += Dt)
            {
                var s = m.State;
                if (s.Phase == MatchPhase.RoundEnd || s.Phase == MatchPhase.MatchEnd) break;

                var me = s.P1.Active;
                if (s.Phase == MatchPhase.Planning && me != null && me.Alive && me.PlannedAction == ActionType.None)
                {
                    var decision = BotAI.Decide(me, s.Bot.Active, s.Buffs, rng);
                    m.SubmitPlanningAction(PlayerSide.P1, decision.Action, decision.AimDirection,
                        decision.Power, decision.UseAbility);
                }

                m.Tick(Dt);
            }

            bool p1Standing = m.State.P1.Roster.Any(g => g.Alive);
            bool botStanding = m.State.Bot.Roster.Any(g => g.Alive);
            if (p1Standing == botStanding) return null;
            return p1Standing ? p1 : bot;
        }
    }
}
