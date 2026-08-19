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
    /// the same time. One definition, both callers.
    /// </summary>
    public static class LifeInput
    {
#if ENABLE_INPUT_SYSTEM
        static Keyboard Kb => Keyboard.current;
#endif

        /// <summary>Alt / Option.</summary>
        public static bool Alt
        {
#if ENABLE_INPUT_SYSTEM
            get { var k = Kb; return k != null && (k.leftAltKey.isPressed || k.rightAltKey.isPressed); }
#else
            get { return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt); }
#endif
        }

        public static bool Shift
        {
#if ENABLE_INPUT_SYSTEM
            get { var k = Kb; return k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed); }
#else
            get { return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); }
#endif
        }

        /// <summary>
        /// The "I mean to edit cells, not move the camera" modifier: Command on
        /// macOS, Control elsewhere. Both are accepted on every platform so the
        /// muscle memory travels.
        /// </summary>
        public static bool PaintModifier
        {
#if ENABLE_INPUT_SYSTEM
            get
            {
                var k = Kb;
                return k != null && (k.leftCommandKey.isPressed || k.rightCommandKey.isPressed
                                  || k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed);
            }
#else
            get
            {
                return Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)
                    || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            }
#endif
        }
    }
}
