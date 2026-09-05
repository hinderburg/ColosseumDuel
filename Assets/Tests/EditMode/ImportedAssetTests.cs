using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEditor;
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
                Assert.IsNotNull(helmet.GetComponent<MeshFilter>().sharedMesh,
                    "the helmet needs a mesh; building the prefabs before the palette's meshes " +
                    "were assigned produced one without and said nothing");
            }
        }
    }
}
