using System.Collections.Generic;
using System.Linq;

namespace ColosseumDuel.Core
{
    /// <summary>
    /// A boon: something a side takes for the rest of the match, on every man of it.
    ///
    /// Each time a new man of a side steps out - at the start of a match, and after each of its
    /// men falls - that side is offered three boons from the five it brought, and keeps the one it
    /// chooses. Three men a squad means three choices a match, of three from five, then from four,
    /// then from the last three. A man falling is a boon earned: the side behind gets one more.
    ///
    /// Kept simple on purpose - one number each, on something the player already reads - so each
    /// is felt at once and changes how the next clash is played rather than only how it adds up.
    /// The numbers are in GameConstants; the mechanics are where the rest of the rules are.
    /// </summary>
    public sealed class BoonDef
    {
        /// <summary>How many a side brings into a match to be offered from.</summary>
        public const int LoadoutSize = 5;

        /// <summary>How many are offered at a time.</summary>
        public const int OfferSize = 3;

        public readonly BoonKey Key;
        public readonly string Name;
        public readonly string Description;

        public BoonDef(BoonKey key, string name, string description)
        {
            Key = key;
            Name = name;
            Description = description;
        }

        public static readonly IReadOnlyList<BoonDef> All = new List<BoonDef>
        {
            new BoonDef(BoonKey.SharpenedSteel, "Sharpened Steel", "+6% damage"),
            new BoonDef(BoonKey.IronHide, "Iron Hide", "-6% damage taken"),
            new BoonDef(BoonKey.FleetFoot, "Fleet Foot", "+8% run distance"),
            new BoonDef(BoonKey.LongReach, "Long Reach", "+7% weapon reach"),
            new BoonDef(BoonKey.BattleSpirit, "Battle Spirit", "each clash starts with 20% rage"),
            new BoonDef(BoonKey.Flanker, "Flanker", "+10% damage to the side or back"),
            new BoonDef(BoonKey.Stalwart, "Stalwart", "a guard takes 40% off a blow, not 30%"),
            new BoonDef(BoonKey.Quartermaster, "Quartermaster",
                "stronger pickups: blessing +1 round, apple heals 40%, horn gives 45% rage"),
        };

        private static readonly Dictionary<BoonKey, BoonDef> ByKey = All.ToDictionary(d => d.Key);

        public static BoonDef Get(BoonKey key) => ByKey[key];

        /// <summary>The five a side brings when nobody has chosen: the first five.</summary>
        public static List<BoonKey> DefaultLoadout() => All.Take(LoadoutSize).Select(d => d.Key).ToList();
    }
}
