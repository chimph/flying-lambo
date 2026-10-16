using UnityEngine;

namespace AstroQuest
{
    public sealed class LandingPad : MonoBehaviour
    {
        public ThrusterVehicle vehicle;
        public Renderer statusRing;
        public float radius = 0.19f;
        public bool Occupied { get; private set; }
        public int Landings { get; private set; }
        public string label = "01";
        bool wasOccupied;
        int lastVehicleLandings;
        MaterialPropertyBlock properties;

        void Awake() => properties = new MaterialPropertyBlock();

        void Update()
        {
            if (vehicle == null) return;
            Vector3 local = transform.InverseTransformPoint(vehicle.transform.position);
            Occupied = vehicle.State == ThrusterVehicle.FlightState.Grounded &&
                new Vector2(local.x, local.z).magnitude < radius - 0.075f &&
                local.y > 0 && local.y < 0.12f;
            if (Occupied && !wasOccupied && vehicle.SuccessfulLandings > lastVehicleLandings)
                Landings++;
            if (Occupied) lastVehicleLandings = vehicle.SuccessfulLandings;
            wasOccupied = Occupied;
            if (statusRing != null)
            {
                Color color = Occupied ? new Color(0.2f, 1f, 0.62f) : new Color(0.2f, 0.72f, 0.88f);
                properties.SetColor("_Color", color);
                properties.SetColor("_EmissionColor", color * 0.6f);
                statusRing.SetPropertyBlock(properties);
            }
        }
    }
}
