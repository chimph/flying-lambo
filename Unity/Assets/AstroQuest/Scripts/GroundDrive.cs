using System;
using UnityEngine;

namespace AstroQuest
{
    // Tyre forces act at four contact patches. The compound colliders carry the
    // weight; no transform steering or artificial velocity redirection is used.
    public sealed class GroundDrive : MonoBehaviour
    {
        [Serializable] public class Wheel
        {
            public Transform visual;
            public Vector3 center;
            public float radius = 0.018f;
            public bool front;
            [NonSerialized] public Transform spin;
            [NonSerialized] public Quaternion restRotation;
        }

        public Wheel[] wheels = Array.Empty<Wheel>();
        public float acceleration = 0.85f;
        public float braking = 1.8f;
        public float maximumSpeed = 1.2f;
        public float maximumReverseSpeed = 0.45f;
        public float steeringAngle = 30;
        public float grip = 0.95f;
        public float CurrentSteerAngle { get; private set; }
        public int SupportedWheels { get; private set; }
        readonly RaycastHit[] hits = new RaycastHit[20];
        RaycastHit[] contacts;
        bool[] touching;
        ThrusterVehicle vehicle;

        void Awake()
        {
            vehicle = GetComponent<ThrusterVehicle>();
            contacts = new RaycastHit[wheels.Length];
            touching = new bool[wheels.Length];
            foreach (var wheel in wheels)
                if (wheel.visual != null) wheel.restRotation = wheel.visual.localRotation;
        }

        public bool SampleSupport()
        {
            SupportedWheels = 0;
            if (Vector3.Dot(transform.up, Vector3.up) < 0.8f) return false;
            for (int i = 0; i < wheels.Length; i++)
            {
                Wheel wheel = wheels[i];
                Vector3 origin = transform.TransformPoint(wheel.center) + transform.up * 0.014f;
                int count = Physics.RaycastNonAlloc(origin, -transform.up, hits,
                    wheel.radius + 0.038f, ~0, QueryTriggerInteraction.Ignore);
                float closest = float.PositiveInfinity;
                touching[i] = false;
                for (int h = 0; h < count; h++)
                {
                    var hit = hits[h];
                    if (hit.collider.attachedRigidbody == vehicle.Body || hit.normal.y < 0.88f || hit.distance >= closest) continue;
                    contacts[i] = hit;
                    touching[i] = true;
                    closest = hit.distance;
                }
                if (touching[i]) SupportedWheels++;
            }
            return SupportedWheels >= 2;
        }

        public void Simulate(FlightCommand command, float dt)
        {
            float speed = Vector3.Dot(vehicle.Body.linearVelocity, transform.forward);
            float desiredSteer = Mathf.Clamp(command.Steering, -1, 1) * steeringAngle /
                (1 + Mathf.Abs(speed) * 0.65f);
            CurrentSteerAngle = Mathf.MoveTowards(CurrentSteerAngle, desiredSteer, 150 * dt);
            if (SupportedWheels == 0) return;
            float drive = Mathf.Clamp01(command.Throttle) * acceleration;
            // Left trigger first brakes forward motion, then becomes reverse.
            if (speed > 0.04f) drive -= Mathf.Clamp01(command.GroundBrake) * braking;
            else drive -= Mathf.Clamp01(command.GroundBrake) * acceleration * 0.65f;
            if ((speed >= maximumSpeed && drive > 0) || (speed <= -maximumReverseSpeed && drive < 0)) drive = 0;
            if (command.Handbrake) drive = -Mathf.Clamp(speed / dt, -braking * 1.5f, braking * 1.5f);
            float wheelMass = vehicle.Body.mass / wheels.Length;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (!touching[i]) continue;
                var hit = contacts[i];
                Vector3 direction = Quaternion.AngleAxis(wheels[i].front ? CurrentSteerAngle : 0, transform.up) * transform.forward;
                direction = Vector3.ProjectOnPlane(direction, hit.normal).normalized;
                Vector3 side = Vector3.Cross(hit.normal, direction).normalized;
                Vector3 velocity = vehicle.Body.GetPointVelocity(hit.point);
                float lateral = -Vector3.Dot(velocity, side) / Mathf.Max(dt, 0.001f);
                float rolling = -Mathf.Clamp(Vector3.Dot(velocity, direction) / dt, -0.06f, 0.06f);
                Vector3 force = wheelMass * (direction * (drive + rolling) + side * lateral);
                float traction = wheelMass * Physics.gravity.magnitude * grip;
                vehicle.Body.AddForceAtPosition(Vector3.ClampMagnitude(force, traction), hit.point);
            }
        }

        void LateUpdate()
        {
            if (vehicle == null || vehicle.Body == null || vehicle.Paused) return;
            foreach (var wheel in wheels)
            {
                if (wheel.visual == null) continue;
                wheel.visual.localRotation = Quaternion.AngleAxis(wheel.front ? CurrentSteerAngle : 0, Vector3.up) * wheel.restRotation;
                if (wheel.spin == null && wheel.visual.childCount > 0) wheel.spin = wheel.visual.GetChild(0);
                if (wheel.spin != null)
                {
                    float speed = Vector3.Dot(vehicle.Body.GetPointVelocity(wheel.visual.position), wheel.visual.forward);
                    wheel.spin.Rotate(Vector3.right, speed / wheel.radius * Mathf.Rad2Deg * Time.deltaTime, Space.Self);
                }
            }
        }
    }
}
