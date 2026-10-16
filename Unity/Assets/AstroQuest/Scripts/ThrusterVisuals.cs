using UnityEngine;

namespace AstroQuest
{
    public sealed class ThrusterVisuals : MonoBehaviour
    {
        public ThrusterVehicle vehicle;
        [Tooltip("The local acceleration direction this exhaust produces.")]
        public Vector3 forceDirection;
        public Vector3 torqueDirection;
        public Transform plume;
        [Min(0.01f)] public float maximumForce = 0.48f;
        public float AppliedForce { get; set; }

        void LateUpdate()
        {
            if (vehicle == null || plume == null) return;
            float strength = Mathf.Clamp01(AppliedForce / maximumForce);
            plume.gameObject.SetActive(strength > 0.015f && !vehicle.Paused);
            plume.localScale = new Vector3(0.008f, Mathf.Clamp(strength * 0.045f, 0.005f, 0.06f), 0.008f);
        }
    }
}
