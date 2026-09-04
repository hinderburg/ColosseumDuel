using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// Eases the camera in slightly while the player is planning and back out for the action phase,
    /// to mark the change of tempo the design doc asks for.
    ///
    /// The doc describes this as a slow-motion effect, but nothing on the arena moves during
    /// Planning - the phase is a timer and two decisions - so slowing time would only stretch the
    /// three seconds the player gets. A gentle push-in reads as the same beat without touching
    /// Time.timeScale, which the simulation's phase timers run on.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PlanningFocusCamera : MonoBehaviour
    {
        public GameController Controller;

        [Tooltip("How much closer the camera sits during Planning, as a fraction of its resting size.")]
        [Range(0f, 0.4f)] public float ZoomAmount = 0.08f;

        [Tooltip("Seconds to ease between the two framings.")]
        public float EaseTime = 0.35f;

        private Camera _camera;
        private float _restingSize;
        private float _velocity;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _restingSize = _camera.orthographicSize;
        }

        private void LateUpdate()
        {
            if (Controller == null || Controller.Manager == null) return;

            bool planning = Controller.Manager.State.Phase == MatchPhase.Planning;
            float target = planning ? _restingSize * (1f - ZoomAmount) : _restingSize;

            _camera.orthographicSize =
                Mathf.SmoothDamp(_camera.orthographicSize, target, ref _velocity, EaseTime);
        }
    }
}
