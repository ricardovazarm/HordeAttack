using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// One place on the player's body an enemy can take hold of, and the bookkeeping that keeps it
    /// to one enemy at a time.
    /// </summary>
    /// <remarks>
    /// Exclusivity is the whole point of this component. Without it every arriving enemy picks the
    /// nearest spot on the body and they stack into a single flickering lump, which reads as one
    /// broken creature rather than three separate ones.
    /// </remarks>
    [DisallowMultipleComponent]
    public class LatchAnchor : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Banda del cuerpo a la que pertenece. Los saltadores prefieren High; los aferradores, Low.")]
        LatchHeight m_Height = LatchHeight.Low;

        [Header("Colocación (solo si cuelga del PlayerBodyProxy)")]
        [SerializeField]
        [Tooltip("Altura como fracción de la altura de ojos REAL del jugador. 0 = suelo, 1 = ojos.")]
        float m_HeightFraction = 0.5f;

        [SerializeField]
        [Tooltip("Desplazamiento lateral (x) y hacia delante (z) respecto al eje del cuerpo, en metros.")]
        Vector2 m_BodyOffset = Vector2.zero;

        [Header("Agarre")]
        [SerializeField]
        [Tooltip("Cuánto cuelga el enemigo por debajo del ancla, en metros. En los hombros cuelga; en las piernas se abraza y vale 0.")]
        float m_HangDrop;

        HordeEnemy m_Occupant;

        /// <summary>Which band of the body this anchor belongs to.</summary>
        public LatchHeight height => m_Height;

        /// <summary>Height of this anchor as a fraction of the player's eye height.</summary>
        public float heightFraction => m_HeightFraction;

        /// <summary>Sideways and forward offset from the body axis, in meters.</summary>
        public Vector2 bodyOffset => m_BodyOffset;

        /// <summary>How far below the anchor a latched enemy's center sits, in meters.</summary>
        public float hangDrop => Mathf.Max(0f, m_HangDrop);

        /// <summary>The enemy currently holding this anchor, if any.</summary>
        public HordeEnemy occupant => m_Occupant;

        /// <summary>Whether no enemy currently holds this anchor.</summary>
        /// <remarks>
        /// Compares against null through Unity's overload on purpose: an occupant that was
        /// destroyed without releasing — which Fase 3's pooling makes easy to do by accident —
        /// leaves the anchor free rather than permanently reserved by a corpse.
        /// </remarks>
        public bool isFree => m_Occupant == null;

        /// <summary>The player this anchor belongs to. Assigned by that player when it collects its anchors.</summary>
        public PlayerLatchTarget owner { get; internal set; }

        /// <summary>
        /// Configures the anchor from code, for the scene builder.
        /// </summary>
        /// <remarks>
        /// The POC's rig is generated rather than authored, so these have to be settable without an
        /// inspector. See <c>HordePocSceneBuilder</c>.
        /// </remarks>
        public void Configure(LatchHeight height, float heightFraction, Vector2 bodyOffset, float hangDrop)
        {
            m_Height = height;
            m_HeightFraction = heightFraction;
            m_BodyOffset = bodyOffset;
            m_HangDrop = hangDrop;
        }

        /// <summary>
        /// Claims this anchor for <paramref name="enemy"/>, or reports that someone else has it.
        /// </summary>
        public bool TryOccupy(HordeEnemy enemy)
        {
            if (enemy == null)
                return false;

            if (!isFree && m_Occupant != enemy)
                return false;

            m_Occupant = enemy;

            return true;
        }

        /// <summary>
        /// Gives the anchor back, but only to whoever actually held it.
        /// </summary>
        /// <remarks>
        /// The identity check matters: an enemy that dies a frame after another one took the anchor
        /// it used to be on would otherwise free a slot that is in use, and two creatures would end
        /// up in the same spot.
        /// </remarks>
        public void Release(HordeEnemy enemy)
        {
            if (m_Occupant == enemy)
                m_Occupant = null;
        }

        /// <inheritdoc/>
        void OnValidate()
        {
            m_HeightFraction = Mathf.Clamp01(m_HeightFraction);
            m_HangDrop = Mathf.Max(0f, m_HangDrop);
        }
    }
}
