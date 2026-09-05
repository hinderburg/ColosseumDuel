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
    /// What is lying on the sand has to match what picking it up will hand over - the size of a
    /// weapon prop is the only warning the player gets that it will cost them their shield.
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

        private Transform WeaponPropFor(ArenaItem item)
        {
            int index = _controller.Manager.State.Items.Items.IndexOf(item);
            var view = _controller.GetComponentsInChildren<ItemView>(true)[index];
            return view.transform.Find("Weapon");
        }

        [UnityTest]
        public IEnumerator ATwoHandedWeaponLiesOnTheSandBiggerThanAOneHanded()
        {
            var slot = _controller.Manager.State.Items.Items.First(i => i.Kind == ItemKind.Weapon);

            slot.WeaponType = WeaponType.OneHanded;
            yield return null;

            var prop = WeaponPropFor(slot);
            Assert.IsNotNull(prop, "the weapon slot has no prop at all");
            Assert.IsTrue(prop.gameObject.activeInHierarchy, "the weapon prop is not on the arena");
            float oneHanded = prop.localScale.y;

            slot.WeaponType = WeaponType.TwoHanded;
            yield return null;

            Assert.IsTrue(prop.gameObject.activeInHierarchy);
            Assert.Greater(prop.localScale.y, oneHanded,
                "a two-hander must lie there visibly bigger - it is the only thing that says " +
                "picking it up will cost the player their shield");
        }

        [UnityTest]
        public IEnumerator TheRandomSlotShowsTheWeaponItActuallyGrants()
        {
            // The third slot rolls a weapon type and hands one over on pickup, but it was drawn as a
            // featureless sphere - so half the two-handers on the arena were invisible as such, and
            // running onto one silently destroyed the shield the player was carrying.
            var slot = _controller.Manager.State.Items.Items.First(i => i.Kind == ItemKind.Random);
            slot.WeaponType = WeaponType.TwoHanded;
            yield return null;

            var prop = WeaponPropFor(slot);
            Assert.IsNotNull(prop, "the random slot has no weapon prop");
            Assert.IsTrue(prop.gameObject.activeInHierarchy,
                "a slot that grants a weapon has to look like one");
        }

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.unscaledDeltaTime; }
        }
    }
}
