using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// A short positional shake for moments of impact.
    ///
    /// Applied as an offset from the camera's resting position rather than by moving the camera
    /// outright, so it composes with PlanningFocusCamera's zoom instead of fighting it - the two
    /// drive different properties and neither has to know about the other.
    /// </summary>
    public sealed class CameraShake : MonoBehaviour
    {
        [Tooltip("World units of displacement at full strength.")]
        public float Amplitude = 0.35f;

        [Tooltip("Seconds a full-strength shake takes to settle.")]
        public float Duration = 0.35f;

        [Tooltip("Shakes per second.")]
        public float Frequency = 26f;

        private Vector3 _restingPosition;
        private float _timeLeft;
        private float _strength;
        private float _phase;

        public bool IsShaking => _timeLeft > 0f;

        private void Awake()
        {
            _restingPosition = transform.position;
        }

        /// <param name="strength">0..1. Scales both how far it throws the camera and how long it rings.</param>
        public void Shake(float strength = 1f)
        {
            strength = Mathf.Clamp01(strength);
            // A new impact during an existing shake should not cut it short.
            if (strength < _strength && _timeLeft > 0f) return;

            _strength = strength;
            _timeLeft = Duration * strength;
            _phase = Random.value * 100f;
        }

        private void LateUpdate()
        {
            if (_timeLeft <= 0f)
            {
                if (transform.position != _restingPosition) transform.position = _restingPosition;
                return;
            }

            _timeLeft = Mathf.Max(0f, _timeLeft - Time.deltaTime);
            float decay = Duration > 0f ? _timeLeft / (Duration * Mathf.Max(_strength, 0.0001f)) : 0f;
            float amount = Amplitude * _strength * decay * decay; // ease out, so it settles rather than stops

            // Two different frequencies so the motion does not read as a clean sine wave.
            float t = Time.time * Frequency + _phase;
            var offset = new Vector3(Mathf.Sin(t) * amount, 0f, Mathf.Cos(t * 1.37f) * amount);
            transform.position = _restingPosition + offset;
        }
    }
}
