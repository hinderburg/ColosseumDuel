using System.Collections;
using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The arena is dressed at runtime from the modular stone kit, and falls back to painted blocks
    /// where the kit is not installed. Both paths have to leave a wall standing on the ellipse.
    /// </summary>
    public class ArenaDecorTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            _controller = Object.FindFirstObjectByType<GameController>();
            Assert.IsNotNull(_controller);
        }

        private Transform Find(string name)
            => _controller.Arena.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == name);

        [Test]
        public void TheWallStandsOnTheEllipseAndCloses()
        {
            var wall = Find("ArenaWall");
            Assert.IsNotNull(wall, "no wall was built");

            var segments = wall.Cast<Transform>()
                .Where(t => t.name.StartsWith("Segment_"))
                .ToList();
            Assert.Greater(segments.Count, 20, "the wall is too coarse to read as a curve");

            foreach (var segment in segments)
            {
                // Measured on the inner face, which is where the simulation bounces. A block centred
                // on the line would eat half its own depth of playable floor - a gap that only shows
                // up as fighters clipping into stone.
                var onFloor = new Vector2(segment.localPosition.x / _controller.Arena.WorldRadiusX,
                                          segment.localPosition.z / _controller.Arena.WorldRadiusZ);
                Assert.AreEqual(1f, onFloor.magnitude, 0.08f,
                    $"{segment.name} is not on the wall line");
            }
        }

        [Test]
        public void TheWallHasNoGaps()
        {
            var wall = Find("ArenaWall");
            var segments = wall.Cast<Transform>()
                .Where(t => t.name.StartsWith("Segment_"))
                .ToList();

            // Both wall meshes are a unit long, so a block covers exactly its own x scale. Neighbours
            // further apart than that leave a hole - which is what equal-angle spacing produces on an
            // oval, and what laying blocks out on the inner ellipse and then pushing them outwards
            // produces at its ends. On screen it shows as daylight through the wall.
            for (int i = 0; i < segments.Count; i++)
            {
                var here = segments[i];
                var next = segments[(i + 1) % segments.Count];
                float gap = Vector2.Distance(new Vector2(here.localPosition.x, here.localPosition.z),
                                             new Vector2(next.localPosition.x, next.localPosition.z));

                Assert.LessOrEqual(gap, here.localScale.x,
                    $"{here.name} and {next.name} are {gap:0.000} apart, wider than the " +
                    $"{here.localScale.x:0.000} block meant to span it");
            }
        }

        [Test]
        public void EveryTorchStandsOnAPost()
        {
            var posts = Find("ArenaWall")?.Cast<Transform>()
                .Where(t => t.name.StartsWith("Post_"))
                .ToList();

            if (posts == null || posts.Count == 0)
            {
                Assert.Ignore("No posts - the modular arena kit is not imported here.");
                return;
            }

            var torchRoot = Find("Torches");
            if (torchRoot == null)
            {
                Assert.Ignore("No torches - Epic Toon FX is not imported here.");
                return;
            }

            // Both rings are laid by the same equal-arc walk, so each flame should land on top of a
            // post rather than on bare parapet. They are separate rings built by separate code, and
            // this is what keeps them agreeing.
            foreach (Transform torch in torchRoot)
            {
                var flame = new Vector2(torch.localPosition.x, torch.localPosition.z);
                float nearest = posts.Min(p => (new Vector2(p.localPosition.x, p.localPosition.z) - flame).magnitude);
                Assert.Less(nearest, 0.3f, $"{torch.name} is burning {nearest:0.00} away from any post");
            }
        }
    }
}
