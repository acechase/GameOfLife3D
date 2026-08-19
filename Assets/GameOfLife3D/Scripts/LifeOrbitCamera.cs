using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GameOfLife3D
{
    /// <summary>
    /// Game-view orbit / pan / zoom around the cell volume, so you can actually
    /// look at the thing while it runs. Put this on your camera; it finds the
    /// LifeVolume by itself. Dropped on the volume by mistake it drives
    /// Camera.main instead and says so, rather than orbiting the volume around
    /// itself — which looks like the background sweeping past a subject that
    /// never moves.
    ///
    ///   Left-drag          PAN            Shift + left-drag  ORBIT
    ///   Two-finger scroll  zoom           F                  frame the volume
    ///   Right-drag         orbit (mouse alias)
    ///   Middle-drag        pan   (mouse alias)
    ///
    /// Orbit is on Shift rather than the right button because a Mac trackpad
    /// cannot right-DRAG: two-finger click is a right-click, but holding two
    /// fingers down and moving is the scroll gesture, so the drag never
    /// arrives. Right-drag is kept for anyone on an actual mouse.
    ///
    /// Editing cells is the deliberate act and
    /// lives behind <see cref="LifeInput.PaintModifier"/> (Cmd / Ctrl); a drag
    /// started with that held belongs to the brush and this component leaves it
    /// alone for its whole duration.
    ///
    /// Pan vs orbit is live, so pressing Shift just after the button goes down
    /// still gets you an orbit. Only *ownership* is latched: a drag begun as a
    /// paint stroke stays the brush's until release, so releasing Cmd partway
    /// through cannot hand a half-finished stroke to the camera.
    ///
    /// The camera is driven as pivot + spherical offset rather than by rotating
    /// the transform in place, so orbiting never accumulates roll. The pivot is
    /// stored as an offset *from the target*, so the view keeps tracking the
    /// volume even if it moves (XR grab) while preserving whatever pan you did.
    /// </summary>
    // Deliberately NOT [RequireComponent(typeof(Camera))]: that quietly adds a
    // second Camera to whatever you drop this on, and dropping it on the Life
    // Volume then gives you two cameras fighting over the Game view while this
    // component orbits the volume around itself. It finds the camera instead.
    [AddComponentMenu("GameOfLife3D/Life Orbit Camera")]
    [DisallowMultipleComponent]
    public class LifeOrbitCamera : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("What to orbit around. Left empty, it finds the LifeVolume in the scene.")]
        public Transform target;
        [Tooltip("Frame the volume on Play. The default scene camera sits 10m from a 0.6m volume, so without this you start staring at a speck.")]
        public bool frameOnStart = true;
        [Tooltip("Extra room around the volume when framing. 1 = exactly fills the view.")]
        public float frameMargin = 1.35f;

        [Header("Feel")]
        [Tooltip("Degrees of orbit per pixel of mouse movement.")]
        public float orbitSpeed = 0.25f;
        [Tooltip("Fraction of the distance panned per pixel of mouse movement.")]
        public float panSpeed = 0.0015f;
        [Tooltip("How hard one scroll notch pulls you in. Zoom is multiplicative, so it eases off as you close in.")]
        public float zoomSpeed = 0.15f;
        [Tooltip("Seconds to catch up to the input. 0 = instant, higher = floatier.")]
        [Range(0f, 0.3f)] public float smoothing = 0.06f;

        [Header("Limits")]
        public float minDistance = 0.05f;
        public float maxDistance = 100f;
        [Tooltip("Pitch clamp in degrees. Short of 90 so the view never flips over the pole.")]
        [Range(0f, 89f)] public float maxPitch = 85f;

        [Header("Angles")]
        public float initialYaw = 30f;
        public float initialPitch = 20f;

        /// <summary>
        /// What a drag is doing. Brush ownership is latched for the whole
        /// stroke; pan-vs-orbit is re-decided each frame from Shift.
        /// </summary>
        enum DragMode { None, Pan, Orbit, Brush }
        DragMode _drag = DragMode.None;

        Camera _cam;
        Transform _rig;          // the transform we actually move (the camera's)
        LifeVolume _volume;

        // Everything the user drives is a "want"; the bare fields chase it.
        Vector3 _offset, _wantOffset, _offsetVel;   // pan, relative to the target
        float _yaw, _pitch, _distance;
        float _wantYaw, _wantPitch, _wantDistance;
        float _yawVel, _pitchVel, _distVel;

        Vector3 TargetPos => target != null ? target.position : Vector3.zero;
        Vector3 Pivot => TargetPos + _offset;

        void Awake()
        {
            if (target == null)
            {
                _volume = FindFirstObjectByType<LifeVolume>();
                if (_volume != null) target = _volume.transform;
            }
            else
            {
                _volume = target.GetComponent<LifeVolume>();
            }

            // Drive the camera on this object — unless this object is the thing
            // we're supposed to be orbiting, in which case the camera we want is
            // the main one. Orbiting a transform around itself just carries the
            // subject along with the view: the background sweeps past but the
            // volume never moves relative to you.
            _cam = GetComponent<Camera>();
            bool onTheVolume = target != null && target == transform;
            if (_cam == null || onTheVolume)
            {
                if (onTheVolume && _cam != null)
                    Debug.LogWarning(
                        "LifeOrbitCamera is on the LifeVolume, which also has a Camera on it — " +
                        "probably added automatically when this component was attached. Driving " +
                        "Camera.main instead, but you should REMOVE the Camera component from " +
                        $"'{name}': two enabled cameras both render the Game view.", this);
                _cam = Camera.main;
            }

            _rig = _cam != null ? _cam.transform : null;

            if (_rig == null)
            {
                Debug.LogError("LifeOrbitCamera: no camera to drive. Put this on your " +
                               "camera, or tag one camera as MainCamera.", this);
                enabled = false;
            }
            else if (_rig == target)
            {
                Debug.LogError("LifeOrbitCamera: the camera and the LifeVolume are the same " +
                               "object, so there is nothing to orbit around. Move the " +
                               "LifeVolume onto its own GameObject.", this);
                enabled = false;
            }
        }

        void Start()
        {
            if (_rig == null) return;
            _wantYaw = _yaw = initialYaw;
            _wantPitch = _pitch = initialPitch;
            _wantDistance = _distance = Vector3.Distance(_rig.position, Pivot);

            if (frameOnStart) Frame();
            else ApplyTransform();
        }

        void LateUpdate()
        {
            ReadInput();

            // Critically-damped catch-up; with smoothing = 0 these are straight assignments.
            //
            // Plain SmoothDamp, NOT SmoothDampAngle. SmoothDampAngle routes via
            // Mathf.DeltaAngle, which takes the SHORTEST path around the circle:
            // if a fast flick (or a frame hitch that dumps a big accumulated
            // mouse delta into one frame) pushes the target more than 180° from
            // where the camera currently is, the shortest path runs BACKWARDS
            // and the view snaps the wrong way. _yaw is a plain unbounded scalar
            // and _pitch is hard-clamped to +/-85, so neither needs wrapping and
            // Quaternion.Euler is happy with yaw past 360.
            _yaw = Mathf.SmoothDamp(_yaw, _wantYaw, ref _yawVel, smoothing);
            _pitch = Mathf.SmoothDamp(_pitch, _wantPitch, ref _pitchVel, smoothing);
            _distance = Mathf.SmoothDamp(_distance, _wantDistance, ref _distVel, smoothing);
            _offset = Vector3.SmoothDamp(_offset, _wantOffset, ref _offsetVel, smoothing);

            ApplyTransform();
        }

        void ReadInput()
        {
            Vector2 delta;
            float scroll;
            bool leftHeld, rightHeld, middleHeld, framePressed;

#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            Keyboard kb = Keyboard.current;
            if (mouse == null) return;

            delta = mouse.delta.ReadValue();
            scroll = mouse.scroll.ReadValue().y;
            leftHeld = mouse.leftButton.isPressed;
            rightHeld = mouse.rightButton.isPressed;
            middleHeld = mouse.middleButton.isPressed;
            framePressed = kb != null && kb.fKey.wasPressedThisFrame;
#else
            delta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
            scroll = Input.GetAxisRaw("Mouse ScrollWheel") * 120f;
            leftHeld = Input.GetMouseButton(0);
            rightHeld = Input.GetMouseButton(1);
            middleHeld = Input.GetMouseButton(2);
            framePressed = Input.GetKeyDown(KeyCode.F);
#endif

            if (framePressed) { Frame(); return; }

            bool anyButton = leftHeld || rightHeld || middleHeld;
            if (!anyButton)
            {
                _drag = DragMode.None;
            }
            else if (_drag == DragMode.None && LifeInput.PaintModifier)
            {
                // Ownership is latched: a drag begun as a paint stroke stays the
                // brush's for its whole duration, so letting go of Cmd partway
                // through cannot hand a half-finished stroke to the camera.
                _drag = DragMode.Brush;
            }
            else if (_drag != DragMode.Brush)
            {
                // Pan vs orbit, however, stays live. It is a harmless switch to
                // make mid-drag, and latching it meant Shift pressed a moment
                // after the button went down left you panning when you asked to
                // orbit — which reads as the control being unreliable.
                _drag = (rightHeld || LifeInput.Shift) ? DragMode.Orbit : DragMode.Pan;
            }

            if (_drag == DragMode.Pan) Pan(delta);
            else if (_drag == DragMode.Orbit) Orbit(delta);

            if (Mathf.Abs(scroll) > 0.01f)
            {
                // Wheels report ~120 per notch, trackpads report small values;
                // normalize both to roughly one "notch" of zoom.
                float notch = Mathf.Abs(scroll) >= 10f ? scroll / 120f : scroll * 0.1f;
                notch = Mathf.Clamp(notch, -3f, 3f);
                _wantDistance = ClampDistance(_wantDistance * Mathf.Exp(-notch * zoomSpeed));
            }
        }

        void Orbit(Vector2 delta)
        {
            // Trackpads report accumulated movement, so one hitched frame can
            // deliver a delta worth hundreds of degrees. Cap it: a real drag
            // never needs more than a quarter turn in a single frame, and
            // without this the camera lurches on any frame-rate stutter.
            float maxStep = 90f;
            float yaw = Mathf.Clamp(delta.x * orbitSpeed, -maxStep, maxStep);
            float pitch = Mathf.Clamp(delta.y * orbitSpeed, -maxStep, maxStep);

            _wantYaw += yaw;
            _wantPitch = Mathf.Clamp(_wantPitch - pitch, -maxPitch, maxPitch);
        }

        /// <summary>
        /// Pan in the view plane, scaled by distance so the volume keeps pace
        /// with the cursor at any zoom level.
        /// </summary>
        void Pan(Vector2 delta)
        {
            _wantOffset += (-_rig.right * delta.x - _rig.up * delta.y)
                           * (panSpeed * _distance);
        }

        void ApplyTransform()
        {
            if (_rig == null) return;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            _rig.rotation = rot;
            _rig.position = Pivot - rot * Vector3.forward * _distance;
        }

        float ClampDistance(float d) => Mathf.Clamp(d, minDistance, maxDistance);

        /// <summary>
        /// Recenter on the volume and pull back far enough that its bounding
        /// sphere fits the narrower of the two view axes, plus a margin.
        /// </summary>
        [ContextMenu("Frame Volume")]
        public void Frame()
        {
            if (_cam == null) return;

            float radius = TargetRadius();
            float vHalf = _cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * _cam.aspect);
            float half = Mathf.Min(vHalf, hHalf);

            _wantDistance = _distance = ClampDistance(radius / Mathf.Max(Mathf.Sin(half), 1e-4f) * frameMargin);
            _wantYaw = _yaw = initialYaw;
            _wantPitch = _pitch = initialPitch;
            _wantOffset = _offset = Vector3.zero;
            _yawVel = _pitchVel = _distVel = 0f;
            _offsetVel = Vector3.zero;

            ApplyTransform();
        }

        /// <summary>World-space radius of the sphere enclosing the grid.</summary>
        float TargetRadius()
        {
            if (_volume == null) return 1f;

            Vector3 halfExtent = (Vector3)_volume.gridSize * (0.5f * _volume.CellSizeLocal);
            Vector3 s = _volume.transform.lossyScale;
            float scale = Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
            return Mathf.Max(halfExtent.magnitude * scale, 1e-3f);
        }
    }
}
