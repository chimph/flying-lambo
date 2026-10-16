using UnityEngine;

namespace AstroQuest
{
    public static class SurfacePlacement
    {
        public const int RoomLayer = 8;
        public const int RoomMask = 1 << RoomLayer;

        public static bool TryFindPad(Ray ray, out Pose pose)
        {
            pose = default;
            if (!Physics.Raycast(ray, out RaycastHit hit, 5, RoomMask, QueryTriggerInteraction.Ignore) ||
                hit.normal.y < 0.94f) return false;
            Vector3 center = hit.point;
            // Require a broad, nearly flat patch and free air above it.
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                Vector3 sample = center + new Vector3(x * 0.18f, 0.12f, z * 0.18f);
                if (!Physics.Raycast(sample, Vector3.down, out RaycastHit support, 0.16f,
                    RoomMask, QueryTriggerInteraction.Ignore) || support.normal.y < 0.94f ||
                    Mathf.Abs(support.point.y - center.y) > 0.018f) return false;
            }
            if (Physics.CheckBox(center + Vector3.up * 0.09f,
                new Vector3(0.18f, 0.06f, 0.18f), Quaternion.identity,
                RoomMask, QueryTriggerInteraction.Ignore)) return false;

            Vector3 forward = Vector3.ProjectOnPlane(ray.direction, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            pose = new Pose(center + Vector3.up * 0.006f,
                Quaternion.LookRotation(forward, Vector3.up));
            return true;
        }
    }
}
