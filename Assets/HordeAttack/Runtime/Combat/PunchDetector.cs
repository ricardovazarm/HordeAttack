using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace HordeAttack
{
    /// <summary>
    /// Turns contact between this hand and an enemy into a punch, and vibrates the controller when
    /// one lands.
    /// </summary>
    /// <remarks>
    /// Lives on the fist, alongside the <see cref="PointVelocityTracker"/> that measures the swing
    /// and the trigger collider that notices the contact.
    /// <para>
    /// The punch is resolved and its feedback played entirely on the machine that threw it, without
    /// waiting for anyone to confirm it. Fase 4 makes the damage authoritative on the enemy's owner
    /// via an RPC, but the haptics stay here: a punch that buzzes a round trip later does not feel
    /// like a punch. It is the same trade the template makes in Whack-A-Pig.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(PointVelocityTracker))]
    public class PunchDetector : MonoBehaviour
    {
        /// <summary>
        /// How many recently punched enemies to remember before pruning expired entries.
        /// </summary>
        /// <remarks>
        /// Only a bound on bookkeeping. The cooldown table would otherwise grow one entry per
        /// enemy ever touched, and by Fase 3 that is every enemy of every wave.
        /// </remarks>
        const int k_MaxTrackedTargets = 32;

        [SerializeField]
        [Tooltip("Mano a la que pertenece este puño. Determina qué mando vibra.")]
        HandSide m_Hand = HandSide.Right;

        [SerializeField]
        [Tooltip("Modelo de golpe: umbral, daño, knockback y vibración.")]
        PunchSettings m_Settings = new PunchSettings();

        [SerializeField]
        [Tooltip("Segundos que tienen que pasar antes de que este puño pueda volver a golpear al MISMO enemigo.")]
        float m_RehitCooldown = 0.35f;

        [SerializeField]
        [Tooltip("Vibrar el mando al conectar. Se apaga para probar sin visor.")]
        bool m_Haptics = true;

        readonly Dictionary<HordeEnemy, float> m_LastHitTime = new Dictionary<HordeEnemy, float>();

        PointVelocityTracker m_Tracker;
        Transform m_HandRoot;
        XRDirectInteractor m_Grip;

        /// <summary>Which hand this fist belongs to.</summary>
        public HandSide hand
        {
            get => m_Hand;
            set => m_Hand = value;
        }

        /// <summary>The punch model this fist hits with.</summary>
        public PunchSettings settings => m_Settings;

        /// <summary>Seconds before the same enemy can be punched again by this fist.</summary>
        public float rehitCooldown => m_RehitCooldown;

        /// <inheritdoc/>
        void Awake()
        {
            m_Tracker = GetComponent<PointVelocityTracker>();

            // The transform the fist hangs off — a controller or a tracked hand. Anything latched
            // below it is on this arm, which is what makes it unpunchable by this fist.
            m_HandRoot = transform.parent;

            m_Grip = FindGrip();

            m_Settings?.Clamp();
        }

        /// <summary>
        /// Locates the interactor this hand grabs with, so the fist can tell when the player is
        /// reaching rather than striking.
        /// </summary>
        /// <remarks>
        /// Searched downward from the hand rather than upward from the fist, because the two are
        /// <em>siblings</em>: the rig hangs both the interactor and this fist off the same hand
        /// anchor, so <c>GetComponentInParent</c> would never find it. Inactive objects are included
        /// because <c>XRInputModalityManager</c> switches the controller and hand-tracking branches
        /// on and off at runtime, and which branch is live is not known when this runs.
        /// <para>
        /// Specifically an <see cref="XRDirectInteractor"/>, not any input interactor. A ray
        /// interactor and a teleport interactor hang off the same anchor, and the teleport one
        /// carries its own controller bound to entirely different actions — asking it about the grip
        /// would answer a question about the thumbstick.
        /// </para>
        /// <para>
        /// Returning null is fine and means "this hand only punches". A fist built without a rig
        /// around it behaves exactly as it did before there was a grip to arbitrate with.
        /// </para>
        /// </remarks>
        XRDirectInteractor FindGrip() =>
            m_HandRoot != null ? m_HandRoot.GetComponentInChildren<XRDirectInteractor>(true) : null;

        /// <inheritdoc/>
        void OnValidate()
        {
            m_RehitCooldown = Mathf.Max(0f, m_RehitCooldown);
            m_Settings?.Clamp();
        }

        /// <inheritdoc/>
        void OnDisable()
        {
            // Cooldowns are wall-clock timestamps, so a hand that comes back after being switched
            // off would still consider every enemy it last touched to be on cooldown or not, based
            // on a time that no longer means anything.
            m_LastHitTime.Clear();
        }

        /// <inheritdoc/>
        void OnTriggerEnter(Collider other) => TryPunch(other);

        /// <summary>
        /// Also resolves while the fist stays inside an enemy, not only on the frame it entered.
        /// </summary>
        /// <remarks>
        /// A hand that lands a punch and stays buried in the enemy never fires another enter event,
        /// so without this a player jabbing repeatedly at close range would land the first punch
        /// and nothing after it. The per-enemy cooldown is what stops this from doing damage every
        /// physics step.
        /// </remarks>
        /// <inheritdoc/>
        void OnTriggerStay(Collider other) => TryPunch(other);

        void TryPunch(Collider other)
        {
            if (other == null || IsGrabbing)
                return;

            // The collider that touched us is the enemy's body, but by Fase 3 an enemy is a
            // hierarchy with several of them, so resolve upward to the enemy itself.
            var enemy = other.GetComponentInParent<HordeEnemy>();
            if (enemy == null || !enemy.isAlive || IsOnThisArm(enemy) || !IsOffCooldown(enemy))
                return;

            var outcome = enemy.ReceivePunch(SwingAgainst(enemy), m_Settings);

            // A miss is a graze, not a punch: it must not start a cooldown, or a slow brush against
            // an enemy would lock out the real punch that follows it a tenth of a second later.
            if (!outcome.landed)
                return;

            RecordHit(enemy);
            PlayHaptics(outcome);
        }

        /// <summary>
        /// Whether the player is squeezing the grip on this hand, in which case the hand is reaching
        /// for something rather than striking it.
        /// </summary>
        /// <remarks>
        /// An open hand punches and a closed one takes hold, and something has to say which, because
        /// the punch trigger and the grab volume are the same 9-10 cm of space on the same hand.
        /// Without this the fist always wins: reaching for a loose creature clears the 1.5 m/s
        /// threshold long before the fingers close, and the knockback throws it five meters away
        /// before there is anything left to grab. It is why, in the headset, the only creature you
        /// could pick up was one already clinging to you — that one is close enough that the hand
        /// barely has to move.
        /// <para>
        /// Read from the logical input state rather than from
        /// <c>XRBaseInputInteractor.isSelectActive</c>. That property is filtered through the
        /// interactor's <c>selectActionTrigger</c> mode and answers "may I start a selection this
        /// frame", which is not the same question — under <c>StateChange</c> it goes false one frame
        /// after the squeeze whenever the hand caught nothing, which is exactly the case that matters
        /// here. <c>logicalSelectState</c> is filled straight from the raw input every frame, and it
        /// is filled the same way whether the rig drives input through the modern readers or through
        /// the older controller components — this one uses the latter.
        /// </para>
        /// <para>
        /// The value is a frame old: this runs during physics, and the toolkit refreshes the state
        /// during the update phase. Fourteen milliseconds against a gesture that takes half a second.
        /// </para>
        /// </remarks>
        bool IsGrabbing => m_Grip != null && m_Grip.logicalSelectState.isPerformed;

        /// <summary>
        /// How fast this fist is closing on <paramref name="enemy"/>, in m/s.
        /// </summary>
        /// <remarks>
        /// For a free enemy this is just how fast the hand is moving, which is the whole punch model
        /// from Fase 1. For one that is riding on a player it is the hand's velocity minus the
        /// enemy's, and it has to be: an enemy clinging to your waist travels with you, so walking
        /// across the room at 1.5 m/s would otherwise register as a punch on every physics step
        /// without the player having thrown anything.
        /// <para>
        /// The subtraction is deliberately limited to latched enemies. Applied to free ones, a
        /// creature sprinting into a motionless fist would be credited to the player as a punch it
        /// never threw — see <see cref="HordeEnemy.carrierVelocity"/>.
        /// </para>
        /// </remarks>
        Vector3 SwingAgainst(HordeEnemy enemy) => m_Tracker.velocity - enemy.carrierVelocity;

        /// <summary>
        /// Whether <paramref name="enemy"/> is attached to this very arm, by its own grip or by the
        /// player's.
        /// </summary>
        /// <remarks>
        /// The punch trigger sits on the hand and an arm anchor sits just behind it, so a creature
        /// latched there is permanently inside this fist's trigger. Without this check every
        /// movement of that arm would resolve a punch on it, and the enemy would be beaten off by
        /// the arm it is holding rather than by the player's other hand.
        /// <para>
        /// The cooldown does not cover this. It would space the phantom punches out, not stop them.
        /// Nor does the relative-velocity subtraction, which only mostly cancels: the anchor sits at
        /// an offset from the fist, so rolling the wrist swings the two apart fast enough to clear
        /// the punch threshold.
        /// </para>
        /// <para>
        /// The grip has exactly the same problem from the other direction. The interactor that picks
        /// an enemy up hangs off the same hand transform as this fist, so a creature being carried
        /// sits inside the trigger of the hand carrying it and would be punched to death by being
        /// moved about. Only <em>this</em> hand's grip counts: an enemy in the other player's hand is
        /// still a fair target.
        /// </para>
        /// </remarks>
        bool IsOnThisArm(HordeEnemy enemy)
        {
            if (m_HandRoot == null)
                return false;

            var anchor = enemy.latchAnchor;
            if (anchor != null && anchor.transform.IsChildOf(m_HandRoot))
                return true;

            var holder = enemy.holder;

            return holder != null && holder.IsChildOf(m_HandRoot);
        }

        bool IsOffCooldown(HordeEnemy enemy)
        {
            if (!m_LastHitTime.TryGetValue(enemy, out float lastHit))
                return true;

            return Time.time - lastHit >= m_RehitCooldown;
        }

        void RecordHit(HordeEnemy enemy)
        {
            if (m_LastHitTime.Count >= k_MaxTrackedTargets)
                PruneExpired();

            m_LastHitTime[enemy] = Time.time;
        }

        void PruneExpired()
        {
            var expired = new List<HordeEnemy>();

            foreach (var entry in m_LastHitTime)
            {
                if (entry.Key == null || Time.time - entry.Value >= m_RehitCooldown)
                    expired.Add(entry.Key);
            }

            foreach (var enemy in expired)
                m_LastHitTime.Remove(enemy);
        }

        void PlayHaptics(PunchOutcome outcome)
        {
            if (!m_Haptics || outcome.hapticDuration <= 0f)
                return;

            // The static utility rather than a HapticImpulsePlayer: the template's rig has no
            // player component authored on either controller, and the one XRI creates on demand is
            // built through an internal API this assembly cannot reach.
            HapticsUtility.SendHapticImpulse(
                outcome.hapticAmplitude,
                outcome.hapticDuration,
                m_Hand == HandSide.Left ? HapticsUtility.Controller.Left : HapticsUtility.Controller.Right);
        }
    }
}
