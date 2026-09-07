using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// Import settings the presentation quietly depends on. These are Editor-side checks because
    /// that is where the setting lives - the equivalent runtime check would need Read/Write enabled
    /// on the mesh, which costs memory in every build for the sake of one assertion.
    /// </summary>
    public class ImportedAssetTests
    {
        private const string ModelPath = "Assets/DoubleL/Model/Armature.fbx";

        [Test]
        public void TheGladiatorModelIsImportedWithCalculatedNormals()
        {
            // Regression: the FBX ships without normals, and importing them as-is leaves a mesh a
            // lit shader draws pure black. The figures were present, correctly sized and correctly
            // placed - and looked exactly like their own shadows, which is a long way to travel to
            // find one import flag.
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Assert.Ignore("The DoubleL model pack is not imported here.");
                return;
            }

            Assert.AreEqual(ModelImporterNormals.Calculate, importer.importNormals,
                "without calculated normals the gladiators render black under a lit shader");
        }

        [Test]
        public void TheAnimatorControllerCarriesEveryParameterTheViewDrives()
        {
            // Spelled out rather than read from GladiatorAnimation: the Editor scripts live in the
            // predefined Assembly-CSharp-Editor, which an assembly-definition test cannot reference.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                "Assets/Animation/Gladiator.controller");
            if (controller == null)
            {
                Assert.Ignore("The DoubleL animation pack is not imported here.");
                return;
            }

            // The one failure mode worth a test here: Animator.SetFloat on a parameter the
            // controller does not have logs nothing and does nothing, so a typo would leave every
            // gladiator standing in his idle pose through the whole match with no error anywhere.
            var names = controller.parameters.Select(p => p.name).ToList();
            foreach (var expected in new[]
                     {
                         AnimatorParams.Speed, AnimatorParams.Defending,
                         AnimatorParams.Attack, AnimatorParams.Hit, AnimatorParams.Dead,
                     })
                CollectionAssert.Contains(names, expected);

            foreach (var state in controller.layers[0].stateMachine.states)
                Assert.IsNotNull(state.state.motion,
                    $"the '{state.state.name}' state has no clip, so it plays the bind pose");
        }

        [Test]
        public void EveryArchetypeHasAFigurePrefab()
        {
            if (AssetImporter.GetAtPath(ModelPath) == null)
            {
                Assert.Ignore("The DoubleL model pack is not imported here.");
                return;
            }

            foreach (var def in GladiatorDef.All)
            {
                string path = $"Assets/Prefabs/Gladiator_{def.Id}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, $"no prefab at {path} - run the bootstrap");

                var helmet = prefab.transform.Find("Helmet");
                Assert.IsNotNull(helmet, $"{def.Name} has no helmet");

                // Anywhere in the helmet, not only on it: it is a model from the weapon pack now,
                // with its geometry a level down. Still checked for a mesh rather than for a
                // component, because building the prefabs before the palette was filled produced a
                // helmet with a renderer and nothing in it, and said nothing about it.
                var meshes = helmet.GetComponentsInChildren<MeshFilter>(true);
                Assert.IsTrue(meshes.Any(m => m.sharedMesh != null),
                    $"{def.Name}'s helmet has no mesh anywhere in it - it would draw nothing");
            }
        }

        // Spelled out rather than read off GearPrefabs: these tests live in an assembly definition,
        // and the Editor scripts do not, so nothing here can see them. Same reason the gladiator
        // prefab path above is written out.
        private const string PackDir = "Assets/Crusader_Castle/Prefabs";

        private static readonly string[] GearPaths =
        {
            "Assets/Prefabs/Gear_Sword.prefab",
            "Assets/Prefabs/Gear_Greatsword.prefab",
            "Assets/Prefabs/Gear_Shield.prefab",
            "Assets/Prefabs/Gear_Helmet.prefab",
        };

        /// <summary>
        /// The contract every view relies on: a gear prefab is centred on its own geometry and
        /// exactly one unit on its longest side.
        ///
        /// This is what lets GearSizes name a length in world units and get it. Without it every
        /// view would have to carry a measurement per model again - which is what the last pack
        /// needed, and what silently went wrong the moment a model was swapped for another.
        /// </summary>
        [Test]
        public void EveryGearPrefabIsCentredAndOneUnitLong()
        {
            if (!AssetDatabase.IsValidFolder(PackDir))
            {
                Assert.Ignore("The Crusader weapon pack is not imported here.");
                return;
            }

            foreach (var path in GearPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, $"no gear prefab at {path} - run the bootstrap");

                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                Assert.IsNotEmpty(renderers, $"{path} draws nothing at all");

                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                Assert.AreEqual(1f, longest, 0.01f,
                    $"{path} is {longest:0.###} units long - a view asking for 1.5 would not get 1.5");
                Assert.Less(bounds.center.magnitude, 0.02f,
                    $"{path} sits {bounds.center} off its own pivot, so it would be drawn beside " +
                    "the spot the simulation says it is on");
            }
        }

        /// <summary>
        /// No LOD groups and no colliders on the gear.
        ///
        /// The pack ships both. An LOD group takes over below a quarter of screen height and nothing
        /// in this game is ever that big, so every pickup would draw at the crudest step while the
        /// good mesh sat in the build unused. Colliders this project has been bitten by before:
        /// engine-code stripping drops collider classes nothing references, and a WebGL build then
        /// fails on them at runtime.
        /// </summary>
        [Test]
        public void GearPrefabsCarryNeitherLodGroupsNorColliders()
        {
            if (!AssetDatabase.IsValidFolder(PackDir))
            {
                Assert.Ignore("The Crusader weapon pack is not imported here.");
                return;
            }

            foreach (var path in GearPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, $"no gear prefab at {path} - run the bootstrap");

                Assert.IsEmpty(prefab.GetComponentsInChildren<LODGroup>(true),
                    $"{path} still has an LOD group and would draw at its worst step");
                Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true),
                    $"{path} still has a collider, which nothing here wants and a build may strip");
                Assert.AreEqual(1, prefab.GetComponentsInChildren<Renderer>(true).Length,
                    $"{path} has more than one renderer - the spare LOD meshes are still in it");
            }
        }
    }
}
