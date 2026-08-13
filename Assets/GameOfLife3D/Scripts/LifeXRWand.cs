#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameOfLife3D
{
    /// <summary>
    /// God-mode wand for XR controllers: reach into (or point at) the colony and
    /// squeeze the trigger to spawn cells, grip to erase; buttons for pause and
    /// reseed. Works with the XR Device Simulator too.
    ///
    /// Setup: put this on your controller GameObject (e.g. the XR Origin's
    /// "Right Controller"), assign the LifeVolume, and bind the actions —
    /// either reference the XRI default input actions (e.g. Activate for paint)
    /// or leave them empty and press "Use Default XR Bindings" in the context
    /// menu (right-click the component header) to create sensible bindings.
    /// </summary>
    public class LifeXRWand : MonoBehaviour
    {
        public LifeVolume volume;

        [Tooltip("Where cells appear: this transform's position pushed forward by Reach.")]
        public Transform tip;
        [Range(0f, 1.5f)] public float reach = 0.25f;
        [Tooltip("Brush radius in world meters.")]
        public float brushRadius = 0.05f;

        [Header("Input Actions")]
        public InputActionProperty paintAction;   // e.g. trigger
        public InputActionProperty eraseAction;   // e.g. grip
        public InputActionProperty pauseAction;   // e.g. primary button
        public InputActionProperty reseedAction;  // e.g. secondary button

        void Awake()
        {
            if (volume == null) volume = FindFirstObjectByType<LifeVolume>();
            if (tip == null) tip = transform;
        }

        void OnEnable()
        {
            paintAction.action?.Enable();
            eraseAction.action?.Enable();
            pauseAction.action?.Enable();
            reseedAction.action?.Enable();
        }

        void OnDisable()
        {
            paintAction.action?.Disable();
            eraseAction.action?.Disable();
            pauseAction.action?.Disable();
            reseedAction.action?.Disable();
        }

        void Update()
        {
            if (volume == null || tip == null) return;

            Vector3 brushPos = tip.position + tip.forward * reach;

            if (IsHeld(paintAction)) volume.PaintSphere(brushPos, brushRadius, erase: false);
            else if (IsHeld(eraseAction)) volume.PaintSphere(brushPos, brushRadius, erase: true);

            if (WasPressed(pauseAction)) volume.Paused = !volume.Paused;
            if (WasPressed(reseedAction)) volume.Reseed();
        }

        static bool IsHeld(InputActionProperty prop)
        {
            var a = prop.action;
            return a != null && a.enabled && a.IsPressed();
        }

        static bool WasPressed(InputActionProperty prop)
        {
            var a = prop.action;
            return a != null && a.enabled && a.WasPressedThisFrame();
        }

        [ContextMenu("Use Default XR Bindings")]
        void UseDefaultBindings()
        {
            paintAction = MakeButton("Paint", "<XRController>{RightHand}/{TriggerButton}");
            eraseAction = MakeButton("Erase", "<XRController>{RightHand}/{GripButton}");
            pauseAction = MakeButton("Pause", "<XRController>{RightHand}/{PrimaryButton}");
            reseedAction = MakeButton("Reseed", "<XRController>{RightHand}/{SecondaryButton}");
        }

        static InputActionProperty MakeButton(string name, string binding)
        {
            var action = new InputAction(name, InputActionType.Button, binding);
            return new InputActionProperty(action);
        }
    }
}
#endif
