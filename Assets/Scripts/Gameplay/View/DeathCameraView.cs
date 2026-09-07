using ColosseumDuel.Core;
using UnityEngine;

namespace ColosseumDuel.Gameplay.View
{
    /// <summary>
    /// Moves in on a knockout and comes back afterwards.
    ///
    /// The camera is otherwise fixed, which is most of what makes this arena readable - the player
    /// learns one frame and every distance in it. A death is the one moment worth breaking that
    /// for: it is the only thing in a match that cannot be undone, and at the standing distance a
    /// gladiator falling over is about twenty pixels of movement.
    ///
    /// It keeps the pitch it always had and only closes the distance, so the shot is the same shot,
    /// nearer. Swinging the angle as well would leave the player re-reading the arena on the frame
    /// the next round starts.
    ///
    /// The distance is computed to fit whoever actually died rather than fixed, because a double
    /// knockout can put the two bodies at opposite ends of the oval - a zoom that framed one of
    /// them would be showing the wrong half of the most important second in the round.
    /// </summary>
    public sealed class DeathCameraView : MonoBehaviour
    {
        public GameController Controller;
        public ArenaView Arena;

        /// <summary>How fast the camera closes in and returns, in fractions of the way per second.</summary>
        [Range(1f, 20f)] public float MoveSpeed = 7f;

        /// <summary>
        /// Margin around the bodies, as a share of the extent being framed. Generous, because a
        /// figure is drawn from the ground up and the point it stands on is not its centre.
        /// </summary>
        [Range(1f, 3f)] public float Margin = 1.8f;

        /// <summary>Closest the camera will ever come, in world units, whatever the maths says.</summary>
        public float MinDistance = 4f;

        private Camera _camera;
        private Vector3 _home;
        private float _homeDistance;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _home = transform.position;

            // Distance from the point the camera is aimed at, measured along its own forward axis at
            // the height the fighters stand on. Everything below moves along that same line.
            var plane = new Plane(Vector3.up, Vector3.zero);
            _homeDistance = plane.Raycast(new Ray(_home, transform.forward), out float enter)
                ? enter
                : _home.magnitude;
        }

        private void LateUpdate()
        {
            var manager = Controller != null ? Controller.Manager : null;
            if (manager == null || _camera == null) return;

            var state = manager.State;
            var target = _home;

            if (state.Phase == MatchPhase.RoundEnd && TryFrameTheFallen(state, out var closeUp))
                target = closeUp;

            // Unscaled, because the whole point of this moment is that the world is running slowly.
            // On scaled time the camera would crawl in at the same quarter speed as the death it is
            // trying to show, and arrive after the round was over.
            transform.position = Vector3.Lerp(transform.position, target,
                1f - Mathf.Exp(-MoveSpeed * Time.unscaledDeltaTime));

            // Straight home the moment a round starts, rather than drifting there. A round that
            // opened with the camera still sliding would have the player reading a frame that is
            // about to change under them.
            if (state.Phase == MatchPhase.Reveal || state.Phase == MatchPhase.Pick)
                transform.position = _home;
        }

        /// <summary>
        /// Where the camera has to sit for every fallen gladiator to be in shot.
        ///
        /// False when nobody is down, which happens on the frames of RoundEnd before the state has
        /// caught up and on a round that ends some other way.
        /// </summary>
        private bool TryFrameTheFallen(MatchState state, out Vector3 position)
        {
            position = _home;
            if (Arena == null) return false;

            var fallen = Vector3.zero;
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            int count = 0;

            foreach (var side in new[] { PlayerSide.P1, PlayerSide.Bot })
            {
                var g = state.Get(side).Active;
                if (g == null || g.Alive) continue;

                var world = Arena.ToWorld(g.Pos);
                fallen += world;
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
                count++;
            }

            if (count == 0) return false;

            var focus = fallen / count;

            // The narrower of the two half-angles: a body can be anywhere in the frame, and framing
            // to the wider one would cut it off across the short side.
            float halfVertical = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfHorizontal = Mathf.Atan(Mathf.Tan(halfVertical) * _camera.aspect);
            float halfAngle = Mathf.Min(halfVertical, halfHorizontal);

            float spread = count > 1 ? (max - min).magnitude * 0.5f : 0f;
            float bodyExtent = Arena.ScaleLength(GameConstants.GladiatorRadius) * 4f;
            float extent = Mathf.Max(spread, bodyExtent) * Margin;

            float distance = Mathf.Clamp(extent / Mathf.Tan(halfAngle), MinDistance, _homeDistance);
            position = focus - transform.forward * distance;
            return true;
        }
    }
}
