using UnityEngine;

namespace AstroQuest
{
    public struct FlightCommand
    {
        public Vector3 Translation;
        public Vector3 Rotation;
        public bool Brake;
        public float Throttle;
        public float GroundBrake;
        public float Steering;
        public bool Handbrake;
    }
}
