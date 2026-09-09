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

        [Tooltip("Which control the player is using. Tapping is the default; the other two work " +
                 "and are switched here, since the menu no longer offers a choice.")]
        public ControlScheme Scheme = ControlScheme.Tap;

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

        /// <summary>
        /// Mirrors what the simulation has filed, so the button can show it. The decision itself
        /// lives in the plan, not here.
        /// </summary>
        public bool AbilityArmed { get; private set; }

        /// <summary>Whether the guard is currently chosen. Reset when the phase ends or a drag starts.</summary>
        public bool DefendArmed { get; private set; }

        public bool IsDragging { get; private set; }

        /// <summary>0..1 pull strength of the drag in progress. Zero when not dragging.</summary>
        public float CurrentPower { get; private set; }

        public Vector2 CurrentAim { get; private set; }

        /// <summary>
        /// How wide the planned run is drawn, in world units - about a gladiator across.
        ///
        /// A lane, not a line. At a fifth of this it told the player where he would end up and
        /// nothing about a body going there; at his own width it is the ground he is about to cover,
        /// and whether it passes through the other man is a thing you can see rather than judge.
        /// </summary>
        private const float TrajectoryWidth = 0.85f;

        /// <summary>World length of one tile of the band - one chevron and one dash of each rail.</summary>
        private const float DashPeriod = 1.15f;

        /// <summary>How much wider than the lane the head on the end of it is.</summary>
        private const float ArrowHeadSpread = 1.35f;

        /// <summary>How high above the sand the lane is drawn. The head goes just above it.</summary>
        private const float TrajectoryHeight = 0.06f;

        private LineRenderer _trajectory;
        private GameObject _tapMarker;
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

            BuildTapMarker(palette);
            BuildArrowHead(palette);
        }

        /// <summary>
        /// The head on the end of the lane.
        ///
        /// Its own quad rather than more line: a LineRenderer has one width along its whole length,
        /// and the point of an arrow is that it has two.
        ///
        /// Parented to the arena for the same reason the tap marker is - this component lives on the
        /// camera, and anything hung off the camera inherits its sixty-six degree pitch, which for
        /// something meant to lie on the floor means being seen edge-on.
        /// </summary>
        private void BuildArrowHead(ViewPalette palette)
        {
            if (palette == null || palette.TrajectoryHead == null || palette.Quad == null) return;

            _arrowHead = new GameObject("TrajectoryHead");
            _arrowHead.transform.SetParent(Controller.Arena.transform, false);
            _arrowHead.AddComponent<MeshFilter>().sharedMesh = palette.Quad;

            var renderer = _arrowHead.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = palette.TrajectoryHead;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _arrowHead.SetActive(false);
        }

        private GameObject _arrowHead;

        /// <summary>
        /// Puts the head at the end of the run, pointing the way it is going.
        ///
        /// The direction comes from the last two points of the preview rather than from the aim: the
        /// run can bounce off the wall, and after a bounce the aim points at where he started from.
        /// </summary>
        private void ShowArrowHead(System.Collections.Generic.List<Vector3> worldPoints)
        {
            if (_arrowHead == null || worldPoints.Count < 2) return;

            var tip = worldPoints[worldPoints.Count - 1];
            var before = worldPoints[worldPoints.Count - 2];
            var travel = tip - before;
            if (travel.sqrMagnitude < 0.000001f) { HideArrowHead(); return; }
            travel.Normalize();

            float span = TrajectoryWidth * ArrowHeadSpread;

            // Set back by half its own length so the arrow's point lands on the end of the run
            // rather than half an arrow past it - and then forward again by the lane's own end cap.
            //
            // That cap is the reason this looked wrong. A LineRenderer with rounded caps draws a
            // half-disc of its own width past the last point, so the band kept going for another
            // half-width after the arrow's tip and the head read as sitting short of the end.
            float pastTheCap = TrajectoryWidth * 0.5f;

            // Above the lane, not under it. Both are transparent and sorted by distance to the
            // camera, and the lane was the higher of the two - so the half of the head that overlaps
            // the band's cap was being painted over by the band, which left a visibly shorter arrow
            // sitting a little way back from where it had been put.
            _arrowHead.transform.position =
                tip + travel * (pastTheCap - span * 0.5f) + Vector3.up * (TrajectoryHeight + 0.01f);

            // Laid flat, then turned about the world's up axis. Composed rather than written as one
            // Euler triple: flat on the ground is ninety degrees of pitch, where Euler angles are
            // gimbal-locked and the turn comes back out of a component nobody put it in.
            float yaw = Mathf.Atan2(travel.x, travel.z) * Mathf.Rad2Deg;
            _arrowHead.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f, 0f, 0f);
            _arrowHead.transform.localScale = new Vector3(span, span, 1f);

            _arrowHead.SetActive(true);
        }

        private void HideArrowHead()
        {
            if (_arrowHead != null && _arrowHead.activeSelf) _arrowHead.SetActive(false);
        }

        /// <summary>
        /// The ring that marks a tapped destination.
        ///
        /// A ring rather than a filled disc: it sits on the sand where the gladiator is about to
        /// arrive, and a solid spot there would be hidden under him the moment he did.
        /// </summary>
        private void BuildTapMarker(ViewPalette palette)
        {
            if (palette == null) return;

            float radius = Controller.Arena.ScaleLength(GameConstants.GladiatorRadius);

            // Parented to the arena, not to this component. This lives on the camera, and a ring
            // hung off the camera inherits its 66-degree pitch - the annulus lies in the XZ plane,
            // so tilted with the camera it is seen edge-on and disappears. The lines get away with
            // it only because they set their own world rotation.
            _tapMarker = new GameObject("TapMarker");
            _tapMarker.transform.SetParent(Controller.Arena.transform, false);
            _tapMarker.AddComponent<MeshFilter>().sharedMesh =
                ViewPrimitives.CreateAnnulus(radius * 0.72f, radius, 40);

            var renderer = _tapMarker.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = palette.PullLine; // the same solid white the pull is drawn in
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _tapMarker.SetActive(false);
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

                // The tap feedback belongs to the phase that produced it. Left up, the ring and the
                // dashes would still be sitting there while the gladiators ran, describing an order
                // that had already been carried out.
                ClearOrderDrawing();
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
                else if (Scheme == ControlScheme.Swipe) TryBeginSwipeFromScreen(Input.mousePosition);
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

            // Folded into the arc he can actually run through before anything is measured off it. A
            // tap outside the zone is not a mis-click to be thrown away: it is a request to go as
            // far that way as he can, and it is answered with the closest thing he can do.
            var envelope = MoveEnvelope.For(g);
            target = envelope.Clamp(target);

            var toTarget = target - g.Pos;
            float distance = toTarget.magnitude;
            if (distance < 0.0001f) return false;

            // Measured along the bend he will actually run rather than across the straight line to
            // the tap, and against the zone's own far edge rather than DashReach - with the speed
            // ability armed those two differ by half again. Taking either the easy way put full
            // power well short of the edge the player is looking at.
            float power = envelope.PowerOnto(target);
            if (power <= MinPowerToSubmit) return false;

            DefendArmed = false;
            CancelDrag();

            var aim = toTarget / distance;
            Controller.SubmitPlayerMove(aim, power);

            // Drawn after submitting, so what is shown is the order that actually went in rather
            // than the one about to. It stays up for the rest of the phase - the whole point is
            // being able to look at the decision you have already made.
            ShowTapOrder(g, target, aim, power);
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

        /// <summary>
        /// Starts a swipe wherever the press landed. There is nothing to hit, which is the point of
        /// it: the gesture is a direction, and a direction can be drawn anywhere.
        /// </summary>
        public bool TryBeginSwipeFromScreen(Vector3 screenPos)
        {
            if (!TryScreenToVirtual(screenPos, out var virtualPoint)) return false;
            return TryBeginSwipe(virtualPoint);
        }

        public bool TryBeginSwipe(Vector2 virtualPoint)
        {
            if (PlayerGladiator() == null) return false;

            _swipeAnchor = virtualPoint;
            _swiping = true;
            BeginDrag(virtualPoint);
            return true;
        }

        /// <summary>Where a swipe started. Meaningless unless <see cref="_swiping"/>.</summary>
        private Vector2 _swipeAnchor;

        private bool _swiping;

        private void BeginDrag(Vector2 virtualPoint)
        {
            // Pulling back is choosing to move, which is the other half of the same either-or. The
            // guard has to let go here, or the button would sit lit while the gladiator charges.
            DefendArmed = false;

            IsDragging = true;
            UpdateDrag(virtualPoint);
        }

        /// <summary>
        /// Turns wherever the finger is now into an aim and a power.
        ///
        /// Both schemes are slingshots: drawn back one way, they run the other. What differs is
        /// where the rubber is anchored. A pull is anchored to the gladiator, so a slightly-off grab
        /// does not bias every launch by that offset; a swipe is anchored wherever the finger landed,
        /// which is the whole point of it - the hand can work in the empty half of the screen instead
        /// of on top of the man it is aiming.
        /// </summary>
        public void UpdateDrag(Vector2 virtualPoint)
        {
            if (!IsDragging) return;
            var g = PlayerGladiator();
            if (g == null) { CancelDrag(); return; }

            Vector2 stroke = _swiping ? _swipeAnchor - virtualPoint : g.Pos - virtualPoint;
            CurrentPower = Mathf.Clamp01(stroke.magnitude / GameConstants.MaxDragVirtual);
            CurrentAim = stroke.sqrMagnitude > 0.0001f ? stroke.normalized : Vector2.zero;

            DrawTrajectory(g);

            // The pull line is the handle on a slingshot. A swipe has no handle - the finger is not
            // holding the gladiator, and a line drawn from him to a point he has nothing to do with
            // says he is attached to it.
            if (_swiping) Hide(_pullLine);
            else DrawPullLine(g, virtualPoint);
        }

        /// <summary>Submits the move. Returns false if the pull was too short to count.</summary>
        public bool ReleaseDrag(Vector2 virtualPoint)
        {
            if (!IsDragging) return false;
            UpdateDrag(virtualPoint);

            float power = CurrentPower;
            Vector2 aim = CurrentAim;
            var g = PlayerGladiator();
            ClearDrag();

            if (power <= MinPowerToSubmit || aim == Vector2.zero) return false;

            // The armed ability is not touched here. It is its own decision, already filed, and
            // ordering a move - or changing it three times before the phase ends - leaves it alone.
            Controller.SubmitPlayerMove(aim, power);

            // And the run stays drawn. Letting go used to take the preview with it, which left the
            // player with an order filed, seconds still on the clock and nothing on screen saying
            // what they had ordered - so the only way to check was to swipe again and read the new
            // one. It goes when the phase does; see ClearOrderDrawing.
            if (g != null) DrawRun(g, aim, power);
            return true;
        }

        public void CancelDrag() => ClearDrag();

        /// <summary>Wired to the HUD's ability toggle; also on Q until the HUD exists.</summary>
        public void ToggleAbility()
        {
            if (PlayerGladiator() == null) return;

            // Filed with the simulation immediately, not held here until a move is submitted. Held,
            // it was lost by anyone who armed it after ordering their move, or who armed it and
            // then chose not to move at all.
            AbilityArmed = Controller.SubmitPlayerAbility(!AbilityArmed);
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
                Controller.SubmitPlayerDefend();
            }
            else
            {
                Controller.ClearPlayerPlan();
            }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Ends the gesture. Deliberately does not take the drawn run with it - a released swipe
        /// leaves its order on screen, and ReleaseDrag redraws it straight after calling this. What
        /// clears the drawing is the phase ending; see ClearOrderDrawing.
        /// </summary>
        private void ClearDrag()
        {
            IsDragging = false;
            _swiping = false;
            CurrentPower = 0f;
            CurrentAim = Vector2.zero;
            Hide(_trajectory);
            Hide(_pullLine);
            HideArrowHead();
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

        /// <summary>
        /// Marks where the player tapped and draws the run they just ordered.
        ///
        /// Tapping had no feedback at all: the order went in and nothing on screen acknowledged it
        /// until the gladiators started moving, so there was no way to tell a registered tap from a
        /// missed one, and no way to check the aim before committing to it.
        ///
        /// The ring sits on the tapped point and the dashes show the actual run, which is the same
        /// preview the pull draws - so when the tap is further than one dash carries, the line stops
        /// short of the ring and says exactly that.
        /// </summary>
        private void ShowTapOrder(GladiatorInstance g, Vector2 target, Vector2 aim, float power)
        {
            if (Controller.Arena == null) return;

            if (_tapMarker != null)
            {
                _tapMarker.transform.position = Controller.Arena.ToWorld(target, 0.05f);
                _tapMarker.SetActive(true);
            }

            if (_trajectory == null) return;

            var points = GameManager.ComputeTrajectoryPreview(g, aim, power);
            _worldPoints.Clear();
            foreach (var p in points)
                _worldPoints.Add(Controller.Arena.ToWorld(p, TrajectoryHeight));

            _trajectory.positionCount = _worldPoints.Count;
            for (int i = 0; i < _worldPoints.Count; i++)
                _trajectory.SetPosition(i, _worldPoints[i]);
            _trajectory.enabled = true;
            ShowArrowHead(_worldPoints);
        }

        /// <summary>
        /// Clears the picture of an order. The order itself stands - it was filed with the
        /// simulation the moment it was given, and this is only what was left on screen to say so.
        ///
        /// Called when the phase ends, which is what the drawing is scoped to: a run drawn while the
        /// gladiators are actually running describes an order already being carried out.
        /// </summary>
        private void ClearOrderDrawing()
        {
            if (_tapMarker != null && _tapMarker.activeSelf) _tapMarker.SetActive(false);
            Hide(_trajectory);
            HideArrowHead();
        }

        private void DrawTrajectory(GladiatorInstance g) => DrawRun(g, CurrentAim, CurrentPower);

        /// <summary>
        /// Draws the lane a run would take, from an aim and a power rather than from the gesture in
        /// progress - so the same drawing serves the swipe being made and the order it left behind.
        /// </summary>
        private void DrawRun(GladiatorInstance g, Vector2 aim, float power)
        {
            if (_trajectory == null || Controller.Arena == null) return;

            if (power <= MinPowerToSubmit || aim == Vector2.zero)
            {
                Hide(_trajectory);
                HideArrowHead();
                return;
            }

            var points = GameManager.ComputeTrajectoryPreview(g, aim, power);
            _worldPoints.Clear();
            foreach (var p in points)
                _worldPoints.Add(Controller.Arena.ToWorld(p, TrajectoryHeight));

            _trajectory.positionCount = _worldPoints.Count;
            for (int i = 0; i < _worldPoints.Count; i++)
                _trajectory.SetPosition(i, _worldPoints[i]);
            _trajectory.enabled = true;
            ShowArrowHead(_worldPoints);
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
