using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GameOfLife3D
{
    /// <summary>
    /// Shared modifier-key state for the desktop controls.
    ///
    /// This exists because the camera and the brush have to agree on who owns a
    /// drag, and when each component tested the keys itself the two definitions
    /// drifted apart — a modifier added to one kept painting and navigating at
    /// the same time. One definition, both callers: add modifiers here.
    /// </summary>
    public static class LifeInput
    {
#if ENABLE_INPUT_SYSTEM
        static Keyboard Kb => Keyboard.current;
#endif

        /// <summary>Orbit modifier, so orbit is reachable without a right-drag.</summary>
        public static bool Shift
        {
#if ENABLE_INPUT_SYSTEM
            get { var k = Kb; return k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed); }
#else
            get { return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); }
#endif
        }

        /// <summary>
        /// The "I mean to edit cells, not move the camera" modifier.
        ///
        /// Command on macOS, Control elsewhere — and deliberately NOT Control on
        /// macOS, where Ctrl+click is a system-level right-click: the OS would
        /// deliver it as a right-button drag, so "Ctrl+drag to paint" would
        /// silently erase instead.
        /// </summary>
        public static bool PaintModifier
        {
#if ENABLE_INPUT_SYSTEM
            get
            {
                var k = Kb;
                if (k == null) return false;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                return k.leftCommandKey.isPressed || k.rightCommandKey.isPressed;
#else
                return k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed
                    || k.leftCommandKey.isPressed || k.rightCommandKey.isPressed;
#endif
            }
#else
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            get { return Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand); }
#else
            get
            {
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                    || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            }
#endif
#endif
        }
    }
}
