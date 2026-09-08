using System.Collections.Generic;
using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// The single place where the simulation's virtual 2D space is converted to world space, plus
    /// the visuals that belong to the arena itself (the shrinking danger rings).
    ///
    /// The simulation is a top-down plane, so virtual (x, y) maps to world (x, 0, y) - the same
    /// convention the original web build used. Nothing else in the project should do this
    /// conversion by hand.
    /// </summary>
    public sealed class ArenaView : MonoBehaviour
    {
        [Tooltip("World radius of the arena floor. GameConstants.ArenaRadius virtual units map onto this.")]
        public float WorldArenaRadius = 8f;

        public ViewPalette Palette;

        [Tooltip("The fixed arena camera. World-space labels billboard towards it.")]
        public Camera ArenaCamera;

        [Tooltip("Height above the floor at which the danger rings are drawn, to avoid z-fighting.")]
        public float HazardRingHeight = 0.03f;

        // --- how the props are laid out ---
        // The props themselves live in the scene and are edited there. These are the figures the
        // layout is generated from, kept here so that regenerating it (Tools > Colosseum > Rebuild
        // arena props) starts from what the arena is actually set to rather than from a constant
        // buried in an Editor script.

        [Tooltip("Top of the wall. The torches burn here, on the posts the decor puts under them.")]
        public float WallHeight = 1.2f;

        [Tooltip("Depth of the stone tier behind the wall.")]
        public float GalleryDepth = 4.5f;

        [Tooltip("How many torches to space around the wall.")]
        public int TorchCount = 14;

        public float VirtualToWorld => WorldArenaRadius / GameConstants.ArenaRadius;

        /// <summary>World semi-axis across the screen.</summary>
        public float WorldRadiusX => WorldArenaRadius;

        /// <summary>World semi-axis up the screen - the long one. The elongation already lives in
        /// the simulation's coordinates, so the virtual-to-world scale stays uniform.</summary>
        public float WorldRadiusZ => WorldArenaRadius * GameConstants.ArenaElongation;

        public Vector3 ToWorld(Vector2 virtualPos, float height = 0f)
            => new Vector3(virtualPos.x * VirtualToWorld, height, virtualPos.y * VirtualToWorld);

        public Vector2 ToVirtual(Vector3 worldPos)
            => new Vector2(worldPos.x, worldPos.z) / VirtualToWorld;

        /// <summary>Converts a virtual length (a radius, a distance) to world units.</summary>
        public float ScaleLength(float virtualLength) => virtualLength * VirtualToWorld;

        private readonly List<Renderer> _hazardRings = new List<Renderer>();
        private MaterialPropertyBlock _ringProperties;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public void BuildHazardRings()
        {
            foreach (var ring in _hazardRings)
                if (ring != null) Destroy(ring.gameObject);
            _hazardRings.Clear();
            _ringProperties = new MaterialPropertyBlock();

            var root = new GameObject("HazardRings");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, HazardRingHeight, 0f);

            foreach (var stage in HazardSystem.Schedule)
            {
                var go = new GameObject($"Ring_{stage.InnerFraction:0.00}-{stage.OuterFraction:0.00}");
                go.transform.SetParent(root.transform, false);

                // Built as a unit ring and stretched onto the arena's ellipse, so a stage at 0.75 is
                // the same fraction of the way to the wall in every direction - matching how
                // HazardSystem actually measures it.
                float inner = Mathf.Max(stage.InnerFraction, 0.001f);
                float outer = stage.OuterFraction;

                go.AddComponent<MeshFilter>().sharedMesh = ViewPrimitives.CreateAnnulus(inner, outer);
                go.transform.localScale = new Vector3(WorldRadiusX, 1f, WorldRadiusZ);
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
                _hazardRings.Add(renderer);
            }
        }


        // ------------------------------------------------------------------
        // spikes: what the danger zone is made of
        // ------------------------------------------------------------------

        [Tooltip("How many spikes are scattered across the arena floor.")]
        public int SpikeCount = 150;

        /// <summary>How fast a spike travels between hidden and standing, in world units a second.</summary>
        private const float SpikeRiseSpeed = 4f;

        private readonly List<Transform> _spikes = new List<Transform>();
        private readonly List<float> _spikeDistance = new List<float>();
        private float _spikeHeight;

        /// <summary>
        /// Scatters the spikes over the whole floor, sunk out of sight.
        ///
        /// Every one of them remembers how far out it sits in the arena's own normalized units -
        /// the same units the hazard schedule is written in - so deciding whether it should be up
        /// is a comparison against that schedule rather than a second copy of the ring geometry.
        ///
        /// A fixed scatter built once, not spawned as the rings light up: a hundred and fifty
        /// meshes appearing mid-cycle is a hitch at exactly the moment the player is trying to run
        /// somewhere, and they cost nothing sitting under the floor.
        /// </summary>
        public void BuildSpikes()
        {
            if (Palette == null || Palette.Spike == null) return;

            var root = new GameObject("Spikes");
            root.transform.SetParent(transform, false);

            float radius = ScaleLength(GameConstants.GladiatorRadius) * 0.30f;
            _spikeHeight = ScaleLength(GameConstants.GladiatorRadius) * 1.15f;
            var mesh = ViewPrimitives.CreateCone(radius, _spikeHeight);

            // Deterministic, so the field is the same shape every run and a screenshot of it means
            // something. The arena's own randomness belongs to the simulation, not to scenery.
            var rng = new System.Random(20260907);

            for (int i = 0; i < SpikeCount; i++)
            {
                // Square root of a uniform draw, or they all bunch towards the middle - the same
                // correction the item spawns use.
                float d = Mathf.Sqrt((float)rng.NextDouble());
                float a = (float)(rng.NextDouble() * System.Math.PI * 2.0);
                var onUnitCircle = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;

                var spike = ViewPrimitives.Create(mesh, $"Spike_{i:000}", root.transform, Palette.Spike);
                spike.transform.localPosition = new Vector3(
                    onUnitCircle.x * WorldRadiusX, -_spikeHeight, onUnitCircle.y * WorldRadiusZ);

                // A field of identical cones in identical poses reads as a texture rather than as
                // iron; a little variation in size and spin is enough to break that up.
                float scale = Mathf.Lerp(0.75f, 1.25f, (float)rng.NextDouble());
                spike.transform.localScale = new Vector3(scale, scale, scale);
                spike.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

                _spikes.Add(spike.transform);
                _spikeDistance.Add(d);
            }
        }

        /// <summary>
        /// Raises the spikes standing in ground that is currently dangerous, and lowers the rest.
        ///
        /// Moved towards a target rather than switched: they are meant to come up out of the sand,
        /// and the moment a ring lights up is the moment the player most needs to notice it.
        /// </summary>
        private void SyncSpikes(MatchState state)
        {
            if (_spikes.Count == 0) return;

            for (int i = 0; i < _spikes.Count; i++)
            {
                // The same test the simulation uses to decide whether it is burning someone, asked
                // at the spike's own place on the floor - so what is up is exactly what hurts.
                bool dangerous = HazardSystem.IsInActiveHazard(
                    ArenaShape.FromUnitCircle(DirectionOf(i) * _spikeDistance[i]), state.Cycle);

                var position = _spikes[i].localPosition;
                float target = dangerous ? 0f : -_spikeHeight;
                if (Mathf.Approximately(position.y, target)) continue;

                position.y = Mathf.MoveTowards(position.y, target, SpikeRiseSpeed * Time.deltaTime);
                _spikes[i].localPosition = position;
            }
        }

        /// <summary>The unit-circle direction a spike sits on, recovered from where it was placed.</summary>
        private Vector2 DirectionOf(int index)
        {
            var p = _spikes[index].localPosition;
            var onCircle = new Vector2(p.x / WorldRadiusX, p.z / WorldRadiusZ);
            return onCircle.sqrMagnitude > 1e-6f ? onCircle.normalized : Vector2.right;
        }

        // ------------------------------------------------------------------
        // traps
        // ------------------------------------------------------------------

        private readonly List<GameObject> _trapProps = new List<GameObject>();

        /// <summary>
        /// Builds one prop per trap slot: a dark iron ring with teeth around its rim.
        ///
        /// A ring rather than a disc, and teeth rather than a flat marker, because it has to read as
        /// something that bites from directly above - which is the only angle it is ever seen from.
        /// </summary>
        public void BuildTraps()
        {
            if (Palette == null) return;

            var root = new GameObject("Traps");
            root.transform.SetParent(transform, false);

            float radius = ScaleLength(GameConstants.TrapRadius);
            var jawMesh = ViewPrimitives.CreateCone(radius * 0.16f, radius * 0.55f, 6);

            for (int i = 0; i < GameConstants.TrapCount; i++)
            {
                var trap = new GameObject($"Trap_{i:00}");
                trap.transform.SetParent(root.transform, false);

                var plate = ViewPrimitives.Create(ViewPrimitives.CreateAnnulus(radius * 0.45f, radius, 24),
                    "Plate", trap.transform, Palette.TrapIron);
                plate.transform.localPosition = new Vector3(0f, HazardRingHeight, 0f);

                const int teeth = 8;
                for (int t = 0; t < teeth; t++)
                {
                    float a = t / (float)teeth * Mathf.PI * 2f;
                    var tooth = ViewPrimitives.Create(jawMesh, $"Tooth_{t}", trap.transform, Palette.TrapIron);
                    tooth.transform.localPosition =
                        new Vector3(Mathf.Cos(a) * radius * 0.78f, 0f, Mathf.Sin(a) * radius * 0.78f);

                    // Leaning inwards, towards whatever steps into the middle of them.
                    tooth.transform.localRotation = Quaternion.Euler(
                        Mathf.Cos(a) * 34f, 0f, -Mathf.Sin(a) * 34f);
                }

                trap.SetActive(false);
                _trapProps.Add(trap);
            }
        }

        /// <summary>Puts each prop where its trap is, and hides the ones already sprung.</summary>
        private void SyncTraps(MatchState state)
        {
            var traps = state.Traps?.Traps;
            for (int i = 0; i < _trapProps.Count; i++)
            {
                bool visible = traps != null && i < traps.Count && traps[i].Armed;
                if (_trapProps[i].activeSelf != visible) _trapProps[i].SetActive(visible);
                if (visible) _trapProps[i].transform.localPosition = ToWorld(traps[i].Pos);
            }
        }

        [Tooltip("How many blood bursts can overlap before the oldest is reused.")]
        public int BloodPoolSize = 6;

        private readonly List<GameObject> _bloodPool = new List<GameObject>();
        private int _nextBlood;

        /// <summary>
        /// Pre-instantiates the blood bursts.
        ///
        /// A pool rather than instantiate-and-destroy per hit: with Mongoose landing twice a cycle
        /// and both sides trading blows simultaneously, spawning would allocate several particle
        /// hierarchies a second during a fight - exactly when the frame budget matters most.
        /// </summary>
        public void BuildBloodPool()
        {
            if (Palette == null || Palette.BloodHit == null) return;

            var root = new GameObject("BloodBursts");
            root.transform.SetParent(transform, false);

            for (int i = 0; i < BloodPoolSize; i++)
            {
                var burst = Instantiate(Palette.BloodHit, root.transform);
                burst.name = $"Blood_{i:00}";
                burst.SetActive(false);

                foreach (var particles in burst.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = particles.main;
                    main.useUnscaledTime = false;
                    main.playOnAwake = false;
                }

                _bloodPool.Add(burst);
            }
        }

        [Tooltip("How many blood stains the sand holds before the oldest is painted over.")]
        public int BloodStainCount = 48;

        /// <summary>How high above the floor a stain sits - under the danger rings, over the sand.</summary>
        private const float BloodStainHeight = 0.012f;

        private readonly List<Transform> _bloodStains = new List<Transform>();
        private int _nextStain;

        /// <summary>
        /// Pre-builds the stains the sand can hold.
        ///
        /// One quad each, all sharing a material, all disabled until something bleeds on them. They
        /// are never taken away during a match: a round is over when somebody falls, but the sand he
        /// fell on is the same sand, and by the third round it should look like it.
        ///
        /// Forty-eight of them, recycled oldest-first past that. A long match trades a dozen blows a
        /// round, so the wrap is far enough out that the arena reads as accumulating rather than as
        /// holding a fixed number of marks.
        /// </summary>
        public void BuildBloodStains()
        {
            foreach (var stain in _bloodStains)
                if (stain != null) Destroy(stain.gameObject);
            _bloodStains.Clear();
            _nextStain = 0;

            if (Palette == null || Palette.BloodStain == null || Palette.Quad == null) return;

            var root = new GameObject("BloodStains");
            root.transform.SetParent(transform, false);

            for (int i = 0; i < BloodStainCount; i++)
            {
                var stain = ViewPrimitives.CreateGroundQuad(
                    Palette.Quad, $"Stain_{i:00}", root.transform, Palette.BloodStain);

                var renderer = stain.GetComponent<Renderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                stain.SetActive(false);
                _bloodStains.Add(stain.transform);
            }
        }

        /// <summary>Wipes the sand clean. A new match, not a new round.</summary>
        public void ClearBloodStains()
        {
            foreach (var stain in _bloodStains)
                if (stain != null) stain.gameObject.SetActive(false);
            _nextStain = 0;
        }

        /// <summary>
        /// Leaves a mark on the sand where a blow landed.
        ///
        /// Turned and sized at random. The texture is one splat, so without this every stain in the
        /// arena would be the same shape at the same angle, and a dozen of them would read as a
        /// pattern rather than as a fight.
        /// </summary>
        private void StainSand(Vector2 virtualPosition)
        {
            if (_bloodStains.Count == 0) return;

            var stain = _bloodStains[_nextStain];
            _nextStain = (_nextStain + 1) % _bloodStains.Count;

            stain.localPosition = ToWorld(virtualPosition, BloodStainHeight);

            // Laid flat first, then spun about the world's up axis. Written as a composition rather
            // than as Euler(90, 0, angle): at ninety degrees of pitch that form is gimbal-locked, so
            // it turns the quad correctly and reads back as something else entirely - the angle
            // arrives in a component nobody put it in.
            stain.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
                                  * Quaternion.Euler(90f, 0f, 0f);

            float span = ScaleLength(GameConstants.GladiatorRadius) * Random.Range(1.5f, 2.4f);
            stain.localScale = new Vector3(span, span, 1f);

            stain.gameObject.SetActive(true);
        }

        /// <summary>Plays a blood burst where a blow landed, and leaves the mark it makes.</summary>
        public void PlayBlood(Vector2 virtualPosition)
        {
            StainSand(virtualPosition);

            if (_bloodPool.Count == 0) return;

            var burst = _bloodPool[_nextBlood];
            _nextBlood = (_nextBlood + 1) % _bloodPool.Count;

            // Chest height, not the floor - a burst at the feet reads as dust, not as a hit.
            burst.transform.localPosition = ToWorld(virtualPosition, ScaleLength(GameConstants.GladiatorRadius) * 1.3f);

            // Restart rather than merely enable: a pooled system that already ran is sitting at the
            // end of its lifetime and would show nothing at all on reuse.
            burst.SetActive(true);
            foreach (var particles in burst.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Clear(true);
                particles.Play(true);
            }
        }

        /// <summary>
        /// Shows the rings that are dealing damage right now, and - during Planning only - the one
        /// that will light up next cycle. The design calls for that stage to be telegraphed a cycle
        /// ahead so the player can plan a move out of it.
        /// </summary>
        public void Sync(MatchState state)
        {
            SyncSpikes(state);
            SyncTraps(state);

            if (_hazardRings.Count == 0 || Palette == null) return;

            var upcoming = HazardSystem.UpcomingStage(state.Cycle);
            bool telegraphing = state.Phase == MatchPhase.Planning && upcoming.HasValue;

            for (int i = 0; i < _hazardRings.Count && i < HazardSystem.Schedule.Count; i++)
            {
                var stage = HazardSystem.Schedule[i];
                var renderer = _hazardRings[i];

                bool active = state.Cycle >= stage.ActivateCycle;
                bool warned = telegraphing && stage.ActivateCycle == upcoming.Value.ActivateCycle;

                renderer.enabled = active || warned;
                if (!renderer.enabled) continue;

                var material = active ? Palette.HazardActive : Palette.HazardTelegraph;
                renderer.sharedMaterial = material;

                // Flat paint reads as decoration; a slow flicker reads as fire, which is what the
                // design asks for and what makes the ring feel dangerous rather than decorative.
                // Each ring gets its own phase so they do not breathe in lockstep, and the warning
                // ring pulses faster and harder to say "this one is not burning yet, but will be".
                float speed = active ? 2.4f : 5.5f;
                float depth = active ? 0.14f : 0.30f;
                float pulse = 1f + Mathf.Sin(Time.time * speed + i * 1.7f) * depth;

                var color = material.color * pulse;
                color.a = material.color.a;
                _ringProperties.SetColor(BaseColorId, color);
                renderer.SetPropertyBlock(_ringProperties);
            }
        }
    }
}
