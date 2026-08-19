using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GameOfLife3D
{
    /// <summary>
    /// Game-view orbit / pan / zoom around the cell volume, so you can actually
    /// look at the thing while it runs. Put this on your camera; it finds the
    /// LifeVolume by itself.
    ///
    ///   Left-drag          PAN            Right-drag        ORBIT
    ///   Two-finger scroll  zoom           F                 frame the volume
    ///   Middle-drag        pan (three-button-mouse alias)
    ///
    /// Four bindings, no modifiers. Editing cells is the deliberate act and
    /// lives behind <see cref="LifeInput.PaintModifier"/> (Cmd / Ctrl); a drag
    /// started with that held belongs to the brush and this component leaves it
    /// alone for its whole duration.
    ///
    /// A drag's meaning is latched when the button goes down and held until it
    /// is released. Deciding per frame let a modifier arriving a frame late flip
    /// pan and orbit mid-stroke, which cancel each other and feel like a dead,
    /// glitchy zone rather than a camera move.
    ///
    /// Alt-modified drags mirror the Scene view's navigation, and they keep the
    /// bare mouse buttons free for painting — <see cref="LifeDesktopControls"/>
    /// deliberately skips painting while Alt is held, so the two never fight.
    ///
    /// The camera is driven as pivot + spherical offset rather than by rotating
    /// the transform in place, so orbiting never accumulates roll. The pivot is
    /// stored as an offset *from the target*, so the view keeps tracking the
    /// volume even if it moves (XR grab) while preserving whatever pan you did.
    /// </summary>
    [RequireComponent(typeof(Camera))]
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

        /// <summary>What a drag is doing, latched when the button goes down.</summary>
        enum DragMode { None, Pan, Orbit, Brush }
        DragMode _drag = DragMode.None;

        Camera _cam;
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
            _cam = GetComponent<Camera>();
            if (target == null)
            {
                _volume = FindFirstObjectByType<LifeVolume>();
                if (_volume != null) target = _volume.transform;
            }
            else
            {
                _volume = target.GetComponent<LifeVolume>();
            }
        }

        void Start()
        {
            _wantYaw = _yaw = initialYaw;
            _wantPitch = _pitch = initialPitch;
            _wantDistance = _distance = Vector3.Distance(transform.position, Pivot);

            if (frameOnStart) Frame();
            else ApplyTransform();
        }

        void LateUpdate()
        {
            ReadInput();

            // Critically-damped catch-up; with smoothing = 0 these are straight assignments.
            _yaw = Mathf.SmoothDampAngle(_yaw, _wantYaw, ref _yawVel, smoothing);
            _pitch = Mathf.SmoothDampAngle(_pitch, _wantPitch, ref _pitchVel, smoothing);
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

            // The mode is decided once, when the button goes down, and held for
            // the whole drag. Deciding it per frame meant a modifier registering
            // a frame late — or flickering mid-drag — silently swapped pan and
            // orbit, which partly cancel and feel like a glitchy dead zone.
            bool anyButton = leftHeld || rightHeld || middleHeld;
            if (!anyButton)
            {
                _drag = DragMode.None;
            }
            else if (_drag == DragMode.None)
            {
                if (LifeInput.PaintModifier)
                    _drag = DragMode.Brush;         // belongs to the brush, not us
                else if (leftHeld || middleHeld)
                    _drag = DragMode.Pan;
                else
                    _drag = DragMode.Orbit;
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
            _wantYaw += delta.x * orbitSpeed;
            _wantPitch = Mathf.Clamp(_wantPitch - delta.y * orbitSpeed, -maxPitch, maxPitch);
        }

        /// <summary>
        /// Pan in the view plane, scaled by distance so the volume keeps pace
        /// with the cursor at any zoom level.
        /// </summary>
        void Pan(Vector2 delta)
        {
            _wantOffset += (-transform.right * delta.x - transform.up * delta.y)
                           * (panSpeed * _distance);
        }

        void ApplyTransform()
        {
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.rotation = rot;
            transform.position = Pivot - rot * Vector3.forward * _distance;
        }

        float ClampDistance(float d) => Mathf.Clamp(d, minDistance, maxDistance);

        /// <summary>
        /// Recenter on the volume and pull back far enough that its bounding
        /// sphere fits the narrower of the two view axes, plus a margin.
        /// </summary>
        [ContextMenu("Frame Volume")]
        public void Frame()
        {
            if (_cam == null) _cam = GetComponent<Camera>();

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
