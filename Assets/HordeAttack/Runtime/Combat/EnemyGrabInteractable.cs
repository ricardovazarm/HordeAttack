using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using XRMultiplayer;

namespace HordeAttack
{
    /// <summary>
    /// Makes an enemy something the player can take hold of with the grip, and tears it off
    /// whatever it was holding when they do.
    /// </summary>
    /// <remarks>
    /// A subclass rather than a component listening to <c>selectEntered</c>, and the reason is
    /// ordering. <see cref="XRGrabInteractable.Grab"/> is where the toolkit records the two things
    /// it will put back on release: the transform's parent, and whether the Rigidbody was kinematic.
    /// A creature that is clinging to the player is <em>both</em> parented to a latch anchor and
    /// kinematic, so a listener notified after the fact would arrive too late — the toolkit would
    /// already have decided to hang the enemy back on the player's chest when it is dropped, and to
    /// freeze it in mid-air on the way there. The public events all fire after <c>Grab</c>; this
    /// override is the only hook that runs before it.
    /// </remarks>
    [DisallowMultipleComponent]
    public class EnemyGrabInteractable : XRGrabInteractable
    {
        HordeEnemy m_Enemy;

        /// <summary>The enemy this handle belongs to.</summary>
        public HordeEnemy enemy => m_Enemy;

        /// <inheritdoc/>
        protected override void Awake()
        {
            base.Awake();

            // Looked up rather than required with [RequireComponent]. Adding one here would let
            // Unity auto-create a second HordeEnemy when components are added in the wrong order at
            // runtime, and the resulting pair — one wired up, one not — is a ghost that took a long
            // time to find in Fase 2a. A missing enemy is worth saying out loud instead.
            if (!TryGetComponent(out m_Enemy))
                Utils.LogError($"'{name}' tiene {nameof(EnemyGrabInteractable)} pero no {nameof(HordeEnemy)}; agarrarlo no lo despegará de nada.");
        }

        /// <summary>
        /// Refuses the grip whenever the creature says it is not there to be taken.
        /// </summary>
        /// <remarks>
        /// This is the hook that actually gets a creature out of a player's hand, and cancelling the
        /// selection is not a substitute for it. The toolkit re-evaluates this every frame for the
        /// interactable a hand is holding and lets go the moment it turns false — but it also hands
        /// back whatever is inside the hand while the grip is still held down, so anything that was
        /// merely cancelled is picked straight back up on the next frame.
        /// <para>
        /// See <see cref="HordeEnemy.isGrabbable"/> for what it refuses and why.
        /// </para>
        /// </remarks>
        /// <inheritdoc/>
        public override bool IsSelectableBy(IXRSelectInteractor interactor) =>
            base.IsSelectableBy(interactor) && (m_Enemy == null || m_Enemy.isGrabbable);

        /// <summary>
        /// Frees the enemy from whatever was holding it, an instant before the toolkit takes over.
        /// </summary>
        /// <inheritdoc/>
        protected override void Grab()
        {
            m_Enemy?.PrepareForGrab();

            base.Grab();
        }

        /// <summary>
        /// Hands the enemy back to physics once the last hand lets go.
        /// </summary>
        /// <remarks>
        /// Guarded on <see cref="XRBaseInteractable.isSelected"/> because this also fires when one of
        /// two hands releases, and an enemy still held by the other one must not start walking.
        /// <para>
        /// The throw velocity is applied by the toolkit at the end of the frame, after this runs, so
        /// there is nothing to preserve here — but the enemy does need the recovery window, or its
        /// own locomotion would overwrite that velocity on the next physics step and the throw would
        /// die on the spot.
        /// </para>
        /// </remarks>
        /// <inheritdoc/>
        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);

            if (!isSelected)
                m_Enemy?.ReleaseFromGrab();
        }
    }
}
