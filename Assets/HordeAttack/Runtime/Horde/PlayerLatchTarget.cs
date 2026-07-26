using System;
using System.Collections.Generic;
using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// The player as far as the horde is concerned: somewhere to walk toward, and a set of places
    /// to take hold of.
    /// </summary>
    /// <remarks>
    /// Sits on the same object as the <see cref="PlayerBodyProxy"/> that derives the torso from the
    /// headset. Enemies find it through <see cref="FindNearest"/> rather than by looking up a name,
    /// which is already the shape Fase 4 needs when there are two of these in the session.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerBodyProxy))]
    public class PlayerLatchTarget : MonoBehaviour
    {
        static readonly List<PlayerLatchTarget> s_Instances = new List<PlayerLatchTarget>();

        [SerializeField]
        [Tooltip("Raíz desde la que se buscan las anclas. Vacío = la raíz de la jerarquía de este objeto.")]
        Transform m_AnchorRoot;

        [SerializeField]
        [Tooltip("Radio prohibido alrededor de la cabeza. Ningún enemigo se cuelga de un ancla más cerca que esto.")]
        float m_HeadClearance = LatchAnchorSelector.k_DefaultHeadClearance;

        readonly List<LatchAnchorSlot> m_Slots = new List<LatchAnchorSlot>();
        readonly List<LatchAnchor> m_Usable = new List<LatchAnchor>();

        LatchAnchor[] m_Anchors;
        PlayerBodyProxy m_Body;

        /// <summary>Raised when an enemy takes hold, with the anchor it took.</summary>
        public event Action<HordeEnemy, LatchAnchor> OnEnemyLatched;

        /// <summary>Raised when an enemy lets go, however that happened.</summary>
        public event Action<HordeEnemy, LatchAnchor> OnEnemyDetached;

        /// <summary>Where the player is standing, at floor level.</summary>
        public Vector3 bodyPosition => m_Body != null ? m_Body.bodyPosition : transform.position;

        /// <summary>Center of the player's head.</summary>
        public Vector3 headPosition => m_Body != null ? m_Body.headPosition : transform.position;

        /// <summary>Radius around the head inside which no anchor may be used, in meters.</summary>
        public float headClearance => m_HeadClearance;

        /// <summary>Every anchor on this player, whether free, taken, or on an inactive hand branch.</summary>
        public IReadOnlyList<LatchAnchor> anchors => m_Anchors;

        /// <summary>How many enemies currently have hold of this player.</summary>
        /// <remarks>
        /// Nothing reads this yet on purpose — being latched costs nothing until Fase 5. It is the
        /// number the health drain will be proportional to, and publishing it now is what keeps
        /// that a small change.
        /// </remarks>
        public int latchedCount
        {
            get
            {
                int count = 0;

                if (m_Anchors != null)
                {
                    foreach (var anchor in m_Anchors)
                    {
                        if (anchor != null && !anchor.isFree)
                            count++;
                    }
                }

                return count;
            }
        }

        /// <inheritdoc/>
        void Awake()
        {
            m_Body = GetComponent<PlayerBodyProxy>();

            var root = m_AnchorRoot != null ? m_AnchorRoot : transform.root;

            // Include inactive: XRI's input modality manager switches the controller and hand
            // tracking branches on and off at runtime, so half the arm anchors are switched off at
            // any moment and which half is not known when this runs.
            m_Anchors = root.GetComponentsInChildren<LatchAnchor>(true);

            foreach (var anchor in m_Anchors)
                anchor.owner = this;
        }

        /// <inheritdoc/>
        void OnEnable() => s_Instances.Add(this);

        /// <inheritdoc/>
        void OnDisable() => s_Instances.Remove(this);

        /// <summary>
        /// Returns the player closest to <paramref name="position"/>, or null if there are none.
        /// </summary>
        /// <remarks>
        /// A registry rather than <c>FindFirstObjectByType</c>: every enemy asks this every time it
        /// needs a target, and a scene-wide search per enemy per frame is exactly the cost Fase 3
        /// cannot afford at 20-30 of them.
        /// </remarks>
        public static PlayerLatchTarget FindNearest(Vector3 position)
        {
            PlayerLatchTarget nearest = null;
            float bestDistance = float.PositiveInfinity;

            foreach (var candidate in s_Instances)
            {
                if (candidate == null)
                    continue;

                float distance = (candidate.bodyPosition - position).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                nearest = candidate;
            }

            return nearest;
        }

        /// <summary>
        /// Claims the anchor <paramref name="style"/> calls for on behalf of
        /// <paramref name="enemy"/>, without attaching it yet.
        /// </summary>
        /// <remarks>
        /// Choosing a slot and claiming it are one step on purpose: split them and two enemies
        /// arriving on the same frame are both told the same anchor is free.
        /// <para>
        /// Reserving ahead of attaching is what a leap needs. The jump takes about half a second,
        /// it has to have somewhere to aim at for all of it, and that destination cannot be taken
        /// by a creature that walked up while this one was in the air — it would land inside the
        /// other one. Callers that take hold immediately want <see cref="TryLatch"/>.
        /// </para>
        /// </remarks>
        public bool TryReserve(HordeEnemy enemy, LatchStyle style, out LatchAnchor anchor)
        {
            anchor = null;

            if (enemy == null || !enemy.isAlive || m_Anchors == null)
                return false;

            CollectUsableAnchors();

            int index = LatchAnchorSelector.Select(
                m_Slots, style, bodyPosition, enemy.transform.position, headPosition, m_HeadClearance);

            if (index < 0)
                return false;

            var candidate = m_Usable[index];
            if (!candidate.TryOccupy(enemy))
                return false;

            anchor = candidate;

            return true;
        }

        /// <summary>
        /// Attaches <paramref name="enemy"/> to an anchor it already reserved.
        /// </summary>
        /// <remarks>
        /// Refuses if the reservation is no longer this enemy's, which is what stops a leap that
        /// was interrupted and cleaned up from landing anyway a frame later.
        /// </remarks>
        public bool CompleteLatch(HordeEnemy enemy, LatchAnchor anchor)
        {
            if (enemy == null || anchor == null || anchor.occupant != enemy || !enemy.isAlive)
                return false;

            enemy.AttachTo(anchor);
            OnEnemyLatched?.Invoke(enemy, anchor);

            return true;
        }

        /// <summary>
        /// Gives back an anchor that was reserved but never used.
        /// </summary>
        /// <remarks>
        /// No <see cref="OnEnemyDetached"/>: nothing ever took hold, so from the player's point of
        /// view nothing happened. Firing it here would make the feedback flash for an attack that
        /// was stopped before it landed.
        /// </remarks>
        public void CancelReservation(HordeEnemy enemy, LatchAnchor anchor)
        {
            if (anchor != null)
                anchor.Release(enemy);
        }

        /// <summary>
        /// Claims an anchor and takes hold of it in one step, for an enemy already close enough.
        /// </summary>
        /// <remarks>
        /// Hands the anchor back if the second half fails, so a refused latch cannot leave a slot
        /// reserved by an enemy that never arrived — a leak nobody would notice until the player
        /// ran out of places to be grabbed.
        /// </remarks>
        public bool TryLatch(HordeEnemy enemy, LatchStyle style)
        {
            if (!TryReserve(enemy, style, out var anchor))
                return false;

            if (CompleteLatch(enemy, anchor))
                return true;

            CancelReservation(enemy, anchor);

            return false;
        }

        /// <summary>
        /// Reports that <paramref name="enemy"/> let go of <paramref name="anchor"/>.
        /// </summary>
        /// <remarks>
        /// Called by the enemy, which is the only thing that knows every way a hold can end: being
        /// punched off, dying, or being recycled back to its spawn.
        /// </remarks>
        internal void NotifyDetached(HordeEnemy enemy, LatchAnchor anchor)
        {
            OnEnemyDetached?.Invoke(enemy, anchor);
        }

        /// <summary>
        /// Rebuilds the parallel lists of anchors and of the plain values the selector reads.
        /// </summary>
        /// <remarks>
        /// Anchors on a switched-off hand branch are dropped here rather than in the selector: an
        /// enemy latched to the hand-tracking anchor while the player is holding controllers would
        /// hang in mid-air off an object that is not being posed by anything.
        /// </remarks>
        void CollectUsableAnchors()
        {
            m_Slots.Clear();
            m_Usable.Clear();

            foreach (var anchor in m_Anchors)
            {
                if (anchor == null || !anchor.isActiveAndEnabled)
                    continue;

                m_Usable.Add(anchor);
                m_Slots.Add(new LatchAnchorSlot(anchor.transform.position, anchor.height, anchor.isFree));
            }
        }
    }
}
