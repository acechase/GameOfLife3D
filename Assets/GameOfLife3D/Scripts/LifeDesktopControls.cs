using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GameOfLife3D
{
    /// <summary>
    /// Mouse + keyboard controls for desktop / XR Device Simulator iteration.
    /// Put this on the same GameObject as LifeVolume (or anywhere, and assign it).
    ///
    ///   Space      pause / resume          N          single step
    ///   R          reseed (new random)     C          clear all cells
    ///   T          cycle rule preset       [  /  ]    slower / faster
    ///   Left-drag  paint cells             Right-drag erase cells
    ///
    /// Painting casts a ray from the active camera through the pointer and
    /// paints where the ray enters the volume. Holding Alt suppresses painting
    /// entirely, leaving those drags to <see cref="LifeOrbitCamera"/>.
    /// </summary>
    public class LifeDesktopControls : MonoBehaviour
    {
        public LifeVolume volume;
        [Tooltip("Paint sphere radius in world meters.")]
        public float brushRadius = 0.045f;
        [Tooltip("How far past the volume surface the brush center sits.")]
        public float brushDepth = 0.05f;

        void Awake()
        {
            if (volume == null) volume = GetComponent<LifeVolume>();
            if (volume == null) volume = FindFirstObjectByType<LifeVolume>();
        }

        void Update()
        {
            if (volume == null) return;

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame) volume.Paused = !volume.Paused;
                if (kb.nKey.wasPressedThisFrame) { volume.Paused = true; volume.StepOnce(); }
                if (kb.rKey.wasPressedThisFrame) volume.Reseed();
                if (kb.cKey.wasPressedThisFrame) { volume.Paused = true; volume.ClearAll(); }
                if (kb.tKey.wasPressedThisFrame) volume.CycleRule(+1);
                if (kb.leftBracketKey.wasPressedThisFrame)
                    volume.stepsPerSecond = Mathf.Max(0.5f, volume.stepsPerSecond / 1.5f);
                if (kb.rightBracketKey.wasPressedThisFrame)
                    volume.stepsPerSecond = Mathf.Min(60f, volume.stepsPerSecond * 1.5f);
            }

            // Alt-drags belong to LifeOrbitCamera; painting sits this one out
            // or every attempt to look around would smear cells across the grid.
            bool alt = kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);

            var mouse = Mouse.current;
            if (mouse != null && !alt)
            {
                bool paint = mouse.leftButton.isPressed;
                bool erase = mouse.rightButton.isPressed;
                if (paint || erase)
                    PaintAtPointer(mouse.position.ReadValue(), erase);
            }
#else
            if (Input.GetKeyDown(KeyCode.Space)) volume.Paused = !volume.Paused;
            if (Input.GetKeyDown(KeyCode.N)) { volume.Paused = true; volume.StepOnce(); }
            if (Input.GetKeyDown(KeyCode.R)) volume.Reseed();
            if (Input.GetKeyDown(KeyCode.C)) { volume.Paused = true; volume.ClearAll(); }
            if (Input.GetKeyDown(KeyCode.T)) volume.CycleRule(+1);
            if (Input.GetKeyDown(KeyCode.LeftBracket))
                volume.stepsPerSecond = Mathf.Max(0.5f, volume.stepsPerSecond / 1.5f);
            if (Input.GetKeyDown(KeyCode.RightBracket))
                volume.stepsPerSecond = Mathf.Min(60f, volume.stepsPerSecond * 1.5f);

            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (!alt && (Input.GetMouseButton(0) || Input.GetMouseButton(1)))
                PaintAtPointer(Input.mousePosition, Input.GetMouseButton(1));
#endif
        }

        void PaintAtPointer(Vector2 screenPos, bool erase)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(screenPos);
            if (RayVolumeEntry(ray, out Vector3 entry, out float rayScale))
                volume.PaintSphere(entry + ray.direction * (brushDepth * rayScale), brushRadius, erase);
        }

        /// <summary>
        /// Slab-test the ray against the volume's oriented bounding box.
        /// Returns the world-space entry point (or the ray origin if it starts
        /// inside), plus the local-to-world length scale along the ray.
        /// </summary>
        bool RayVolumeEntry(Ray ray, out Vector3 entryWorld, out float rayScale)
        {
            Transform t = volume.transform;
            Vector3 o = t.InverseTransformPoint(ray.origin);
            Vector3 d = t.InverseTransformVector(ray.direction);
            float dLen = d.magnitude;
            rayScale = 1f; // paint depth offset is applied in world units along the world ray
            entryWorld = default;
            if (dLen < 1e-8f) return false;
            d /= dLen;

            Vector3 half = (Vector3)volume.gridSize * (0.5f * volume.CellSizeLocal);
            float tMin = 0f, tMax = float.PositiveInfinity;
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(d[i]) < 1e-8f)
                {
                    if (Mathf.Abs(o[i]) > half[i]) return false;
                }
                else
                {
                    float inv = 1f / d[i];
                    float t0 = (-half[i] - o[i]) * inv;
                    float t1 = (half[i] - o[i]) * inv;
                    if (t0 > t1) (t0, t1) = (t1, t0);
                    tMin = Mathf.Max(tMin, t0);
                    tMax = Mathf.Min(tMax, t1);
                    if (tMin > tMax) return false;
                }
            }

            Vector3 entryLocal = o + d * tMin;
            entryWorld = t.TransformPoint(entryLocal);
            return true;
        }
    }
}
