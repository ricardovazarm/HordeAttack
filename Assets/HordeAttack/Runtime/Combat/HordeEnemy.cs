using System;
using System.Collections;
using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// An enemy that can be punched: it holds health, takes knockback, and dies.
    /// </summary>
    /// <remarks>
    /// Deliberately thin. Every decision about what a punch is worth lives in
    /// <see cref="PunchResolver"/>; this component only owns the state that decision reads and
    /// writes, plus the physical and visual response. Fase 4 makes this a
    /// <c>NetworkPhysicsInteractable</c> and moves the health mutation behind an owner RPC, so
    /// keeping the maths out of here is what makes that a small change.
    /// </remarks>
    [RequireComponent(typeof(Rigidbody))]
    public class HordeEnemy : MonoBehaviour
    {
        /// <summary>Shader property URP's Lit shader tints with.</summary>
        static readonly int k_BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Vida")]
        [SerializeField]
        [Tooltip("Golpes estándar que aguanta. Un golpe muy fuerte cuenta por dos.")]
        int m_MaxHealth = 3;

        [Header("Presentación")]
        [SerializeField]
        [Tooltip("Color del enemigo con vida.")]
        Color m_HealthyColor = new Color(0.45f, 0.65f, 0.4f);

        [SerializeField]
        [Tooltip("Destello al recibir un golpe. Es la confirmación visual de que el puñetazo contó.")]
        Color m_HitColor = Color.white;

        [SerializeField]
        [Tooltip("Color al morir.")]
        Color m_DeadColor = new Color(0.25f, 0.12f, 0.12f);

        [SerializeField]
        [Tooltip("Duración del destello de impacto, en segundos.")]
        float m_HitFlashDuration = 0.12f;

        [Header("Muerte")]
        [SerializeField]
        [Tooltip("Segundos que el cadáver sigue visible antes de desaparecer.")]
        float m_DespawnDelay = 1.5f;

        [SerializeField]
        [Tooltip("Segundos, ya desaparecido, hasta reaparecer en su pose inicial. En 0 no reaparece.")]
        float m_RespawnDelay = 3f;

        int m_Health;
        Rigidbody m_Body;
        Renderer[] m_Renderers;
        Collider[] m_Colliders;
        MaterialPropertyBlock m_PropertyBlock;
        Vector3 m_SpawnPosition;
        Quaternion m_SpawnRotation;
        IEnumerator m_FlashRoutine;
        IEnumerator m_DeathRoutine;

        /// <summary>Health remaining. Zero means dead.</summary>
        public int health => m_Health;

        /// <summary>Health this enemy starts and respawns with.</summary>
        public int maxHealth => Mathf.Max(1, m_MaxHealth);

        /// <summary>Whether this enemy can still be punched.</summary>
        public bool isAlive => m_Health > 0;

        /// <summary>Raised on every punch that landed, with what that punch was worth.</summary>
        public event Action<PunchOutcome> OnPunched;

        /// <summary>Raised once, when the punch that empties this enemy's health lands.</summary>
        public event Action<HordeEnemy> OnDied;

        /// <inheritdoc/>
        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            m_Colliders = GetComponentsInChildren<Collider>(true);
            m_PropertyBlock = new MaterialPropertyBlock();

            m_SpawnPosition = transform.position;
            m_SpawnRotation = transform.rotation;

            m_Health = maxHealth;
            ApplyColor(m_HealthyColor);
        }

        /// <inheritdoc/>
        void OnValidate()
        {
            m_MaxHealth = Mathf.Max(1, m_MaxHealth);
            m_HitFlashDuration = Mathf.Max(0f, m_HitFlashDuration);
            m_DespawnDelay = Mathf.Max(0f, m_DespawnDelay);
            m_RespawnDelay = Mathf.Max(0f, m_RespawnDelay);
        }

        /// <summary>
        /// Takes a punch thrown at <paramref name="handVelocity"/> and returns what it was worth,
        /// so the puncher can play its own feedback without waiting for anything.
        /// </summary>
        /// <remarks>
        /// Returns a miss rather than throwing when the swing was too slow or the enemy is already
        /// dead. Both are ordinary during a fight, and it is the caller that decides whether a miss
        /// is worth reacting to.
        /// </remarks>
        /// <param name="handVelocity">Smoothed world-space hand velocity, in m/s.</param>
        /// <param name="settings">Tuning for the punch model.</param>
        public PunchOutcome ReceivePunch(Vector3 handVelocity, PunchSettings settings)
        {
            var outcome = PunchResolver.Resolve(handVelocity, m_Health, settings);
            if (!outcome.landed)
                return outcome;

            m_Health = outcome.remainingHealth;

            if (m_Body != null && !m_Body.isKinematic)
                m_Body.AddForce(outcome.impulse, ForceMode.Impulse);

            OnPunched?.Invoke(outcome);

            if (outcome.isLethal)
                Die();
            else
                Flash();

            return outcome;
        }

        /// <summary>
        /// Puts the enemy back on its feet at full health, in the pose it started in.
        /// </summary>
        /// <remarks>
        /// The POC has three fixed dummies and no spawner until Fase 3, so without this the only
        /// way to try the third punch again is to leave play mode and re-enter it. Fase 3's pooled
        /// spawner reuses this as its recycle step.
        /// </remarks>
        public void Respawn()
        {
            StopRoutine(ref m_DeathRoutine);
            StopRoutine(ref m_FlashRoutine);

            transform.SetPositionAndRotation(m_SpawnPosition, m_SpawnRotation);

            if (m_Body != null)
            {
                m_Body.isKinematic = false;
                m_Body.linearVelocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
            }

            SetVisible(true);
            m_Health = maxHealth;
            ApplyColor(m_HealthyColor);
        }

        void Die()
        {
            ApplyColor(m_DeadColor);
            OnDied?.Invoke(this);

            // The corpse keeps whatever knockback killed it, so the lethal punch is the one that
            // visibly throws the enemy instead of freezing it in place.
            if (isActiveAndEnabled)
                RestartRoutine(ref m_DeathRoutine, DeathRoutine());
        }

        /// <summary>
        /// Lets the corpse fly, hides it, and — unless respawning is switched off — brings it back.
        /// </summary>
        /// <remarks>
        /// The GameObject is hidden by switching off renderers and colliders rather than by
        /// deactivating it. A deactivated object stops its own coroutines, so the respawn timer
        /// would never fire and the enemy would be gone for the rest of the session.
        /// </remarks>
        IEnumerator DeathRoutine()
        {
            yield return new WaitForSeconds(m_DespawnDelay);

            SetVisible(false);

            // Freeze the hidden corpse: an invisible body still collides with the ground and with
            // live enemies, and it would go on being pushed around by them.
            if (m_Body != null)
                m_Body.isKinematic = true;

            if (m_RespawnDelay > 0f)
            {
                yield return new WaitForSeconds(m_RespawnDelay);
                Respawn();
            }

            m_DeathRoutine = null;
        }

        void Flash()
        {
            if (m_HitFlashDuration <= 0f || !isActiveAndEnabled)
                return;

            RestartRoutine(ref m_FlashRoutine, FlashRoutine());
        }

        IEnumerator FlashRoutine()
        {
            ApplyColor(m_HitColor);
            yield return new WaitForSeconds(m_HitFlashDuration);

            // A punch landing during the flash can kill; repainting healthy would undo the death
            // colour that the same frame just applied.
            if (isAlive)
                ApplyColor(m_HealthyColor);

            m_FlashRoutine = null;
        }

        void SetVisible(bool visible)
        {
            if (m_Renderers != null)
            {
                foreach (var renderer in m_Renderers)
                {
                    if (renderer != null)
                        renderer.enabled = visible;
                }
            }

            if (m_Colliders == null)
                return;

            foreach (var collider in m_Colliders)
            {
                if (collider != null)
                    collider.enabled = visible;
            }
        }

        /// <summary>
        /// Tints the enemy through a property block rather than by touching its material.
        /// </summary>
        /// <remarks>
        /// Assigning to <c>Renderer.material</c> clones the material per enemy, so at the 20-30
        /// enemies this POC targets that is 30 materials that can no longer batch. A property block
        /// leaves the shared material alone.
        /// </remarks>
        void ApplyColor(Color color)
        {
            if (m_Renderers == null)
                return;

            foreach (var renderer in m_Renderers)
            {
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(m_PropertyBlock);
                m_PropertyBlock.SetColor(k_BaseColorId, color);
                renderer.SetPropertyBlock(m_PropertyBlock);
            }
        }

        void RestartRoutine(ref IEnumerator field, IEnumerator routine)
        {
            StopRoutine(ref field);
            field = routine;
            StartCoroutine(field);
        }

        void StopRoutine(ref IEnumerator field)
        {
            if (field == null)
                return;

            if (isActiveAndEnabled)
                StopCoroutine(field);

            field = null;
        }

        /// <inheritdoc/>
        void OnDisable()
        {
            m_FlashRoutine = null;
            m_DeathRoutine = null;
        }
    }
}
