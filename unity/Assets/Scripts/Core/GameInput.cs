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
        // round 4
        Reload,
        Primary,
        Secondary,
        Buy,
    }

    public static class GameInput
    {
        /// <summary>Degrees of camera turn per mouse pixel at sensitivity 1.</summary>
        public const float DegreesPerPixel = 0.08f;

        /// <summary>The fixed key map, for the settings' keybind list (action, keys).</summary>
        public static readonly string[][] Bindings =
        {
            new[] { "Move", "W A S D" },
            new[] { "Walk (quiet)", "Shift" },
            new[] { "Crouch", "Ctrl" },
            new[] { "Fire", "Left mouse" },
            new[] { "Scope (Longshot)", "Right mouse" },
            new[] { "Reload", "R" },
            new[] { "Primary weapon", "1" },
            new[] { "Secondary weapon", "2" },
            new[] { "Cycle weapons", "Mouse wheel" },
            new[] { "Buy menu (buy phase)", "B" },
            new[] { "Smoke", "Hold C, release" },
            new[] { "Flash", "Hold Q, release" },
            new[] { "Dash", "E" },
            new[] { "Recon dart", "Hold X, release" },
            new[] { "Plant / defuse", "Hold F" },
            new[] { "Tactical map / peek", "M / hold Tab" },
            new[] { "Frame rate readout", "F3" },
            new[] { "Pause, close menus", "Esc" },
            new[] { "Jump", "Space" },
            new[] { "Confirm", "Enter / Space" },
            new[] { "Move focus in menus", "Arrow keys / Tab" },
        };

        /// <summary>
        /// Keys held by the screenshot tour (Core/GameController.Tour.cs) instead of a person; null normally.
        /// Held and Pressed report these as well as the real keyboard.
        /// </summary>
        public static System.Func<GameKey, bool> SyntheticHeld;

        /// <summary>Look movement the tour adds each frame (degrees at sensitivity 1).</summary>
        public static Vector2 SyntheticLook;
        /// <summary>Demo recording: a key press for one frame, and the fire button held.</summary>
        public static System.Func<GameKey, bool> SyntheticPressed;
        public static bool SyntheticFire;

        static bool Synthetic(GameKey k)
        {
            return SyntheticHeld != null && SyntheticHeld(k);
        }

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
            new[] { Key.R },
            new[] { Key.Digit1 },
            new[] { Key.Digit2 },
            new[] { Key.B },
        };

        static KeyControl Control(Key k)
        {
            Keyboard kb = Keyboard.current;
            return kb == null ? null : kb[k];
        }

        public static bool Held(GameKey k)
        {
            if (Synthetic(k)) return true;
            foreach (Key key in Keys[(int)k])
            {
                KeyControl c = Control(key);
                if (c != null && c.isPressed) return true;
            }
            return false;
        }

        public static bool Pressed(GameKey k)
        {
            if (SyntheticPressed != null && SyntheticPressed(k)) return true;
            foreach (Key key in Keys[(int)k])
            {
                KeyControl c = Control(key);
                if (c != null && c.wasPressedThisFrame) return true;
            }
            return false;
        }

        public static bool JumpPressed => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        public static bool FireHeld => SyntheticFire || (Mouse.current != null && Mouse.current.leftButton.isPressed);
        public static bool AimHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;
        public static bool AimPressed => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;

        /// <summary>Mouse wheel this frame: positive = up, in notches (roughly).</summary>
        public static float Scroll
        {
            get
            {
                if (Mouse.current == null) return 0f;
                float y = Mouse.current.scroll.ReadValue().y;
                // The Input System reports 120 per notch on some platforms and 1 on others.
                return Mathf.Abs(y) >= 20f ? y / 120f : y;
            }
        }

        /// <summary>Mouse movement this frame, in degrees at sensitivity 1 (x right, y up).</summary>
        public static Vector2 LookDelta()
        {
            if (Mouse.current == null) return SyntheticLook;
            return Mouse.current.delta.ReadValue() * DegreesPerPixel + SyntheticLook;
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
            new[] { KeyCode.R },
            new[] { KeyCode.Alpha1 },
            new[] { KeyCode.Alpha2 },
            new[] { KeyCode.B },
        };

        public static bool Held(GameKey k)
        {
            if (Synthetic(k)) return true;
            foreach (KeyCode key in Keys[(int)k])
                if (Input.GetKey(key)) return true;
            return false;
        }

        public static bool Pressed(GameKey k)
        {
            if (SyntheticPressed != null && SyntheticPressed(k)) return true;
            foreach (KeyCode key in Keys[(int)k])
                if (Input.GetKeyDown(key)) return true;
            return false;
        }

        public static bool JumpPressed => Input.GetKeyDown(KeyCode.Space);
        public static bool FireHeld => SyntheticFire || Input.GetMouseButton(0);
        public static bool AimHeld => Input.GetMouseButton(1);
        public static bool AimPressed => Input.GetMouseButtonDown(1);
        public static float Scroll => Input.mouseScrollDelta.y;

        /// <summary>Mouse movement this frame, in degrees at sensitivity 1 (x right, y up).</summary>
        public static Vector2 LookDelta()
        {
            // The default "Mouse X/Y" axes are pixels * 0.1.
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * (DegreesPerPixel * 10f) + SyntheticLook;
        }
#endif
    }
}
