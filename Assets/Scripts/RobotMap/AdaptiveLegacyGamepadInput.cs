using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.RobotMap
{
    public enum LegacyGamepadFamily
    {
        None,
        Xbox,
        Sony,
        Generic
    }

    /// <summary>
    /// Reads normalized controls from the Input System when it is available,
    /// while retaining the project's legacy Input Manager axis mappings as a
    /// fallback. Unity 6 can expose a controller through the Input System
    /// without exposing its legacy joystick axes.
    /// </summary>
    public static class AdaptiveLegacyGamepadInput
    {
        private const string MoveAxis = "Gamepad Move";
        private const string TurnAxis = "Gamepad Turn";
        private const string XboxTriggerThrottleAxis =
            "Gamepad Trigger Throttle";
        private const string XboxBalanceHorizontalAxis =
            "Gamepad Balance Horizontal";
        private const string XboxBalanceVerticalAxis =
            "Gamepad Balance Vertical";
        private const string SonyBalanceHorizontalAxis =
            "Gamepad Sony Balance Horizontal";
        private const string SonyBalanceVerticalAxis =
            "Gamepad Sony Balance Vertical";
        private const string SonyLeftTriggerAxis =
            "Gamepad Sony Left Trigger";
        private const string SonyRightTriggerAxis =
            "Gamepad Sony Right Trigger";

        private static readonly HashSet<string> MissingAxes = new();
        private static float nextDeviceRefreshTime;
        private static bool sonyLeftTriggerIsBipolar;
        private static bool sonyRightTriggerIsBipolar;
        private static string activeDeviceName = string.Empty;

        public static LegacyGamepadFamily ActiveFamily { get; private set; }
        public static string ActiveDeviceName => activeDeviceName;
        public static bool HasConnectedGamepad =>
            ActiveFamily != LegacyGamepadFamily.None;

        public static float ReadMove()
        {
#if ENABLE_INPUT_SYSTEM
            if (TryGetInputSystemGamepad(out Gamepad gamepad))
                return gamepad.leftStick.ReadValue().y;
#endif

            RefreshDeviceIfNeeded();
            return HasConnectedGamepad ? ReadAxisSafely(MoveAxis) : 0f;
        }

        public static float ReadSteering()
        {
#if ENABLE_INPUT_SYSTEM
            if (TryGetInputSystemGamepad(out Gamepad gamepad))
                return gamepad.leftStick.ReadValue().x;
#endif

            RefreshDeviceIfNeeded();
            return HasConnectedGamepad ? ReadAxisSafely(TurnAxis) : 0f;
        }

        public static Vector2 ReadLeftStick()
        {
            return new Vector2(ReadSteering(), ReadMove());
        }

        public static bool IsLeftStickButtonHeld()
        {
#if ENABLE_INPUT_SYSTEM
            foreach (Gamepad gamepad in Gamepad.all)
            {
                if (gamepad != null
                    && gamepad.added
                    && gamepad.leftStickButton.isPressed)
                {
                    return true;
                }
            }
#endif
            // Legacy Xbox/XInput and most generic Windows mappings expose L3
            // as joystick button 8. The Input System path above handles Sony
            // layouts without relying on their legacy button indices.
            return Input.GetKey(KeyCode.JoystickButton8);
        }

        public static Vector2 ReadBalance()
        {
#if ENABLE_INPUT_SYSTEM
            if (TryGetInputSystemGamepad(out Gamepad gamepad))
                return gamepad.rightStick.ReadValue();
#endif

            RefreshDeviceIfNeeded();
            if (!HasConnectedGamepad)
                return Vector2.zero;

            return ActiveFamily == LegacyGamepadFamily.Sony
                ? new Vector2(
                    ReadAxisSafely(SonyBalanceHorizontalAxis),
                    ReadAxisSafely(SonyBalanceVerticalAxis))
                : new Vector2(
                    ReadAxisSafely(XboxBalanceHorizontalAxis),
                    ReadAxisSafely(XboxBalanceVerticalAxis));
        }

        public static Vector2 ReadRightStick()
        {
            return ReadBalance();
        }

        public static bool IsRightShoulderHeld()
        {
#if ENABLE_INPUT_SYSTEM
            bool hasInputSystemGamepad = false;
            foreach (Gamepad gamepad in Gamepad.all)
            {
                if (gamepad == null || !gamepad.added)
                    continue;

                hasInputSystemGamepad = true;
                if (gamepad.rightShoulder.isPressed)
                    return true;
            }

            if (hasInputSystemGamepad)
                return false;
#endif

            RefreshDeviceIfNeeded();
            return Input.GetKey(KeyCode.JoystickButton5);
        }

        public static bool WasRightShoulderPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            bool hasInputSystemGamepad = false;
            foreach (Gamepad gamepad in Gamepad.all)
            {
                if (gamepad == null || !gamepad.added)
                    continue;

                hasInputSystemGamepad = true;
                if (gamepad.rightShoulder.wasPressedThisFrame)
                    return true;
            }

            if (hasInputSystemGamepad)
                return false;
#endif

            RefreshDeviceIfNeeded();
            return Input.GetKeyDown(KeyCode.JoystickButton5);
        }

        public static bool WasWestFaceButtonPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            bool hasInputSystemGamepad = false;
            foreach (Gamepad gamepad in Gamepad.all)
            {
                if (gamepad == null || !gamepad.added)
                    continue;

                hasInputSystemGamepad = true;
                if (gamepad.buttonWest.wasPressedThisFrame)
                    return true;
            }

            // Do not also read the legacy button when the Input System owns a
            // connected device. Reading both paths can turn one physical press
            // into two mode changes on some Windows controller drivers.
            if (hasInputSystemGamepad)
                return false;
#endif

            RefreshDeviceIfNeeded();
            return ActiveFamily == LegacyGamepadFamily.Sony
                ? Input.GetKeyDown(KeyCode.JoystickButton0)
                : Input.GetKeyDown(KeyCode.JoystickButton2);
        }

        public static bool WasEastFaceButtonPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            bool hasInputSystemGamepad = false;
            foreach (Gamepad gamepad in Gamepad.all)
            {
                if (gamepad == null || !gamepad.added)
                    continue;

                hasInputSystemGamepad = true;
                if (gamepad.buttonEast.wasPressedThisFrame)
                    return true;
            }

            if (hasInputSystemGamepad)
                return false;
#endif

            RefreshDeviceIfNeeded();
            return ActiveFamily == LegacyGamepadFamily.Sony
                ? Input.GetKeyDown(KeyCode.JoystickButton2)
                : Input.GetKeyDown(KeyCode.JoystickButton1);
        }

        public static bool WasNorthFaceButtonPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            bool hasInputSystemGamepad = false;
            foreach (Gamepad gamepad in Gamepad.all)
            {
                if (gamepad == null || !gamepad.added)
                    continue;

                hasInputSystemGamepad = true;
                if (gamepad.buttonNorth.wasPressedThisFrame)
                    return true;
            }

            if (hasInputSystemGamepad)
                return false;
#endif

            RefreshDeviceIfNeeded();
            return Input.GetKeyDown(KeyCode.JoystickButton3);
        }

        public static bool WasDpadRightPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            bool hasInputSystemGamepad = false;
            foreach (Gamepad gamepad in Gamepad.all)
            {
                if (gamepad == null || !gamepad.added)
                    continue;

                hasInputSystemGamepad = true;
                if (gamepad.dpad.right.wasPressedThisFrame)
                    return true;
            }

            // A connected Input System device owns the D-pad. Avoid also
            // sampling a legacy axis because some Windows drivers expose both
            // paths and would turn one physical press into two toggles.
            if (hasInputSystemGamepad)
                return false;
#endif

            // D-pads do not have a consistent legacy joystick-button index on
            // Windows. The project runs with Both input backends enabled, so
            // supported controllers use the device-independent path above.
            return false;
        }

        public static float ReadTriggerThrottle()
        {
#if ENABLE_INPUT_SYSTEM
            if (TryGetInputSystemGamepad(out Gamepad gamepad))
            {
                return Mathf.Clamp(
                    gamepad.rightTrigger.ReadValue()
                    - gamepad.leftTrigger.ReadValue(),
                    -1f,
                    1f);
            }
#endif

            RefreshDeviceIfNeeded();
            if (!HasConnectedGamepad)
                return 0f;

            if (ActiveFamily != LegacyGamepadFamily.Sony)
                return ReadAxisSafely(XboxTriggerThrottleAxis);

            float rawLeft = ReadAxisSafely(SonyLeftTriggerAxis);
            float rawRight = ReadAxisSafely(SonyRightTriggerAxis);
            if (rawLeft < -0.25f)
                sonyLeftTriggerIsBipolar = true;
            if (rawRight < -0.25f)
                sonyRightTriggerIsBipolar = true;

            float left = NormalizeSeparateTrigger(
                rawLeft,
                sonyLeftTriggerIsBipolar);
            float right = NormalizeSeparateTrigger(
                rawRight,
                sonyRightTriggerIsBipolar);
            return Mathf.Clamp(right - left, -1f, 1f);
        }

        public static void ForceDeviceRefresh()
        {
            nextDeviceRefreshTime = 0f;
            RefreshDeviceIfNeeded(true);
        }

        private static void RefreshDeviceIfNeeded(bool force = false)
        {
            if (!force && Time.unscaledTime < nextDeviceRefreshTime)
                return;

            nextDeviceRefreshTime = Time.unscaledTime + 0.75f;
            string[] names = Input.GetJoystickNames();
            string detectedName = string.Empty;
            LegacyGamepadFamily detectedFamily = LegacyGamepadFamily.None;
            if (names != null)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i];
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    LegacyGamepadFamily family = DetectFamily(name);
                    if (detectedFamily == LegacyGamepadFamily.None
                        || family != LegacyGamepadFamily.Generic)
                    {
                        detectedName = name.Trim();
                        detectedFamily = family;
                    }

                    if (family == LegacyGamepadFamily.Sony
                        || family == LegacyGamepadFamily.Xbox)
                    {
                        break;
                    }
                }
            }

#if ENABLE_INPUT_SYSTEM
            // In Unity 6, a device can be available through the new Input
            // System but absent from Input.GetJoystickNames(). Preserve the
            // legacy name when it exists, otherwise use the Input System's
            // device name solely for detection and diagnostics.
            if (detectedFamily == LegacyGamepadFamily.None
                && TryGetInputSystemGamepad(out Gamepad gamepad))
            {
                detectedName = string.IsNullOrWhiteSpace(gamepad.displayName)
                    ? gamepad.name
                    : gamepad.displayName;
                detectedFamily = DetectFamily(detectedName);
            }
#endif

            if (detectedFamily == ActiveFamily
                && string.Equals(
                    detectedName,
                    activeDeviceName,
                    StringComparison.Ordinal))
            {
                return;
            }

            ActiveFamily = detectedFamily;
            activeDeviceName = detectedName;
            sonyLeftTriggerIsBipolar = false;
            sonyRightTriggerIsBipolar = false;
            if (ActiveFamily == LegacyGamepadFamily.None)
            {
                Debug.Log("Adaptive gamepad input: no controller detected.");
            }
            else
            {
                Debug.Log(
                    $"Adaptive gamepad input: '{activeDeviceName}' uses "
                    + $"{ActiveFamily} legacy axis layout.");
            }
        }

        private static LegacyGamepadFamily DetectFamily(string deviceName)
        {
            string name = deviceName.ToLowerInvariant();
            if (name.Contains("dualsense")
                || name.Contains("dualshock")
                || name.Contains("wireless controller")
                || name.Contains("playstation")
                || name.Contains("sony")
                || name.Contains("ps4")
                || name.Contains("ps5"))
            {
                return LegacyGamepadFamily.Sony;
            }

            if (name.Contains("xbox")
                || name.Contains("xinput")
                || name.Contains("x-box"))
            {
                return LegacyGamepadFamily.Xbox;
            }

            // Preserve the project's previous Xbox-style mapping for unknown
            // legacy devices instead of disabling controller input entirely.
            return LegacyGamepadFamily.Generic;
        }

        private static float ReadAxisSafely(string axisName)
        {
            if (MissingAxes.Contains(axisName))
                return 0f;

            try
            {
                return Input.GetAxisRaw(axisName);
            }
            catch (ArgumentException)
            {
                MissingAxes.Add(axisName);
                Debug.LogWarning(
                    $"Adaptive gamepad input axis '{axisName}' is missing. "
                    + "Exit Play Mode and run Animal Game/Repair Gamepad Input Axes.");
                return 0f;
            }
            catch (InvalidOperationException)
            {
                // This occurs when the project is switched to the new Input
                // System only. Do not cache the axis as missing: it is still
                // valid if the project is switched back to Both.
                return 0f;
            }
        }

#if ENABLE_INPUT_SYSTEM
        private static bool TryGetInputSystemGamepad(out Gamepad gamepad)
        {
            foreach (Gamepad candidate in Gamepad.all)
            {
                if (candidate != null && candidate.added)
                {
                    gamepad = candidate;
                    return true;
                }
            }

            gamepad = null;
            return false;
        }
#endif

        private static float NormalizeSeparateTrigger(
            float rawValue,
            bool bipolar)
        {
            return bipolar
                ? Mathf.Clamp01((rawValue + 1f) * 0.5f)
                : Mathf.Clamp01(rawValue);
        }
    }
}
