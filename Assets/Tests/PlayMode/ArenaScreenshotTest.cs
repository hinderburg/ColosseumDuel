using System.Collections;
using System.IO;
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
    /// Renders a frame of a live match to a PNG so the arena can be inspected without opening the
    /// Editor - useful when iterating headlessly, and as a cheap visual artifact for CI.
    ///
    /// Opt-in: set COLOSSEUM_SCREENSHOT to an output path and run WITHOUT -nographics
    /// (a null graphics device cannot render). Without the variable the test still exercises the
    /// render path and asserts the camera produced a non-empty image.
    /// </summary>
    public class ArenaScreenshotTest
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";
        private const int Width = 576;
        private const int Height = 1024;

        [UnityTest]
        public IEnumerator ArenaRendersAFrame()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device (running with -nographics); nothing to render.");

            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            var controller = Object.FindFirstObjectByType<GameController>();
            var input = Object.FindFirstObjectByType<PlayerInputController>();
            Assert.IsNotNull(controller);
            Assert.IsNotNull(input);

            // A Screen Space - Overlay canvas draws straight to the display and never appears in a
            // camera's target texture. The game ships in Overlay mode; switching to Camera mode just
            // for the capture composites the same layout into the frame we read back.
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var originalMode = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            canvas.planeDistance = 1f;

            // The menu is what the game actually opens on, so it gets a frame of its own - and so
            // does the roster screen, which is the only place the weapon badges are ever read.
            var menu = Object.FindFirstObjectByType<ColosseumDuel.Gameplay.Hud.MenuView>();
            yield return Capture(SuffixPath("-menu"));

            menu.OpenRosterScreen();
            yield return null;
            yield return Capture(SuffixPath("-roster"));

            menu.StartMatch();
            yield return null;
            yield return Capture(SuffixPath("-pick"));

            controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.1f);

            // Jump the cycle counter so the frame also shows the closing danger rings, which would
            // otherwise take seven real cycles (~35 seconds) to appear.
            controller.Manager.State.Cycle = 8;

            // The spikes come up out of the sand rather than snapping into place, so the frame has
            // to be taken a moment after the rings light up or it catches them still underground.
            yield return RunSeconds(0.8f);

            var player = controller.Manager.State.P1.Active;

            // Armed, so the frame also shows the carried gear in his hands - the sword and the
            // shield are only visible when someone is actually holding them, and a capture of an
            // unarmed fighter says nothing about whether they attach where they should.
            player.Weapon = WeaponKind.SwordAndShield;
            player.WeaponIsGilded = true;
            controller.Manager.State.Bot.Active.Weapon = WeaponKind.TwoHandedMace;

            // A few rounds' worth of blood on the sand. The stains are the one thing on the arena
            // that is meant to build up over a whole match, so a frame taken at cycle eight of round
            // one shows none of what they are for.
            foreach (var spot in new[]
                     {
                         new Vector2(-70f, 40f), new Vector2(30f, -80f), new Vector2(90f, 120f),
                         new Vector2(-40f, -160f), new Vector2(10f, 200f), new Vector2(-120f, -60f),
                     })
                controller.Arena.PlayBlood(spot);

            // Long enough for the bursts to finish. A blow throws bright particles at chest height
            // and leaves a dark mark on the sand, and a frame taken while the particles are still in
            // the air is a frame of the particles - they sit directly over the marks and are the
            // brighter of the two by a long way. The world is running at a fifth of its speed here, so
            // this is under a second of it.
            yield return RunSeconds(4f);

            // Back to a planning phase before anything is aimed. The waits above are long enough to
            // cross a cycle boundary, and a preview drawn during the action phase is not drawn at
            // all - which is how this frame came back with an empty arena and nothing to say so.
            yield return RunUntil(() => controller.Manager.State.Phase == MatchPhase.Planning, 10f);

            // An order in, so the captured frame shows the lane the run is previewed as and the head
            // on the end of it. Given through the control the game ships on, so the frame is a frame
            // of what a player sees rather than of a scheme they would have to go and choose.
            input.Scheme = ControlScheme.Tap;
            var runTo = player.Pos + new Vector2(-0.707f, 0.707f) * GameConstants.TutorialRunLength;
            input.TapTo(input.ArenaCamera.WorldToScreenPoint(controller.Arena.ToWorld(runTo)));
            yield return null;

            // A couple of damage numbers in the air, so the frame carries the one part of the HUD
            // that only exists for a second at a time and can never be caught by waiting for it.
            var numbers = Object.FindFirstObjectByType<ColosseumDuel.Gameplay.Hud.DamageNumbersView>();
            if (numbers != null)
            {
                numbers.Show(player.Pos, 34f, ColosseumDuel.Gameplay.Hud.DamageNumbersView.Source.Blow);
                numbers.Show(controller.Manager.State.Bot.Active.Pos, 65f,
                    ColosseumDuel.Gameplay.Hud.DamageNumbersView.Source.Spikes);
                yield return null;
            }

            yield return Capture(OutputPath());

            canvas.renderMode = originalMode;
        }

        /// <summary>
        /// One frame per archetype, each holding each weapon.
        ///
        /// Everything about a gladiator that can only be judged by eye is in these: whether the
        /// helmet is on his head at his own build, and whether he is holding the sword by the hilt
        /// rather than by the point. Both have been wrong at some stage and both looked fine in
        /// every assertion that could be written about them.
        /// </summary>
        [UnityTest]
        public IEnumerator EachArchetypeRendersAFrame()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device (running with -nographics); nothing to render.");

            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            var controller = Object.FindFirstObjectByType<GameController>();

            foreach (var def in GladiatorDef.All)
            {
                controller.RestartMatch();
                yield return null;

                controller.SubmitPlayerPick(def.Id);
                yield return RunSeconds(GameConstants.RevealTime + 0.2f);

                // Nose to nose in the middle, so the pair fills the frame at the fixed camera
                // distance and a head is more than a dozen pixels across.
                var player = controller.Manager.State.P1.Active;
                var bot = controller.Manager.State.Bot.Active;
                player.Pos = new Vector2(-46f, -30f);
                bot.Pos = new Vector2(46f, -30f);
                // Each in the weapon he trained on, so the three frames between them cover all
                // three loadouts rather than showing the same pair three times.
                player.Weapon = def.SkilledWith;
                bot.Weapon = WeaponKind.SwordAndShield;
                yield return null;

                yield return Capture(SuffixPath($"-{def.Id}"));
            }
        }

        /// <summary>
        /// The planned run, close up and across open sand.
        ///
        /// The lane and the head on the end of it are small in a frame of the whole arena, and
        /// judging whether the head sits where it should from one of those is guesswork. This puts
        /// the run across the middle of the arena with nothing else in the way and brings the camera
        /// in on it.
        /// </summary>
        [UnityTest]
        public IEnumerator ThePlannedRunRendersAFrame()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device (running with -nographics); nothing to render.");

            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            var controller = Object.FindFirstObjectByType<GameController>();
            var input = Object.FindFirstObjectByType<PlayerInputController>();
            var camera = Camera.main;

            var cameraDriver = camera.GetComponent<DeathCameraView>();
            if (cameraDriver != null) cameraDriver.enabled = false;

            controller.SubmitPlayerPick(GladiatorId.Hilius);
            yield return RunSeconds(GameConstants.RevealTime + 0.3f);

            var player = controller.Manager.State.P1.Active;
            var bot = controller.Manager.State.Bot.Active;

            // Straight up the arena from the near end, so the whole run is on open sand and the
            // camera does not have to look at it end-on.
            player.Pos = new Vector2(0f, -230f);
            bot.Pos = new Vector2(200f, 260f);
            yield return null;

            // Through the control the game ships on, so the frame shows what a player sees.
            input.Scheme = ControlScheme.Tap;
            var target = player.Pos + new Vector2(0f, GameConstants.TutorialRunLength);
            input.TapTo(camera.WorldToScreenPoint(controller.Arena.ToWorld(target)));
            yield return null;

            FrameOnPair(camera, controller.Arena, new Vector2(0f, -60f), 320f, 0f);
            yield return null;

            yield return Capture(SuffixPath("-run"));

            if (cameraDriver != null) cameraDriver.enabled = true;
        }

        /// <summary>
        /// A run round a column: the tap is on the far side of it, and the lane has to bend round
        /// the stone to get there. A run across open sand looks as it always did, which is why this
        /// frame exists alongside that one.
        /// </summary>
        [UnityTest]
        public IEnumerator TheRoutedRunRendersAFrame()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device (running with -nographics); nothing to render.");

            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            var controller = Object.FindFirstObjectByType<GameController>();
            var input = Object.FindFirstObjectByType<PlayerInputController>();
            var camera = Camera.main;

            var cameraDriver = camera.GetComponent<DeathCameraView>();
            if (cameraDriver != null) cameraDriver.enabled = false;

            controller.SubmitPlayerPick(GladiatorId.Barbarius);
            yield return RunSeconds(GameConstants.RevealTime + 0.3f);

            var player = controller.Manager.State.P1.Active;
            var bot = controller.Manager.State.Bot.Active;

            // Just below the first column, and the tap just above it: the straight line between
            // goes through the stone, so the lane has to go round.
            Obstacle column = null;
            foreach (var o in controller.Manager.State.Obstacles.Obstacles)
                if (o.Kind == ObstacleKind.Column) { column = o; break; }
            Assert.IsNotNull(column, "the arena has no columns to run round");

            player.Pos = column.Centre + new Vector2(8f, -70f);
            bot.Pos = column.Centre + new Vector2(170f, 90f);
            yield return null;

            input.Scheme = ControlScheme.Tap;
            var target = column.Centre + new Vector2(-8f, 70f);
            input.TapTo(camera.WorldToScreenPoint(controller.Arena.ToWorld(target)));
            yield return null;

            // FrameOnPair centres on the middle of the span it is given, measured rightwards from
            // the point passed in - it was built for the reach frames, where the subject stands at
            // the left edge. Shifted back by half the span so the run sits in the middle instead.
            const float span = 260f;
            var middle = (player.Pos + target) * 0.5f - new Vector2(span * 0.5f, 0f);
            FrameOnPair(camera, controller.Arena, middle, span, 0f);
            yield return null;

            yield return Capture(SuffixPath("-routed"));

            if (cameraDriver != null) cameraDriver.enabled = true;
        }

        /// <summary>
        /// One frame per weapon: a fighter mid-swing, the ring at the distance that weapon is
        /// allowed to strike from, and an opponent standing exactly on it.
        ///
        /// The ring is WeaponDef.Reach itself, centre to centre, drawn at the scale the arena is
        /// drawn at. Whether a blade covers the ground it looks like it covers is the one thing
        /// about the reach numbers that cannot be read off them, and this is the only place it can
        /// be seen; the measured extents go to the log beside the frame.
        /// </summary>
        [UnityTest]
        public IEnumerator WeaponReachRendersAFrame()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device (running with -nographics); nothing to render.");

            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            var controller = Object.FindFirstObjectByType<GameController>();
            var camera = Camera.main;
            var cameraPos = camera.transform.position;
            var cameraRot = camera.transform.rotation;

            // The camera writes its own position every LateUpdate to drift home after a death, so
            // moving it from here without switching that off lasts exactly no frames.
            var cameraDriver = camera.GetComponent<DeathCameraView>();
            if (cameraDriver != null) cameraDriver.enabled = false;

            foreach (var weapon in WeaponDef.All)
            {
                controller.RestartMatch();
                yield return null;

                // Each weapon on the archetype trained in it, at his own build: anyone else holding
                // it is drawn inside the red untrained shell, which is a different thing to look at.
                controller.SubmitPlayerPick(OwnerOf(weapon.Kind));
                yield return RunSeconds(GameConstants.RevealTime + 0.2f);

                var player = controller.Manager.State.P1.Active;
                var bot = controller.Manager.State.Bot.Active;
                player.Weapon = weapon.Kind;

                // The opponent is here as a distance marker and nothing else: armed, he would be
                // holding a weapon he is not trained in, and the red shell that warns about that is
                // a second red circle in a frame whose whole subject is a red circle.
                bot.Weapon = WeaponKind.None;

                // Set apart along X, the axis a camera at sixty-six degrees does not foreshorten, so
                // the rings are read across their widest diameter and the two stand level.
                var centre = new Vector2(0f, -40f);
                player.Pos = centre;
                bot.Pos = centre + new Vector2(weapon.Reach, 0f);
                yield return null;

                // Mid-swing, because a weapon at rest hangs down the body and says nothing about how
                // far it covers. This is the pose the reach is supposed to describe.
                PlayerView().PlaySwing();
                yield return RunSeconds(0.18f);

                float tip = MeasureWeaponExtent(controller.Arena, weapon);
                var rings = DrawReachRings(controller.Arena, centre, weapon.Reach, tip);
                FrameOnPair(camera, controller.Arena, centre, weapon.Reach, tip);
                yield return null;

                yield return Capture(SuffixPath($"-reach-{weapon.Kind}"));

                Object.DestroyImmediate(rings);
            }

            camera.transform.SetPositionAndRotation(cameraPos, cameraRot);
            if (cameraDriver != null) cameraDriver.enabled = true;
        }

        /// <summary>
        /// The three circles around a fighter that the question is about: the body the simulation
        /// collides with, the distance his weapon is allowed to strike from, and the distance the
        /// weapon in his fist actually reaches to. The last two are the comparison.
        /// </summary>
        private static GameObject DrawReachRings(ArenaView arena, Vector2 centreVirtual,
            float reachVirtual, float tipVirtual)
        {
            var root = new GameObject("ReachRings");
            root.transform.position = arena.ToWorld(centreVirtual, 0.06f);

            float band = arena.ScaleLength(2.6f);
            AddRing(root, arena.ScaleLength(GameConstants.GladiatorRadius), band, arena.Palette.BarBackground);
            AddRing(root, arena.ScaleLength(reachVirtual), band, arena.Palette.HazardActive);
            if (tipVirtual > 0f)
                AddRing(root, arena.ScaleLength(tipVirtual), band, arena.Palette.PlayerHelmet);
            return root;
        }

        /// <summary>Which archetype carries this weapon as his own.</summary>
        private static GladiatorId OwnerOf(WeaponKind kind)
        {
            foreach (var def in GladiatorDef.All)
                if (def.SkilledWith == kind) return def.Id;
            return GladiatorId.Barbarius;
        }

        private static void AddRing(GameObject parent, float radius, float band, Material material)
        {
            var ring = new GameObject($"Ring_{radius:0.00}");
            ring.transform.SetParent(parent.transform, false);
            ring.AddComponent<MeshFilter>().sharedMesh =
                ViewPrimitives.CreateAnnulus(Mathf.Max(0.01f, radius - band), radius, 96);
            var renderer = ring.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// Pulls the camera in until everything the frame is about fits: both rings, whichever is
        /// the larger, and the opponent standing out at the reach.
        ///
        /// Framed on the span rather than on the pair. The outer ring is centred on the fighter and
        /// the opponent is off to one side of him, so the two are not centred on the same point, and
        /// aiming at either of them alone runs the other off the edge.
        /// </summary>
        private static void FrameOnPair(Camera camera, ArenaView arena, Vector2 centreVirtual,
            float reachVirtual, float tipVirtual)
        {
            const float pitch = 66f;   // the arena camera's own angle, so the frame reads like the game

            float right = Mathf.Max(reachVirtual + GameConstants.GladiatorRadius, tipVirtual);
            float left = -Mathf.Max(tipVirtual, GameConstants.GladiatorRadius);
            const float margin = 1.12f;
            float halfExtent = arena.ScaleLength((right - left) * 0.5f * margin);

            float halfVertical = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfHorizontal = Mathf.Atan(Mathf.Tan(halfVertical) * Width / (float)Height);
            float distance = halfExtent / Mathf.Tan(halfHorizontal);

            var target = arena.ToWorld(centreVirtual + new Vector2((left + right) * 0.5f, 0f), 1.2f);
            var forward = Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward;
            camera.transform.SetPositionAndRotation(target - forward * distance,
                Quaternion.Euler(pitch, 0f, 0f));
        }

        private static GladiatorView PlayerView()
            => System.Array.Find(Object.FindObjectsByType<GladiatorView>(FindObjectsSortMode.None),
                v => v.name == "Player");

        /// <summary>
        /// How far the weapon in his fist actually sticks out, measured off the meshes that are on
        /// screen rather than derived from the numbers that placed them.
        ///
        /// Taken from each mesh's own local bounds through its own transform, not from the
        /// world-axis-aligned bounds: a blade held at an angle fills a box far wider than itself,
        /// and that box would report a reach the weapon does not have.
        /// </summary>
        private static float MeasureWeaponExtent(ArenaView arena, WeaponDef weapon)
        {
            var view = PlayerView();
            if (view == null) return 0f;

            var centre = view.transform.position;
            float furthest = 0f;
            foreach (var filter in view.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || !IsHeldWeapon(filter.transform)) continue;

                var bounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var local = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                    var world = filter.transform.TransformPoint(local);
                    float flat = new Vector2(world.x - centre.x, world.z - centre.z).magnitude;
                    if (flat > furthest) furthest = flat;
                }
            }

            float tipVirtual = furthest / arena.VirtualToWorld;
            Debug.Log($"[Colosseum] reach check {weapon.Kind}: " +
                      $"reach {weapon.Reach:0.0} centre-to-centre, " +
                      $"so the enemy's near edge sits {weapon.Reach - GameConstants.GladiatorRadius:0.0} out; " +
                      $"the weapon itself reaches {tipVirtual:0.0} out from his own centre " +
                      $"(body radius {GameConstants.GladiatorRadius:0.0}, all in virtual units)");
            return tipVirtual;
        }

        private static bool IsHeldWeapon(Transform t)
        {
            for (var node = t; node != null; node = node.parent)
                if (node.name == "HeldWeapon" || node.name == "HeldOffHand") return true;
            return false;
        }

        /// <summary>
        /// The drawn run as the player sees it: the line widening from his feet into the arrow head,
        /// round a bend the way a finger draws one. The look is copied from a reference and worth an eye.
        /// </summary>
        [UnityTest]
        public IEnumerator TapControlRendersAFrame()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device (running with -nographics); nothing to render.");

            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            var controller = Object.FindFirstObjectByType<GameController>();
            var input = Object.FindFirstObjectByType<PlayerInputController>();
            input.Scheme = ControlScheme.Draw;

            var canvas = Object.FindFirstObjectByType<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            canvas.planeDistance = 1f;

            // Past the menu, or the frame meant to show the control shows the title screen over it -
            // which is what it did, for as long as this frame has existed.
            Object.FindFirstObjectByType<ColosseumDuel.Gameplay.Hud.MenuView>().StartMatch();
            yield return null;

            controller.SubmitPlayerPick(GladiatorId.Hilius);
            yield return RunSeconds(GameConstants.RevealTime + 0.1f);

            // Up the open middle and bending off to one side, the way a finger draws a run, clear of
            // the columns - so the frame shows the widening line, its bend, and the head turned along it.
            var player = controller.Manager.State.P1.Active;
            input.BeginDraw(player.Pos);
            for (int i = 1; i <= 12; i++)
            {
                float t = i / 12f;
                input.DrawTo(player.Pos + new Vector2(-Mathf.Sin(t * 1.4f) * 50f, t * 260f));
            }
            input.EndDraw();
            yield return null;

            yield return Capture(SuffixPath("-tap"));
        }

        /// <summary>Captures the moment an ability fires, so the burst effect can be eyeballed.</summary>
        [UnityTest]
        public IEnumerator AbilityBurstRendersAFrame()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device (running with -nographics); nothing to render.");

            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            var controller = Object.FindFirstObjectByType<GameController>();
            controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.1f);

            var canvas = Object.FindFirstObjectByType<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            canvas.planeDistance = 1f;

            controller.Manager.State.P1.Active.Rage = GameConstants.RageMax;
            controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, useAbility: true);
            controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);

            // Late enough in the expansion that the ring has cleared the gladiator's own body -
            // early on it is smaller than he is and hides behind him.
            yield return RunSeconds(GameConstants.PlanningTime + 0.16f);

            yield return Capture(SuffixPath("-burst"));
        }

        private static IEnumerator Capture(string outputPath)
        {
            var camera = Camera.main;
            Assert.IsNotNull(camera, "the Arena scene needs a MainCamera");

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                // Let the render pipeline draw into the texture on its normal frame loop. Calling
                // camera.Render() by hand is a Built-in pipeline idiom; under URP it bypasses the
                // pipeline's own setup and comes out wrong. Two plain frames rather than
                // WaitForEndOfFrame, which never fires in batchmode.
                camera.targetTexture = target;
                yield return null;
                yield return null;
                camera.targetTexture = null;

                var previous = RenderTexture.active;
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                readback.Apply();
                RenderTexture.active = previous;

                Assert.IsTrue(HasVisibleContent(readback),
                    "the rendered frame is a flat colour - the camera is pointing at nothing, " +
                    "or every material failed to render");

                if (!string.IsNullOrEmpty(outputPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
                    File.WriteAllBytes(outputPath, readback.EncodeToPNG());
                    Debug.Log($"[Colosseum] Arena screenshot written to {outputPath}");
                }
            }
            finally
            {
                Object.DestroyImmediate(readback);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static string OutputPath()
            => System.Environment.GetEnvironmentVariable("COLOSSEUM_SCREENSHOT");

        private static string SuffixPath(string suffix)
        {
            string path = OutputPath();
            if (string.IsNullOrEmpty(path)) return null;
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            return Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + suffix + Path.GetExtension(path));
        }

        /// <summary>A frame that is one uniform colour means nothing actually drew.</summary>
        private static bool HasVisibleContent(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            var first = pixels[0];
            foreach (var p in pixels)
                if (p.r != first.r || p.g != first.g || p.b != first.b) return true;
            return false;
        }

        private static IEnumerator RunUntil(System.Func<bool> done, float timeout)
        {
            float t = 0f;
            while (t < timeout && !done())
            {
                yield return null;
                t += Time.unscaledDeltaTime;
            }
        }

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                t += Time.unscaledDeltaTime;
            }
        }
    }
}
