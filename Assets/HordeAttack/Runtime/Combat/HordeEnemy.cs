using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace HordeAttack
{
    /// <summary>
    /// An enemy that can be punched: it holds health, takes knockback, dies, and keeps track of
    /// whether it currently has hold of a player.
    /// </summary>
    /// <remarks>
    /// Deliberately thin on decisions. Every judgement about what a punch is worth lives in
    /// <see cref="PunchResolver"/> and every judgement about where to walk lives in
    /// <see cref="HordeSteering"/>; this component owns the state those decisions read and write,
    /// plus the physical and visual response.
    /// <para>
    /// The one thing it does own outright is the attachment to a player, because letting go has to
    /// happen in the same breath as being punched, dying and being recycled. Splitting those across
    /// two components is how an enemy ends up as a corpse riding on someone's shoulder.
    /// </para>
    /// <para>
    /// Fase 4 makes this a <c>NetworkPhysicsInteractable</c> and moves the health mutation behind an
    /// owner RPC, so keeping the maths out of here is what makes that a small change.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class HordeEnemy : MonoBehaviour
    {
        /// <summary>Shader property URP's Lit shader tints with.</summary>
        static readonly int k_BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Below this a direction vector is treated as having no direction at all.</summary>
        const float k_DirectionEpsilon = 1e-6f;

        /// <summary>
        /// Seconds a freshly respawned enemy refuses to be picked up, in seconds.
        /// </summary>
        /// <remarks>
        /// Long enough for the hand that was holding it to notice the creature is no longer inside
        /// its trigger. Without it, recycling an enemy that someone still has hold of teleports it
        /// home and the toolkit immediately hands it back — the player is left holding a creature
        /// that officially respawned somewhere else. It is the same trick the template's
        /// <c>NetworkSocketInteractor</c> plays when it switches its socket off for half a second
        /// after spawning.
        /// </remarks>
        const float k_RespawnGrabBlock = 0.25f;

        static readonly List<HordeEnemy> s_All = new List<HordeEnemy>();

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
        Transform m_HomeParent;
        LatchAnchor m_Anchor;
        EnemyLocomotion m_Locomotion;
        PointVelocityTracker m_Tracker;
        XRGrabInteractable m_Grab;
        float m_GrabbableFrom;
        IEnumerator m_FlashRoutine;
        IEnumerator m_DeathRoutine;

        /// <summary>Every enabled enemy in the scene, in no particular order.</summary>
        /// <remarks>
        /// A registry rather than a scene search: separation needs every enemy to know about its
        /// neighbours every physics step, and <c>FindObjectsByType</c> per enemy per step is the
        /// cost that stops Fase 3 from reaching 20-30 of them.
        /// </remarks>
        public static IReadOnlyList<HordeEnemy> all => s_All;

        /// <summary>Health remaining. Zero means dead.</summary>
        public int health => m_Health;

        /// <summary>Health this enemy starts and respawns with.</summary>
        public int maxHealth => Mathf.Max(1, m_MaxHealth);

        /// <summary>Whether this enemy can still be punched.</summary>
        public bool isAlive => m_Health > 0;

        /// <summary>The anchor this enemy is holding on to, or null when it is not attached.</summary>
        public LatchAnchor latchAnchor => m_Anchor;

        /// <summary>Whether this enemy currently has hold of a player.</summary>
        public bool isLatched => m_Anchor != null;

        /// <summary>
        /// Whether the grip may take hold of this enemy at all.
        /// </summary>
        /// <remarks>
        /// The one lever that reliably gets a creature out of a hand, and the reason both cases go
        /// through it. Asking the interaction manager to cancel a selection does not stick while the
        /// player is still squeezing the grip: the creature is inside the hand's trigger, so the
        /// toolkit hands it back on the next frame. What the toolkit <em>does</em> respect is an
        /// interactable that says it cannot be selected, which it re-checks every frame — so a
        /// corpse is dropped, and a recycled creature is not dragged back out of its spawn point.
        /// </remarks>
        public bool isGrabbable => isAlive && Time.time >= m_GrabbableFrom;

        /// <summary>Whether a player has this enemy by the grip right now.</summary>
        /// <remarks>
        /// Read off the interactable rather than mirrored into a field of its own, for the same
        /// reason <see cref="state"/> is derived: the toolkit already owns this fact, and a second
        /// copy of it is a copy that can disagree.
        /// </remarks>
        public bool isHeld => m_Grab != null && m_Grab.isSelected;

        /// <summary>
        /// The hand holding this enemy, or null when nobody is.
        /// </summary>
        /// <remarks>
        /// Published so a fist can tell whether the creature in front of it is the one it is
        /// carrying. See <see cref="PunchDetector"/>: the punch trigger and the grip share a hand,
        /// so a held enemy sits inside its own captor's trigger permanently.
        /// </remarks>
        public Transform holder =>
            isHeld && m_Grab.interactorsSelecting.Count > 0
                ? m_Grab.interactorsSelecting[0].transform
                : null;

        /// <summary>
        /// What this enemy is doing, derived from the parts that actually own it.
        /// </summary>
        /// <remarks>
        /// Derived rather than stored. A stored copy has to be written from four places — dying,
        /// latching, letting go, launching — and the interesting bugs are all the ones where two of
        /// those disagree. Reading it from the underlying facts means it cannot be wrong.
        /// </remarks>
        public EnemyState state
        {
            get
            {
                if (!isAlive)
                    return EnemyState.Dead;
                if (isHeld)
                    return EnemyState.Grabbed;
                if (isLatched)
                    return EnemyState.Latched;

                return m_Locomotion != null && m_Locomotion.isLeaping
                    ? EnemyState.Leaping
                    : EnemyState.Walking;
            }
        }

        /// <summary>
        /// How fast this enemy is being carried by whatever it is holding on to, in m/s.
        /// </summary>
        /// <remarks>
        /// Zero unless latched, and that restriction is the point. A punch thrown at an enemy riding
        /// on the player has to be measured against the enemy, or walking across the room would
        /// register as a flurry of punches; a punch thrown at a <em>free</em> enemy must not be,
        /// or an enemy running into a motionless fist would be counted as having been hit by it.
        /// </remarks>
        public Vector3 carrierVelocity =>
            isLatched && m_Tracker != null ? m_Tracker.velocity : Vector3.zero;

        /// <summary>Raised on every punch that landed, with what that punch was worth.</summary>
        public event Action<PunchOutcome> OnPunched;

        /// <summary>Raised once, when the punch that empties this enemy's health lands.</summary>
        public event Action<HordeEnemy> OnDied;

        /// <summary>Raised when this enemy takes hold of a player.</summary>
        public event Action<HordeEnemy, LatchAnchor> OnLatched;

        /// <summary>Raised when this enemy lets go, however that came about.</summary>
        public event Action<HordeEnemy, LatchAnchor> OnDetached;

        /// <inheritdoc/>
        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            m_Colliders = GetComponentsInChildren<Collider>(true);
            m_PropertyBlock = new MaterialPropertyBlock();

            TryGetComponent(out m_Locomotion);
            TryGetComponent(out m_Tracker);
            TryGetComponent(out m_Grab);

            m_SpawnPosition = transform.position;
            m_SpawnRotation = transform.rotation;
            m_HomeParent = transform.parent;

            m_Health = maxHealth;
            ApplyColor(m_HealthyColor);
        }

        /// <inheritdoc/>
        void OnEnable() => s_All.Add(this);

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
        /// <param name="handVelocity">
        /// Smoothed hand velocity relative to this enemy, in m/s. For a free enemy that is just the
        /// hand's world velocity; for a latched one the caller has to subtract
        /// <see cref="carrierVelocity"/> first.
        /// </param>
        /// <param name="settings">Tuning for the punch model.</param>
        public PunchOutcome ReceivePunch(Vector3 handVelocity, PunchSettings settings)
        {
            var outcome = PunchResolver.Resolve(handVelocity, m_Health, settings);
            if (!outcome.landed)
                return outcome;

            m_Health = outcome.remainingHealth;

            ReleaseToPhysics();

            // Set rather than added. A creature that was walking at the player carries momentum
            // straight into the punch, and adding to it would eat part of the knockback — so the
            // enemy that most deserves to be sent flying is the one that would go least far.
            if (m_Body != null && !m_Body.isKinematic)
                m_Body.linearVelocity = outcome.launchVelocity;

            OnPunched?.Invoke(outcome);

            if (outcome.isLethal)
                Die();
            else
                Flash();

            return outcome;
        }

        /// <summary>
        /// Takes <paramref name="damage"/> from running into something at speed, and reports whether
        /// there was anything to take it off.
        /// </summary>
        /// <remarks>
        /// The throwing half of the fight, resolved by <see cref="ImpactDetector"/>. Unlike
        /// <see cref="ReceivePunch"/> this applies no velocity of its own: the physics engine has
        /// already bounced both bodies apart by the time it runs, and a scripted impulse on top would
        /// double the response. What it does share is letting go first, and for the same reasons —
        /// a creature that keeps clinging to the player through the hit is a creature the hit did
        /// not visibly do anything to.
        /// </remarks>
        public bool ReceiveImpact(int damage)
        {
            if (damage <= 0 || !isAlive)
                return false;

            m_Health = Mathf.Max(0, m_Health - damage);

            ReleaseToPhysics();

            if (isAlive)
                Flash();
            else
                Die();

            return true;
        }

        /// <summary>
        /// Lets go of everything that was moving this enemy, an instant before a player takes hold
        /// of it with the grip.
        /// </summary>
        /// <remarks>
        /// Called from <see cref="EnemyGrabInteractable.Grab"/> before the toolkit records what it
        /// will restore on release. Doing it after would mean being dropped puts the creature back on
        /// the player's chest, kinematic and frozen in the air.
        /// <para>
        /// Unlike the knockback release this asks for no recovery window. Nothing needs protecting
        /// from the enemy's own locomotion while a hand owns the body — <see cref="EnemyLocomotion"/>
        /// stands down for as long as <see cref="isHeld"/> is true.
        /// </para>
        /// </remarks>
        internal void PrepareForGrab()
        {
            Detach();
            m_Locomotion?.ReleaseForGrab();
        }

        /// <summary>
        /// Puts the enemy back under its own power after the last hand lets go.
        /// </summary>
        /// <remarks>
        /// The recovery window is the whole point: the toolkit applies the throw velocity at the end
        /// of the frame, and without it the enemy's next walking step would overwrite that velocity
        /// and the throw would end where it started.
        /// </remarks>
        internal void ReleaseFromGrab()
        {
            m_Locomotion?.ReleaseForKnockback();
        }

        /// <summary>
        /// Attaches this enemy to <paramref name="anchor"/>, riding along with the player from now
        /// on.
        /// </summary>
        /// <remarks>
        /// Called by <see cref="PlayerLatchTarget.TryLatch"/>, which has already claimed the anchor.
        /// Going through the player rather than letting the enemy grab an anchor itself is what
        /// makes choosing a slot and claiming it a single step.
        /// </remarks>
        public void AttachTo(LatchAnchor anchor)
        {
            // A creature in someone's fist does not get to climb onto them. Locomotion already
            // stands down while held, so this is the belt to that braces — and it is the guard that
            // keeps a hand-held enemy from grabbing an anchor it would never let go of.
            if (anchor == null || !isAlive || isHeld)
                return;

            if (m_Anchor != null)
                Detach();

            m_Anchor = anchor;

            if (m_Body != null)
            {
                // Only worth zeroing if there is anything to zero. An enemy arriving mid-leap is
                // already kinematic, and writing a velocity to a kinematic body is an error Unity
                // logs and discards — noise that would bury a real physics complaint.
                if (!m_Body.isKinematic)
                {
                    m_Body.linearVelocity = Vector3.zero;
                    m_Body.angularVelocity = Vector3.zero;
                    m_Body.isKinematic = true;
                }
            }

            transform.SetParent(anchor.transform, worldPositionStays: true);

            // Positioned in world space and only then baked into the parent, because "hangs below
            // the anchor" means below in the world. An arm anchor rolls with the wrist, and a drop
            // expressed in its local space would swing the creature out sideways when the player
            // turns their hand over.
            transform.position = anchor.transform.position + Vector3.down * anchor.hangDrop;
            FaceTheHead(anchor);

            // The snap onto the anchor covers a meter in one frame. Left in the window, that reads
            // as the enemy travelling at tens of m/s, and the first punch thrown at it would be
            // measured against a velocity that never existed.
            m_Tracker?.ResetSamples();

            OnLatched?.Invoke(this, anchor);
        }

        /// <summary>
        /// Lets go of whatever this enemy was holding, putting it back under its own physics.
        /// </summary>
        /// <remarks>
        /// Safe to call when not attached, which is what lets every path that ends a hold — punched
        /// off, killed, recycled — simply call it rather than each having to check first.
        /// </remarks>
        public void Detach()
        {
            if (m_Anchor == null)
                return;

            var anchor = m_Anchor;
            m_Anchor = null;

            transform.SetParent(m_HomeParent, worldPositionStays: true);

            if (m_Body != null)
                m_Body.isKinematic = false;

            m_Tracker?.ResetSamples();

            anchor.Release(this);
            anchor.owner?.NotifyDetached(this, anchor);
            OnDetached?.Invoke(this, anchor);
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

            // Shut the door before knocking the hand off it, and both before the locomotion timers
            // are cleared: prising the creature loose ends with ReleaseFromGrab arming a recovery
            // window, and doing that later would leave the respawned enemy standing still for a
            // second. The refusal has to come first or the hand simply takes it again.
            m_GrabbableFrom = Time.time + k_RespawnGrabBlock;
            ForceRelease();

            // Before the pose is restored: an enemy that died while holding on is still parented to
            // a player who has since walked off, and setting a world position on it would otherwise
            // be immediately re-interpreted through that parent.
            Detach();
            m_Locomotion?.ResetForRespawn();

            transform.SetParent(m_HomeParent, worldPositionStays: true);
            transform.SetPositionAndRotation(m_SpawnPosition, m_SpawnRotation);

            if (m_Body != null)
            {
                m_Body.isKinematic = false;
                m_Body.linearVelocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
            }

            m_Tracker?.ResetSamples();

            SetVisible(true);
            m_Health = maxHealth;
            ApplyColor(m_HealthyColor);
        }

        void Die()
        {
            // Nothing here makes the hand let go, and that is on purpose: cancelling the selection
            // does not survive a grip that is still held down, because the toolkit re-selects
            // whatever is inside the hand on the very next frame. What actually gets a corpse out of
            // the player's fist is EnemyGrabInteractable refusing to be selectable once the creature
            // is dead, which the toolkit re-checks every frame.

            // Belt and braces: ReceivePunch already let go before applying the knockback, but a
            // corpse still riding on the player's shoulder is bad enough to be worth the second
            // check when a future caller kills an enemy some other way.
            Detach();

            ApplyColor(m_DeadColor);
            OnDied?.Invoke(this);

            // The corpse keeps whatever knockback killed it, so the lethal punch is the one that
            // visibly throws the enemy instead of freezing it in place.
            if (isActiveAndEnabled)
                RestartRoutine(ref m_DeathRoutine, DeathRoutine());
        }

        /// <summary>
        /// Hands the body back to the physics engine so the hit that just landed can be seen.
        /// </summary>
        /// <remarks>
        /// Shared by every way an enemy gets hurt, because both of them run into the same two traps.
        /// A creature clinging to a player is kinematic and parented to it, so a velocity or an
        /// impulse written to it is discarded without a word — the hit would take health off it and
        /// leave it hanging there, which reads as nothing having happened. A creature caught mid-leap
        /// is kinematic for the same reason. And the recovery window is what stops the enemy's own
        /// locomotion from overwriting the response on the very next physics step.
        /// </remarks>
        void ReleaseToPhysics()
        {
            Detach();
            m_Locomotion?.ReleaseForKnockback();
        }

        /// <summary>
        /// Ends a hold that is in progress, now rather than on the next frame.
        /// </summary>
        /// <remarks>
        /// Only half of getting a creature out of a hand, and useless on its own: it is
        /// <see cref="isGrabbable"/> that stops the toolkit handing it straight back. Routed through
        /// the interaction manager rather than the interactor because the manager owns the select
        /// state, and going through it is what makes the hand run its own exit properly instead of
        /// being left believing it still has something.
        /// <para>
        /// Dying does not need this at all. Refusing to be grabbable is enough on its own, and the
        /// toolkit lets go within the frame.
        /// </para>
        /// </remarks>
        void ForceRelease()
        {
            if (m_Grab == null || !m_Grab.isSelected || m_Grab.interactionManager == null)
                return;

            m_Grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)m_Grab);
        }

        /// <summary>
        /// Turns the enemy to face the player it is holding on to.
        /// </summary>
        /// <remarks>
        /// Set in world space after parenting, so the local rotation it bakes down to is right for
        /// whichever anchor this is. Facing outward would show the player the creature's back and
        /// lose the one thing that makes being grabbed read as being grabbed.
        /// </remarks>
        void FaceTheHead(LatchAnchor anchor)
        {
            if (anchor.owner == null)
                return;

            var toHead = anchor.owner.headPosition - transform.position;
            toHead.y = 0f;

            if (toHead.sqrMagnitude > k_DirectionEpsilon)
                transform.rotation = Quaternion.LookRotation(toHead.normalized, Vector3.up);
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
            s_All.Remove(this);

            // Give the anchor back without reparenting. The transform is on its way out — being
            // destroyed, or pooled by Fase 3 — and reparenting during teardown is the kind of thing
            // Unity complains about, but an anchor still marked as taken by an enemy that no longer
            // exists is a slot nobody can ever use again.
            ReleaseAnchorOnly();

            m_FlashRoutine = null;
            m_DeathRoutine = null;
        }

        void ReleaseAnchorOnly()
        {
            if (m_Anchor == null)
                return;

            var anchor = m_Anchor;
            m_Anchor = null;

            anchor.Release(this);
            anchor.owner?.NotifyDetached(this, anchor);
            OnDetached?.Invoke(this, anchor);
        }
    }
}
