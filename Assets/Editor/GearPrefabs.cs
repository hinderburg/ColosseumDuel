using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ColosseumDuel.EditorTools
{
    /// <summary>
    /// Builds the game's weapon, shield and helmet prefabs out of the imported Crusader pack.
    ///
    /// The pack's own prefabs are not used directly. Each carries an LODGroup whose lowest step
    /// takes over below a quarter of screen height - and a sword on this arena is a twentieth of it,
    /// so every pickup in the game would draw at the crudest LOD while the detailed mesh sat in the
    /// build unused. They also carry colliders, which this project has no use for and has already
    /// been bitten by once: engine-code stripping removes collider classes that nothing references,
    /// and a WebGL build then fails on them at runtime.
    ///
    /// What comes out is deliberately uniform: every gear prefab is centred on its own geometry and
    /// scaled so its longest side is exactly one unit. Views then ask for a length in world units
    /// and get it, instead of carrying a hand-measured offset per model - which is what the previous
    /// pack needed, and what broke the moment a model was swapped.
    /// </summary>
    public static class GearPrefabs
    {
        public const string PrefabDir = "Assets/Prefabs";
        private const string PackDir = "Assets/Crusader_Castle";
        private const string PackPrefabs = PackDir + "/Prefabs";
        private const string PackTextures = PackDir + "/Textures";
        private const string MaterialsDir = "Assets/Materials";

        public const string SwordPath = PrefabDir + "/Gear_Sword.prefab";
        public const string GreatswordPath = PrefabDir + "/Gear_Greatsword.prefab";
        public const string ShieldPath = PrefabDir + "/Gear_Shield.prefab";
        public const string HelmetPath = PrefabDir + "/Gear_Helmet.prefab";

        /// <summary>
        /// Which of the pack's models each piece of gear is.
        ///
        /// A short blade for the one-handed sword and a long one for the two-hander, rather than one
        /// model at two scales the way the previous pack forced. The size difference still carries -
        /// it is what tells the player, from above, which weapon they are running at and whether
        /// picking it up will cost them their shield - but now the silhouettes differ too.
        /// </summary>
        private const string SwordModel = "Shortsword_01";
        private const string GreatswordModel = "Longsword_02";
        private const string ShieldModel = "Heater_Shield_01";
        private const string HelmetModel = "Tophelm";

        /// <summary>True if the pack is imported at all. A clean clone has none of this.</summary>
        public static bool PackImported => AssetDatabase.IsValidFolder(PackPrefabs);

        [MenuItem("Tools/Colosseum/Rebuild gear prefabs")]
        public static void Rebuild()
        {
            EnsureAll();
            AssetDatabase.Refresh();
        }

        public static bool EnsureAll()
        {
            if (!PackImported)
            {
                Debug.LogWarning($"[Colosseum] Crusader pack not found at {PackDir} - gear and " +
                                 "helmets will fall back to primitives. Import it to get them.");
                return false;
            }

            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            var steel = TexturedMaterial("Gear_Steel", "Sword_Crusaders_01", Color.white, 0.55f);
            var shield = TexturedMaterial("Gear_Shield", "Crusader_Shields_02", Color.white, 0.20f);

            // The helmet's material is replaced per side at runtime - blue for the player, red for
            // the opponent - so what it is built with only shows if that ever fails to happen.
            var helmet = HelmetMaterial("Gear_Helmet", Color.white);

            bool ok = Build(SwordModel, SwordPath, steel)
                      & Build(GreatswordModel, GreatswordPath, steel)
                      & Build(ShieldModel, ShieldPath, shield)
                      & Build(HelmetModel, HelmetPath, helmet);

            AssetDatabase.SaveAssets();
            return ok;
        }

        /// <summary>
        /// The helmet texture on a Lit material, ready to be tinted.
        ///
        /// Public because the two side colours are the bootstrap's business, not this file's, and
        /// both have to be built the same way or one side's helmet comes out untextured.
        /// </summary>
        public static Material HelmetMaterial(string name, Color tint)
            => TexturedMaterial(name, "Crusader_Helmets", tint, 0.55f);

        private static bool Build(string modelName, string outputPath, Material material)
        {
            string sourcePath = $"{PackPrefabs}/{modelName}.prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                Debug.LogWarning($"[Colosseum] Gear model {sourcePath} not found.");
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(outputPath));
            try
            {
                // Unpacked before anything is deleted: a prefab instance refuses to have children
                // removed, and the LOD strip below does exactly that.
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                                  InteractionMode.AutomatedAction);

                KeepHighestLodOnly(instance);
                StripColliders(instance);
                if (material != null) Repaint(instance, material);

                instance.transform.SetParent(root.transform, false);
                Normalise(instance);

                PrefabUtility.SaveAsPrefabAsset(root, outputPath);
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Throws away every LOD but the finest, and the group along with it.
        ///
        /// The group decides by screen height, and nothing in this game is ever big enough on screen
        /// to keep it on LOD0 - so leaving it in would mean shipping three meshes per item and
        /// drawing the worst of them.
        /// </summary>
        private static void KeepHighestLodOnly(GameObject instance)
        {
            var group = instance.GetComponentInChildren<LODGroup>(true);
            if (group == null) return;

            var lods = group.GetLODs();
            var keep = new HashSet<GameObject>();
            if (lods.Length > 0)
                foreach (var renderer in lods[0].renderers)
                    if (renderer != null) keep.Add(renderer.gameObject);

            Object.DestroyImmediate(group);

            // Collected first: destroying while walking the children skips every other one.
            var doomed = new List<GameObject>();
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                if (!keep.Contains(renderer.gameObject)) doomed.Add(renderer.gameObject);

            foreach (var go in doomed) Object.DestroyImmediate(go);
        }

        private static void StripColliders(GameObject instance)
        {
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
        }

        private static void Repaint(GameObject instance, Material material)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var slots = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
            }
        }

        /// <summary>
        /// Centres the model on the prefab's pivot and scales it to one unit on its longest side.
        ///
        /// Measured off the renderers rather than the transforms: what the player sees is the mesh,
        /// and an imported model's root transform routinely sits somewhere the geometry does not.
        /// This is the whole contract the views rely on, so it is asserted by a test.
        /// </summary>
        private static void Normalise(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest < 0.0001f) return;

            float scale = 1f / longest;
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = -bounds.center * scale;

            // The one line that says which way round each model is. Read it once when picking a
            // replacement model rather than discovering it from a render.
            Debug.Log($"[Colosseum] {model.name}: measured {bounds.size} at {bounds.center}, " +
                      $"normalised by {scale:0.###}.");
        }

        /// <summary>
        /// A URP Lit material carrying one of the pack's texture sets.
        ///
        /// The pack ships its materials on the built-in Standard shader, which under URP draws
        /// magenta - so none of them can be used as they are. Rebuilt here against the same albedo
        /// and normal maps instead of dropping the textures and painting the gear flat: a sword that
        /// reads as steel is most of what importing a weapon pack buys.
        /// </summary>
        private static Material TexturedMaterial(string name, string textureSet, Color tint, float smoothness)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader && shader != null)
            {
                material.shader = shader;
            }

            material.color = tint;

            var albedo = LoadTexture(textureSet, "A");
            if (albedo != null) material.mainTexture = albedo;

            var normal = LoadTexture(textureSet, "N");
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }

            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D LoadTexture(string set, string suffix)
        {
            string path = $"{PackTextures}/{set}/{set}_{suffix}.tga";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) Debug.LogWarning($"[Colosseum] Gear texture missing: {path}");
            return texture;
        }
    }
}
