using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace HordeAttack
{
    /// <summary>
    /// Turns contact between this hand and an enemy into a punch, and vibrates the controller when
    /// one lands.
    /// </summary>
    /// <remarks>
    /// Lives on the fist, alongside the <see cref="HandVelocityTracker"/> that measures the swing
    /// and the trigger collider that notices the contact.
    /// <para>
    /// The punch is resolved and its feedback played entirely on the machine that threw it, without
    /// waiting for anyone to confirm it. Fase 4 makes the damage authoritative on the enemy's owner
    /// via an RPC, but the haptics stay here: a punch that buzzes a round trip later does not feel
    /// like a punch. It is the same trade the template makes in Whack-A-Pig.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(HandVelocityTracker))]
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

        HandVelocityTracker m_Tracker;

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
            m_Tracker = GetComponent<HandVelocityTracker>();
            m_Settings?.Clamp();
        }

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
            if (other == null)
                return;

            // The collider that touched us is the enemy's body, but by Fase 3 an enemy is a
            // hierarchy with several of them, so resolve upward to the enemy itself.
            var enemy = other.GetComponentInParent<HordeEnemy>();
            if (enemy == null || !enemy.isAlive || !IsOffCooldown(enemy))
                return;

            var outcome = enemy.ReceivePunch(m_Tracker.velocity, m_Settings);

            // A miss is a graze, not a punch: it must not start a cooldown, or a slow brush against
            // an enemy would lock out the real punch that follows it a tenth of a second later.
            if (!outcome.landed)
                return;

            RecordHit(enemy);
            PlayHaptics(outcome);
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
