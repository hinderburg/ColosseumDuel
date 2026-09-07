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
        /// life-sized figure on an arena this wide reads as a speck. Raised a further quarter in a
        /// later pass - the fighters are what the eye should land on first.
        /// </summary>
        public const float TargetHeight = 3.75f;

        /// <summary>Where the helmet's centre sits up the figure, as a fraction of its height.</summary>
        private const float HelmetHeightFraction = 0.885f;

        /// <summary>
        /// How tall the helmet is, as a fraction of the figure.
        ///
        /// Big enough to swallow the model's own head rather than to match it: the helm is hollow,
        /// and one sized to the head leaves the head showing through the eye slit and over the top.
        /// </summary>
        private const float HelmetSizeFraction = 0.135f;

        /// <summary>How far up its own height the helmet rides above the head bone. Only a rough
        /// placement: GladiatorView seats it exactly, against the figure at its own build.</summary>
        private const float HelmetLiftFromNeck = 0.28f;

        /// <summary>
        /// How far forward the helm sits from the head bone, as a share of its own depth.
        ///
        /// The bone is at the back of the skull on this rig, so a helm centred on it covers the
        /// crown and leaves the face in the open. Pushed forward until the brow is under it.
        /// </summary>
        private const float HelmetForward = 0.3f;

        public static string PathFor(GladiatorId id) => $"{PrefabDir}/Gladiator_{id}.prefab";

        /// <summary>Builds or rebuilds all three. Returns false if the model is not imported.</summary>
        public static bool EnsureAll(GameObject helmetModel, Mesh fallbackHelmetMesh)
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

            var controller = GladiatorAnimation.EnsureController();

            foreach (var def in GladiatorDef.All)
                Build(def, model, helmetModel, fallbackHelmetMesh, controller);

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

        private static void Build(GladiatorDef def, GameObject model, GameObject helmetModel,
                                 Mesh fallbackHelmetMesh, RuntimeAnimatorController controller)
        {
            var root = new GameObject($"Gladiator_{def.Id}");
            try
            {
                var figure = (GameObject)PrefabUtility.InstantiatePrefab(model);
                figure.name = "Figure";
                figure.transform.SetParent(root.transform, false);

                // The imported model brings its own Animator with the humanoid avatar; the clips are
                // humanoid too, so they retarget onto it. Only the controller has to be supplied.
                //
                // applyRootMotion stays off: the clips used are the in-place variants, and the
                // simulation is the one authority on where a gladiator stands.
                var animator = figure.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;

                    // Off-screen culling would freeze a gladiator the moment his own bounds left the
                    // frame - and on an arena this long, the far end is exactly where that happens.
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
                else if (controller != null)
                {
                    Debug.LogWarning($"[Colosseum] {def.Name}'s model has no Animator - " +
                                     "the controller was built but nothing will play it.");
                }

                // Scale from the model's own height rather than a hard-coded number, so re-importing
                // the model at a different scale does not silently change how big a gladiator is.
                float modelHeight = MeasureHeight(figure);
                float scale = modelHeight > 0.01f ? TargetHeight / modelHeight : 1f;
                figure.transform.localScale = Vector3.one * scale;

                // The model has no helmet, and the design needs one to carry the owning side's
                // colour - it is the only thing on the arena that says which of two identical
                // archetypes is yours. Its material is left alone here and assigned per side at
                // runtime, since the prefab is shared by both players.
                //
                // A child of the prefab root rather than of the head bone: at this height it reads
                // as the head, and the model's own head sits inside it. Hung off the bone it would
                // inherit every animation's neck movement, which on a figure this small on screen
                // buys nothing and costs the certainty that it is always where it should be.
                var helmet = new GameObject("Helmet");
                helmet.transform.SetParent(root.transform, false);
                helmet.transform.localPosition = MeasureHeadCentre(root.transform, animator);
                helmet.transform.localScale = Vector3.one * (TargetHeight * HelmetSizeFraction);

                if (helmetModel != null)
                {
                    // GearPrefabs hands over a model one unit on its longest side, so the scale is
                    // the height in world units and nothing here has to know how big a Tophelm is.
                    var worn = (GameObject)PrefabUtility.InstantiatePrefab(helmetModel);
                    worn.name = "Model";
                    worn.transform.SetParent(helmet.transform, false);
                }
                else
                {
                    helmet.AddComponent<MeshFilter>().sharedMesh = fallbackHelmetMesh;
                    helmet.AddComponent<MeshRenderer>();
                }


                PrefabUtility.SaveAsPrefabAsset(root, PathFor(def.Id));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Where the helmet goes, in the prefab root's own space.
        ///
        /// Off the rig's head bone rather than at a fraction of the figure's height. The fraction
        /// was right for a sphere, which is round and forgiving; a great helm sitting a few
        /// hundredths too low or too far back leaves the model's own head poking out of the top of
        /// it, which is exactly how the first attempt rendered - a hood behind a bare head.
        ///
        /// The bone sits at the base of the skull, so the helm is lifted by most of its own height
        /// to sit over the head rather than around the neck.
        /// </summary>
        private static Vector3 MeasureHeadCentre(Transform root, Animator animator)
        {
            float fallback = TargetHeight * HelmetHeightFraction;

            var head = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Head)
                : null;
            if (head == null)
            {
                Debug.LogWarning("[Colosseum] No head bone on the gladiator rig - the helmet is " +
                                 "placed by proportion instead, and may not sit right.");
                return new Vector3(0f, fallback, 0f);
            }

            var local = root.InverseTransformPoint(head.position);

            local.y += TargetHeight * HelmetSizeFraction * HelmetLiftFromNeck;
            return local;
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
