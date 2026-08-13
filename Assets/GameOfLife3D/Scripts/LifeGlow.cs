using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameOfLife3D
{
    /// <summary>
    /// Post-processing for the cell volume, with zero asset wiring.
    ///
    /// The cell shader writes genuinely HDR colors (young cyan peaks near 4.0),
    /// so all the "glow" is really bloom doing its job on values above 1. This
    /// component builds its own global <see cref="Volume"/> — profile and all —
    /// in code at a high priority, so it overrides whatever generic profile the
    /// scene happens to carry without touching Unity's template assets. Drop it
    /// on the same GameObject as <see cref="LifeVolume"/> and it boots itself.
    ///
    /// The volume object and its profile are created with DontSave hide flags:
    /// they never appear in the Hierarchy and never dirty the scene, in edit
    /// mode or at runtime. Tweak the fields below and the look updates live.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("GameOfLife3D/Life Glow")]
    [DisallowMultipleComponent]
    public class LifeGlow : MonoBehaviour
    {
        [Header("Bloom")]
        [Tooltip("How much the glow spills. This is the main 'bioluminescence' dial.")]
        [Range(0f, 3f)] public float intensity = 1.0f;
        [Tooltip("Brightness a pixel must exceed to bloom. Cells run 1..4 in HDR, so ~1 lets only lit cells glow.")]
        [Range(0f, 3f)] public float threshold = 0.95f;
        [Tooltip("How far the glow spreads. Higher = softer, more volumetric haze.")]
        [Range(0f, 1f)] public float scatter = 0.7f;
        [Tooltip("Ceiling on how bright a single pixel may contribute. Tames fireflies on dense grids.")]
        public float clamp = 20f;
        [Tooltip("Off on Quest: high-quality filtering costs real fill rate on mobile GPUs.")]
        public bool highQualityFiltering = true;

        [Header("Grade")]
        public TonemappingMode tonemapping = TonemappingMode.Neutral;
        [Tooltip("Stops of exposure applied before tonemapping. Lift to make the whole volume hotter.")]
        [Range(-2f, 2f)] public float postExposure = 0f;
        [Range(-100f, 100f)] public float contrast = 10f;
        [Range(-100f, 100f)] public float saturation = 10f;
        [Tooltip("Darkened corners. Reads as depth around a floating volume; 0 disables.")]
        [Range(0f, 1f)] public float vignette = 0.25f;

        [Header("Camera")]
        [Tooltip("Force HDR + post-processing on the main camera at play time. Off if you manage the camera yourself.")]
        public bool configureCamera = true;
        [Tooltip("Clear to near-black so the glow reads. MUST be off for AR passthrough — it would paint over the real world.")]
        public bool darkBackground = true;
        public Color backgroundColor = new Color(0.012f, 0.014f, 0.022f, 1f);
        [Tooltip("SMAA looks best on desktop; use FXAA in XR, where SMAA is unsupported.")]
        public AntialiasingMode antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        GameObject _volumeGO;
        Volume _volume;
        VolumeProfile _profile;
        Bloom _bloom;
        Tonemapping _tonemapping;
        ColorAdjustments _grade;
        Vignette _vignette;

        void OnEnable()
        {
            BuildVolume();
            ApplySettings();
            if (configureCamera && Application.isPlaying) ConfigureCamera();
        }

        void OnDisable()
        {
            DestroyVolume();
        }

        void OnValidate()
        {
            // Live-tune from the inspector without re-entering play mode.
            if (_bloom != null) ApplySettings();
        }

        void BuildVolume()
        {
            if (_volume != null) return;

            // Layer 0 (Default): the camera's volume mask is Default-only out of
            // the box, and a volume on an unmasked layer is silently ignored.
            _volumeGO = new GameObject("Life Glow Volume") { layer = 0 };
            _volumeGO.hideFlags = HideFlags.HideAndDontSave;
            _volumeGO.transform.SetParent(transform, false);

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "LifeGlowProfile";
            _profile.hideFlags = HideFlags.HideAndDontSave;

            // Add(true) turns on every override state, so these win the blend
            // over the scene's profile rather than inheriting stale values.
            _bloom = _profile.Add<Bloom>(true);
            _tonemapping = _profile.Add<Tonemapping>(true);
            _grade = _profile.Add<ColorAdjustments>(true);
            _vignette = _profile.Add<Vignette>(true);

            _volume = _volumeGO.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;   // above the scene's Global Volume
            _volume.weight = 1f;
            _volume.sharedProfile = _profile;
        }

        void ApplySettings()
        {
            if (_bloom == null) return;

            _bloom.intensity.value = intensity;
            _bloom.threshold.value = threshold;
            _bloom.scatter.value = scatter;
            _bloom.clamp.value = clamp;
            _bloom.highQualityFiltering.value = highQualityFiltering;

            _tonemapping.mode.value = tonemapping;

            _grade.postExposure.value = postExposure;
            _grade.contrast.value = contrast;
            _grade.saturation.value = saturation;

            _vignette.intensity.value = vignette;
            _vignette.smoothness.value = 0.4f;
            _vignette.active = vignette > 0f;
        }

        void DestroyVolume()
        {
            if (_profile != null) DestroyAsset(_profile);
            if (_volumeGO != null) DestroyAsset(_volumeGO);
            _volumeGO = null; _volume = null; _profile = null;
            _bloom = null; _tonemapping = null; _grade = null; _vignette = null;
        }

        static void DestroyAsset(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        /// <summary>
        /// Makes sure the rendering camera can actually show bloom: HDR buffers
        /// (values above 1 survive), the post-processing pass enabled, and a
        /// dark background for the glow to read against.
        /// </summary>
        void ConfigureCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            cam.allowHDR = true;
            if (darkBackground)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = backgroundColor;
            }

            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;

            data.renderPostProcessing = true;
            data.antialiasing = antialiasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.dithering = true;   // kills banding in the dark falloff around the volume
        }
    }
}
