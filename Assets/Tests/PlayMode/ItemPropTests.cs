using System.Collections;
using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay;
using ColosseumDuel.Gameplay.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// What is lying on the sand has to match what picking it up will hand over. Three weapons that
    /// fight differently are only a decision if the player can tell them apart from above.
    /// </summary>
    public class ItemPropTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            _controller = Object.FindFirstObjectByType<GameController>();
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);
        }

        private ItemView ViewFor(ArenaItem item)
        {
            int index = _controller.Manager.State.Items.Items.IndexOf(item);
            return _controller.GetComponentsInChildren<ItemView>(true)[index];
        }

        private static readonly string[] PropNames = { "DualSwords", "SwordAndShield", "Mace" };

        [UnityTest]
        public IEnumerator EveryWeaponOnTheSandDrawsAsItsOwnKindAndNothingElse()
        {
            var items = _controller.Manager.State.Items.Items;
            Assert.AreEqual(GameConstants.ItemCountOnArena, items.Count);

            foreach (var item in items)
            {
                var view = ViewFor(item);
                string expected = NameFor(item.Kind);

                foreach (var name in PropNames)
                {
                    var prop = view.transform.Find(name);
                    Assert.IsNotNull(prop, $"the item view has no {name} prop at all");

                    // Both directions. Checking only that the right one is up would pass just as
                    // well with all three drawn on top of each other, which is what the player
                    // would actually be looking at.
                    Assert.AreEqual(name == expected, prop.gameObject.activeInHierarchy,
                        $"a {item.Kind} on the sand should show {expected} and only {expected}");
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator TheMaceLiesThereBiggerThanTheBlades()
        {
            // Size is what says "this one hits hardest and furthest" from a camera that never moves
            // in for a closer look.
            var mace = _controller.Manager.State.Items.Items.First(i => i.Kind == WeaponKind.TwoHandedMace);
            var swords = _controller.Manager.State.Items.Items.First(i => i.Kind == WeaponKind.DualSwords);
            yield return null;

            float maceLength = ViewFor(mace).transform.Find("Mace").localScale.y;
            float swordLength = ViewFor(swords).transform.Find("DualSwords").localScale.y;

            Assert.Greater(maceLength, swordLength,
                "the mace has to read as the big weapon before anyone runs at it");
        }

        [UnityTest]
        public IEnumerator TheTwinSwordsAreDrawnAsTwo()
        {
            // Stacked exactly they read as one sword, and the pair is the whole identity of the
            // weapon - two blows and a bleed rather than one heavy swing.
            var swords = _controller.Manager.State.Items.Items.First(i => i.Kind == WeaponKind.DualSwords);
            yield return null;

            var prop = ViewFor(swords).transform.Find("DualSwords");
            var left = prop.Find("Left");
            var right = prop.Find("Right");

            Assert.IsNotNull(left);
            Assert.IsNotNull(right);
            Assert.Greater(Vector3.Distance(left.position, right.position), 0.05f,
                "the two blades are drawn on top of each other and read as one");
        }

        private static string NameFor(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.DualSwords: return "DualSwords";
                case WeaponKind.SwordAndShield: return "SwordAndShield";
                default: return "Mace";
            }
        }

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.unscaledDeltaTime; }
        }
    }
}
