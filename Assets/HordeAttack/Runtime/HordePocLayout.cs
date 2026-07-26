using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// Canonical layout of the POC arena: the names the scene builder assigns to the
    /// objects it creates, the arena dimensions, and the math used to distribute
    /// spawn positions around the player.
    /// </summary>
    /// <remarks>
    /// This is the shared contract between the editor scene builder and anything that
    /// needs to locate arena objects at runtime, so the names live in one place only.
    /// </remarks>
    public static class HordePocLayout
    {
        public const string k_ArenaCenterName = "Arena Center";
        public const string k_GroundName = "Ground";
        public const string k_LightName = "Directional Light";
        public const string k_PlayerRigName = "XR Origin (POC)";
        public const string k_DummyRootName = "Dummies";
        public const string k_DummyPrefix = "Dummy_";
        public const string k_FistName = "Fist";

        /// <summary>
        /// Children of the rig's camera offset that track a hand, in the order the builder
        /// walks them.
        /// </summary>
        /// <remarks>
        /// Both the controller and the hand-tracking branch are listed on purpose. XRI's
        /// <c>XRInputModalityManager</c> enables exactly one pair at runtime depending on what the
        /// headset reports, and which one that is cannot be known when the scene is built. Putting
        /// a fist under all four means the player sees one whichever branch wins.
        /// </remarks>
        public static readonly string[] k_HandAnchorNames =
        {
            "Left Controller",
            "Right Controller",
            "Left Hand",
            "Right Hand",
        };

        /// <summary>Diameter of the fist marker, roughly a closed adult hand, in meters.</summary>
        public const float k_FistDiameter = 0.11f;

        /// <summary>
        /// World-space radius of the trigger that registers punches, in meters.
        /// </summary>
        /// <remarks>
        /// Deliberately wider than the fist itself. VR hands meet no resistance, so by the time the
        /// visible fist is inside an enemy the player has already swung past it; a slightly generous
        /// trigger registers the hit at the moment it looks like contact rather than after it.
        /// </remarks>
        public const float k_PunchTriggerRadius = 0.09f;

        /// <summary>
        /// Radius the punch trigger needs in the fist's local space to end up
        /// <see cref="k_PunchTriggerRadius"/> across in the world.
        /// </summary>
        /// <remarks>
        /// The fist is scaled uniformly by <see cref="k_FistDiameter"/>, and a
        /// <see cref="UnityEngine.SphereCollider"/>'s radius is in local units, so the world radius
        /// has to be divided back out. Authoring the world figure and converting keeps the tuning
        /// value in the unit a designer thinks in.
        /// </remarks>
        public const float k_PunchTriggerLocalRadius = k_PunchTriggerRadius / k_FistDiameter;

        /// <summary>
        /// Returns which hand <paramref name="anchorName"/> belongs to.
        /// </summary>
        /// <remarks>
        /// Handedness has to be baked in when the scene is built because haptics are addressed by
        /// hand, not by transform: <c>HapticsUtility</c> takes a left/right selector, so the fist
        /// has to know which controller to buzz.
        /// </remarks>
        /// <param name="anchorName">One of <see cref="k_HandAnchorNames"/>.</param>
        public static HandSide HandSideOf(string anchorName)
        {
            if (string.IsNullOrEmpty(anchorName))
                throw new System.ArgumentException("Hand anchor name is empty.", nameof(anchorName));

            if (anchorName.StartsWith("Left", System.StringComparison.OrdinalIgnoreCase))
                return HandSide.Left;
            if (anchorName.StartsWith("Right", System.StringComparison.OrdinalIgnoreCase))
                return HandSide.Right;

            throw new System.ArgumentException(
                $"'{anchorName}' does not name a left or right hand anchor.", nameof(anchorName));
        }

        /// <summary>Half-extent of the square ground plate, in meters.</summary>
        public const float k_ArenaRadius = 10f;

        /// <summary>Distance from the arena center at which reference dummies are placed.</summary>
        public const float k_DummyRingRadius = 3f;

        /// <summary>
        /// Total height of an enemy, in meters. They are gnome sized: roughly waist high on a
        /// standing player, so a horde reads as a swarm rather than as a crowd of adults.
        /// </summary>
        public const float k_DummyHeight = 1f;

        /// <summary>
        /// Uniform scale that turns Unity's capsule primitive into an enemy.
        /// </summary>
        /// <remarks>
        /// The primitive is 2 m tall and 1 m wide at scale 1, so scaling uniformly by half the
        /// target height keeps the proportions and makes the body <see cref="k_DummyHeight"/> tall.
        /// </remarks>
        public const float k_DummyScale = k_DummyHeight / 2f;

        /// <summary>Height of a dummy's center above the ground, in meters.</summary>
        public const float k_DummyCenterHeight = k_DummyHeight / 2f;

        /// <summary>
        /// Mass of an enemy, in kilograms.
        /// </summary>
        /// <remarks>
        /// Light for something a meter tall, on purpose: with the default punch model an ordinary
        /// swing delivers around 45 N·s, which at this mass is a couple of meters per second of
        /// knockback — enough that the player can watch the hit land.
        /// </remarks>
        public const float k_DummyMass = 20f;

        /// <summary>
        /// Distributes <paramref name="count"/> positions evenly around a horizontal ring
        /// centered on the origin, with index 0 placed on +Z and increasing indices
        /// advancing clockwise when viewed from above.
        /// </summary>
        /// <param name="index">
        /// Position to compute. Values outside [0, count) wrap around, so callers can
        /// feed a monotonically increasing spawn counter directly.
        /// </param>
        /// <param name="count">Number of positions in the ring. Must be positive.</param>
        /// <param name="radius">Ring radius in meters. Must not be negative.</param>
        public static Vector3 RingPosition(int index, int count, float radius)
        {
            if (count <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(count), count, "Ring must contain at least one position.");
            if (radius < 0f)
                throw new System.ArgumentOutOfRangeException(nameof(radius), radius, "Ring radius cannot be negative.");

            // Wrap so a free-running spawn counter maps onto the ring without the caller
            // having to do modulo arithmetic. RealMod handles negative indices correctly,
            // which the C# % operator does not.
            int wrapped = XRMultiplayer.Utils.RealMod(index, count);
            float angle = 2f * Mathf.PI * wrapped / count;

            return new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
        }
    }
}
