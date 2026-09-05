using ColosseumDuel.Core;
using UnityEditor;
using UnityEngine;

namespace ColosseumDuel.EditorTools
{
    /// <summary>
    /// Builds one prefab per gladiator archetype from the imported humanoid model, each with a
    /// helmet the model does not come with.
    ///
    /// Prefabs rather than assembling the figure at runtime: the model is a skinned mesh with a
    /// humanoid avatar, and the animator that will drive it belongs on a prefab that can be opened
    /// and inspected. The rest of the presentation is still built in code - this is the one place
    /// where an imported asset has to be wrapped.
    /// </summary>
    public static class GladiatorPrefabs
    {
        public const string PrefabDir = "Assets/Prefabs";
        private const string ModelPath = "Assets/DoubleL/Model/Armature.fbx";

        /// <summary>
        /// World height of a gladiator. Chosen against the camera rather than against realism: a
        /// tilted view foreshortens vertical extent to about 40% of its true size, so a
        /// life-sized figure on an arena this wide reads as a speck.
        /// </summary>
        public const float TargetHeight = 3.0f;

        public static string PathFor(GladiatorId id) => $"{PrefabDir}/Gladiator_{id}.prefab";

        /// <summary>Builds or rebuilds all three. Returns false if the model is not imported.</summary>
        public static bool EnsureAll(Mesh helmetMesh)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogWarning($"[Colosseum] Gladiator model not found at {ModelPath} - " +
                                 "falling back to primitive bodies. Import the DoubleL pack to get it.");
                return false;
            }

            EnsureNormals();

            // Only through the AssetDatabase: creating the folder on disk first leaves Unity to
            // notice it separately and mint a second .meta for the same directory.
            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            foreach (var def in GladiatorDef.All)
                Build(def, model, helmetMesh);

            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// Makes the importer calculate normals instead of taking them from the file.
        ///
        /// The FBX ships without any, and "import" then yields a mesh with none - which a lit
        /// shader renders pure black. The figures were drawing, at the right size and in the right
        /// place, and looked like shadows. Doing this here rather than by hand keeps it reproducible
        /// for anyone who imports the pack into a fresh clone.
        /// </summary>
        private static void EnsureNormals()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null || importer.importNormals == ModelImporterNormals.Calculate) return;

            importer.importNormals = ModelImporterNormals.Calculate;
            importer.SaveAndReimport();
            Debug.Log("[Colosseum] Gladiator model re-imported with calculated normals.");
        }

        private static void Build(GladiatorDef def, GameObject model, Mesh helmetMesh)
        {
            var root = new GameObject($"Gladiator_{def.Id}");
            try
            {
                var figure = (GameObject)PrefabUtility.InstantiatePrefab(model);
                figure.name = "Figure";
                figure.transform.SetParent(root.transform, false);

                // Scale from the model's own height rather than a hard-coded number, so re-importing
                // the model at a different scale does not silently change how big a gladiator is.
                float modelHeight = MeasureHeight(figure);
                float scale = modelHeight > 0.01f ? TargetHeight / modelHeight : 1f;
                figure.transform.localScale = Vector3.one * scale;

                // The model has no helmet, and the design needs one to carry the owning side's
                // colour. Its material is left alone here and assigned per side at runtime - the
                // prefab is shared by both players.
                var helmet = new GameObject("Helmet");
                helmet.transform.SetParent(root.transform, false);
                helmet.transform.localPosition = new Vector3(0f, TargetHeight * 0.885f, 0f);
                helmet.transform.localScale = Vector3.one * (TargetHeight * 0.135f);
                helmet.AddComponent<MeshFilter>().sharedMesh = helmetMesh;
                helmet.AddComponent<MeshRenderer>();

                PrefabUtility.SaveAsPrefabAsset(root, PathFor(def.Id));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Height of the renderers, which is what actually shows - not the transform.</summary>
        private static float MeasureHeight(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return 0f;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds.size.y;
        }
    }
}
