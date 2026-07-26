using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// A stand-in for the player's torso, derived from the headset pose.
    /// </summary>
    /// <remarks>
    /// A VR rig tracks a head and two hands and knows nothing about the body between them, but
    /// enemies need somewhere to walk toward and somewhere to take hold of, and neither can hang
    /// off the camera directly: look down and a camera-parented shoulder would swing forward and
    /// down with your gaze. So this follows the head's position and its <em>yaw only</em>, at floor
    /// level. It is the same trick the template's <c>XRAvatarIK</c> uses to place a torso under a
    /// replicated head, kept local and much smaller.
    /// <para>
    /// It also places its own <see cref="LatchAnchor"/> children, as a fraction of the player's real
    /// eye height rather than at fixed heights. A fixed shoulder height authored for a tall player
    /// ends up level with a short player's chin, where the head-clearance guard refuses it and
    /// leapers quietly stop being able to land on anyone.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public class PlayerBodyProxy : MonoBehaviour
    {
        /// <summary>
        /// Shortest eye height the anchors are placed from, in meters.
        /// </summary>
        /// <remarks>
        /// The headset reports a height near zero before tracking settles, and for the frames where
        /// it does every anchor would collapse onto the floor. Clamping keeps the body plausible
        /// until real tracking arrives.
        /// </remarks>
        public const float k_MinEyeHeight = 0.9f;

        /// <summary>Below this a direction vector is treated as having no direction at all.</summary>
        const float k_DirectionEpsilon = 1e-6f;

        [SerializeField]
        [Tooltip("La cámara del jugador. De aquí salen la posición y el giro del cuerpo.")]
        Transform m_Head;

        LatchAnchor[] m_Anchors;

        /// <summary>The head this body is derived from.</summary>
        public Transform head
        {
            get => m_Head;
            set => m_Head = value;
        }

        /// <summary>Where the player is standing: under the head, at floor level.</summary>
        public Vector3 bodyPosition => transform.position;

        /// <summary>Center of the player's head, the point latch anchors have to stay clear of.</summary>
        public Vector3 headPosition => m_Head != null ? m_Head.position : transform.position;

        /// <summary>The player's tracked eye height, in meters, never below <see cref="k_MinEyeHeight"/>.</summary>
        public float eyeHeight =>
            m_Head != null ? Mathf.Max(k_MinEyeHeight, m_Head.position.y - FloorHeight()) : k_MinEyeHeight;

        /// <inheritdoc/>
        void Awake()
        {
            // Only the anchors in this body's own subtree. The arm anchors are deliberately not
            // here: an arm follows the controller, not the torso, so they hang off the hand
            // transforms and keep whatever local offset the builder gave them.
            m_Anchors = GetComponentsInChildren<LatchAnchor>(true);
        }

        /// <summary>
        /// Follows the head after the XR pose providers have written it.
        /// </summary>
        /// <remarks>
        /// <c>LateUpdate</c> for the same reason <see cref="PointVelocityTracker"/> samples there:
        /// the tracked poses are written during <c>Update</c>, so anything reading them earlier is a
        /// frame behind, and a body that lags the head by a frame drags its anchors through the
        /// world every time the player turns.
        /// </remarks>
        void LateUpdate()
        {
            if (m_Head == null)
                return;

            FollowHead();
            PlaceAnchors();
        }

        void FollowHead()
        {
            var headPos = m_Head.position;
            transform.position = new Vector3(headPos.x, FloorHeight(), headPos.z);

            // Looking straight up or down leaves no horizontal component of forward to yaw by. The
            // head's up axis points along the body in exactly those poses, so it stands in; if both
            // degenerate the body simply keeps the rotation it had.
            var forward = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up);
            if (forward.sqrMagnitude <= k_DirectionEpsilon)
                forward = Vector3.ProjectOnPlane(m_Head.up, Vector3.up);

            if (forward.sqrMagnitude > k_DirectionEpsilon)
                transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        void PlaceAnchors()
        {
            if (m_Anchors == null)
                return;

            float height = eyeHeight;

            foreach (var anchor in m_Anchors)
            {
                if (anchor == null)
                    continue;

                var offset = anchor.bodyOffset;
                anchor.transform.localPosition = new Vector3(
                    offset.x, anchor.heightFraction * height, offset.y);
            }
        }

        /// <summary>
        /// Height of the floor the player is standing on.
        /// </summary>
        /// <remarks>
        /// Taken from the parent, which on a floor-tracked rig is the camera offset sitting at the
        /// play area's floor plane. Falling back to this object's own height keeps a body proxy
        /// built standalone — in a test, say — from measuring eye height against the world origin.
        /// </remarks>
        float FloorHeight() => transform.parent != null ? transform.parent.position.y : transform.position.y;
    }
}
