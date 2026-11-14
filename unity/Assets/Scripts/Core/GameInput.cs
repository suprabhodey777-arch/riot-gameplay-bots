// One input facade over both backends. ENABLE_INPUT_SYSTEM is defined when the
// project's Active Input Handling is "Input System Package" or "Both"; otherwise
// the legacy UnityEngine.Input manager is used.
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Saltworks
{
    public enum GameKey
    {
        Forward,
        Back,
        Left,
        Right,
        Walk,
        Crouch,
        Use,
        Smoke,
        Flash,
        Dash,
        Recon,
        Pause,
        Confirm,
        Fps,
        Minimap,
        FullMap,
    }

    public static class GameInput
    {
        /// <summary>Degrees of camera turn per mouse pixel at sensitivity 1.</summary>
        public const float DegreesPerPixel = 0.08f;

#if ENABLE_INPUT_SYSTEM
        static readonly Key[][] Keys =
        {
            new[] { Key.W },
            new[] { Key.S },
            new[] { Key.A },
            new[] { Key.D },
            new[] { Key.LeftShift, Key.RightShift },
            new[] { Key.LeftCtrl, Key.RightCtrl },
            new[] { Key.F },
            new[] { Key.C },
            new[] { Key.Q },
            new[] { Key.E },
            new[] { Key.X },
            new[] { Key.Escape },
            new[] { Key.Enter, Key.NumpadEnter, Key.Space },
            new[] { Key.F3 },
            new[] { Key.M },
            new[] { Key.Tab },
        };

        static KeyControl Control(Key k)
        {
            Keyboard kb = Keyboard.current;
            return kb == null ? null : kb[k];
        }

        public static bool Held(GameKey k)
        {
            foreach (Key key in Keys[(int)k])
            {
                KeyControl c = Control(key);
                if (c != null && c.isPressed) return true;
            }
            return false;
        }

        public static bool Pressed(GameKey k)
        {
            foreach (Key key in Keys[(int)k])
            {
                KeyControl c = Control(key);
                if (c != null && c.wasPressedThisFrame) return true;
            }
            return false;
        }

        public static bool FireHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;

        /// <summary>Mouse movement this frame, in degrees at sensitivity 1 (x right, y up).</summary>
        public static Vector2 LookDelta()
        {
            if (Mouse.current == null) return Vector2.zero;
            return Mouse.current.delta.ReadValue() * DegreesPerPixel;
        }
#else
        static readonly KeyCode[][] Keys =
        {
            new[] { KeyCode.W },
            new[] { KeyCode.S },
            new[] { KeyCode.A },
            new[] { KeyCode.D },
            new[] { KeyCode.LeftShift, KeyCode.RightShift },
            new[] { KeyCode.LeftControl, KeyCode.RightControl },
            new[] { KeyCode.F },
            new[] { KeyCode.C },
            new[] { KeyCode.Q },
            new[] { KeyCode.E },
            new[] { KeyCode.X },
            new[] { KeyCode.Escape },
            new[] { KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space },
            new[] { KeyCode.F3 },
            new[] { KeyCode.M },
            new[] { KeyCode.Tab },
        };

        public static bool Held(GameKey k)
        {
            foreach (KeyCode key in Keys[(int)k])
                if (Input.GetKey(key)) return true;
            return false;
        }

        public static bool Pressed(GameKey k)
        {
            foreach (KeyCode key in Keys[(int)k])
                if (Input.GetKeyDown(key)) return true;
            return false;
        }

        public static bool FireHeld => Input.GetMouseButton(0);

        /// <summary>Mouse movement this frame, in degrees at sensitivity 1 (x right, y up).</summary>
        public static Vector2 LookDelta()
        {
            // The default "Mouse X/Y" axes are pixels * 0.1.
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * (DegreesPerPixel * 10f);
        }
#endif
    }
}
