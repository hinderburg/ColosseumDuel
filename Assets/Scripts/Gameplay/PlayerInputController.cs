using System.Collections.Generic;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEngine;

namespace ColosseumDuel.Gameplay
{
    /// <summary>
    /// Drag-to-launch ("slingshot") input: press on your gladiator, pull back, release to run in
    /// the opposite direction with power proportional to the pull. While dragging, the full bounced
    /// trajectory is drawn from GameManager.ComputeTrajectoryPreview - the same maths the action
    /// phase will run, so the preview is a promise rather than an approximation.
    ///
    /// The drag itself lives in TryBeginDrag/UpdateDrag/ReleaseDrag, which take virtual-space points
    /// and know nothing about a mouse. Update() is only the mapping from device to those calls,
    /// which keeps the interesting half testable without synthesising input events.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PlayerInputController : MonoBehaviour
    {
        public GameController Controller;
        public Camera ArenaCamera;

        [Tooltip("Which control the player is using. Set from the pick screen; drag is the default.")]
        public ControlScheme Scheme = ControlScheme.Drag;

        [Tooltip("How far from the gladiator, in virtual units, a press still counts as grabbing " +
                 "them. Only used by the simulation-space entry point; real presses are measured " +
                 "against the figure on screen.")]
        public float GrabRadiusVirtual = GameConstants.GladiatorRadius * 3f;

        /// <summary>
        /// How far from the drawn figure a press still grabs him, as a fraction of screen height.
        ///
        /// A fraction rather than pixels so the target stays the same size on the finger whatever
        /// the screen; at the reference height of 1024 this is about 56 pixels, a little over a
        /// thumb's contact patch.
        /// </summary>
        private const float GrabScreenFraction = 0.055f;

        /// <summary>Height of a gladiator in body radii, for finding the top of him on screen.</summary>
        private const float BodyHeightInRadii = 5.6f;

        [Tooltip("Pulls shorter than this fraction of the maximum are treated as a mis-click, not a move.")]
        public float MinPowerToSubmit = 0.05f;

        /// <summary>Armed by the ability toggle; consumed by the next submitted action.</summary>
        public bool AbilityArmed { get; private set; }

        /// <summary>Whether the guard is currently chosen. Reset when the phase ends or a drag starts.</summary>
        public bool DefendArmed { get; private set; }

        public bool IsDragging { get; private set; }

        /// <summary>0..1 pull strength of the drag in progress. Zero when not dragging.</summary>
        public float CurrentPower { get; private set; }

        public Vector2 CurrentAim { get; private set; }

        /// <summary>World width of the trajectory line. Wide on purpose - the old hairline was easy
        /// to lose against bright sand and the red danger rings.</summary>
        private const float TrajectoryWidth = 0.22f;

        /// <summary>World length of one dash plus its gap.</summary>
        private const float DashPeriod = 0.55f;

        private LineRenderer _trajectory;
        private LineRenderer _pullLine;
        private readonly List<Vector3> _worldPoints = new List<Vector3>();

        private void Reset()
        {
            ArenaCamera = GetComponent<Camera>();
        }

        private void Awake()
        {
            if (ArenaCamera == null) ArenaCamera = GetComponent<Camera>();
        }

        private void Start()
        {
            BuildTrajectoryLine();
        }

        private void BuildTrajectoryLine()
        {
            var palette = Controller != null && Controller.Arena != null ? Controller.Arena.Palette : null;

            _trajectory = CreateLine("TrajectoryPreview", palette != null ? palette.Trajectory : null,
                TrajectoryWidth);

            // Dashes come from a tiled texture keyed to distance along the line, so they stay evenly
            // spaced through a bounce even though the preview's points are not evenly spaced.
            _trajectory.textureMode = LineTextureMode.Tile;
            _trajectory.textureScale = new Vector2(1f / DashPeriod, 1f);

            // The pull is drawn slightly narrower and solid, so at a glance the two lines read as
            // different things: what you are doing now, and what will happen when you let go.
            _pullLine = CreateLine("PullLine", palette != null ? palette.PullLine : null,
                TrajectoryWidth * 0.65f);
        }

        private LineRenderer CreateLine(string name, Material material, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.alignment = LineAlignment.TransformZ; // lie flat on the arena, not billboard at the camera
            line.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (material != null) line.sharedMaterial = material;
            line.enabled = false;
            return line;
        }

        // ------------------------------------------------------------------
        // device mapping
        // ------------------------------------------------------------------

        private void Update()
        {
            if (Controller == null || Controller.Manager == null) return;

            HandleKeyboardShortcuts();

            if (Controller.Manager.State.Phase != MatchPhase.Planning)
            {
                if (IsDragging) CancelDrag();

                // Both arm flags are decisions about one planning phase, and by now that phase has
                // submitted whatever it was going to. Left standing they would come up already
                // pressed at the start of the next one - or, after a restart, carry an armed ability
                // into a match where nothing has been chosen at all.
                DefendArmed = false;
                AbilityArmed = false;
                return;
            }

            if (Input.GetMouseButtonDown(1) && IsDragging)
            {
                CancelDrag();
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                // A press that landed on a HUD button must not also start a pull underneath it.
                if (IsPointerOverHud()) return;

                if (Scheme == ControlScheme.Tap) TapTo(Input.mousePosition);
                else TryBeginDragFromScreen(Input.mousePosition);
            }
            else if (Input.GetMouseButton(0) && IsDragging)
            {
                if (TryScreenToVirtual(Input.mousePosition, out var current)) UpdateDrag(current);
            }
            else if (Input.GetMouseButtonUp(0) && IsDragging)
            {
                if (TryScreenToVirtual(Input.mousePosition, out var end)) ReleaseDrag(end);
                else CancelDrag();
            }
        }

        /// <summary>
        /// Temporary keyboard bridge so the game is playable before the HUD exists (phase 4):
        /// 1/2/3 pick a gladiator, Space defends, Q arms the ability.
        /// </summary>
        private void HandleKeyboardShortcuts()
        {
            var state = Controller.Manager.State;

            if (state.P1.NeedsPick)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) Controller.SubmitPlayerPick(GladiatorId.Brutius);
                if (Input.GetKeyDown(KeyCode.Alpha2)) Controller.SubmitPlayerPick(GladiatorId.Barbarius);
                if (Input.GetKeyDown(KeyCode.Alpha3)) Controller.SubmitPlayerPick(GladiatorId.Hilius);
                return;
            }

            if (state.Phase != MatchPhase.Planning) return;
            if (Input.GetKeyDown(KeyCode.Q)) ToggleAbility();
            if (Input.GetKeyDown(KeyCode.Space)) ToggleDefend();
        }

        // ------------------------------------------------------------------
        // the drag itself - device independent, so tests can drive it directly
        // ------------------------------------------------------------------

        /// <summary>
        /// Starts a pull if the press landed anywhere on the player's own gladiator on screen.
        ///
        /// Tested against his silhouette rather than against a circle on the ground, which is what
        /// the old check did and why grabbing him so often missed: the camera looks down at 66
        /// degrees, so the model stands well above the point it occupies on the floor, and a press
        /// on the chest or the head projects onto the sand somewhere behind him - sometimes outside
        /// the grab radius entirely. The taller the figure, the worse it got.
        ///
        /// The target is the segment from his feet to the top of his head, plus a thumb's width, so
        /// what is grabbable is exactly what is drawn.
        /// </summary>
        public bool TryBeginDragFromScreen(Vector3 screenPos)
        {
            var g = PlayerGladiator();
            if (g == null || ArenaCamera == null || Controller == null || Controller.Arena == null)
                return false;

            var arena = Controller.Arena;
            var feet = ArenaCamera.WorldToScreenPoint(arena.ToWorld(g.Pos));
            var head = ArenaCamera.WorldToScreenPoint(
                arena.ToWorld(g.Pos, arena.ScaleLength(GameConstants.GladiatorRadius) * BodyHeightInRadii));

            // Behind the camera - not something a fixed camera above the arena can produce, but a
            // negative w would otherwise fold the screen point back on itself and grab at random.
            if (feet.z <= 0f || head.z <= 0f) return false;

            float grabPixels = Screen.height * GrabScreenFraction;
            if (DistanceToSegment(screenPos, feet, head) > grabPixels) return false;
            if (!TryScreenToVirtual(screenPos, out var virtualPoint)) return false;

            // The pull is measured from the gladiator himself, so where on him the press landed
            // does not bias the launch.
            BeginDrag(virtualPoint);
            return true;
        }

        /// <summary>
        /// Sends the gladiator at a point on the sand: the whole of the alternative control.
        ///
        /// Aims at the tap and pulls exactly hard enough to arrive there, or as hard as he can if
        /// the point is further than one dash carries. Reach comes from the simulation rather than
        /// from a number here, so "as hard as he can" stays true when the speed or the phase length
        /// is retuned.
        /// </summary>
        public bool TapTo(Vector3 screenPos)
        {
            var g = PlayerGladiator();
            if (g == null) return false;
            if (!TryScreenToVirtual(screenPos, out var target)) return false;

            var toTarget = target - g.Pos;
            float distance = toTarget.magnitude;
            if (distance < 0.0001f) return false;

            float reach = g.DashReach();
            float power = reach > 0.0001f ? Mathf.Clamp01(distance / reach) : 1f;
            if (power <= MinPowerToSubmit) return false;

            DefendArmed = false;
            CancelDrag();

            bool ability = AbilityArmed;
            Controller.SubmitPlayerMove(toTarget / distance, power, ability);
            AbilityArmed = false;
            return true;
        }

        /// <summary>Perpendicular distance from a point to a line segment, in screen pixels.</summary>
        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            var ab = (Vector2)(b - a);
            var ap = (Vector2)(point - a);
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 0.0001f) return ap.magnitude;

            float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / lengthSq);
            return (ap - ab * t).magnitude;
        }

        /// <summary>
        /// Starts a pull from a point on the arena floor, within a radius measured on the floor too.
        ///
        /// Kept for callers that work in simulation space - tests, and anything driving the game
        /// without a camera. Real presses go through TryBeginDragFromScreen, which measures against
        /// the figure as drawn; this one cannot, because a point on the ground carries no
        /// information about where the model above it lands on screen.
        /// </summary>
        public bool TryBeginDrag(Vector2 virtualPoint)
        {
            var g = PlayerGladiator();
            if (g == null) return false;
            if (Vector2.Distance(virtualPoint, g.Pos) > GrabRadiusVirtual) return false;

            BeginDrag(virtualPoint);
            return true;
        }

        private void BeginDrag(Vector2 virtualPoint)
        {
            // Pulling back is choosing to move, which is the other half of the same either-or. The
            // guard has to let go here, or the button would sit lit while the gladiator charges.
            DefendArmed = false;

            IsDragging = true;
            UpdateDrag(virtualPoint);
        }

        /// <summary>
        /// The pull is measured from the gladiator, not from wherever the press landed: anchoring it
        /// to the body means a slightly-off grab does not bias every launch by that offset.
        /// </summary>
        public void UpdateDrag(Vector2 virtualPoint)
        {
            if (!IsDragging) return;
            var g = PlayerGladiator();
            if (g == null) { CancelDrag(); return; }

            Vector2 pull = g.Pos - virtualPoint; // release runs opposite the pull, like a slingshot
            CurrentPower = Mathf.Clamp01(pull.magnitude / GameConstants.MaxDragVirtual);
            CurrentAim = pull.sqrMagnitude > 0.0001f ? pull.normalized : Vector2.zero;

            DrawTrajectory(g);
            DrawPullLine(g, virtualPoint);
        }

        /// <summary>Submits the move. Returns false if the pull was too short to count.</summary>
        public bool ReleaseDrag(Vector2 virtualPoint)
        {
            if (!IsDragging) return false;
            UpdateDrag(virtualPoint);

            float power = CurrentPower;
            Vector2 aim = CurrentAim;
            bool ability = AbilityArmed;
            ClearDrag();

            if (power <= MinPowerToSubmit || aim == Vector2.zero) return false;

            Controller.SubmitPlayerMove(aim, power, ability);
            AbilityArmed = false; // consumed by the action it was armed for
            return true;
        }

        public void CancelDrag() => ClearDrag();

        /// <summary>Wired to the HUD's ability toggle; also on Q until the HUD exists.</summary>
        public void ToggleAbility()
        {
            var g = PlayerGladiator();
            if (g == null) return;
            // Arming an ability that cannot fire would silently do nothing on submit.
            AbilityArmed = !AbilityArmed && g.CanActivateAbility;
        }

        /// <summary>
        /// Toggles the guard, the same way the ability button toggles.
        ///
        /// It used to commit the plan and leave no trace: the button flashed, the plan was filed,
        /// and nothing on screen said which of the two choices had been made. A guard held for a
        /// whole planning phase is a decision the player needs to see they made - and be able to
        /// take back, which pressing again now does.
        ///
        /// Turning it off files ActionType.None, which is the same "nothing chosen" state the phase
        /// starts in; a player who leaves it there still ends up defending, because that is what an
        /// unmade decision falls back to.
        /// </summary>
        public void ToggleDefend()
        {
            if (PlayerGladiator() == null) return;

            DefendArmed = !DefendArmed;
            if (DefendArmed)
            {
                CancelDrag();
                Controller.SubmitPlayerDefend(AbilityArmed);
            }
            else
            {
                Controller.ClearPlayerPlan();
            }
        }

        // ------------------------------------------------------------------

        private void ClearDrag()
        {
            IsDragging = false;
            CurrentPower = 0f;
            CurrentAim = Vector2.zero;
            Hide(_trajectory);
            Hide(_pullLine);
        }

        private static void Hide(LineRenderer line)
        {
            if (line == null) return;
            line.positionCount = 0;
            line.enabled = false;
        }

        /// <summary>
        /// The pull itself: gladiator to pointer. Without it the drag has no visible handle - the
        /// trajectory alone shows the result but not the gesture producing it.
        /// </summary>
        private void DrawPullLine(GladiatorInstance g, Vector2 pointer)
        {
            if (_pullLine == null || Controller.Arena == null) return;

            if (CurrentPower <= MinPowerToSubmit)
            {
                Hide(_pullLine);
                return;
            }

            // Clamped to the maximum useful pull, so dragging further does not draw a line that
            // promises power the release will not deliver.
            Vector2 pull = g.Pos - pointer;
            Vector2 clamped = pull.magnitude > GameConstants.MaxDragVirtual
                ? pull.normalized * GameConstants.MaxDragVirtual
                : pull;

            _pullLine.positionCount = 2;
            _pullLine.SetPosition(0, Controller.Arena.ToWorld(g.Pos, 0.07f));
            _pullLine.SetPosition(1, Controller.Arena.ToWorld(g.Pos - clamped, 0.07f));
            _pullLine.enabled = true;
        }

        private void DrawTrajectory(GladiatorInstance g)
        {
            if (_trajectory == null || Controller.Arena == null) return;

            if (CurrentPower <= MinPowerToSubmit || CurrentAim == Vector2.zero)
            {
                Hide(_trajectory);
                return;
            }

            var points = GameManager.ComputeTrajectoryPreview(g, CurrentAim, CurrentPower);
            _worldPoints.Clear();
            foreach (var p in points)
                _worldPoints.Add(Controller.Arena.ToWorld(p, 0.06f));

            _trajectory.positionCount = _worldPoints.Count;
            for (int i = 0; i < _worldPoints.Count; i++)
                _trajectory.SetPosition(i, _worldPoints[i]);
            _trajectory.enabled = true;
        }

        private GladiatorInstance PlayerGladiator()
        {
            var manager = Controller != null ? Controller.Manager : null;
            if (manager == null || manager.State.Phase != MatchPhase.Planning) return null;
            var g = manager.State.P1.Active;
            return g != null && g.Alive ? g : null;
        }

        private static bool IsPointerOverHud()
            => UnityEngine.EventSystems.EventSystem.current != null
               && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        /// <summary>
        /// Screen point to the simulation's virtual plane. This raycasts the mathematical ground
        /// plane rather than a collider, so the arena floor needs no collider and no layer setup.
        /// </summary>
        public bool TryScreenToVirtual(Vector3 screenPos, out Vector2 virtualPos)
        {
            virtualPos = default;
            if (ArenaCamera == null || Controller == null || Controller.Arena == null) return false;

            var ray = ArenaCamera.ScreenPointToRay(screenPos);
            var floorPlane = new Plane(Vector3.up, Vector3.zero);
            if (!floorPlane.Raycast(ray, out float enter)) return false;

            virtualPos = Controller.Arena.ToVirtual(ray.GetPoint(enter));
            return true;
        }
    }
}
