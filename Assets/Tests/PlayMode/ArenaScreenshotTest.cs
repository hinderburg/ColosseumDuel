using System.Collections;
using System.IO;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay;
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
        private const int Width = 1280;
        private const int Height = 720;

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

            yield return Capture(SuffixPath("-pick"));

            controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.1f);

            // Jump the cycle counter so the frame also shows the closing danger rings, which would
            // otherwise take seven real cycles (~35 seconds) to appear.
            controller.Manager.State.Cycle = 8;

            // Hold a full-power pull aimed at the far wall, so the captured frame shows the
            // trajectory preview including its bounce.
            var player = controller.Manager.State.P1.Active;
            var aim = new Vector2(-0.707f, 0.707f);
            input.TryBeginDrag(player.Pos);
            input.UpdateDrag(player.Pos - aim * GameConstants.MaxDragVirtual);
            yield return null;

            yield return Capture(OutputPath());

            canvas.renderMode = originalMode;
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

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                t += Time.deltaTime;
            }
        }
    }
}
