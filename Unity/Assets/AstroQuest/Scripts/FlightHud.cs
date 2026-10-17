using UnityEngine;
using UnityEngine.InputSystem;

namespace AstroQuest
{
    public sealed class FlightHud : MonoBehaviour
    {
        public ThrusterVehicle vehicle;
        public VehicleInput input;
        public TextMesh readout;
        public Transform viewer;
        public string roomStatus = "EDITOR TEST ROOM";
        public string vehicleCredit;
        const string HelpPreference = "AstroQuest.HelpVisible";
        public bool HelpVisible { get; private set; } = true;

        void Awake()
        {
            HelpVisible = PlayerPrefs.GetInt(HelpPreference, 1) != 0;
            ApplyTextVisibility();
        }

        public void ToggleHelp()
        {
            HelpVisible = !HelpVisible;
            ApplyTextVisibility();
            PlayerPrefs.SetInt(HelpPreference, HelpVisible ? 1 : 0);
            PlayerPrefs.Save();
        }

        void ApplyTextVisibility()
        {
            foreach (var text in FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var renderer = text.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = HelpVisible;
            }
            if (!HelpVisible && readout != null) readout.text = "";
        }

        void LateUpdate()
        {
            Gamepad pad = Gamepad.current;
            if ((pad != null && pad.selectButton.wasPressedThisFrame) ||
                (Application.isEditor && Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame))
                ToggleHelp();
            if (vehicle == null || readout == null) return;
            if (!HelpVisible)
            {
                readout.text = "";
                return;
            }
            if (viewer != null)
            {
                transform.position = viewer.position + viewer.forward * 0.85f + viewer.up * -0.16f;
                transform.rotation = Quaternion.LookRotation(transform.position - viewer.position, viewer.up);
            }
            if (vehicle.Body == null || !vehicle.gameObject.activeSelf)
            {
                readout.text = "F L Y I N G   L A M B O\n\n" + roomStatus;
                return;
            }
            string state = vehicle.Paused ? "PAUSED · MENU / ESC TO RESUME" :
                vehicle.State == ThrusterVehicle.FlightState.FreeFlight ?
                    (vehicle.StopAndLevelActive ? "STOP + LEVEL · THRUSTER POSITION HOLD" : "ZERO G · FREE FLIGHT") :
                vehicle.State == ThrusterVehicle.FlightState.Settling ? "CONTACT · SETTLING" : "DRIVING";
            bool airborne = vehicle.State == ThrusterVehicle.FlightState.FreeFlight;
            readout.text = "F L Y I N G   L A M B O\n" + roomStatus + "\n\n" + state +
                $"\n{vehicle.Body.linearVelocity.magnitude:0.00} m/s    LANDINGS {vehicle.SuccessfulLandings}" +
                $"\nATTITUDE ASSIST {(vehicle.attitudeAssist ? "ON" : "OFF")} · B TO TOGGLE" +
                (airborne ? "\nRT/LT FORWARD/REVERSE   LS STEER + UP/DOWN\nRS PITCH/YAW · PULL BACK NOSE UP   LB/RB ROLL\nA / H STOP + LEVEL · MOVE TO CANCEL" :
                    "\nRT / W ACCELERATE   LT / S BRAKE + REVERSE\nLS / A-D STEER   A / SHIFT HANDBRAKE") +
                "\nHOLD X / R RESET   MENU / ESC PAUSE" +
                "\nY / SPACE LAUNCH   VIEW / F1 HIDE HELP" +
                (input == null ? "" : "\n" + input.DeviceName + " · " + input.Notice) +
                (vehicle.Paused && !string.IsNullOrEmpty(vehicleCredit) ?
                    "\n\n" + vehicleCredit + "\nDPAD LEFT: MODEL SOURCE   RIGHT: LICENSE" : "");
        }
    }
}
