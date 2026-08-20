using UnityEngine;

namespace GameOfLife3D
{
    /// <summary>
    /// Is something other than us driving the camera?
    ///
    /// When the scene swaps its plain camera for an XR rig, the headset (or the
    /// XR Device Simulator standing in for one) owns the camera pose. Anything
    /// else writing to that transform fights it every frame, which looks like
    /// jitter rather than like a bug. <see cref="LifeOrbitCamera"/> stands down
    /// when this reports true, and <see cref="LifeGlow"/> uses it to pick an
    /// antialiasing mode that XR actually supports.
    /// </summary>
    public static class LifeXR
    {
        /// <summary>
        /// True when an XR display is running. Note this is FALSE under the XR
        /// Device Simulator with no loader enabled — the simulator fakes the
        /// input devices, not the display — which is exactly the case on macOS,
        /// where OpenXR has no runtime. Use <see cref="DrivesCamera"/> for the
        /// question that actually matters.
        /// </summary>
        public static bool DisplayActive => UnityEngine.XR.XRSettings.isDeviceActive;

        /// <summary>
        /// True if this transform carries a TrackedPoseDriver — i.e. its pose
        /// comes from a tracked device.
        ///
        /// Matched by type name on purpose. There are two TrackedPoseDriver
        /// types in circulation (the Input System's and the legacy
        /// SpatialTracking one), they live in assemblies this project does not
        /// reference, and neither exists at all until the XR packages are
        /// installed. Matching the name keeps this compiling in a project with
        /// no XR packages while still recognising either one.
        /// </summary>
        public static bool IsPoseDriven(Transform t)
        {
            if (t == null) return false;
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name == "TrackedPoseDriver") return true;
            }
            return false;
        }

        /// <summary>Something other than this project owns the camera pose.</summary>
        public static bool DrivesCamera(Transform cameraTransform)
            => DisplayActive || IsPoseDriven(cameraTransform);
    }
}
