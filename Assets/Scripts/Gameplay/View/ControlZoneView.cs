using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// The wedge painted on the sand in front of the player's gladiator while he is being given his
    /// orders - the ground his weapon covers - and the dashed circle round him at the length of his
    /// reach, which stays up while he carries the orders out as well.
    ///
    /// Both white and see-through. The wedge was a flat red; it is a white light fading in from the
    /// man towards the edge of his reach now, and the circle is a white dashed line, so the sand and
    /// the fight both show through them.
    ///
    /// A picture of a rule the simulation enforces rather than a helper drawn beside it - the swing
    /// arc that decides whether a blow lands at all. A man outside the red is a man who does not get
    /// hit.
    ///
    /// There used to be a green arc under it as well, the ground he could be sent onto. It went
    /// when the arena got obstacles and a tap became an order to go anywhere: with no limit on where
    /// he can run there is nothing for it to draw.
    ///
    /// Rebuilt only when the wedge's shape changes - a different man, or Lunge or Trident Throw
    /// armed or wearing off - so in the ordinary case this allocates nothing.
    /// </summary>
    public class ControlZoneView : MonoBehaviour
    {
        /// <summary>How high off the sand the wedge sits. Under the trajectory, over the hazard.</summary>
        private const float StrikeZoneHeight = 0.043f;

        private ArenaView _arena;
        private Transform _root;
        private MeshFilter _strike;
        private MeshRenderer _strikeRenderer;

        private float _builtReach = -1f, _builtSwing = -1f;

        /// <summary>How high off the sand the circle sits: just over the wedge, so the two never fight for a pixel.</summary>
        private const float RingHeight = 0.047f;

        /// <summary>How thick the circle's line is, in virtual units.</summary>
        private const float RingWidth = 3f;

        /// <summary>One dash and the gap after it, in virtual units along the circle.</summary>
        private const float DashPeriod = 20f;

        /// <summary>How much of each period is dash.</summary>
        private const float DashFill = 0.55f;

        private Transform _ringRoot;
        private MeshFilter _ring;
        private MeshRenderer _ringRenderer;
        private float _builtRingReach = -1f;

        /// <summary>How many dashes the circle is drawn in. For tests.</summary>
        public int RingDashes { get; private set; }

        /// <summary>
        /// Builds the wedge and the circle under the arena. <paramref name="prefix"/> goes on every
        /// object's name, so a second zone - the opponent's, which shows only its circle - is
        /// tellable from the player's in the scene and in the tests.
        /// </summary>
        public void Bind(ArenaView arena, string prefix = "")
        {
            _arena = arena;
            if (_root != null) return;

            var palette = arena != null ? arena.Palette : null;

            var root = new GameObject(prefix + "ControlZone");
            root.transform.SetParent(arena != null ? arena.transform : transform, false);
            _root = root.transform;

            var go = new GameObject(prefix + "StrikeZone");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(0f, StrikeZoneHeight, 0f);

            _strike = go.AddComponent<MeshFilter>();
            _strikeRenderer = go.AddComponent<MeshRenderer>();
            _strikeRenderer.sharedMaterial = palette != null ? palette.StrikeZone : null;
            _strikeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _strikeRenderer.receiveShadows = false;

            // Its own root, not the wedge's: the wedge is turned to where he looks, and dashes turned
            // with him would crawl round the circle every time he did.
            var ringRoot = new GameObject(prefix + "ReachRingRoot");
            ringRoot.transform.SetParent(arena != null ? arena.transform : transform, false);
            _ringRoot = ringRoot.transform;

            var ring = new GameObject(prefix + "ReachRing");
            ring.transform.SetParent(_ringRoot, false);
            ring.transform.localPosition = new Vector3(0f, RingHeight, 0f);
            _ring = ring.AddComponent<MeshFilter>();
            _ringRenderer = ring.AddComponent<MeshRenderer>();
            _ringRenderer.sharedMaterial = palette != null ? palette.ReachRing : null;
            _ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ringRenderer.receiveShadows = false;
            _ringRenderer.enabled = false;

            BuildSectors(prefix, palette);
            SetVisible(false);
        }

        // ------------------------------------------------------------------
        // the other man's sides
        // ------------------------------------------------------------------

        /// <summary>
        /// Four arcs round the opponent, one a quarter, in the colours of his sectors: his front
        /// red, his sides yellow, his back green - turned with him, and the one the player's run
        /// would end in lit up. Which side of him a blow lands on decides what it is worth (see
        /// GameConstants.BackAttackMult), and this is that rule drawn where the decision is made.
        /// It gives nothing away: which way he is looking is on the model already.
        /// </summary>
        private const float SectorInner = 1.35f, SectorOuter = 1.65f;   // in body radii
        private const float SectorHeight = RingHeight + 0.002f;
        private const float SectorDimAlpha = 0.3f, SectorLitAlpha = 0.85f;

        private static readonly Color FrontColor = new Color(1f, 0.35f, 0.30f);
        private static readonly Color SideColor = new Color(1f, 0.80f, 0.25f);
        private static readonly Color BackColor = new Color(0.40f, 1f, 0.40f);

        private Transform _sectorRoot;

        /// <summary>Front, right, back, left - the order the children are turned in, a quarter turn each.</summary>
        private readonly MeshRenderer[] _sectorArcs = new MeshRenderer[4];
        private readonly MaterialPropertyBlock[] _sectorProperties = new MaterialPropertyBlock[4];
        private float _builtSectorRadius = -1f;

        /// <summary>Which of the opponent's sectors is lit - the one the run would end in. Null when none are drawn.</summary>
        public HitSector? LitSector { get; private set; }

        public bool AreSectorsShowing => _sectorRoot != null && _sectorRoot.gameObject.activeSelf;

        private void BuildSectors(string prefix, ViewPalette palette)
        {
            var root = new GameObject(prefix + "OpponentSectors");
            root.transform.SetParent(_arena != null ? _arena.transform : transform, false);
            _sectorRoot = root.transform;

            string[] names = { "Front", "Right", "Back", "Left" };
            for (int i = 0; i < 4; i++)
            {
                var arc = new GameObject(prefix + "SectorArc_" + names[i]);
                arc.transform.SetParent(_sectorRoot, false);
                arc.transform.localPosition = new Vector3(0f, SectorHeight, 0f);
                arc.transform.localRotation = Quaternion.Euler(0f, 90f * i, 0f);
                arc.AddComponent<MeshFilter>();
                var renderer = arc.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = palette != null ? palette.ReachRing : null;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _sectorArcs[i] = renderer;
                _sectorProperties[i] = new MaterialPropertyBlock();
            }
            _sectorRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Draws the opponent's sectors, with the one <paramref name="from"/> falls in lit - where
        /// the player's run ends, or where he stands if he is not running. Hides them when there is
        /// nobody to draw them round.
        /// </summary>
        public void SyncOpponentSectors(GladiatorInstance opponent, Vector2 from, bool visible)
        {
            if (_sectorRoot == null || _arena == null) return;

            bool show = visible && opponent != null && opponent.Alive;
            if (_sectorRoot.gameObject.activeSelf != show) _sectorRoot.gameObject.SetActive(show);
            if (!show)
            {
                LitSector = null;
                return;
            }

            float radius = _arena.ScaleLength(GameConstants.GladiatorRadius);
            if (!Mathf.Approximately(radius, _builtSectorRadius))
            {
                foreach (var arc in _sectorArcs)
                    arc.GetComponent<MeshFilter>().sharedMesh =
                        ViewPrimitives.CreateAnnulusSector(radius * SectorInner, radius * SectorOuter, 90f, 24);
                _builtSectorRadius = radius;
            }

            _sectorRoot.position = _arena.ToWorld(opponent.Pos);
            _sectorRoot.rotation = Quaternion.LookRotation(ToWorldDirection(opponent.Facing), Vector3.up);

            // Which quarter the point is in, counted the way the children are turned: a quarter turn
            // about the world's up is a clockwise quarter on the sand, so the child at +90 is on his
            // right and the one at 270 on his left.
            var facing = opponent.Facing.sqrMagnitude > 0.0001f ? opponent.Facing.normalized : Vector2.up;
            var toPoint = from - opponent.Pos;
            float angle = toPoint.sqrMagnitude > 0.0001f ? Vector2.SignedAngle(facing, toPoint) : 0f;
            int lit = angle > -45f && angle <= 45f ? 0 : angle > 45f && angle <= 135f ? 3 : angle > -135f && angle <= -45f ? 1 : 2;
            LitSector = lit == 0 ? HitSector.Front : lit == 2 ? HitSector.Back : HitSector.Side;

            for (int i = 0; i < 4; i++)
            {
                var color = i == 0 ? FrontColor : i == 2 ? BackColor : SideColor;
                color.a = i == lit ? SectorLitAlpha : SectorDimAlpha;
                _sectorProperties[i].SetColor(BaseColorId, color);
                _sectorArcs[i].SetPropertyBlock(_sectorProperties[i]);
            }
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// Shows the wedge for this gladiator, or hides it when there is nobody to show it for.
        ///
        /// Called every frame of planning and nothing else: it describes a decision being made, and
        /// left up while the gladiators run it would describe one already carried out.
        /// </summary>
        public void Sync(GladiatorInstance g, bool visible, bool ringVisible = false)
        {
            if (_root == null || _arena == null) return;

            bool alive = g != null && g.Alive;
            SyncRing(g, ringVisible && alive);

            if (!visible || !alive)
            {
                SetVisible(false);
                return;
            }

            Rebuild(g.PlannedWeaponReach(), g.WeaponDef.SwingArcDegrees);

            // Built about +Z, so pointing the root at his heading points the wedge.
            _root.position = _arena.ToWorld(g.Pos);
            _root.rotation = Quaternion.LookRotation(ToWorldDirection(g.Facing), Vector3.up);
            SetVisible(true);
        }

        /// <summary>
        /// A heading on the simulation's plane as a direction in the world. The scale between the
        /// two is uniform - the arena's elongation lives in the simulation's own coordinates rather
        /// than in the conversion - so a heading is the same heading in both.
        /// </summary>
        private static Vector3 ToWorldDirection(Vector2 facing)
        {
            var direction = new Vector3(facing.x, 0f, facing.y);
            return direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.forward;
        }

        /// <summary>
        /// To his reach rather than his weapon's: Lunge and Trident Throw lengthen it, and the wedge
        /// was drawn the weapon's length through both - through the rounds they were working and
        /// through the planning phase they were armed in, which is when it matters.
        /// </summary>
        private void Rebuild(float weaponReach, float swingDegrees)
        {
            float reach = _arena.ScaleLength(weaponReach);
            if (Mathf.Approximately(reach, _builtReach)
                && Mathf.Approximately(swingDegrees, _builtSwing))
                return;

            // From his own edge rather than from his feet: the middle of it is where he is standing,
            // and a wedge drawn through him reads as a stain rather than as reach.
            _strike.sharedMesh = ViewPrimitives.CreateAnnulusSector(
                _arena.ScaleLength(GameConstants.GladiatorRadius * 0.6f), reach, swingDegrees);
            _builtReach = reach;
            _builtSwing = swingDegrees;
        }

        private void SetVisible(bool visible)
        {
            if (_strikeRenderer != null) _strikeRenderer.enabled = visible;
        }

        /// <summary>Whether the wedge is currently painted. For tests and for the screenshot rig.</summary>
        public bool IsShowing => _strikeRenderer != null && _strikeRenderer.enabled;

        /// <summary>Whether the reach circle is drawn. For tests and for the screenshot rig.</summary>
        public bool IsRingShowing => _ringRenderer != null && _ringRenderer.enabled;

        /// <summary>
        /// The circle round him at the length of his reach - the reach his armed ability will give
        /// him while it is armed, and what he has while it works - kept on him wherever he runs.
        /// </summary>
        private void SyncRing(GladiatorInstance g, bool visible)
        {
            if (_ringRenderer == null) return;
            if (_ringRenderer.enabled != visible) _ringRenderer.enabled = visible;
            if (!visible) return;

            float reach = _arena.ScaleLength(g.PlannedWeaponReach());
            if (!Mathf.Approximately(reach, _builtRingReach))
            {
                _ring.sharedMesh = CreateDashedRing(reach, _arena.ScaleLength(RingWidth), _arena.ScaleLength(DashPeriod), out int dashes);
                RingDashes = dashes;
                _builtRingReach = reach;
            }
            _ringRoot.position = _arena.ToWorld(g.Pos);
        }

        /// <summary>
        /// A circle of this radius drawn as a dashed line this wide: the dashes an even length
        /// whatever the radius, a whole number of them so the join does not show, each a short arc.
        /// </summary>
        private static Mesh CreateDashedRing(float radius, float width, float period, out int dashes)
        {
            const int segmentsPerDash = 3;
            dashes = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * radius / Mathf.Max(0.0001f, period)), 12, 120);

            int ringsPerDash = segmentsPerDash + 1;
            var vertices = new Vector3[dashes * ringsPerDash * 2];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[dashes * segmentsPerDash * 6];
            float inner = Mathf.Max(0f, radius - width * 0.5f), outer = radius + width * 0.5f;
            float step = Mathf.PI * 2f / dashes;

            int v = 0, t = 0;
            for (int d = 0; d < dashes; d++)
            {
                int first = v;
                for (int s = 0; s < ringsPerDash; s++)
                {
                    float angle = step * (d + DashFill * s / segmentsPerDash);
                    float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                    vertices[v] = new Vector3(cos * inner, 0f, sin * inner);
                    vertices[v + 1] = new Vector3(cos * outer, 0f, sin * outer);
                    normals[v] = normals[v + 1] = Vector3.up;
                    uvs[v] = new Vector2(s / (float)segmentsPerDash, 0f);
                    uvs[v + 1] = new Vector2(s / (float)segmentsPerDash, 1f);
                    v += 2;
                }
                for (int s = 0; s < segmentsPerDash; s++)
                {
                    int a = first + s * 2, b = a + 1, c = a + 2, e = a + 3;
                    // Wound as the full ring is (see ViewPrimitives.CreateAnnulus): angles run from +X
                    // towards +Z, and this order faces up at the camera.
                    triangles[t++] = a; triangles[t++] = e; triangles[t++] = b;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = e;
                }
            }

            var mesh = new Mesh { name = $"ReachRing_{radius:0.00}" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
