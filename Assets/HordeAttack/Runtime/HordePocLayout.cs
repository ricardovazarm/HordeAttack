using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

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
        public const string k_PlayerBodyName = "Player Body";
        public const string k_ArmAnchorName = "Arm Anchor";

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

        /// <summary>
        /// How the player's hands decide that the grip counts as "I am holding on".
        /// </summary>
        /// <remarks>
        /// <c>State</c> — squeezed means grabbing, for every frame it stays squeezed — rather than
        /// the template's <c>StateChange</c>, where a squeeze with nothing in reach counts for a
        /// single frame and then stops. That difference is what makes the punch and the grip able to
        /// share a hand: <see cref="PunchDetector"/> stands down while the grip is held, so the
        /// player has to be able to close their hand <em>before</em> reaching, or they knock the
        /// creature away on the way in. It also buys holding still with a closed hand and letting a
        /// creature walk into it.
        /// <para>
        /// Lives here because the scene builder sets it and the play mode tests have to build hands
        /// that behave the same way. Both read this rather than each stating the choice separately.
        /// </para>
        /// </remarks>
        public const XRBaseInputInteractor.InputTriggerType k_GripTrigger =
            XRBaseInputInteractor.InputTriggerType.State;

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

        /// <summary>
        /// Where on the player's body an enemy can take hold, and how it hangs once it does.
        /// </summary>
        /// <remarks>
        /// Heights are fractions of the player's real eye height rather than meters, because
        /// anchors authored for a tall player end up at a short one's chin, where the head clearance
        /// guard refuses them and leapers quietly stop being able to land on anyone. See
        /// <see cref="PlayerBodyProxy"/>, which does the placing.
        /// </remarks>
        public readonly struct LatchAnchorLayout
        {
            /// <summary>Name of the anchor object in the hierarchy.</summary>
            public readonly string name;

            /// <summary>Band of the body this anchor belongs to.</summary>
            public readonly LatchHeight height;

            /// <summary>Height as a fraction of the player's eye height.</summary>
            public readonly float heightFraction;

            /// <summary>Sideways (x) and forward (y) offset from the body axis, in meters.</summary>
            public readonly Vector2 bodyOffset;

            /// <summary>How far below the anchor a latched enemy's center sits, in meters.</summary>
            public readonly float hangDrop;

            public LatchAnchorLayout(
                string name, LatchHeight height, float heightFraction, Vector2 bodyOffset, float hangDrop)
            {
                this.name = name;
                this.height = height;
                this.heightFraction = heightFraction;
                this.bodyOffset = bodyOffset;
                this.hangDrop = hangDrop;
            }
        }

        /// <summary>
        /// The anchors that hang off the player's torso.
        /// </summary>
        /// <remarks>
        /// Two high and four low, which matches how the horde arrives: leapers aim for the chest,
        /// everyone else takes a leg or a hip. The arms are not here — they hang off the hand
        /// transforms instead, because an arm follows the controller and not the torso.
        /// <para>
        /// Nothing on the head, and nothing high enough for the clearance guard to have to catch. A
        /// creature hanging in front of the visor does not read as frightening, it reads as a bug.
        /// </para>
        /// <para>
        /// At 1 m tall on a human torso, latched enemies overlap each other. That is left alone on
        /// purpose: a pile is the intended read, and spacing six anchors far enough apart to avoid
        /// it would mean putting them somewhere a person does not have a body.
        /// </para>
        /// </remarks>
        public static readonly LatchAnchorLayout[] k_BodyAnchors =
        {
            new LatchAnchorLayout("Chest Left", LatchHeight.High, 0.74f, new Vector2(-0.2f, 0.1f), 0.25f),
            new LatchAnchorLayout("Chest Right", LatchHeight.High, 0.74f, new Vector2(0.2f, 0.1f), 0.25f),
            new LatchAnchorLayout("Waist Left", LatchHeight.Low, 0.5f, new Vector2(-0.26f, -0.14f), 0f),
            new LatchAnchorLayout("Waist Right", LatchHeight.Low, 0.5f, new Vector2(0.26f, -0.14f), 0f),
            new LatchAnchorLayout("Leg Left", LatchHeight.Low, 0.34f, new Vector2(-0.14f, 0f), 0f),
            new LatchAnchorLayout("Leg Right", LatchHeight.Low, 0.34f, new Vector2(0.14f, 0f), 0f),
        };

        /// <summary>
        /// Shortest eye height the anchor placement is expected to hold up at, in meters.
        /// </summary>
        /// <remarks>
        /// The two things it has to hold up against pull in opposite directions: the chest anchors
        /// have to stay outside the head clearance, which wants them low, and a latched enemy has to
        /// keep its feet out of the floor, which wants them high. Both are satisfied from here up.
        /// Below it — a crouching player — the chest anchors start being refused and leapers land on
        /// legs instead, which is the intended way for this to degrade.
        /// </remarks>
        public const float k_ReferenceEyeHeight = 1.5f;

        /// <summary>
        /// How far behind the hand the arm anchor sits, in meters.
        /// </summary>
        /// <remarks>
        /// Roughly the wrist. Far enough back that a creature holding on there is visibly on the
        /// forearm rather than in the fist, and close enough that it stays inside the reach of the
        /// other hand.
        /// </remarks>
        public const float k_ArmAnchorSetback = 0.12f;

        /// <summary>
        /// Half-extent of the square ground plate, in meters.
        /// </summary>
        /// <remarks>
        /// Sized from where enemies start rather than the other way round: they spawn up to
        /// <see cref="k_SpawnFarDistance"/> away and have to be standing on something, with room to
        /// spare for the ones that get punched past their own spawn point.
        /// </remarks>
        public const float k_ArenaRadius = 15f;

        /// <summary>Closest an enemy starts to the player, in meters.</summary>
        /// <remarks>
        /// Tuned as an arrival time, not as a distance: at the walking speed enemies actually use
        /// this puts the first creature on the player at around four seconds. Long enough to look
        /// around and watch the horde come — at 3 m the first one was holding on before the player
        /// had finished settling into the headset — and short enough that the game does not open
        /// with a wait.
        /// </remarks>
        public const float k_SpawnNearDistance = 7f;

        /// <summary>Furthest an enemy starts from the player, in meters.</summary>
        /// <remarks>
        /// The width of the band is what staggers arrivals, so it has to grow with the size of the
        /// group. The golden-ratio sequence leaves its closest pair about a eighteenth of the band
        /// apart at ten enemies, which over 1.5 m was under 10 cm — close enough that two of them
        /// walked in shoulder to shoulder and the horde read as a formation. 2.5 m puts that pair
        /// back over a tenth of a meter. The near edge is left alone: it is tuned as an arrival
        /// time, and only the tail of the wave gets later.
        /// </remarks>
        public const float k_SpawnFarDistance = 9.5f;

        /// <summary>
        /// Width of the fan enemies start in, in degrees, centered on the direction the player faces.
        /// </summary>
        /// <remarks>
        /// In front rather than all around, so the opening read is "they are coming for me" and not
        /// "something grabbed me from behind before I knew the game had started". Surrounding the
        /// player is Fase 3's business, once waves exist and the player has learned to turn.
        /// </remarks>
        public const float k_SpawnArcDegrees = 70f;

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
        /// Golden ratio conjugate, the step of the sequence that scatters spawn distances.
        /// </summary>
        /// <remarks>
        /// Stepping through the unit interval by this and wrapping produces values that never
        /// repeat and never clump, which is what a random number generator is usually reached for.
        /// It is used instead of one because the scene has to build the same way every time: a
        /// randomised layout means a bug that only shows up on some runs.
        /// </remarks>
        const float k_GoldenRatioConjugate = 0.6180339887f;

        /// <summary>
        /// Places <paramref name="count"/> enemies in a fan in front of the player, each at a
        /// different distance.
        /// </summary>
        /// <remarks>
        /// Two things are deliberate. The fan spreads evenly across the arc, so the horde spans the
        /// player's view rather than arriving down a single line. The distances do not: they come
        /// from a golden-ratio sequence, so no two enemies are the same distance away and the group
        /// does not read as a formation marching in step. Evenly spaced distances would have them
        /// arrive in a neat, predictable order.
        /// <para>
        /// Returned relative to the arena center, at ground level. Height is the caller's business.
        /// </para>
        /// </remarks>
        /// <param name="index">Which enemy. Values outside [0, count) wrap, so a spawn counter works directly.</param>
        /// <param name="count">How many enemies the fan holds. Must be positive.</param>
        /// <param name="nearDistance">Distance of the closest possible spawn, in meters.</param>
        /// <param name="farDistance">Distance of the furthest possible spawn. Must not be nearer than <paramref name="nearDistance"/>.</param>
        /// <param name="arcDegrees">Total width of the fan, centered on +Z.</param>
        public static Vector3 ApproachPosition(
            int index, int count, float nearDistance, float farDistance, float arcDegrees)
        {
            if (count <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(count), count, "A fan must hold at least one enemy.");
            if (nearDistance < 0f)
                throw new System.ArgumentOutOfRangeException(nameof(nearDistance), nearDistance, "Spawn distance cannot be negative.");
            if (farDistance < nearDistance)
                throw new System.ArgumentOutOfRangeException(nameof(farDistance), farDistance, "The far spawn distance is nearer than the near one.");

            int wrapped = XRMultiplayer.Utils.RealMod(index, count);

            // A single enemy goes straight ahead; any more spread symmetrically about the centre.
            float acrossArc = count > 1 ? (float)wrapped / (count - 1) - 0.5f : 0f;
            float angle = acrossArc * arcDegrees * Mathf.Deg2Rad;

            // The half-step offset stops index 0 from always landing exactly on the near distance,
            // which would make the closest enemy the same one every time.
            float alongDepth = Fraction((wrapped + 0.5f) * k_GoldenRatioConjugate);
            float distance = Mathf.Lerp(nearDistance, farDistance, alongDepth);

            return new Vector3(Mathf.Sin(angle) * distance, 0f, Mathf.Cos(angle) * distance);
        }

        static float Fraction(float value) => value - Mathf.Floor(value);

        /// <summary>
        /// Distributes <paramref name="count"/> positions evenly around a horizontal ring
        /// centered on the origin, with index 0 placed on +Z and increasing indices
        /// advancing clockwise when viewed from above.
        /// </summary>
        /// <remarks>
        /// Nothing calls this yet — the POC starts its enemies in a fan in front of the player, see
        /// <see cref="ApproachPosition"/>. It is kept for Fase 3, whose later waves are meant to
        /// come at the player from every side.
        /// </remarks>
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
