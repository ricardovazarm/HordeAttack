using System.Collections.Generic;
using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// A place on the player an enemy could take hold of, reduced to what choosing between them
    /// needs: where it is, which band of the body it belongs to, and whether anyone has it.
    /// </summary>
    /// <remarks>
    /// A plain value rather than the <see cref="LatchAnchor"/> component, so the choice itself is
    /// ordinary maths that can be tested without building a player rig.
    /// </remarks>
    public readonly struct LatchAnchorSlot
    {
        /// <summary>World position of the anchor.</summary>
        public readonly Vector3 position;

        /// <summary>Which band of the body this anchor belongs to.</summary>
        public readonly LatchHeight height;

        /// <summary>Whether no enemy currently holds this anchor.</summary>
        public readonly bool isFree;

        public LatchAnchorSlot(Vector3 position, LatchHeight height, bool isFree)
        {
            this.position = position;
            this.height = height;
            this.isFree = isFree;
        }
    }

    /// <summary>
    /// Picks which anchor an arriving enemy takes hold of. Pure maths, no Unity lifecycle.
    /// </summary>
    /// <remarks>
    /// Three rules, in order of how much they matter:
    /// <list type="number">
    /// <item>An anchor too close to the player's head is never offered, whatever else is true. An
    /// enemy hanging in front of the visor does not read as frightening in VR, it reads as a bug,
    /// and it is the fastest way to make someone take the headset off.</item>
    /// <item>One enemy per anchor. Without that they pile into the same spot and the player sees a
    /// single flickering lump instead of three creatures.</item>
    /// <item>Everything else is preference: the band the style wants, and the side the enemy is
    /// arriving from, so a creature coming at you from the left ends up on your left.</item>
    /// </list>
    /// </remarks>
    public static class LatchAnchorSelector
    {
        /// <summary>
        /// How close to the player's head an anchor may sit before it is refused outright, in
        /// meters.
        /// </summary>
        /// <remarks>
        /// This is not a tuning knob for feel, it is the comfort guard. It exists because anchors
        /// are placed as a fraction of the player's real eye height and the player can crouch,
        /// which brings the head down toward the shoulders without moving the anchors.
        /// </remarks>
        public const float k_DefaultHeadClearance = 0.3f;

        /// <summary>Below this a direction vector is treated as having no direction at all.</summary>
        const float k_DirectionEpsilon = 1e-6f;

        /// <summary>
        /// How much matching the preferred band outweighs arriving from the right side. Large
        /// enough that a leaper always prefers a free high anchor to a well-placed low one.
        /// </summary>
        const float k_HeightPreferenceWeight = 10f;

        /// <summary>Returns the band <paramref name="style"/> aims for.</summary>
        public static LatchHeight PreferredHeight(LatchStyle style) =>
            style == LatchStyle.Leaper ? LatchHeight.High : LatchHeight.Low;

        /// <summary>
        /// Returns the index of the anchor <paramref name="style"/> should take, or -1 when there
        /// is nothing left to take hold of.
        /// </summary>
        /// <remarks>
        /// A style that finds no free anchor in its own band falls back to the other one rather
        /// than giving up: an enemy that reaches the player and then stands there doing nothing
        /// looks broken, and by Fase 3 there will be far more enemies than anchors.
        /// </remarks>
        /// <param name="slots">Every anchor on the player, free or not.</param>
        /// <param name="style">How this enemy is arriving.</param>
        /// <param name="bodyPosition">Center of the player's body, used to work out which side an anchor is on.</param>
        /// <param name="enemyPosition">Where the enemy is coming from.</param>
        /// <param name="headPosition">Center of the player's head, the point anchors must stay clear of.</param>
        /// <param name="headClearance">Radius of that clearance, in meters. See <see cref="k_DefaultHeadClearance"/>.</param>
        public static int Select(
            IReadOnlyList<LatchAnchorSlot> slots,
            LatchStyle style,
            Vector3 bodyPosition,
            Vector3 enemyPosition,
            Vector3 headPosition,
            float headClearance = k_DefaultHeadClearance)
        {
            if (slots == null)
                return -1;

            var preferred = PreferredHeight(style);
            var approach = Horizontal(enemyPosition - bodyPosition);
            bool hasApproach = approach.sqrMagnitude > k_DirectionEpsilon;
            if (hasApproach)
                approach.Normalize();

            int best = -1;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];

                if (!slot.isFree)
                    continue;

                if (!IsClearOfHead(slot.position, headPosition, headClearance))
                    continue;

                float score = slot.height == preferred ? k_HeightPreferenceWeight : 0f;

                // Anchors on the body's centre line have no side, so they score neutrally and are
                // taken only when nothing better is free.
                if (hasApproach)
                {
                    var side = Horizontal(slot.position - bodyPosition);
                    if (side.sqrMagnitude > k_DirectionEpsilon)
                        score += Vector3.Dot(side.normalized, approach);
                }

                // Strictly greater, so ties go to the earlier anchor and the choice is repeatable.
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Whether an anchor sits far enough from the player's head to be usable.
        /// </summary>
        /// <remarks>
        /// Checked against the anchor rather than the enemy's centre because an enemy hangs
        /// downward from the point it grabs: the anchor is the closest the creature ever gets to
        /// the player's face.
        /// </remarks>
        public static bool IsClearOfHead(Vector3 anchorPosition, Vector3 headPosition, float clearance)
        {
            if (clearance <= 0f)
                return true;

            return (anchorPosition - headPosition).sqrMagnitude >= clearance * clearance;
        }

        static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
