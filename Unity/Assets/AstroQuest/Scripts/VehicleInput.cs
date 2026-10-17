using UnityEngine;
using UnityEngine.InputSystem;

namespace AstroQuest
{
    [RequireComponent(typeof(ThrusterVehicle))]
    public sealed class VehicleInput : MonoBehaviour
    {
        public Transform resetPoint;
        public Transform viewer;
        public LayerMask environmentMask = 1 << 8;
        public string modelSourceUrl;
        public string modelLicenseUrl;
        public string DeviceName { get; private set; } = "Keyboard (Editor)";
        public string Notice { get; private set; } = "Y / Space to launch · A / H stop and level";

        ThrusterVehicle vehicle;
        float resetHeld;
        bool focused = true;

        void Awake() => vehicle = GetComponent<ThrusterVehicle>();

        void Update()
        {
            if (!focused) return;
            FlightCommand command = default;
            bool launch = false, toggleAssist = false, toggleHold = false, pause = false, reset = false;
            Gamepad pad = Gamepad.current;
            DeviceName = pad != null ? pad.displayName :
                Application.isEditor ? "Keyboard (Editor)" : "Connect a Bluetooth gamepad";
            if (pad != null)
            {
                Vector2 move = pad.leftStick.ReadValue();
                Vector2 turn = pad.rightStick.ReadValue();
                command.Translation = new Vector3(0, move.y,
                    pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue());
                command.Rotation = new Vector3(turn.y, Mathf.Clamp(move.x + turn.x, -1, 1),
                    (pad.leftShoulder.isPressed ? 1 : 0) - (pad.rightShoulder.isPressed ? 1 : 0));
                command.Throttle = pad.rightTrigger.ReadValue();
                command.GroundBrake = pad.leftTrigger.ReadValue();
                command.Steering = move.x;
                command.Handbrake = pad.buttonSouth.isPressed;
                toggleHold = pad.buttonSouth.wasPressedThisFrame;
                launch = pad.buttonNorth.wasPressedThisFrame;
                toggleAssist = pad.buttonEast.wasPressedThisFrame;
                pause = pad.startButton.wasPressedThisFrame;
                reset = pad.buttonWest.isPressed;
            }

            Keyboard keyboard = Keyboard.current;
            if (Application.isEditor && pad == null && keyboard != null)
            {
                command.Translation = new Vector3(Axis(keyboard.dKey.isPressed, keyboard.aKey.isPressed),
                    Axis(keyboard.eKey.isPressed, keyboard.qKey.isPressed),
                    Axis(keyboard.wKey.isPressed, keyboard.sKey.isPressed));
                command.Rotation = new Vector3(Axis(keyboard.upArrowKey.isPressed, keyboard.downArrowKey.isPressed),
                    Axis(keyboard.rightArrowKey.isPressed, keyboard.leftArrowKey.isPressed),
                    Axis(keyboard.zKey.isPressed, keyboard.cKey.isPressed));
                command.Brake = keyboard.leftShiftKey.isPressed;
                command.Throttle = keyboard.wKey.isPressed ? 1 : 0;
                command.GroundBrake = keyboard.sKey.isPressed ? 1 : 0;
                command.Steering = Axis(keyboard.dKey.isPressed, keyboard.aKey.isPressed);
                command.Handbrake = keyboard.leftShiftKey.isPressed;
                toggleHold = keyboard.hKey.wasPressedThisFrame;
                launch = keyboard.spaceKey.wasPressedThisFrame;
                toggleAssist = keyboard.bKey.wasPressedThisFrame;
                pause = keyboard.escapeKey.wasPressedThisFrame;
                reset = keyboard.rKey.isPressed;
            }

            // A lost controller clears thrust and pauses, retaining the current flight state.
            if (pad == null && !Application.isEditor)
            {
                vehicle.Command = default;
                vehicle.SetPaused(true);
                Notice = "Pair your gamepad, then press Menu to resume";
                return;
            }
            if (pause) vehicle.SetPaused(!vehicle.Paused);
            if (vehicle.Paused && pad != null)
            {
                if (pad.dpad.left.wasPressedThisFrame && !string.IsNullOrEmpty(modelSourceUrl))
                    Application.OpenURL(modelSourceUrl);
                if (pad.dpad.right.wasPressedThisFrame && !string.IsNullOrEmpty(modelLicenseUrl))
                    Application.OpenURL(modelLicenseUrl);
            }
            if (toggleAssist) vehicle.attitudeAssist = !vehicle.attitudeAssist;
            if (launch) vehicle.Launch();
            if (toggleHold) vehicle.ToggleStopAndLevel();

            resetHeld = reset ? resetHeld + Time.unscaledDeltaTime : 0;
            if (resetHeld >= 0.8f)
            {
                TryReset();
                resetHeld = -100;
            }
            vehicle.Command = vehicle.Paused ? default : command;
        }

        public bool TryReset()
        {
            Vector3 start = resetPoint != null ? resetPoint.position : transform.position;
            Quaternion orientation = resetPoint != null ? resetPoint.rotation : Quaternion.identity;
            // Three small offsets allow recovery if furniture moved into the saved spawn.
            for (int i = 0; i < 3; i++)
            {
                Vector3 candidate = start + Vector3.up * (0.03f + i * 0.12f);
                if (Physics.CheckBox(candidate, new Vector3(0.072f, 0.035f, 0.135f),
                    orientation, environmentMask, QueryTriggerInteraction.Ignore)) continue;
                vehicle.ResetAt(candidate, orientation);
                Notice = "Reset · Y / Space to launch";
                return true;
            }
            Notice = "Reset blocked: clear the launch area or reload the room";
            return false;
        }

        void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            if (!hasFocus && vehicle != null) vehicle.SetPaused(true);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && vehicle != null) vehicle.SetPaused(true);
        }

        void OnDisable()
        {
            if (vehicle != null) vehicle.Command = default;
        }

        static float Axis(bool positive, bool negative) => (positive ? 1 : 0) - (negative ? 1 : 0);
    }
}
