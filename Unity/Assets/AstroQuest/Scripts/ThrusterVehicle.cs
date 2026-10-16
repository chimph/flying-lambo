using System.Collections.Generic;
using UnityEngine;

namespace AstroQuest
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ThrusterVehicle : MonoBehaviour
    {
        public enum FlightState { Grounded, FreeFlight, Settling }

        [Header("Thrust (metres and seconds)")]
        [Min(0.01f)] public float translationAcceleration = 0.65f;
        [Min(0.01f)] public float rotationAcceleration = 2.2f;
        [Min(0.01f)] public float brakeAcceleration = 1.2f;
        [Min(0.01f)] public float launchSpeed = 0.5f;
        public bool attitudeAssist = true;
        public bool StopAndLevelActive { get; private set; }
        public Vector3 HoldPosition { get; private set; }
        Quaternion holdRotation;
        GroundDrive groundDrive;
        ThrusterVisuals[] jets;
        Vector3[] jetForces, jetTorques;
        float[] jetCommands;

        [Header("Landing")]
        [Min(0.01f)] public float maximumLandingSpeed = 0.4f;
        [Min(0.01f)] public float settlingTime = 0.25f;
        [Range(0, 1)] public float minimumSurfaceUp = 0.88f;
        [Range(0, 1)] public float minimumVehicleUp = 0.9f;

        public FlightState State { get; private set; } = FlightState.Grounded;
        public Rigidbody Body { get; private set; }
        public FlightCommand Command { get; set; }
        public Vector3 AppliedTranslation { get; private set; }
        public Vector3 AppliedRotation { get; private set; }
        public int SuccessfulLandings { get; private set; }
        public bool Paused { get; private set; }
        public float LastLandingSpeed { get; private set; }

        float flightAge;
        float contactAge;
        float settleAge;
        Vector3 storedVelocity, storedAngularVelocity;
        readonly HashSet<Collider> supports = new();

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.mass = 0.4f;
            Body.linearDamping = 0;
            Body.angularDamping = 0;
            Body.maxAngularVelocity = 3;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.useGravity = true;
            Body.centerOfMass = new Vector3(0, -0.012f, 0);
            Body.solverIterations = 12;
            Body.solverVelocityIterations = 6;
            groundDrive = GetComponent<GroundDrive>();
        }

        void FixedUpdate() => SimulateControls(Time.fixedDeltaTime);

        public void SimulateControls(float dt)
        {
            AppliedTranslation = AppliedRotation = Vector3.zero;
            if (jets == null) InitializeJets();
            foreach (var jet in jets) jet.AppliedForce = 0;
            if (Paused || dt <= 0) return;
            flightAge += dt;
            // Sleeping bodies stop sending OnCollisionStay. Retain support until exit,
            // rather than mistaking a resting car for an airborne one.
            supports.RemoveWhere(collider => collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy);
            bool wheelSupport = groundDrive != null && groundDrive.SampleSupport();
            contactAge = supports.Count > 0 || (State != FlightState.FreeFlight && wheelSupport) ? 0 : contactAge + dt;

            // Earth gravity applies after contact. Once airborne, drift is inertial.
            if (State != FlightState.FreeFlight)
            {
                StopAndLevelActive = false;
                if (contactAge > 0.18f)
                {
                    State = FlightState.FreeFlight;
                    Body.useGravity = false;
                    settleAge = 0;
                }
                else if (State == FlightState.Settling)
                {
                    settleAge += dt;
                    if (settleAge >= settlingTime)
                    {
                        State = FlightState.Grounded;
                        SuccessfulLandings++;
                    }
                }
                if (State != FlightState.FreeFlight && groundDrive != null) groundDrive.Simulate(Command, dt);
                return;
            }

            Vector3 translation = Vector3.ClampMagnitude(Command.Translation, 1);
            Vector3 rotation = Vector3.ClampMagnitude(Command.Rotation, 1);
            AppliedTranslation = translation * translationAcceleration;
            AppliedRotation = rotation * rotationAcceleration;

            if (StopAndLevelActive && (translation.sqrMagnitude > 0.0064f || rotation.sqrMagnitude > 0.0064f))
                StopAndLevelActive = false;

            if (StopAndLevelActive)
            {
                Vector3 error = HoldPosition - Body.position;
                float desiredSpeed = Mathf.Min(error.magnitude * 1.8f,
                    Mathf.Sqrt(2 * brakeAcceleration * error.magnitude));
                Vector3 desiredVelocity = error.sqrMagnitude > 0.000001f ? error.normalized * desiredSpeed : Vector3.zero;
                AppliedTranslation = transform.InverseTransformDirection(
                    Vector3.ClampMagnitude((desiredVelocity - Body.linearVelocity) * 3.5f, brakeAcceleration));
                Quaternion difference = holdRotation * Quaternion.Inverse(Body.rotation);
                difference.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180) angle -= 360;
                Vector3 orientationError = float.IsNaN(axis.x) || axis.sqrMagnitude < 0.001f ?
                    Vector3.zero : axis.normalized * (angle * Mathf.Deg2Rad);
                AppliedRotation = transform.InverseTransformDirection(Vector3.ClampMagnitude(
                    orientationError * 9 - Body.angularVelocity * 5, rotationAcceleration * 2));
            }

            else if (Command.Brake)
            {
                // Finite, world-relative counter-thrust; never teleport velocity to zero.
                Vector3 deceleration = Vector3.ClampMagnitude(-Body.linearVelocity / dt, brakeAcceleration);
                AppliedTranslation += transform.InverseTransformDirection(deceleration);
                AppliedRotation += transform.InverseTransformDirection(
                    Vector3.ClampMagnitude(-Body.angularVelocity / dt, rotationAcceleration * 2));
            }
            else if (attitudeAssist && rotation.sqrMagnitude < 0.001f)
            {
                AppliedRotation += transform.InverseTransformDirection(
                    Vector3.ClampMagnitude(-Body.angularVelocity * 3, rotationAcceleration));
            }

            AppliedTranslation = Vector3.ClampMagnitude(AppliedTranslation, Mathf.Max(translationAcceleration, brakeAcceleration));
            AppliedRotation = Vector3.ClampMagnitude(AppliedRotation, rotationAcceleration * 2);
            ApplyThrusters(AppliedTranslation, AppliedRotation);
        }

        public void ToggleStopAndLevel() => SetStopAndLevel(!StopAndLevelActive);

        public void SetStopAndLevel(bool enabled)
        {
            if (Paused || State != FlightState.FreeFlight) return;
            StopAndLevelActive = enabled;
            if (!enabled) return;
            HoldPosition = Body.position;
            Vector3 heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (heading.sqrMagnitude < 0.01f) heading = Vector3.Cross(transform.right, Vector3.up);
            holdRotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
            Body.WakeUp();
        }

        void InitializeJets()
        {
            jets = GetComponentsInChildren<ThrusterVisuals>();
            jetForces = new Vector3[jets.Length];
            jetTorques = new Vector3[jets.Length];
            jetCommands = new float[jets.Length];
            for (int i = 0; i < jets.Length; i++)
            {
                jetForces[i] = jets[i].forceDirection.normalized;
                Vector3 arm = transform.InverseTransformPoint(jets[i].transform.position) - Body.centerOfMass;
                jetTorques[i] = Vector3.Cross(arm, jetForces[i]);
            }
        }

        void ApplyThrusters(Vector3 acceleration, Vector3 angularAcceleration)
        {
            if (jets.Length == 0)
            {
                // Bare physics test bodies have no art/ports.
                Body.AddRelativeForce(acceleration, ForceMode.Acceleration);
                Body.AddRelativeTorque(angularAcceleration, ForceMode.Acceleration);
                return;
            }
            Quaternion inertiaRotation = Body.inertiaTensorRotation;
            Vector3 torque = inertiaRotation * Vector3.Scale(Body.inertiaTensor,
                Quaternion.Inverse(inertiaRotation) * angularAcceleration);
            float torqueWeight = 18;
            Vector3 remainingForce = acceleration * Body.mass;
            Vector3 remainingTorque = torque * torqueWeight;
            System.Array.Clear(jetCommands, 0, jetCommands.Length);
            // Bounded nonnegative allocation: a nozzle only pushes in its actual
            // direction. Its lever arm produces the rotation through PhysX.
            for (int pass = 0; pass < 32; pass++)
            for (int i = 0; i < jets.Length; i++)
            {
                Vector3 force = jetForces[i], moment = jetTorques[i] * torqueWeight;
                float step = (Vector3.Dot(force, remainingForce) + Vector3.Dot(moment, remainingTorque)) /
                    Mathf.Max(force.sqrMagnitude + moment.sqrMagnitude, 0.0001f);
                float next = Mathf.Clamp(jetCommands[i] + step, 0, jets[i].maximumForce);
                float change = next - jetCommands[i];
                jetCommands[i] = next;
                remainingForce -= force * change;
                remainingTorque -= moment * change;
            }
            for (int i = 0; i < jets.Length; i++)
            {
                jets[i].AppliedForce = jetCommands[i];
                Body.AddForceAtPosition(transform.TransformDirection(jetForces[i]) * jetCommands[i], jets[i].transform.position);
            }
        }

        public void Launch()
        {
            if (Paused || State == FlightState.FreeFlight) return;
            State = FlightState.FreeFlight;
            StopAndLevelActive = false;
            Body.useGravity = false;
            Body.WakeUp();
            Body.AddForce(Vector3.up * launchSpeed, ForceMode.VelocityChange);
            flightAge = settleAge = 0;
            contactAge = 10;
            supports.Clear();
        }

        public static bool IsLandingContact(Vector3 normal, Vector3 vehicleUp, float speed,
            float angularSpeed, bool underside, float maxSpeed = 0.4f,
            float minSurfaceUp = 0.88f, float minVehicleUp = 0.9f)
        {
            return underside && normal.y >= minSurfaceUp &&
                Vector3.Dot(vehicleUp, Vector3.up) >= minVehicleUp &&
                speed <= maxSpeed && angularSpeed < 0.65f;
        }

        void OnCollisionEnter(Collision collision) => HandleContact(collision);
        void OnCollisionStay(Collision collision) => HandleContact(collision);
        void OnCollisionExit(Collision collision) => supports.Remove(collision.collider);

        void HandleContact(Collision collision)
        {
            if (Paused || (State == FlightState.FreeFlight && flightAge < 0.6f)) return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                bool underside = transform.InverseTransformPoint(contact.point).y < -0.015f;
                float speed = collision.relativeVelocity.magnitude;
                bool retainingSupport = State != FlightState.FreeFlight && underside &&
                    contact.normal.y >= minimumSurfaceUp && transform.up.y >= minimumVehicleUp;
                if (!retainingSupport && !IsLandingContact(contact.normal, transform.up, speed,
                    Body.angularVelocity.magnitude, underside, maximumLandingSpeed,
                    minimumSurfaceUp, minimumVehicleUp)) continue;

                contactAge = 0;
                supports.Add(collision.collider);
                if (State != FlightState.FreeFlight) return;
                State = FlightState.Settling;
                LastLandingSpeed = speed;
                settleAge = 0;
                Body.useGravity = true;
                return;
            }
            supports.Remove(collision.collider);
        }

        public void SetPaused(bool paused)
        {
            if (Paused == paused) return;
            Paused = paused;
            Command = default;
            if (paused)
            {
                storedVelocity = Body.linearVelocity;
                storedAngularVelocity = Body.angularVelocity;
                Body.isKinematic = true;
            }
            else
            {
                Body.isKinematic = false;
                Body.linearVelocity = storedVelocity;
                Body.angularVelocity = storedAngularVelocity;
            }
        }

        public void ResetAt(Vector3 position, Quaternion rotation)
        {
            if (Paused) SetPaused(false);
            Body.position = position;
            Body.rotation = rotation;
            Body.linearVelocity = Body.angularVelocity = Vector3.zero;
            Command = default;
            State = FlightState.Grounded;
            StopAndLevelActive = false;
            Body.useGravity = true;
            contactAge = 0;
            flightAge = settleAge = 0;
            supports.Clear();
            Body.WakeUp();
        }
    }
}
