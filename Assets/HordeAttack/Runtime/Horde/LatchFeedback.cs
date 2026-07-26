using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using XRMultiplayer;

namespace HordeAttack
{
    /// <summary>
    /// Tells the player, through the controllers and the edges of their vision, that something just
    /// got hold of them.
    /// </summary>
    /// <remarks>
    /// In Fase 2a being latched costs nothing, so this is the only thing that tells the player it
    /// happened at all. Both channels are needed: a creature can take hold of a leg that is outside
    /// your field of view, so the vibration is what makes you look, and the flash is what tells you
    /// the vibration was not your own punch landing.
    /// <para>
    /// It is a vignette rather than a full red screen, and a brief one. Filling a headset with
    /// opaque colour is uncomfortable in a way that filling a monitor is not — it removes the
    /// horizon, which is the thing that keeps people from feeling sick. Fase 5 hangs the real health
    /// drain off the same event.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerLatchTarget))]
    public class LatchFeedback : MonoBehaviour
    {
        /// <summary>URP's unlit shader, the one the vignette is drawn with.</summary>
        public const string k_UnlitShaderName = "Universal Render Pipeline/Unlit";

        /// <summary>Name given to the vignette object, so the scene is readable in the hierarchy.</summary>
        public const string k_VignetteName = "Latch Vignette";

        /// <summary>Resolution of the generated vignette texture, per side.</summary>
        const int k_VignetteResolution = 64;

        /// <summary>
        /// Fraction of the way to the edge at which the vignette starts to tint.
        /// </summary>
        /// <remarks>
        /// The middle of the view stays completely clear. Anything else and the player is being
        /// asked to look through red at the creature they are trying to punch off.
        /// </remarks>
        const float k_ClearCenterFraction = 0.5f;

        [Header("Vibración")]
        [SerializeField]
        [Tooltip("Amplitud del pulso en ambos mandos al engancharse un enemigo (0-1).")]
        float m_HapticAmplitude = 0.6f;

        [SerializeField]
        [Tooltip("Duración del pulso, en segundos. Más largo que el del puñetazo: es algo que te pasa a ti, no algo que hiciste.")]
        float m_HapticDuration = 0.18f;

        [Header("Destello")]
        [SerializeField]
        [Tooltip("Color de la viñeta.")]
        Color m_FlashColor = new Color(0.8f, 0.05f, 0.05f);

        [SerializeField]
        [Tooltip("Opacidad máxima de la viñeta (0-1). Alto marea; es un aviso, no una pantalla de daño.")]
        [Range(0f, 1f)]
        float m_PeakAlpha = 0.55f;

        [SerializeField]
        [Tooltip("Cuánto tarda en desvanecerse, en segundos.")]
        float m_FlashDuration = 0.45f;

        [SerializeField]
        [Tooltip("A qué distancia de la cámara se dibuja, en metros. Delante de cualquier enemigo colgado, y detrás del near clip.")]
        float m_VignetteDistance = 0.2f;

        PlayerLatchTarget m_Target;
        PlayerBodyProxy m_Body;
        Renderer m_Vignette;
        Material m_Material;
        IEnumerator m_FlashRoutine;

        /// <summary>Current opacity of the vignette, 0 when nothing has hold of the player.</summary>
        public float flashAlpha => m_Material != null ? m_Material.color.a : 0f;

        /// <summary>The vignette renderer, created on first use.</summary>
        public Renderer vignette => m_Vignette;

        /// <inheritdoc/>
        void Awake()
        {
            m_Target = GetComponent<PlayerLatchTarget>();
            m_Body = GetComponent<PlayerBodyProxy>();

            CreateVignette();
        }

        /// <inheritdoc/>
        void OnEnable()
        {
            if (m_Target != null)
                m_Target.OnEnemyLatched += HandleLatched;
        }

        /// <inheritdoc/>
        void OnDisable()
        {
            if (m_Target != null)
                m_Target.OnEnemyLatched -= HandleLatched;

            m_FlashRoutine = null;
            SetAlpha(0f);
        }

        /// <inheritdoc/>
        void OnValidate()
        {
            m_HapticAmplitude = Mathf.Clamp01(m_HapticAmplitude);
            m_HapticDuration = Mathf.Max(0f, m_HapticDuration);
            m_FlashDuration = Mathf.Max(0f, m_FlashDuration);
            m_VignetteDistance = Mathf.Max(0.05f, m_VignetteDistance);
        }

        void HandleLatched(HordeEnemy enemy, LatchAnchor anchor) => Play();

        /// <summary>
        /// Plays the whole reaction: both controllers and the vignette.
        /// </summary>
        /// <remarks>
        /// Public so a test can fire it without staging a creature jumping onto a rig, and so Fase 5
        /// can reuse it for the health drain rather than inventing a second damage cue.
        /// </remarks>
        public void Play()
        {
            PlayHaptics();

            if (m_FlashDuration <= 0f || !isActiveAndEnabled)
                return;

            if (m_FlashRoutine != null)
                StopCoroutine(m_FlashRoutine);

            m_FlashRoutine = FlashRoutine();
            StartCoroutine(m_FlashRoutine);
        }

        /// <summary>
        /// Buzzes both controllers, not the one nearest the creature.
        /// </summary>
        /// <remarks>
        /// A one-sided buzz reads as feedback for something the player did with that hand. This is
        /// something being done to them, and it has to feel different from their own punch landing
        /// or they will not know which just happened.
        /// </remarks>
        void PlayHaptics()
        {
            if (m_HapticDuration <= 0f)
                return;

            HapticsUtility.SendHapticImpulse(m_HapticAmplitude, m_HapticDuration, HapticsUtility.Controller.Left);
            HapticsUtility.SendHapticImpulse(m_HapticAmplitude, m_HapticDuration, HapticsUtility.Controller.Right);
        }

        IEnumerator FlashRoutine()
        {
            // Snaps to full and fades out, rather than fading in. A cue that ramps up reads as
            // something building; being grabbed is instantaneous and has to land that way.
            SetAlpha(m_PeakAlpha);

            for (float elapsed = 0f; elapsed < m_FlashDuration; elapsed += Time.deltaTime)
            {
                SetAlpha(Mathf.Lerp(m_PeakAlpha, 0f, elapsed / m_FlashDuration));
                yield return null;
            }

            SetAlpha(0f);
            m_FlashRoutine = null;
        }

        void SetAlpha(float alpha)
        {
            if (m_Material == null)
                return;

            var color = m_FlashColor;
            color.a = Mathf.Clamp01(alpha);
            m_Material.color = color;
        }

        /// <summary>
        /// Builds the quad the vignette is drawn on, parented to the player's head.
        /// </summary>
        /// <remarks>
        /// Generated at runtime rather than authored into the scene, for the same reason the scene
        /// itself is generated: a transparent material is a pile of blend-mode fields in YAML, and
        /// getting one of them wrong shows up as an invisible quad with no error anywhere.
        /// </remarks>
        void CreateVignette()
        {
            var head = m_Body != null ? m_Body.head : null;
            if (head == null)
            {
                Utils.LogWarning(
                    "LatchFeedback no encontró la cabeza del jugador; habrá vibración pero no destello.");
                return;
            }

            var shader = Shader.Find(k_UnlitShaderName);
            if (shader == null)
            {
                Utils.LogWarning(
                    $"No se encontró el shader '{k_UnlitShaderName}'; habrá vibración pero no destello.");
                return;
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = k_VignetteName;

            // The quad ships with a collider, and one sitting 20 cm in front of the player's eyes
            // would be the first thing every punch trigger and every enemy ran into.
            Destroy(quad.GetComponent<Collider>());

            quad.transform.SetParent(head, false);
            quad.transform.localPosition = Vector3.forward * m_VignetteDistance;
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = Vector3.one * (m_VignetteDistance * k_QuadSizePerMeter);

            m_Material = CreateVignetteMaterial(shader);
            m_Vignette = quad.GetComponent<Renderer>();
            m_Vignette.sharedMaterial = m_Material;
            m_Vignette.shadowCastingMode = ShadowCastingMode.Off;
            m_Vignette.receiveShadows = false;

            SetAlpha(0f);
        }

        /// <summary>
        /// How wide the quad has to be per meter of distance to cover a headset's field of view.
        /// </summary>
        /// <remarks>
        /// Generously oversized. A quad that only just covers the view shows its own straight edge
        /// as a hard line across the periphery, which is far more distracting than the flash it is
        /// meant to deliver.
        /// </remarks>
        const float k_QuadSizePerMeter = 5f;

        static Material CreateVignetteMaterial(Shader shader)
        {
            var material = new Material(shader) { name = "Latch Vignette (generated)" };

            // URP's unlit shader ships opaque. Transparency is not one switch but a set of them,
            // and leaving any out yields a quad that renders as a solid red wall or not at all.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.mainTexture = CreateVignetteTexture();

            return material;
        }

        /// <summary>
        /// Builds the radial mask: clear through the middle, solid at the edges.
        /// </summary>
        static Texture2D CreateVignetteTexture()
        {
            var texture = new Texture2D(k_VignetteResolution, k_VignetteResolution, TextureFormat.RGBA32, false)
            {
                name = "Latch Vignette (generated)",
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[k_VignetteResolution * k_VignetteResolution];
            float half = (k_VignetteResolution - 1) * 0.5f;

            for (int y = 0; y < k_VignetteResolution; y++)
            {
                for (int x = 0; x < k_VignetteResolution; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(k_ClearCenterFraction, 1f, radius));

                    pixels[y * k_VignetteResolution + x] =
                        new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return texture;
        }
    }
}
