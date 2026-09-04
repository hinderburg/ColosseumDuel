using System;
using System.IO;
using System.Linq;
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

        /// <summary>World radius of the arena floor; GameConstants.ArenaRadius maps onto this.</summary>
        private const float WorldArenaRadius = 8f;

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

            palette.Body = Lit("GladiatorBody", new Color(0.72f, 0.70f, 0.66f));
            palette.PlayerHelmet = Lit("HelmetPlayer", new Color(0.20f, 0.45f, 0.95f));
            palette.BotHelmet = Lit("HelmetBot", new Color(0.90f, 0.22f, 0.20f));

            palette.Weapon = Lit("ItemWeapon", new Color(0.85f, 0.80f, 0.35f));
            palette.Shield = Lit("ItemShield", new Color(0.55f, 0.60f, 0.70f));
            palette.RandomItem = Lit("ItemRandom", new Color(0.60f, 0.35f, 0.80f));

            palette.HazardActive = Unlit("HazardActive", new Color(0.70f, 0.12f, 0.06f));
            palette.HazardTelegraph = Unlit("HazardTelegraph", new Color(0.55f, 0.38f, 0.10f));

            palette.BarBackground = Unlit("BarBackground", new Color(0.06f, 0.06f, 0.08f));
            palette.BarHp = Unlit("BarHp", new Color(0.30f, 0.85f, 0.35f));
            palette.BarRage = Unlit("BarRage", new Color(0.95f, 0.65f, 0.15f));

            palette.Trajectory = Unlit("Trajectory", new Color(0.98f, 0.95f, 0.55f));
            palette.Burst = TransparentUnlit("Burst", Color.white);

            // Inter (SIL OFL 1.1, shipped with the Editor and copied into Assets/Fonts along with
            // its licence). Unity's built-in font has no Cyrillic glyphs, so it draws nothing at all
            // for the Russian captions once there are no OS fonts to fall back on - i.e. in a build.
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
            var wallMat = Lit("Wall", new Color(0.32f, 0.29f, 0.27f));
            // The floor gets its texture once across the whole disc - no repeat, so no seams and no
            // tiling pattern to notice. The wall is 48 separate blocks, so that one does repeat.
            ApplyTexture(sandMat, ProceduralTextures.EnsureSand(TexturesDir + "/Sand.png", Color.white), Vector2.one);
            ApplyTexture(wallMat, ProceduralTextures.EnsureWall(TexturesDir + "/Wall.png", Color.white), new Vector2(2f, 1f));

            // --- arena root: owns the virtual->world conversion and the hazard ring visuals ---
            var arenaGo = new GameObject("Arena");
            var arena = arenaGo.AddComponent<ArenaView>();
            arena.WorldArenaRadius = r;
            arena.Palette = palette;

            // floor: a Unity cylinder is radius 0.5 and height 2
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "ArenaFloor";
            floor.transform.SetParent(arenaGo.transform, false);
            floor.transform.localScale = new Vector3(r * 2f, 0.25f, r * 2f);
            floor.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            floor.GetComponent<Renderer>().sharedMaterial = sandMat;
            // Nothing raycasts the floor - input projects onto a mathematical plane - and a collider
            // left in the scene would drag the whole physics module into the build.
            UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());

            // wall: a ring of thin boxes, cheap and good enough for grey-box
            var wallRoot = new GameObject("ArenaWall");
            wallRoot.transform.SetParent(arenaGo.transform, false);
            const int segments = 48;
            float segmentWidth = 2f * Mathf.PI * r / segments * 1.08f; // slight overlap, no gaps
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = $"Segment_{i:00}";
                block.transform.SetParent(wallRoot.transform, false);
                block.transform.localPosition = new Vector3(Mathf.Cos(angle) * r, 0.5f, Mathf.Sin(angle) * r);
                block.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
                block.transform.localScale = new Vector3(0.4f, 1.2f, segmentWidth);
                block.GetComponent<Renderer>().sharedMaterial = wallMat;
                UnityEngine.Object.DestroyImmediate(block.GetComponent<Collider>());
            }

            // --- camera: straight down, framing the whole arena ---
            var cameraGo = new GameObject("ArenaCamera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.position = new Vector3(0f, 20f, 0f);
            cameraGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var cam = cameraGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = r * 1.15f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.05f, 0.07f);
            cameraGo.AddComponent<AudioListener>();

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

            var focus = cameraGo.AddComponent<PlanningFocusCamera>();
            focus.Controller = controller;

            controller.Shake = cameraGo.AddComponent<CameraShake>();

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
            scaler.referenceResolution = new Vector2(1920f, 1080f);
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
