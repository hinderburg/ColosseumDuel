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

        /// <summary>World radius of the arena floor; GameConstants.ArenaRadius maps onto this.</summary>
        private const float WorldArenaRadius = 8f;

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
            BuildViewPalette();
            RebuildArenaScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Colosseum] Bootstrap finished.");
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

            // GitHub Pages cannot be told to send Content-Encoding, which is why the usual advice is
            // to turn compression off entirely - at the cost of shipping a ~44 MB uncompressed
            // player. The decompression fallback is the better answer: Unity embeds a JS
            // decompressor in the loader, so a compressed build works on any dumb static host with
            // no server headers at all. Gzip rather than Brotli because its fallback decoder is much
            // faster in JS, and the size difference is small at this scale.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            // Not ExplicitlyThrownExceptionsOnly: that mode silently swallows null references, so a
            // per-frame exception in Update would look exactly like "the game ignores input" with
            // nothing in the console to explain it. Without stack traces keeps most of the size back
            // (they cost ~4 MB of wasm); switch to FullWithStacktrace while chasing a live bug.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithoutStacktrace;

            // Download size matters a lot here: compression is off (see above), so whatever the
            // build weighs is what a player waits for. Embedded debug symbols alone were most of a
            // 46 MB wasm.
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
            Debug.Log("[Colosseum] Player settings configured (WebGL compression disabled, input handling = Both).");
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
            palette.PlayerHelmet = Lit("HelmetPlayer", new Color(0.58f, 0.76f, 1.00f));
            palette.BotHelmet = Lit("HelmetBot", new Color(1.00f, 0.60f, 0.55f));

            palette.Weapon = Lit("ItemWeapon", new Color(0.85f, 0.80f, 0.35f));
            palette.Shield = Lit("ItemShield", new Color(0.55f, 0.60f, 0.70f));
            palette.RandomItem = Lit("ItemRandom", new Color(0.60f, 0.35f, 0.80f));

            palette.HazardActive = Unlit("HazardActive", new Color(0.70f, 0.12f, 0.06f));
            palette.HazardTelegraph = Unlit("HazardTelegraph", new Color(0.55f, 0.38f, 0.10f));

            palette.BarBackground = Unlit("BarBackground", new Color(0.06f, 0.06f, 0.08f));
            palette.BarHp = Unlit("BarHp", new Color(0.30f, 0.85f, 0.35f));
            palette.BarRage = Unlit("BarRage", new Color(0.95f, 0.65f, 0.15f));

            // White, not the old yellow: over bright sand and a red danger ring the yellow line was
            // hard to pick out, which is what made the preview easy to miss.
            palette.Trajectory = TransparentUnlit("Trajectory", Color.white);
            ApplyTexture(palette.Trajectory, ProceduralTextures.EnsureDash(TexturesDir + "/Dash.png"), Vector2.one);

            palette.PullLine = TransparentUnlit("PullLine", new Color(1f, 1f, 1f, 0.75f));
            palette.Burst = TransparentUnlit("Burst", Color.white);

            // Inter (SIL OFL 1.1, shipped with the Editor and copied into Assets/Fonts along with
            // its licence). Unity's built-in font has no Cyrillic glyphs, so it draws nothing at all
            // for the Russian captions once there are no OS fonts to fall back on - i.e. in a build.
            palette.Skull = ProceduralTextures.EnsureSkull(TexturesDir + "/Skull.png");
            palette.Disc = ProceduralTextures.EnsureDisc(TexturesDir + "/Disc.png");
            palette.Ring = ProceduralTextures.EnsureDisc(TexturesDir + "/Ring.png", innerFraction: 0.78f);

            // From Epic Toon FX, which is not in the repository (see PROJECT_CONTEXT.md). Missing is
            // a normal state for a clean clone, so it warns rather than failing the bootstrap.
            palette.AbilityReadyFire = AssetDatabase.LoadAssetAtPath<GameObject>(AbilityFirePrefabPath);
            if (palette.AbilityReadyFire == null)
                Debug.LogWarning($"[Colosseum] Effect prefab not found at {AbilityFirePrefabPath} - " +
                                 "the ability-ready flame will be skipped. Import Epic Toon FX to get it.");

            palette.Torch = AssetDatabase.LoadAssetAtPath<GameObject>(TorchPrefabPath);
            if (palette.Torch == null)
                Debug.LogWarning($"[Colosseum] Torch prefab not found at {TorchPrefabPath} - the wall will be unlit.");

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

            var sandMat = Lit("Sand", new Color(0.76f, 0.66f, 0.44f));
            var wallMat = Lit("Wall", new Color(0.52f, 0.36f, 0.24f)); // brown stone, per the layout sketch
            // The floor gets its texture once across the whole disc - no repeat, so no seams and no
            // tiling pattern to notice. The wall is 48 separate blocks, so that one does repeat.
            ApplyTexture(sandMat, ProceduralTextures.EnsureSand(TexturesDir + "/Sand.png", Color.white), Vector2.one);
            ApplyTexture(wallMat, ProceduralTextures.EnsureWall(TexturesDir + "/Wall.png", Color.white), new Vector2(2f, 1f));

            // --- arena root: owns the virtual->world conversion and the hazard ring visuals ---
            var arenaGo = new GameObject("Arena");
            var arena = arenaGo.AddComponent<ArenaView>();
            arena.WorldArenaRadius = r;
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

            // wall: blocks laid along the ellipse. Their spacing is stepped by arc length rather
            // than by angle - equal angles bunch up at the ends of an elongated oval and leave gaps
            // along its flanks.
            var wallRoot = new GameObject("ArenaWall");
            wallRoot.transform.SetParent(arenaGo.transform, false);
            const int segments = 72;
            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                var position = new Vector3(Mathf.Cos(t) * r, 0.5f, Mathf.Sin(t) * rz);

                // Tangent of the ellipse at this angle, so each block lies flat along the wall.
                var tangent = new Vector3(-Mathf.Sin(t) * r, 0f, Mathf.Cos(t) * rz);
                float segmentLength = tangent.magnitude * (Mathf.PI * 2f / segments) * 1.12f;

                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = $"Segment_{i:00}";
                block.transform.SetParent(wallRoot.transform, false);
                block.transform.localPosition = position;
                block.transform.localRotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                block.transform.localScale = new Vector3(0.4f, 1.2f, segmentLength);
                block.GetComponent<Renderer>().sharedMaterial = wallMat;
                UnityEngine.Object.DestroyImmediate(block.GetComponent<Collider>());
            }

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
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;

            // --- game logic host ---
            var gameGo = new GameObject("Game");
            var controller = gameGo.AddComponent<GameController>();
            controller.Arena = arena;

            var input = cameraGo.AddComponent<PlayerInputController>();
            input.Controller = controller;
            input.ArenaCamera = cam;


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
            // Re-assert the settings a Pages deploy depends on, so a build can never go out with
            // compression on just because someone flipped it in the inspector.
            ConfigurePlayerSettings();

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

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = destination,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"[Colosseum] WebGL build {summary.result}: {summary.totalErrors} error(s).");

            Debug.Log($"[Colosseum] WebGL build succeeded: {destination}, " +
                      $"{summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0} s.");
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
