using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace BrickBurst
{
    /// <summary>
    /// One place that reads keyboard and mouse, so the game works whichever Active Input Handling the
    /// project uses (Input Manager (Old), Input System Package (New) or Both). The mouse position needs
    /// either the legacy Input.mousePosition or the new Mouse.current; both are implemented here.
    /// </summary>
    public static class InputReader
    {
        /// <summary>Keyboard paddle direction: -1 (A / left arrow), 0, or +1 (D / right arrow).</summary>
        public static float PaddleAxis()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetAxisRaw("Horizontal");
#elif ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            if (k == null) return 0f;
            float x = 0f;
            if (k.aKey.isPressed || k.leftArrowKey.isPressed) x -= 1f;
            if (k.dKey.isPressed || k.rightArrowKey.isPressed) x += 1f;
            return x;
#else
            return 0f;
#endif
        }

        /// <summary>Mouse position in screen pixels; false when there is no mouse.</summary>
        public static bool TryGetPointer(out Vector2 screenPosition)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            Vector3 p = Input.mousePosition;
            screenPosition = new Vector2(p.x, p.y);
            return true;
#elif ENABLE_INPUT_SYSTEM
            Mouse m = Mouse.current;
            if (m == null)
            {
                screenPosition = new Vector2(0f, 0f);
                return false;
            }
            screenPosition = m.position.ReadValue();
            return true;
#else
            screenPosition = new Vector2(0f, 0f);
            return false;
#endif
        }

        /// <summary>Launch the ball / continue after a level: Space, Enter, Up arrow or left mouse button.</summary>
        public static bool LaunchPressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetMouseButtonDown(0);
#elif ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            Mouse m = Mouse.current;
            return (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame))
                || (m != null && m.leftButton.wasPressedThisFrame);
#else
            return false;
#endif
        }

        public static bool PausePressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape);
#elif ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            return k != null && (k.pKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame);
#else
            return false;
#endif
        }

        public static bool RestartPressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.R);
#elif ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            return k != null && k.rKey.wasPressedThisFrame;
#else
            return false;
#endif
        }
    }
}
