using System.Collections.Generic;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay.View;
using UnityEngine;

namespace ColosseumDuel.Gameplay
{
    /// <summary>
    /// The player's orders. The game ships on drawing: touch the gladiator or anywhere on the sand
    /// and the run joins him to it, then follows the finger until it lifts or his speed runs out.
    /// The lane drawn under it comes from the same pathfinder and the same reach the action phase
    /// uses - so the preview is a promise rather than an approximation.
    ///
    /// The older slingshot and swipe schemes are still here and still work - they give an aim and a
    /// power, which the simulation turns into a point and routes like any other order - but nothing
    /// offers them any more; they are switched on the inspector.
    ///
    /// The gestures live in BeginDraw/DrawTo/EndDraw, TapTo and TryBeginDrag/UpdateDrag/ReleaseDrag,
    /// which take points and know nothing about a mouse. Update() only maps the device onto them,
    /// which keeps the interesting half testable without synthesising input events.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PlayerInputController : MonoBehaviour
    {
        public GameController Controller;
        public Camera ArenaCamera;

        [Tooltip("Which control the player is using. Drawing is the default; the others work " +
                 "and are switched here, since the menu no longer offers a choice.")]
        public ControlScheme Scheme = ControlScheme.Draw;

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
        /// How wide the planned run is drawn, in world units: a thin stripe, about a sixth of a
        /// gladiator across.
        ///
        /// It was a dashed lane his own width, then half that, with an arrow head on the end. Asked
        /// for simply a thin white line - three or four times thinner than the half-width lane - and
        /// that is what a hand-drawn run wants: its bends read as bends instead of as a blot.
        /// </summary>
        private const float TrajectoryWidth = 0.12f;

        /// <summary>How high above the sand the stripe is drawn.</summary>
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

            // The pull is drawn a little narrower and fainter, so at a glance the two lines read as
            // different things: what you are doing now, and what will happen when you let go.
            _pullLine = CreateLine("PullLine", palette != null ? palette.PullLine : null,
                TrajectoryWidth * 0.65f);

            BuildTapMarker(palette);
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
                _tapping = false;
                if (IsDrawing) EndDraw();

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

                if (Scheme == ControlScheme.Draw)
                {
                    BeginDrawFromScreen(Input.mousePosition);
                }
                else if (Scheme == ControlScheme.Tap)
                {
                    // Latched, so the rest of the gesture belongs to the sand even if the finger
                    // wanders over a button on its way. Checking the HUD every frame would hand the
                    // order to whatever the thumb happened to be over halfway through placing it.
                    _tapping = true;
                    TapTo(Input.mousePosition);
                }
                else if (Scheme == ControlScheme.Swipe) TryBeginSwipeFromScreen(Input.mousePosition);
                else TryBeginDragFromScreen(Input.mousePosition);
            }
            else if (Input.GetMouseButton(0) && IsDrawing)
            {
                DrawToScreen(Input.mousePosition);
            }
            else if (Input.GetMouseButtonUp(0) && IsDrawing)
            {
                DrawToScreen(Input.mousePosition);
                EndDraw();
            }
            else if (Input.GetMouseButton(0) && _tapping)
            {
                // The tap is a drag while it is held: the order follows the finger, so the run can
                // be aimed by looking at it rather than by pressing and hoping. Every frame refiles
                // the plan, which costs nothing - a plan is one field, and the last one filed before
                // the phase ends is the one that runs.
                TapTo(Input.mousePosition);
            }
            else if (Input.GetMouseButtonUp(0) && _tapping)
            {
                TapTo(Input.mousePosition);
                _tapping = false;
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
        /// Whether a tap is being held and dragged right now. Latched on a press that landed on the
        /// sand, so the gesture is not stolen by a button the finger crosses on its way.
        /// </summary>
        private bool _tapping;

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

            if (!PressIsOnGladiator(screenPos, g)) return false;
            if (!TryScreenToVirtual(screenPos, out var virtualPoint)) return false;

            // The pull is measured from the gladiator himself, so where on him the press landed
            // does not bias the launch.
            BeginDrag(virtualPoint);
            return true;
        }

        /// <summary>
        /// Sends the gladiator towards a point on the sand, as far as his speed carries him.
        ///
        /// Anywhere at all. The way there goes round whatever stands in it - the pathfinder works
        /// that out, not the player - and a tap on a crate or outside the wall is answered with the
        /// nearest ground he can stand on rather than thrown away. Held and dragged it is called
        /// every frame and the order follows the finger; tapped again, the new order replaces the
        /// old one. Nothing limits how often, for as long as the phase lasts.
        /// </summary>
        public bool TapTo(Vector3 screenPos)
        {
            var g = PlayerGladiator();
            if (g == null) return false;
            if (!TryScreenToVirtual(screenPos, out var tapped)) return false;

            var path = Controller.Manager.ComputeTrajectoryPreview(g, tapped);

            // A tap on his own feet is not an order to go anywhere. Measured along the run rather
            // than in a straight line, so a tap just the far side of a column still counts; a body's
            // width is the shortest run that reads as a run.
            if (ObstacleField.Length(path) < GameConstants.GladiatorRadius) return false;

            DefendArmed = false;
            CancelDrag();

            Controller.SubmitPlayerMoveTo(tapped);

            // Drawn after submitting, so what is shown is the order that actually went in. It stays
            // up for the rest of the phase - the whole point is being able to look at the decision
            // already made. The ring goes where the run ends, which is short of the tap when the tap
            // is further than he can run.
            ShowTapOrder(path[path.Count - 1], path);
            return true;
        }

        // ------------------------------------------------------------------
        // drawing a run
        // ------------------------------------------------------------------

        /// <summary>Whether a run is being drawn right now: pressed, and not yet lifted.</summary>
        public bool IsDrawing { get; private set; }

        /// <summary>
        /// Whether the run being drawn has used up his reach. Once it has, moving the finger further
        /// draws nothing: the run stops where his speed ran out, and the lane no longer following
        /// the finger is how the player sees that it did.
        /// </summary>
        public bool DrawExhausted { get; private set; }

        /// <summary>The run drawn so far, corner to corner from where he stands.</summary>
        public IReadOnlyList<Vector2> DrawnRun => _drawnRun;

        private readonly List<Vector2> _drawnRun = new List<Vector2>();

        /// <summary>The run that stood before the stroke in progress, kept in case the stroke comes to nothing.</summary>
        private readonly List<Vector2> _previousRun = new List<Vector2>();

        /// <summary>
        /// Set while a press that began on the figure is still over it. The ground under his chest
        /// or his head on screen is behind him on the sand - the camera looks down at an angle - so
        /// taking those points as the run would start every run with a step backwards.
        /// </summary>
        private bool _drawStillOnFigure;

        /// <summary>
        /// How far apart, in virtual units, the points of a drawn run are taken. Half a body: close
        /// enough to follow a curve, far enough apart that the tremor of a finger does not become a
        /// zig-zag he turns along one frame at a time.
        /// </summary>
        private const float DrawSampleSpacing = GameConstants.GladiatorRadius * 0.5f;

        /// <summary>
        /// Starts drawing from a real press. On the figure, the run starts on him; anywhere else, he
        /// is joined to the press by the way he would run there.
        /// </summary>
        public bool BeginDrawFromScreen(Vector3 screenPos)
        {
            var g = PlayerGladiator();
            if (g == null) return false;

            if (PressIsOnGladiator(screenPos, g))
            {
                _drawStillOnFigure = true;
                return BeginDraw(g.Pos);
            }

            _drawStillOnFigure = false;
            return TryScreenToVirtual(screenPos, out var point) && BeginDraw(point);
        }

        /// <summary>Carries a real press on - ignored while it is still over the figure it began on.</summary>
        public void DrawToScreen(Vector3 screenPos)
        {
            if (!IsDrawing) return;

            if (_drawStillOnFigure)
            {
                var g = PlayerGladiator();
                if (g != null && PressIsOnGladiator(screenPos, g)) return;
                _drawStillOnFigure = false;
            }

            if (TryScreenToVirtual(screenPos, out var point)) DrawTo(point);
        }

        /// <summary>
        /// Starts a new run at a point on the sand. The run begins on the gladiator whatever the
        /// point is: a press on him starts there, and a press anywhere else is the first stretch of
        /// the run, from him to it.
        ///
        /// The order already standing is left alone until the new run is long enough to be one, so
        /// a stray touch does not wipe out a run that was drawn with care.
        /// </summary>
        public bool BeginDraw(Vector2 virtualPoint)
        {
            var g = PlayerGladiator();
            if (g == null) return false;

            // Only a pull in progress is cancelled - cancelling clears the lane, and the lane on
            // screen belongs to the order that stands until this stroke replaces it.
            if (IsDragging) CancelDrag();

            _previousRun.Clear();
            _previousRun.AddRange(_drawnRun);
            _drawnRun.Clear();
            _drawnRun.Add(g.Pos);
            IsDrawing = true;
            DrawExhausted = false;

            DrawTo(virtualPoint);
            return true;
        }

        /// <summary>
        /// Extends the run to where the finger is now, round anything in the way, and files it.
        ///
        /// Stops taking points once his reach is spent: the run is cut exactly where it runs out and
        /// nothing after that is drawn, however far the finger goes on. Filed as it grows rather
        /// than on release, so a planning phase that ends mid-stroke keeps what was drawn.
        /// </summary>
        public void DrawTo(Vector2 virtualPoint)
        {
            if (!IsDrawing || DrawExhausted) return;
            var g = PlayerGladiator();
            if (g == null) { EndDraw(); return; }

            var last = _drawnRun[_drawnRun.Count - 1];
            if (Vector2.Distance(last, virtualPoint) < DrawSampleSpacing) return;

            var leg = Controller.Manager.PlanLeg(last, virtualPoint);
            for (int i = 1; i < leg.Count; i++) _drawnRun.Add(leg[i]);

            float reach = g.PlannedReach();
            if (ObstacleField.Length(_drawnRun) >= reach)
            {
                var cut = ObstacleField.Truncate(_drawnRun, reach);
                _drawnRun.Clear();
                _drawnRun.AddRange(cut);
                DrawExhausted = true;
            }

            // Shorter than a body is a touch, not a run - the same rule a tap has.
            if (ObstacleField.Length(_drawnRun) < GameConstants.GladiatorRadius) return;

            DefendArmed = false;
            Controller.SubmitPlayerPath(_drawnRun);
            ShowTapOrder(_drawnRun[_drawnRun.Count - 1], _drawnRun);
        }

        /// <summary>The finger lifted. Whatever was drawn is already filed; this only stops drawing.</summary>
        public void EndDraw()
        {
            // A stroke that never became a run leaves the one before it standing, corners and all,
            // so the ability can still cut it to a new reach.
            if (IsDrawing && ObstacleField.Length(_drawnRun) < GameConstants.GladiatorRadius)
            {
                _drawnRun.Clear();
                _drawnRun.AddRange(_previousRun);
            }

            IsDrawing = false;
            _drawStillOnFigure = false;
        }

        /// <summary>
        /// Files the drawn run again after something changed how far he can run - the ability armed
        /// or taken back - so the order and the lane are cut to the reach he now has.
        /// </summary>
        private void RefileDrawnRun()
        {
            var g = PlayerGladiator();
            if (g == null || _drawnRun.Count < 2 || g.PlannedAction != ActionType.Move) return;

            Controller.SubmitPlayerPath(_drawnRun);
            if (g.PlannedPath.Count > 1) ShowTapOrder(g.PlannedPath[g.PlannedPath.Count - 1], g.PlannedPath);
            DrawExhausted = ObstacleField.Length(g.PlannedPath) >= g.PlannedReach() - 0.01f;
        }

        /// <summary>
        /// Whether a press landed on the gladiator as he is drawn, rather than on the ground under
        /// him. The camera looks down at 66 degrees, so the model stands well above the point it
        /// occupies on the floor and a press on the chest or the head projects onto the sand behind
        /// him. The target is the segment from his feet to the top of his head, plus the width of a
        /// thumb, so what counts as him is exactly what is drawn.
        /// </summary>
        private bool PressIsOnGladiator(Vector3 screenPos, GladiatorInstance g)
        {
            if (ArenaCamera == null || Controller == null || Controller.Arena == null) return false;

            var arena = Controller.Arena;
            var feet = ArenaCamera.WorldToScreenPoint(arena.ToWorld(g.Pos));
            var head = ArenaCamera.WorldToScreenPoint(
                arena.ToWorld(g.Pos, arena.ScaleLength(GameConstants.GladiatorRadius) * BodyHeightInRadii));

            // Behind the camera - not something a fixed camera above the arena can produce, but a
            // negative w would otherwise fold the screen point back on itself and grab at random.
            if (feet.z <= 0f || head.z <= 0f) return false;

            float grabPixels = Screen.height * GrabScreenFraction;
            return DistanceToSegment(screenPos, feet, head) <= grabPixels;
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

            // Spirit changes how far he can run, so a run already drawn is cut to the new reach.
            RefileDrawnRun();
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
        /// Marks where the player sent him and draws the run he will make.
        ///
        /// Tapping used to have no feedback at all: the order went in and nothing on screen
        /// acknowledged it until the gladiators started moving. The ring sits on the point he was
        /// sent to and the lane shows the actual run round the obstacles, all the way to the ring -
        /// a run always arrives now, so the lane never has anywhere short of it to stop.
        /// </summary>
        private void ShowTapOrder(Vector2 target, List<Vector2> run)
        {
            if (Controller.Arena == null) return;

            if (_tapMarker != null)
            {
                _tapMarker.transform.position = Controller.Arena.ToWorld(target, 0.05f);
                _tapMarker.SetActive(true);
            }

            DrawLane(run);
        }

        /// <summary>Lays the lane along a run, corner to corner, and puts the head on the end of it.</summary>
        private void DrawLane(List<Vector2> run)
        {
            if (_trajectory == null || Controller.Arena == null) return;

            if (run == null || run.Count < 2)
            {
                Hide(_trajectory);
                return;
            }

            _worldPoints.Clear();
            foreach (var p in run)
                _worldPoints.Add(Controller.Arena.ToWorld(p, TrajectoryHeight));

            _trajectory.positionCount = _worldPoints.Count;
            for (int i = 0; i < _worldPoints.Count; i++)
                _trajectory.SetPosition(i, _worldPoints[i]);
            _trajectory.enabled = true;
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
            _drawnRun.Clear();
            DrawExhausted = false;
            if (_tapMarker != null && _tapMarker.activeSelf) _tapMarker.SetActive(false);
            Hide(_trajectory);
        }

        private void DrawTrajectory(GladiatorInstance g) => DrawRun(g, CurrentAim, CurrentPower);

        /// <summary>
        /// Draws the lane for an aim and a power - the drag controls' way of giving an order - by
        /// turning them into the point they reach and routing to it like any other order. The same
        /// conversion GameManager.SubmitPlanningAction makes, so the lane is the run that will be
        /// filed.
        /// </summary>
        private void DrawRun(GladiatorInstance g, Vector2 aim, float power)
        {
            if (_trajectory == null || Controller.Arena == null) return;

            if (power <= MinPowerToSubmit || aim == Vector2.zero)
            {
                Hide(_trajectory);
                return;
            }

            var target = g.Pos + aim.normalized * (Mathf.Clamp01(power) * g.PlannedReach());
            DrawLane(Controller.Manager.ComputeTrajectoryPreview(g, target));
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
