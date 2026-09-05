using System.Collections.Generic;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEditor;
using UnityEngine;

namespace ColosseumDuel.EditorTools
{
    /// <summary>
    /// Lays out the arena's props - the wall, the posts, the gallery behind them and the torches -
    /// as real objects in the scene.
    ///
    /// They used to be built at runtime, which kept a clean clone free of references to the paid
    /// packs that are not in the repository. They are placed in the scene instead so they can be
    /// selected, nudged and re-dressed in the Editor, which is worth more than that: an arena laid
    /// out by arithmetic can only be changed by editing the arithmetic. The cost is real and lands
    /// on a fresh clone without the packs, which opens the scene to a few hundred missing objects
    /// until it is bootstrapped again.
    ///
    /// Placed as prefab instances rather than plain copies, so a change to a kit piece still
    /// propagates and any hand-tweak stays visible as an override that can be reverted.
    ///
    /// Everything round the wall shares one equal-arc walk, so blocks, posts and flames line up
    /// with each other instead of each drifting round the oval on its own schedule.
    /// </summary>
    public static class ArenaDecor
    {
        /// <summary>
        /// How many world units one metre of the modular stone kit should measure.
        ///
        /// The arena is not modelled at human scale - a gladiator stands three units tall because a
        /// steeply tilted camera flattens vertical extent, not because he is three metres. Laying
        /// the kit out at 1:1 would put chest-high blocks beside a figure four times their height
        /// and make the wall read as a garden border. This restores the proportion the pieces were
        /// modelled at.
        /// </summary>
        private const float MetreToWorld = 1.67f;

        /// <summary>
        /// Warm sandstone, per the layout sketch. The kit's own stone is a cool grey that sits
        /// oddly against orange sand.
        /// </summary>
        private static readonly Color StoneTint = new Color(0.86f, 0.66f, 0.45f);

        private const string StoneMaterialDir = "Assets/Materials";

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly Dictionary<Material, Material> Tinted = new Dictionary<Material, Material>();

        /// <summary>
        /// Regenerates the props in the open scene from the figures on its ArenaView.
        ///
        /// Separate from the full bootstrap so that changing the wall height or the torch count and
        /// laying the props out again does not also rebuild the camera, the HUD and the palette -
        /// and, more to the point, does not throw away every other hand-made change to the scene.
        /// </summary>
        [MenuItem("Tools/Colosseum/Rebuild arena props", priority = 10)]
        public static void RebuildInOpenScene()
        {
            var arena = Object.FindFirstObjectByType<ArenaView>();
            if (arena == null)
            {
                Debug.LogWarning("[Colosseum] No ArenaView in the open scene - open Assets/Scenes/Arena.unity first.");
                return;
            }

            BuildAll(arena, arena.Palette);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);
            Debug.Log("[Colosseum] Arena props rebuilt. Save the scene to keep them.");
        }

        /// <summary>
        /// Builds every prop under the arena root. Safe to call again - it clears first.
        ///
        /// The palette is passed in rather than read off the ArenaView, because during a full
        /// bootstrap that field can be holding a reference the Editor has unloaded: alive enough to
        /// serialise into the scene, but reading as null to any code that asks. Taking it as an
        /// argument makes the caller responsible for handing over one it has just loaded.
        /// </summary>
        public static void BuildAll(ArenaView arena, ViewPalette palette)
        {
            Tinted.Clear();

            Clear(arena.transform, "ArenaWall");
            Clear(arena.transform, "Gallery");
            Clear(arena.transform, "Torches");

            float radiusX = arena.WorldRadiusX;
            float radiusZ = arena.WorldRadiusZ;

            var wall = new GameObject("ArenaWall");
            wall.transform.SetParent(arena.transform, false);

            if (palette != null && palette.WallBlock != null)
            {
                BuildWall(wall.transform, palette, radiusX, radiusZ, arena.WallHeight);
                BuildPosts(wall.transform, palette, radiusX, radiusZ, arena.WallHeight, arena.TorchCount);
                BuildGallery(arena.transform, palette, radiusX, radiusZ, arena.WallHeight, arena.GalleryDepth);
            }
            else
            {
                BuildPrimitiveWall(wall.transform, palette, radiusX, radiusZ, arena.WallHeight);
            }

            BuildTorches(arena.transform, palette, radiusX, radiusZ, arena.WallHeight, arena.TorchCount);
        }

        private static void BuildWall(Transform root, ViewPalette palette,
                                      float radiusX, float radiusZ, float wallHeight)
        {
            // The block mesh is a metre square and a quarter deep, and scale multiplies that depth
            // rather than setting it - the first version asked for 0.42 and got 0.10, a wall thin
            // enough to read as a drawn outline. Built thicker than the kit's own proportion on
            // purpose: at this camera angle the top of the wall is most of what is seen of it.
            const float thickness = 0.6f;
            float depthScale = thickness / 0.25f;

            // The blocks sit with their inner face on the arena's ellipse, so their centres run
            // round a slightly larger one - and it is that larger curve they have to cover. Laying
            // them out on the inner ellipse and then pushing each one outwards stretches the ring
            // it needs to fill without adding the blocks to fill it, which opens hairline gaps at
            // the ends of the oval where the curvature is sharpest.
            float centreX = radiusX + thickness * 0.5f;
            float centreZ = radiusZ + thickness * 0.5f;
            float perimeter = ArenaShape.Perimeter(centreX, centreZ);

            // One block per kit-metre of wall, so the brickwork keeps the proportions it was
            // modelled with instead of being stretched to whatever count came out round.
            int count = Mathf.Max(Mathf.RoundToInt(perimeter / MetreToWorld), 8);
            float step = perimeter / count;
            var angles = ArenaShape.EvenlySpacedAngles(count, centreX, centreZ);

            for (int i = 0; i < count; i++)
            {
                float t = angles[i];
                var piece = Place(palette.WallBlock, root, $"Segment_{i:00}",
                                  OnEllipse(t, centreX, centreZ, 0f), Outward(t, centreX, centreZ));

                // A hair of overlap: neighbours meeting exactly leave hairline gaps wherever the
                // chord cuts inside the curve, which on the ends of an oval is everywhere.
                piece.transform.localScale = new Vector3(step * 1.04f, wallHeight, depthScale);
            }
        }

        /// <summary>
        /// Painted blocks, for a clone without the kit. Kept rather than leaving the arena open: the
        /// wall is what the eye reads the playable edge from, and without it the bounces look like
        /// the fighters are turning round at nothing.
        /// </summary>
        private static void BuildPrimitiveWall(Transform root, ViewPalette palette,
                                               float radiusX, float radiusZ, float wallHeight)
        {
            const int segments = 72;
            float step = ArenaShape.Perimeter(radiusX, radiusZ) / segments;
            var angles = ArenaShape.EvenlySpacedAngles(segments, radiusX, radiusZ);

            for (int i = 0; i < segments; i++)
            {
                float t = angles[i];

                var go = new GameObject($"Segment_{i:00}");
                go.transform.SetParent(root, false);
                go.transform.localPosition = OnEllipse(t, radiusX, radiusZ, wallHeight * 0.5f);
                go.transform.localRotation = Quaternion.LookRotation(Outward(t, radiusX, radiusZ), Vector3.up);
                go.transform.localScale = new Vector3(step * 1.06f, wallHeight, 0.4f);

                go.AddComponent<MeshFilter>().sharedMesh = palette != null ? palette.Cube : null;
                go.AddComponent<MeshRenderer>().sharedMaterial = palette != null ? palette.WallStone : null;
            }
        }

        /// <summary>Posts at the torch positions, so each flame stands on something.</summary>
        private static void BuildPosts(Transform root, ViewPalette palette,
                                       float radiusX, float radiusZ, float wallHeight, int count)
        {
            if (palette.WallPost == null || count <= 0) return;

            var angles = ArenaShape.EvenlySpacedAngles(count, radiusX, radiusZ);
            for (int i = 0; i < count; i++)
            {
                float t = angles[i];
                var outward = Outward(t, radiusX, radiusZ);
                var post = Place(palette.WallPost, root, $"Post_{i:00}",
                                 OnEllipse(t, radiusX, radiusZ, 0f) + outward * 0.1f, outward);

                // The mesh is two metres tall, scaled so its top lands where the torches burn. Kept
                // narrow: at kit scale the posts were wider than the wall was thick and read as
                // crates set down beside it rather than as part of it.
                post.transform.localScale = new Vector3(1.1f, wallHeight / 2f, 1.1f);
            }
        }

        /// <summary>
        /// A tier of stone behind the wall, with a rail along its outer edge.
        ///
        /// Pure scenery, and it earns its place for one reason: beyond the wall the frame was empty
        /// background, and an arena that stops at a waist-high parapet reads as a model on a table
        /// rather than a building.
        /// </summary>
        private static void BuildGallery(Transform parent, ViewPalette palette,
                                         float radiusX, float radiusZ, float wallHeight, float depth)
        {
            if (palette.GallerySlab == null) return;

            var root = new GameObject("Gallery");
            root.transform.SetParent(parent, false);

            // Spaced on the outer edge: pieces stepped by the inner arc would pile into each other
            // by the far side of a ring this deep.
            float outerX = radiusX + depth;
            float outerZ = radiusZ + depth;
            float perimeter = ArenaShape.Perimeter(outerX, outerZ);
            int count = Mathf.Max(Mathf.RoundToInt(perimeter / MetreToWorld), 8);
            float step = perimeter / count;
            var angles = ArenaShape.EvenlySpacedAngles(count, outerX, outerZ);

            for (int i = 0; i < count; i++)
            {
                float t = angles[i];
                var outward = Outward(t, outerX, outerZ);
                var onOuter = OnEllipse(t, outerX, outerZ, wallHeight);

                var slab = Place(palette.GallerySlab, root.transform, $"GalleryFloor_{i:00}",
                                 onOuter - outward * (depth * 0.5f), outward);
                slab.transform.localScale = new Vector3(step * 1.06f, 1f, depth);

                if (palette.GalleryRail == null) continue;

                var rail = Place(palette.GalleryRail, root.transform, $"GalleryRail_{i:00}",
                                 onOuter, outward);
                rail.transform.localScale = new Vector3(step * 1.06f, MetreToWorld, MetreToWorld);
            }
        }

        /// <summary>
        /// Torches along the top of the wall. They are the visible clock of the match: during
        /// Planning the world runs slow, and a burning torch is the only thing on a still arena
        /// that shows it.
        /// </summary>
        private static void BuildTorches(Transform parent, ViewPalette palette,
                                         float radiusX, float radiusZ, float wallHeight, int count)
        {
            if (palette == null || palette.Torch == null || count <= 0) return;

            var root = new GameObject("Torches");
            root.transform.SetParent(parent, false);

            var angles = ArenaShape.EvenlySpacedAngles(count, radiusX, radiusZ);
            for (int i = 0; i < count; i++)
            {
                float t = angles[i];
                var position = OnEllipse(t, radiusX, radiusZ, wallHeight);

                // Facing inwards, towards the fight - so the flame is turned towards the camera
                // rather than showing its back over the parapet.
                var inward = new Vector3(-position.x, 0f, -position.z);
                if (inward.sqrMagnitude < 0.0001f) inward = Vector3.forward;

                var torch = (GameObject)PrefabUtility.InstantiatePrefab(palette.Torch, root.transform);
                torch.name = $"Torch_{i:00}";
                torch.transform.localPosition = position;
                torch.transform.localRotation = Quaternion.LookRotation(inward.normalized, Vector3.up);

                // A particle system set to unscaled time would keep burning at full speed through
                // the planning slowdown - and these flames are the only thing that shows it. Set
                // here, into the scene, rather than fixed up at runtime.
                foreach (var particles in torch.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = particles.main;
                    main.useUnscaledTime = false;
                }
            }
        }

        private static Vector3 OnEllipse(float t, float radiusX, float radiusZ, float height)
            => new Vector3(Mathf.Cos(t) * radiusX, height, Mathf.Sin(t) * radiusZ);

        private static Vector3 Outward(float t, float radiusX, float radiusZ)
        {
            var normal = ArenaShape.OutwardNormal(t, radiusX, radiusZ);
            return new Vector3(normal.x, 0f, normal.y);
        }

        private static GameObject Place(GameObject prefab, Transform parent, string name,
                                        Vector3 localPosition, Vector3 outward)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterial = TintedStone(renderer.sharedMaterial);

            // The kit ships a collider on every piece. Nothing here is ever raycast - input projects
            // onto a mathematical plane, and the wall is a formula - so several hundred of them
            // would be pure weight.
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            return instance;
        }

        /// <summary>
        /// One warmed copy per kit material, saved as an asset and shared by every piece using it.
        ///
        /// An asset rather than a runtime copy, now that the props live in the scene: a material
        /// created in memory would be gone the moment the scene was saved, leaving the whole wall
        /// pointing at nothing. A shared asset rather than a per-renderer property block, because a
        /// block breaks SRP batching and there are a few hundred of these.
        /// </summary>
        private static Material TintedStone(Material source)
        {
            if (source == null) return null;
            if (Tinted.TryGetValue(source, out var cached)) return cached;

            string path = $"{StoneMaterialDir}/Arena_{source.name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }

            if (material.HasProperty(ColorId)) material.SetColor(ColorId, StoneTint);
            else if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, StoneTint);

            EditorUtility.SetDirty(material);
            Tinted[source] = material;
            return material;
        }

        private static void Clear(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
        }
    }
}
