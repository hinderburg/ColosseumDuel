using System;
using System.IO;
using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay;
using ColosseumDuel.Gameplay.Hud;
using ColosseumDuel.Gameplay.View;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ColosseumDuel.EditorTools
{
    /// <summary>
    /// One-shot project setup that would otherwise be a checklist of clicks in the Editor:
    /// render pipeline asset, player settings that WebGL/GitHub Pages needs, the material palette
    /// the runtime-built views draw with, and a grey-box arena scene registered in Build Settings.
    ///
    /// Everything here is idempotent - run it again after pulling and it just reasserts the setup.
    /// Run from the menu (Tools > Colosseum > ...) or headless:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod ColosseumDuel.EditorTools.ProjectBootstrap.RunAll
    /// </summary>
    public static class ProjectBootstrap
    {
        private const string SettingsDir = "Assets/Settings";
        private const string ScenesDir = "Assets/Scenes";
        private const string MaterialsDir = "Assets/Materials";
        private const string ArenaScenePath = ScenesDir + "/Arena.unity";
        private const string UrpAssetPath = SettingsDir + "/UniversalRP.asset";
        private const string UrpRendererPath = SettingsDir + "/UniversalRP_Renderer.asset";
        private const string PalettePath = SettingsDir + "/ViewPalette.asset";
        private const string HudFontPath = "Assets/Fonts/Inter-Regular.ttf";
        private const string TexturesDir = "Assets/Textures";
        private const string AbilityFirePrefabPath =
            "Assets/Epic Toon FX/Prefabs/Environment/Fire/Cartoon/Torch Intense/CartoonFireTorchIntenseYellow.prefab";
        private const string TorchPrefabPath =
            "Assets/Epic Toon FX/Prefabs/Environment/Fire/Cartoon/Torch/CartoonFireTorchRed.prefab";
        private const string BloodPrefabPath =
            "Assets/Epic Toon FX/Prefabs/Combat/Blood/Red/BloodExplosion.prefab";


        // Modular stone kit (LoafbrrAssets/ModularArena), used to dress the arena.
        private const string ArenaKitDir = "Assets/LoafbrrAssets/ModularArena/Prefabs";
        private const string WallBlockPath = ArenaKitDir + "/wall/Wall_A_1x1.prefab";
        private const string WallPostPath = ArenaKitDir + "/wall/Wall_Post_B_2m.prefab";
        private const string GallerySlabPath = ArenaKitDir + "/floor/Ground_C_1x1.prefab";
        private const string GalleryRailPath = ArenaKitDir + "/stair/Rail_A_1m.prefab";

        // The columns standing on the sand. Low Poly Trim Sheet's pillar, sized at runtime to the
        // footprint the simulation gives a column - see ArenaView.BuildObstacles.
        private const string ColumnPrefabPath =
            "Assets/Low Poly Trim Sheet Asset Collection/TrimSheet_Prefabs/Pillar.prefab";

        /// <summary>World radius of the arena floor; GameConstants.ArenaRadius maps onto this.</summary>
        private const float WorldArenaRadius = 8f;

        /// <summary>
        /// How high the wall stands: shoulder to shoulder with the gladiators, by request.
        ///
        /// It was a waist-high parapet for a long time, for a reason that still holds - the camera
        /// looks down the length of the arena, so a wall as tall as a fighter hides the strip of
        /// floor immediately behind it at the far end. That strip is outside the playable ellipse,
        /// so nothing is lost that anyone fights over, and tying it to the gladiators means the two
        /// cannot drift apart when either is resized.
        /// </summary>
        private const float WallHeight = GladiatorPrefabs.TargetHeight;

        // --- presentation format ---
        // Portrait 9:16. The arena occupies the middle band and the two rosters sit above and below
        // it, which is what the vertical shape buys.
        private const int ScreenWidth = 576;
        private const int ScreenHeight = 1024;

        /// <summary>Degrees above the horizontal that the camera looks down at the arena.</summary>
        private const float CameraPitch = 66f;
        private const float CameraFieldOfView = 55f;

        /// <summary>
        /// Puts the camera on a fixed arc above and in front of the arena, far enough back that the
        /// full circle fits the narrow dimension of a portrait frame.
        ///
        /// In portrait the horizontal field of view is the binding constraint - it is far narrower
        /// than the vertical one - so the distance is derived from it rather than guessed.
        /// </summary>
        private static void PlaceArenaCamera(Transform camera, float arenaRadius)
        {
            float aspect = ScreenWidth / (float)ScreenHeight;
            float halfVertical = CameraFieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfHorizontal = Mathf.Atan(Mathf.Tan(halfVertical) * aspect);

            const float margin = 1.06f; // so the wall does not touch the edge of the frame

            // Both axes have to fit, and which one binds depends on the elongation and the tilt: a
            // portrait frame is narrow, but a long arena seen at an angle is also tall on screen.
            // Taking the larger of the two distances lets either constraint win.
            float halfWidth = arenaRadius * margin;
            float halfDepth = arenaRadius * GameConstants.ArenaElongation * margin;
            float projectedHalfHeight = halfDepth * Mathf.Sin(CameraPitch * Mathf.Deg2Rad);

            // How much of the frame height the arena may claim. At this elongation an oval that
            // fills the width is taller than the frame, so something has to give: keeping the width
            // and letting the ends run behind the squad corners reads better than a shape that fits
            // entirely but sits small with margins on both sides. The corner tiles are opaque, so
            // they stay legible over it.
            const float verticalBandFraction = 0.95f;

            float distance = Mathf.Max(
                halfWidth / Mathf.Tan(halfHorizontal),
                projectedHalfHeight / (Mathf.Tan(halfVertical) * verticalBandFraction));

            // Aim slightly nearer than the centre of the arena. Under perspective the near half of
            // an oval takes up far more screen than the far half, so aiming dead centre leaves the
            // shape sitting low in the frame and overlapping the player's own squad.
            var target = new Vector3(0f, 0f, -arenaRadius * GameConstants.ArenaElongation * 0.16f);

            var forward = Quaternion.Euler(CameraPitch, 0f, 0f) * Vector3.forward;
            camera.position = target - forward * distance;
            camera.rotation = Quaternion.Euler(CameraPitch, 0f, 0f);
        }

        [MenuItem("Tools/Colosseum/Bootstrap project (settings + scene)", priority = 0)]
        public static void RunAll()
        {
            ConfigureRenderPipeline();
            ConfigurePlayerSettings();
            CapImportedTextures();
            BuildViewPalette();
            RebuildArenaScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Colosseum] Bootstrap finished.");
        }


        // ------------------------------------------------------------------
        // texture budget: what the download actually weighs
        // ------------------------------------------------------------------

        /// <summary>
        /// The Asset Store packs, as they are named on disk. Everything under one of these is
        /// third-party content imported by hand and absent from a fresh clone, which is exactly why
        /// its import settings have to be applied by a script rather than committed.
        /// </summary>
        private static readonly string[] ImportedPacks =
        {
            "Assets/Epic Toon FX/",
            "Assets/DoubleL/",
            "Assets/LoafbrrAssets/",
            "Assets/Low Poly Trim Sheet Asset Collection/",
            "Assets/Matthew Guz/",
            "Assets/Crusader_Castle/",
            "Assets/ExplosiveLLC/",
            "Assets/Modern GDR - Free icons pack/",
        };

        /// <summary>
        /// How large a texture from an imported pack is allowed to be, in pixels on its long edge.
        ///
        /// Five hundred and twelve for everything, because nothing in this game is ever seen large:
        /// the camera looks down at a phone-shaped frame 576 pixels across, a gladiator is about a
        /// fifth of that tall and a sword is fifty pixels long. The packs ship 2K source art, which
        /// is the right thing for them to ship and four times the pixels this game can display in
        /// each direction.
        /// </summary>
        private const int PackTextureMaxSize = 512;

        /// <summary>
        /// Paths that get a different cap from the blanket one, longest match first.
        ///
        /// Two kinds of exception. The icon atlas is sliced into sprites drawn at 24 to 40 pixels,
        /// but there are a hundred of them on one sheet, so the sheet needs to stay larger than any
        /// one icon - halving it is enough and taking it to 512 would visibly soften the HUD.
        ///
        /// The rest is demo content that is never displayed and cannot be excluded, because it sits
        /// in a folder called Resources and Unity ships everything in one of those whether anything
        /// references it or not. It cannot be dropped without deleting somebody's licensed files, so
        /// it is shrunk to nothing instead - four megabytes of a stranger's example scene was the
        /// third largest thing in the build.
        /// </summary>
        private static readonly (string Path, int MaxSize)[] TextureExceptions =
        {
            ("Assets/Modern GDR - Free icons pack/01_Demo/", 32),
            ("Assets/Modern GDR - Free icons pack/00_Atlas/", 1024),
        };

        /// <summary>
        /// Brings every imported pack texture down to something this game can actually display.
        ///
        /// Textures were 93% of the build - 75 MB of an 80 MB payload - and the download was most
        /// of a minute on a decent connection. Compression was never the problem and has been on
        /// the whole time (gzip with Unity's JS fallback, since Pages cannot send an encoding
        /// header): gzip has nothing to do with GPU-compressed texture data, which is already
        /// packed. The only way to make a texture smaller is to have fewer pixels in it.
        ///
        /// Run from RunAll and again from BuildWebGL, so a build cannot go out at full size just
        /// because somebody re-imported a pack since the last bootstrap. Idempotent, and it only
        /// ever lowers a cap - a texture already smaller than its budget is left alone.
        /// </summary>
        [MenuItem("Tools/Colosseum/Cap imported texture sizes", priority = 30)]
        public static void CapImportedTextures()
        {
            int changed = 0;
            long saved = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                int cap = TextureCapFor(path);
                if (cap <= 0) continue;

                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                if (importer.maxTextureSize <= cap) continue;

                // Rough, and only for the log line: area scales with the square of the edge.
                saved += (long)importer.maxTextureSize * importer.maxTextureSize
                         - (long)cap * cap;

                importer.maxTextureSize = cap;
                importer.SaveAndReimport();
                changed++;
            }

            Debug.Log($"[Colosseum] Capped {changed} pack textures (about {saved / 1_000_000f:0.#} " +
                      "megapixels of source art the camera could never show).");
        }

        /// <summary>
        /// The cap for a path, or zero for anything this is not allowed to touch - the project's own
        /// art, which is drawn at the size it is used, and everything in Packages.
        /// </summary>
        private static int TextureCapFor(string path)
        {
            foreach (var exception in TextureExceptions)
                if (path.StartsWith(exception.Path, StringComparison.Ordinal))
                    return exception.MaxSize;

            foreach (string pack in ImportedPacks)
                if (path.StartsWith(pack, StringComparison.Ordinal))
                    return PackTextureMaxSize;

            return 0;
        }
        // ------------------------------------------------------------------
        // render pipeline
        // ------------------------------------------------------------------

        [MenuItem("Tools/Colosseum/Configure render pipeline (URP)", priority = 20)]
        public static void ConfigureRenderPipeline()
        {
            if (GraphicsSettings.defaultRenderPipeline != null)
            {
                Debug.Log($"[Colosseum] Render pipeline already set to {GraphicsSettings.defaultRenderPipeline.name}, leaving it alone.");
                return;
            }

            EnsureFolder(SettingsDir);

            // URP's concrete types are reached reflectively on purpose: this Editor script must keep
            // compiling even if the URP package is absent or renames something between versions.
            // A hard type reference would turn a package hiccup into a project-wide compile error.
            var rendererDataType = FindType("UnityEngine.Rendering.Universal.UniversalRendererData");
            var pipelineAssetType = FindType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset");
            if (rendererDataType == null || pipelineAssetType == null)
            {
                Debug.LogWarning("[Colosseum] URP types not found - is com.unity.render-pipelines.universal installed? " +
                                 "Skipping pipeline setup; the project will render with the Built-in pipeline.");
                return;
            }

            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableObject>(UrpRendererPath);
            if (rendererData == null)
            {
                rendererData = (ScriptableObject)ScriptableObject.CreateInstance(rendererDataType);
                AssetDatabase.CreateAsset(rendererData, UrpRendererPath);
            }

            var pipelineAsset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(UrpAssetPath);
            if (pipelineAsset == null)
            {
                pipelineAsset = (RenderPipelineAsset)ScriptableObject.CreateInstance(pipelineAssetType);
                AssetDatabase.CreateAsset(pipelineAsset, UrpAssetPath);

                // The renderer list is private; wire it through SerializedObject rather than guessing
                // at an API that has changed shape more than once across URP versions.
                var so = new SerializedObject(pipelineAsset);
                var list = so.FindProperty("m_RendererDataList");
                if (list != null)
                {
                    list.arraySize = 1;
                    list.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
                    var defaultIndex = so.FindProperty("m_DefaultRendererIndex");
                    if (defaultIndex != null) defaultIndex.intValue = 0;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                else
                {
                    Debug.LogWarning("[Colosseum] Could not find m_RendererDataList on the URP asset - " +
                                     "open " + UrpAssetPath + " and assign the renderer manually.");
                }
            }

            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            QualitySettings.renderPipeline = pipelineAsset;
            AssetDatabase.SaveAssets();
            Debug.Log("[Colosseum] URP asset created and assigned in Graphics/Quality settings.");
        }

        private static Type FindType(string fullName)
            => AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType(fullName); } catch { return null; } })
                .FirstOrDefault(t => t != null);

        // ------------------------------------------------------------------
        // player settings
        // ------------------------------------------------------------------

        [MenuItem("Tools/Colosseum/Configure player settings (WebGL/Pages)", priority = 21)]
        public static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "ColosseumDuel";
            PlayerSettings.productName = "Colosseum Duel";
            PlayerSettings.runInBackground = true;

            // Portrait. The camera framing in PlaceArenaCamera is derived from this ratio, so the
            // two have to be changed together or the arena stops fitting the frame.
            PlayerSettings.defaultWebScreenWidth = ScreenWidth;
            PlayerSettings.defaultWebScreenHeight = ScreenHeight;

            // Our own page rather than Unity's. The stock template puts the player in a fixed-size
            // box, absolutely positioned and centred with a translate - which takes it out of the
            // flow, so the page has no height and cannot scroll, and centres a box taller than the
            // window at a negative top. On any screen shorter than the canvas the top of the game
            // was cut off and unreachable at once. See Assets/WebGLTemplates/Colosseum.
            PlayerSettings.WebGL.template = "PROJECT:Colosseum";

            // GitHub Pages cannot be told to send Content-Encoding, which is why the usual advice is
            // to turn compression off entirely - at the cost of shipping a ~44 MB uncompressed
            // player. The decompression fallback is the better answer: Unity embeds a JS
            // decompressor in the loader, so a compressed build works on any dumb static host with
            // no server headers at all. Gzip rather than Brotli because its fallback decoder is much
            // faster in JS, and the size difference is small at this scale.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            // Off. It keeps the build's data file in the browser's IndexedDB, which sounds like a
            // free second-visit speedup and in practice serves the previous build: the URL does not
            // change between publishes, so a returning player gets today's loader with last week's
            // game and no indication that anything is stale. That has now cost real confusion twice
            // - a balance change that "did not apply" was this. A slower first frame is a fair price
            // for the build on screen being the build that was published.
            PlayerSettings.WebGL.dataCaching = false;
            // Not ExplicitlyThrownExceptionsOnly: that mode silently swallows null references, so a
            // per-frame exception in Update would look exactly like "the game ignores input" with
            // nothing in the console to explain it. Without stack traces keeps most of the size back
            // (they cost ~4 MB of wasm); switch to FullWithStacktrace while chasing a live bug.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithoutStacktrace;

            // Download size matters a lot here, and gzip does nothing for a wasm full of symbols -
            // they compress, but there is no reason to ship them at all. Embedded debug symbols
            // alone were most of a 46 MB wasm. See CapImportedTextures for the other 93%.
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
            PlayerSettings.stripEngineCode = true;
            // Low, not High: this project builds most of its objects at runtime, and aggressive
            // managed stripping removes types that only a runtime call path reaches - the kind of
            // breakage that shows up in the browser and nowhere else.
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Master);

            // Input Manager (old). The Input System package was in the original manifest but nothing
            // ever used it - the input controller and uGUI's StandaloneInputModule are both legacy -
            // so it has been removed and this no longer needs to be "Both".
            SetActiveInputHandling(0);

            AssetDatabase.SaveAssets();
            Debug.Log("[Colosseum] Player settings configured (WebGL gzip with JS fallback, "
                      + "Input Manager only).");
        }

        /// <summary>0 = old Input Manager, 1 = new Input System, 2 = both. No public API exists.</summary>
        private static void SetActiveInputHandling(int mode)
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (settings == null)
            {
                Debug.LogWarning("[Colosseum] Could not open ProjectSettings.asset to set Active Input Handling.");
                return;
            }

            var so = new SerializedObject(settings);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null)
            {
                Debug.LogWarning("[Colosseum] activeInputHandler property not found; set Active Input Handling manually.");
                return;
            }
            if (prop.intValue == mode) return;

            prop.intValue = mode;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[Colosseum] Active Input Handling set to Both - the Editor must be restarted for it to take effect.");
        }

        // ------------------------------------------------------------------
        // view palette
        // ------------------------------------------------------------------

        [MenuItem("Tools/Colosseum/Rebuild view palette", priority = 22)]
        public static void BuildViewPalette()
        {
            EnsureFolder(SettingsDir);
            EnsureFolder(MaterialsDir);

            var palette = AssetDatabase.LoadAssetAtPath<ViewPalette>(PalettePath);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<ViewPalette>();
                AssetDatabase.CreateAsset(palette, PalettePath);
            }

            // Whole body in the side colour, helmet a lighter tint: at this camera distance a small
            // coloured helmet on a grey body was not enough to tell the two fighters apart.
            palette.PlayerBody = Lit("BodyPlayer", new Color(0.18f, 0.42f, 0.92f));
            palette.BotBody = Lit("BodyBot", new Color(0.86f, 0.20f, 0.18f));
            // The helmet is the pack's steel, tinted rather than painted over: the tint carries the
            // side and the texture keeps it looking like a helmet. Darker than the old flat colours,
            // because a base map multiplies them down and a pale tint came out white.
            palette.PlayerHelmet = GearPrefabs.HelmetMaterial("HelmetPlayer", new Color(0.42f, 0.62f, 1.00f));
            palette.BotHelmet = GearPrefabs.HelmetMaterial("HelmetBot", new Color(1.00f, 0.40f, 0.34f));

            palette.Weapon = Lit("ItemWeapon", new Color(0.85f, 0.80f, 0.35f));
            palette.Shield = Lit("ItemShield", new Color(0.55f, 0.60f, 0.70f));
            palette.RandomItem = Lit("ItemRandom", new Color(0.60f, 0.35f, 0.80f));

            // Washes over the sand rather than paint on top of it. Opaque and fully saturated, the
            // danger zone read as the arena having been repainted red - it took over the frame from
            // the fight happening on it. Transparent and dulled, it is ground you can see is
            // dangerous and still see the floor of.
            palette.HazardActive = TransparentUnlit("HazardActive", new Color(0.55f, 0.22f, 0.17f, 0.62f));
            palette.HazardTelegraph = TransparentUnlit("HazardTelegraph", new Color(0.50f, 0.40f, 0.24f, 0.50f));

            palette.BarBackground = Unlit("BarBackground", new Color(0.06f, 0.06f, 0.08f));
            palette.BarHp = Unlit("BarHp", new Color(0.30f, 0.85f, 0.35f));
            palette.BarRage = Unlit("BarRage", new Color(0.95f, 0.65f, 0.15f));

            // White, not the old yellow: over bright sand the yellow line was hard to pick out.
            //
            // A plain stripe. It was a dashed lane a body wide, then half that, with an arrow head;
            // asked for simply a thin white line. The material is reused from disk if it exists, so
            // the dash texture it used to carry is cleared here rather than left to show through.
            palette.Trajectory = TransparentUnlit("Trajectory", Color.white);
            palette.Trajectory.mainTexture = null;
            palette.Trajectory.mainTextureScale = Vector2.one;
            EditorUtility.SetDirty(palette.Trajectory);

            // The inside of the arrow head: the same white, weaker, so the head reads as a solid rim
            // round a lighter body, the way the reference drawing has it.
            palette.TrajectoryFill = TransparentUnlit("TrajectoryFill", new Color(1f, 1f, 1f, 0.78f));

            palette.PullLine = TransparentUnlit("PullLine", new Color(1f, 1f, 1f, 0.75f));

            // The ground his weapon covers, drawn on the sand. Faint enough to read as light on the
            // floor rather than as paint, because the fight has to stay legible through it.
            palette.StrikeZone = TransparentUnlit("StrikeZone", new Color(0.88f, 0.20f, 0.18f, 0.44f));
            palette.Burst = TransparentUnlit("Burst", Color.white);

            // Inter (SIL OFL 1.1, shipped with the Editor and copied into Assets/Fonts along with
            // its licence). Unity's built-in font has no Cyrillic glyphs, so it draws nothing at all
            // for the Russian captions once there are no OS fonts to fall back on - i.e. in a build.
            palette.Skull = IconPack.Sprite("Cross_Bright")
                            ?? ProceduralTextures.EnsureSkull(TexturesDir + "/Skull.png");
            palette.Disc = ProceduralTextures.EnsureDisc(TexturesDir + "/Disc.png");
            palette.Ring = ProceduralTextures.EnsureDisc(TexturesDir + "/Ring.png", innerFraction: 0.78f);
            palette.RoundedPanel = ProceduralTextures.EnsureRoundedRect(TexturesDir + "/RoundedPanel.png");

            // Dark and slightly transparent, so a stain sits in the sand rather than on it, and so
            // two that overlap darken instead of hiding one another.
            palette.BloodStain = TransparentUnlit("BloodStain", new Color(0.55f, 0.07f, 0.07f, 0.90f));
            ApplyTexture(palette.BloodStain,
                ProceduralTextures.EnsureBloodStain(TexturesDir + "/BloodStain.png"), Vector2.one);

            // White tint: the cloth and the wreath are both painted into the texture, so anything
            // other than white here would push one of the two off its own colour.
            palette.Banner = TransparentUnlit("Banner", Color.white);
            ApplyTexture(palette.Banner,
                ProceduralTextures.EnsureBanner(TexturesDir + "/Banner.png"), Vector2.one);

            palette.ArchetypeIcons = new Sprite[GladiatorDef.All.Count];
            for (int i = 0; i < GladiatorDef.All.Count; i++)
            {
                var def = GladiatorDef.All[i];
                palette.ArchetypeIcons[i] = IconPack.Sprite(IconPack.ForArchetype(def.Id))
                    ?? ProceduralTextures.EnsureArchetypeIcon($"{TexturesDir}/Icon_{def.Id}.png", def.Id);
            }

            palette.WeaponIcons = new Sprite[WeaponDef.All.Count];
            for (int i = 0; i < WeaponDef.All.Count; i++)
            {
                var weapon = WeaponDef.All[i];
                palette.WeaponIcons[i] = IconPack.Sprite(IconPack.ForWeapon(weapon.Kind))
                    ?? ProceduralTextures.EnsureWeaponIcon($"{TexturesDir}/Weapon_{weapon.Kind}.png", weapon.Kind);
            }

            // From Epic Toon FX, which is not in the repository (see PROJECT_CONTEXT.md). Missing is
            // a normal state for a clean clone, so it warns rather than failing the bootstrap.
            palette.AbilityReadyFire = AssetDatabase.LoadAssetAtPath<GameObject>(AbilityFirePrefabPath);
            if (palette.AbilityReadyFire == null)
                Debug.LogWarning($"[Colosseum] Effect prefab not found at {AbilityFirePrefabPath} - " +
                                 "the ability-ready flame will be skipped. Import Epic Toon FX to get it.");

            palette.Torch = AssetDatabase.LoadAssetAtPath<GameObject>(TorchPrefabPath);
            if (palette.Torch == null)
                Debug.LogWarning($"[Colosseum] Torch prefab not found at {TorchPrefabPath} - the wall will be unlit.");

            // Iron, for the spikes that fill a danger zone and for the jaws of the traps. Dark and
            // barely lit: they come up out of the sand and should read as a threat rather than as
            // decoration, and against bright sand a dark silhouette does that better than a colour.
            palette.Spike = Lit("Spike", new Color(0.30f, 0.31f, 0.35f));
            palette.TrapIron = Lit("TrapIron", new Color(0.22f, 0.22f, 0.25f));

            palette.BloodHit = AssetDatabase.LoadAssetAtPath<GameObject>(BloodPrefabPath);
            if (palette.BloodHit == null)
                Debug.LogWarning($"[Colosseum] Blood effect not found at {BloodPrefabPath} - hits will land without one.");

            // The modular stone kit the arena is dressed with. Same story as the effects: absent in
            // a clean clone, where ArenaView falls back to painted blocks and skips the gallery.
            palette.WallBlock = AssetDatabase.LoadAssetAtPath<GameObject>(WallBlockPath);
            palette.WallPost = AssetDatabase.LoadAssetAtPath<GameObject>(WallPostPath);
            palette.GallerySlab = AssetDatabase.LoadAssetAtPath<GameObject>(GallerySlabPath);
            palette.GalleryRail = AssetDatabase.LoadAssetAtPath<GameObject>(GalleryRailPath);
            if (palette.WallBlock == null)
                Debug.LogWarning($"[Colosseum] Arena kit not found at {WallBlockPath} - " +
                                 "the wall will be plain blocks. Import LoafbrrAssets/ModularArena to get it.");

            palette.WallStone = Lit("Wall", new Color(0.52f, 0.50f, 0.47f)); // grey stone, per the reference frame
            ApplyTexture(palette.WallStone,
                         ProceduralTextures.EnsureWall(TexturesDir + "/Wall.png", Color.white),
                         new Vector2(2f, 1f));

            // The obstacles on the sand. The pillar is Low Poly Trim Sheet and absent in a clean
            // clone, where ArenaView stands a cylinder in the same stone as the wall; the crate is
            // drawn in code and always there.
            palette.ColumnModel = AssetDatabase.LoadAssetAtPath<GameObject>(ColumnPrefabPath);
            if (palette.ColumnModel == null)
                Debug.LogWarning($"[Colosseum] Pillar not found at {ColumnPrefabPath} - " +
                                 "columns will be plain stone cylinders.");

            // The pillar's own sandstone, lifted onto URP's Lit shader. The pack ships it on the
            // built-in Standard shader, which URP draws as solid magenta - the textures are fine, so
            // they move onto a material URP can draw, and the pillar's own UVs still land on the
            // right part of its trim sheet. Read off the pillar itself rather than a material path,
            // so it is whatever the pillar actually wears. Plain wall stone when the pack is absent.
            var pillar = palette.ColumnModel != null ? palette.ColumnModel.GetComponentInChildren<Renderer>(true) : null;
            var sandstone = pillar != null ? pillar.sharedMaterial : null;

            palette.ColumnStone = Lit("ColumnStone", Color.white);
            if (sandstone != null && sandstone.mainTexture is Texture2D albedo)
            {
                ApplyTexture(palette.ColumnStone, albedo, Vector2.one);
                var normal = sandstone.HasProperty("_BumpMap") ? sandstone.GetTexture("_BumpMap") : null;
                if (normal != null)
                {
                    palette.ColumnStone.SetTexture("_BumpMap", normal);
                    palette.ColumnStone.EnableKeyword("_NORMALMAP");
                }
            }
            else
            {
                palette.ColumnStone.color = new Color(0.60f, 0.57f, 0.52f);
                ApplyTexture(palette.ColumnStone,
                             ProceduralTextures.EnsureWall(TexturesDir + "/Wall.png", Color.white),
                             new Vector2(1f, 2f));
            }

            palette.CrateWood = Lit("CrateWood", Color.white);
            ApplyTexture(palette.CrateWood,
                         ProceduralTextures.EnsureCrate(TexturesDir + "/Crate.png"), Vector2.one);

            // Built from the imported weapon pack rather than loaded from it: the pack's own prefabs
            // carry LOD groups and colliders this game has no use for, and its materials are on the
            // built-in Standard shader, which under URP draws magenta.
            GearPrefabs.EnsureAll();
            palette.SwordModel = AssetDatabase.LoadAssetAtPath<GameObject>(GearPrefabs.SwordPath);
            palette.MaceModel = AssetDatabase.LoadAssetAtPath<GameObject>(GearPrefabs.MacePath);
            palette.ShieldModel = AssetDatabase.LoadAssetAtPath<GameObject>(GearPrefabs.ShieldPath);
            palette.HelmetModel = AssetDatabase.LoadAssetAtPath<GameObject>(GearPrefabs.HelmetPath);
            palette.GearUntrained = InsideOutUnlit("GearUntrained", new Color(0.95f, 0.12f, 0.10f));
            if (palette.SwordModel == null)
                Debug.LogWarning($"[Colosseum] Gear prefabs missing at {GearPrefabs.SwordPath} - pickups " +
                                 "will be primitives and nobody will carry anything visible.");

            palette.Vignette = ProceduralTextures.EnsureVignette(TexturesDir + "/Vignette.png");

            palette.HudFont = AssetDatabase.LoadAssetAtPath<Font>(HudFontPath);
            if (palette.HudFont == null)
                Debug.LogWarning($"[Colosseum] HUD font missing at {HudFontPath} - Cyrillic will not render in a build.");

            // Built-in meshes captured as asset references. Fetched here, in the Editor, rather than
            // at runtime: an unreferenced built-in mesh is not included in a player build.
            palette.Cube = BuiltinMesh("Cube.fbx");
            palette.Sphere = BuiltinMesh("Sphere.fbx");
            palette.Capsule = BuiltinMesh("Capsule.fbx");
            palette.Cylinder = BuiltinMesh("Cylinder.fbx");
            palette.Quad = BuiltinMesh("Quad.fbx");

            // Figures last, and deliberately so: the helmet is built from a palette mesh, so the
            // meshes above have to be assigned before this runs. Placing it earlier worked only
            // because a previously built palette still held the mesh - a clean clone would have
            // produced helmets with no mesh at all and said nothing about it.
            palette.ArchetypeBodies = new Material[GladiatorDef.All.Count];
            for (int i = 0; i < GladiatorDef.All.Count; i++)
            {
                var def = GladiatorDef.All[i];
                palette.ArchetypeBodies[i] = Lit($"Body{def.Id}", palette.ArchetypeColor(def.Id));
            }

            if (GladiatorPrefabs.EnsureAll(palette.HelmetModel, palette.Sphere))
            {
                palette.GladiatorFigures = new GameObject[GladiatorDef.All.Count];
                for (int i = 0; i < GladiatorDef.All.Count; i++)
                    palette.GladiatorFigures[i] =
                        AssetDatabase.LoadAssetAtPath<GameObject>(GladiatorPrefabs.PathFor(GladiatorDef.All[i].Id));
            }
            else
            {
                palette.GladiatorFigures = null;
            }

            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Colosseum] View palette rebuilt at {PalettePath}.");
        }

        // ------------------------------------------------------------------
        // scene
        // ------------------------------------------------------------------

        [MenuItem("Tools/Colosseum/Rebuild arena scene", priority = 23)]
        public static void RebuildArenaScene()
        {
            EnsureFolder(ScenesDir);
            EnsureFolder(MaterialsDir);

            var palette = AssetDatabase.LoadAssetAtPath<ViewPalette>(PalettePath);
            if (palette == null)
            {
                BuildViewPalette();
                palette = AssetDatabase.LoadAssetAtPath<ViewPalette>(PalettePath);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            float r = WorldArenaRadius;

            // The floor gets its texture once across the whole disc - no repeat, so no seams and no
            // tiling pattern to notice. The wall material lives in the palette, since the wall is
            // built at runtime.
            // Pale bone, matching the reference frame: light and nearly colourless, but warm rather
            // than grey. It went from orange sand to grey stone dust and has landed between the two,
            // which is where the reference has it - bright enough that the figures read as dark
            // shapes on it, drained enough that the red of the danger zone and the blood are the
            // only saturated things on the floor.
            var sandMat = Lit("Sand", new Color(0.60f, 0.56f, 0.48f));
            ApplyTexture(sandMat, ProceduralTextures.EnsureSand(TexturesDir + "/Sand.png", Color.white), Vector2.one);

            // Re-loaded here rather than reused from above, and not defensively - it is genuinely
            // needed. Writing the sand texture and building its material runs an asset import, and
            // an import unloads a ScriptableObject that nothing in the scene (still empty at that
            // point) references yet. What is left is a reference alive enough to serialise into the
            // scene correctly, but reading as null to any code that asks - so the arena was dressed
            // with the no-kit fallback wall while the saved scene pointed at a perfectly good
            // palette. Nothing logged, and the scene file looked right.
            palette = AssetDatabase.LoadAssetAtPath<ViewPalette>(PalettePath);

            // --- arena root: owns the virtual->world conversion and the hazard ring visuals ---
            var arenaGo = new GameObject("Arena");
            var arena = arenaGo.AddComponent<ArenaView>();
            arena.WorldArenaRadius = r;
            arena.WallHeight = WallHeight;
            arena.Palette = palette;
            // Assigned below, once the camera exists - world-space labels billboard towards it.

            float rz = r * GameConstants.ArenaElongation;

            // floor: a Unity cylinder is radius 0.5 and height 2, squashed here onto the ellipse
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "ArenaFloor";
            floor.transform.SetParent(arenaGo.transform, false);
            floor.transform.localScale = new Vector3(r * 2f, 0.25f, rz * 2f);
            floor.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            floor.GetComponent<Renderer>().sharedMaterial = sandMat;
            // Nothing raycasts the floor - input projects onto a mathematical plane - and a collider
            // left in the scene would drag the whole physics module into the build.
            UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());

            // The wall, the posts, the gallery and the torches are placed into the scene as prefab
            // instances, so they can be selected and re-dressed by hand in the Editor. Rebuilding
            // the layout from these figures is a separate menu item, so tweaking one prop does not
            // mean regenerating the whole arena.
            ArenaDecor.BuildAll(arena, palette);

            // --- camera: fixed, angled, perspective ---
            // It never moves - no follow, no zoom, no shake - so the arena always sits in exactly
            // the same place on screen and the player can aim by muscle memory.
            var cameraGo = new GameObject("ArenaCamera");
            cameraGo.tag = "MainCamera";
            var cam = cameraGo.AddComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView = CameraFieldOfView;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.05f, 0.07f);
            PlaceArenaCamera(cameraGo.transform, r);
            cameraGo.AddComponent<AudioListener>();
            arena.ArenaCamera = cam;

            var lightGo = new GameObject("Sun");
            // Steep, and steeper since the wall grew: shadow length is height over the tangent of
            // the elevation, so tripling the wall at 50 degrees threw a shadow three units deep
            // across the floor and put a third of the playing area in the dark. At 68 it is under
            // one and a half, which reads as a wall standing in sunlight rather than as a stain.
            lightGo.transform.rotation = Quaternion.Euler(78f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.00f, 0.96f, 0.88f);   // sunlight, not daylight-balanced white
            light.intensity = 0.92f;
            light.shadows = LightShadows.Soft;

            // Half strength. The reference frame is lit like an illustration - shapes read by their
            // own colour, not by what they are standing in - and at full strength the wall threw a
            // hard band across a third of the floor and painted its own inner face black.
            light.shadowStrength = 0.45f;

            // Ambient, set explicitly rather than left at the default. A scene created empty has no
            // skybox, so the default ambient is nearly black - and with a single sun that means every
            // vertical surface in the arena is lit only where the sun grazes it. The wall came out as
            // a black ring around bright sand and read as an outline rather than as stone.
            //
            // A three-band ambient does the work a bounce would: warm light off the sand fills the
            // inside of the wall, cool light from above keeps the sand itself from going flat.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Warm-neutral rather than blue. The cool sky band was the whole reason the stone read as
            // slate: the kit's own texture is already cool, and a blue fill on top of it turned a
            // sandstone arena into a winter one.
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.33f, 0.31f);
            // Lifted hard. This band is what lights vertical surfaces, and the inner face of the
            // wall is vertical, faces inward and never catches a sun that comes in at sixty-eight
            // degrees - so the arena was a near-black ring around a bright floor.
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.41f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.27f, 0.19f);

            // --- game logic host ---
            var gameGo = new GameObject("Game");
            var controller = gameGo.AddComponent<GameController>();
            controller.Arena = arena;

            var input = cameraGo.AddComponent<PlayerInputController>();
            input.Controller = controller;
            input.ArenaCamera = cam;

            // Written out rather than left to the field initialiser: this is the value that ends up
            // in the .unity file, and it is the one the build actually plays with. The last time
            // this was left to the initialiser the built game shipped on the wrong scheme and every
            // press on the sand did nothing.
            input.Scheme = ControlScheme.Draw;

            // The one thing allowed to move the camera. It reads its home pose off the transform at
            // startup, so it has to be added after the camera has been placed.
            var deathCamera = cameraGo.AddComponent<DeathCameraView>();
            deathCamera.Controller = controller;
            deathCamera.Arena = arena;


            BuildHud(controller, input);

            EditorSceneManager.SaveScene(scene, ArenaScenePath);
            RegisterSceneInBuildSettings(ArenaScenePath);

            Debug.Log($"[Colosseum] Arena scene rebuilt at {ArenaScenePath} and added to Build Settings.");
        }

        /// <summary>
        /// Canvas + EventSystem for the HUD. MatchHud builds its own contents at runtime, so all the
        /// scene needs is the canvas to hang them on and an event system to route the clicks.
        /// </summary>
        private static void BuildHud(GameController controller, PlayerInputController input)
        {
            var canvasGo = new GameObject("HudCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ScreenWidth, ScreenHeight);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            var hud = canvasGo.AddComponent<MatchHud>();
            hud.Controller = controller;
            hud.Input = input;

            // Without an EventSystem the buttons render but never receive a click.
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<StandaloneInputModule>();
            }
        }

        private static void RegisterSceneInBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == scenePath))
            {
                foreach (var s in scenes) s.enabled = s.path == scenePath || s.enabled;
                EditorBuildSettings.scenes = scenes.ToArray();
                return;
            }
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ------------------------------------------------------------------
        // WebGL build
        // ------------------------------------------------------------------

        /// <summary>Where a local build lands. Kept out of git by .gitignore.</summary>
        public const string WebGLBuildPath = "Build/WebGL";

        /// <summary>
        /// Entry point for game-ci's unity-builder, which passes the output directory as
        /// -customBuildPath. Routing CI through the same method as the local build means the two
        /// cannot drift apart on the settings a Pages deploy depends on.
        /// </summary>
        public static void BuildWebGLForCI() => BuildWebGL(ReadCommandLineArg("-customBuildPath"));

        private static string ReadCommandLineArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        [MenuItem("Tools/Colosseum/Build WebGL", priority = 40)]
        public static void BuildWebGL() => BuildWebGL(null);

        private static void BuildWebGL(string outputPath)
        {
            // Re-assert the settings a Pages deploy depends on, so a build can never go out on the
            // wrong compression because someone flipped it in the inspector - or at full texture
            // size because a pack was re-imported since the last bootstrap, which is the difference
            // between a fifteen megabyte download and a sixty megabyte one.
            ConfigurePlayerSettings();
            CapImportedTextures();

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new BuildFailedException("[Colosseum] No scenes enabled in Build Settings - run the bootstrap first.");

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                Debug.Log("[Colosseum] Switching active build target to WebGL (this reimports assets once).");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                    throw new BuildFailedException("[Colosseum] Could not switch to WebGL - is the WebGL module installed?");
            }

            string destination = string.IsNullOrEmpty(outputPath) ? WebGLBuildPath : outputPath;

            // Stamped with where it came from, for the length of the build only. The menu shows it:
            // the same link serves every build published to it, and a browser will happily keep
            // serving an old one from its cache, so without this nobody could tell which was open.
            string version = DescribeBuild();
            string previousVersion = PlayerSettings.bundleVersion;
            PlayerSettings.bundleVersion = version;

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = destination,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None
                });
            }
            finally
            {
                // Put back, so ProjectSettings does not pick up a new number every time anybody
                // builds.
                PlayerSettings.bundleVersion = previousVersion;
            }

            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"[Colosseum] WebGL build {summary.result}: {summary.totalErrors} error(s).");

            Debug.Log($"[Colosseum] WebGL build succeeded: {destination}, build \"{version}\", " +
                      $"{summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0} s.");
        }

        /// <summary>
        /// What a build calls itself: the branch, the commit and when it was made - for example
        /// "iteration4 943609b 2026-09-10 23:40". A "+" after the commit means the working tree had
        /// changes nobody had committed yet, so the build is not exactly that commit.
        /// </summary>
        private static string DescribeBuild()
        {
            string branch = Git("rev-parse --abbrev-ref HEAD");
            string commit = Git("rev-parse --short HEAD");
            if (string.IsNullOrEmpty(commit)) commit = "unknown";
            else if (!string.IsNullOrEmpty(Git("status --porcelain --untracked-files=no"))) commit += "+";

            string when = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

            // A detached checkout - which is what CI builds from - has no branch to name.
            return string.IsNullOrEmpty(branch) || branch == "HEAD"
                ? $"{commit} {when}"
                : $"{branch} {commit} {when}";
        }

        /// <summary>Runs git in the project folder and returns what it printed, or null if it could not.</summary>
        private static string Git(string arguments)
        {
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = Path.GetDirectoryName(Application.dataPath),
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var git = System.Diagnostics.Process.Start(start))
                {
                    string output = git.StandardOutput.ReadToEnd().Trim();
                    return git.WaitForExit(5000) && git.ExitCode == 0 ? output : null;
                }
            }
            catch (Exception)
            {
                // No git on the machine, or not a checkout: the build still goes out, just unnamed.
                return null;
            }
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Assigns a tiling base map. The material keeps its own colour, which the texture is
        /// generated white so as to multiply into rather than replace.
        /// </summary>
        private static void ApplyTexture(Material material, Texture2D texture, Vector2 tiling)
        {
            if (material == null || texture == null) return;
            material.mainTexture = texture;
            material.mainTextureScale = tiling;
            material.SetFloat("_Smoothness", 0.05f); // sand and stone are not shiny
            EditorUtility.SetDirty(material);
        }

        private static Mesh BuiltinMesh(string name)
        {
            var mesh = Resources.GetBuiltinResource<Mesh>(name);
            if (mesh == null) Debug.LogWarning($"[Colosseum] Built-in mesh {name} not found.");
            return mesh;
        }

        private static Material Lit(string name, Color color)
            => EnsureMaterial(name, color, "Universal Render Pipeline/Lit", "Standard");

        private static Material Unlit(string name, Color color)
            => EnsureMaterial(name, color, "Universal Render Pipeline/Unlit", "Unlit/Color");

        /// <summary>
        /// URP does not expose a "make this transparent" API - the surface type is a set of shader
        /// properties plus a keyword plus a render queue, and getting one of them wrong leaves the
        /// material silently opaque. This is the full incantation.
        /// </summary>
        private static Material TransparentUnlit(string name, Color color)
        {
            var mat = Unlit(name, color);
            mat.SetFloat("_Surface", 1f); // 0 opaque, 1 transparent
            mat.SetFloat("_Blend", 0f);   // alpha blend
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// An unlit colour drawn on back faces only - the standard way to get an outline without a
        /// custom shader. Put on a slightly larger copy of a mesh, the near half is culled away and
        /// what remains is the far half showing past the real model's silhouette.
        /// </summary>
        private static Material InsideOutUnlit(string name, Color color)
        {
            var mat = Unlit(name, color);
            mat.SetFloat("_Cull", (float)CullMode.Front);
            mat.doubleSidedGI = false;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material EnsureMaterial(string name, Color color, string shaderName, string fallbackShaderName)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var shader = Shader.Find(shaderName) ?? Shader.Find(fallbackShaderName);

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                // Re-assert colour and shader so tweaking the palette in code and re-running the
                // bootstrap actually updates the assets instead of silently keeping the old ones.
                if (existing.shader != shader && shader != null) existing.shader = shader;
                existing.color = color;
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var mat = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
