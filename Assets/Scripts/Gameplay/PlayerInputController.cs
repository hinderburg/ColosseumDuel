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

        [Tooltip("How far from the gladiator, in virtual units, a press still counts as grabbing them.")]
        public float GrabRadiusVirtual = GameConstants.GladiatorRadius * 3f;

        [Tooltip("Pulls shorter than this fraction of the maximum are treated as a mis-click, not a move.")]
        public float MinPowerToSubmit = 0.05f;

        /// <summary>Armed by the ability toggle; consumed by the next submitted action.</summary>
        public bool AbilityArmed { get; private set; }

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
                if (!IsPointerOverHud() && TryScreenToVirtual(Input.mousePosition, out var start))
                    TryBeginDrag(start);
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
            if (Input.GetKeyDown(KeyCode.Space)) SubmitDefend();
        }

        // ------------------------------------------------------------------
        // the drag itself - device independent, so tests can drive it directly
        // ------------------------------------------------------------------

        /// <summary>Starts a pull, but only if the press landed on the player's own gladiator.</summary>
        public bool TryBeginDrag(Vector2 virtualPoint)
        {
            var g = PlayerGladiator();
            if (g == null) return false;
            if (Vector2.Distance(virtualPoint, g.Pos) > GrabRadiusVirtual) return false;

            IsDragging = true;
            UpdateDrag(virtualPoint);
            return true;
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

        public void SubmitDefend()
        {
            if (PlayerGladiator() == null) return;
            CancelDrag();
            Controller.SubmitPlayerDefend(AbilityArmed);
            AbilityArmed = false;
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
