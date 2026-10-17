using System;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace AstroQuest
{
    public sealed class RoomSession : MonoBehaviour
    {
        public MRUK roomKit;
        public ThrusterVehicle vehicle;
        public VehicleInput input;
        public FlightHud hud;
        public Transform viewer;
        public Transform targetPad;
        public Transform spawnPoint;
        public Material debugMaterial;

        EffectMesh collisions;
        bool loading;

        async void Start() => await ReloadRoom();

        void Update()
        {
            Gamepad pad = Gamepad.current;
            if (pad == null || loading) return;
            if (pad.dpad.up.wasPressedThisFrame) _ = ReloadRoom();
            if (pad.dpad.down.wasPressedThisFrame && targetPad != null)
            {
                if (SurfacePlacement.TryFindPad(new Ray(viewer.position, viewer.forward), out Pose pose))
                {
                    targetPad.SetPositionAndRotation(pose.position, pose.rotation);
                    targetPad.gameObject.SetActive(true);
                    hud.roomStatus = "TARGET PAD PLACED · SCANNED ROOM";
                }
                else hud.roomStatus = "LOOK AT A CLEAR FLAT SURFACE · DPAD DOWN TO PLACE";
            }
        }

        public async Task ReloadRoom()
        {
            if (loading || roomKit == null) return;
            loading = true;
            vehicle.gameObject.SetActive(false);
            targetPad.gameObject.SetActive(false);
            hud.roomStatus = "LOADING YOUR ROOM…";
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!await RequestScenePermission())
                {
                    hud.roomStatus = "ALLOW SPATIAL DATA IN APP PERMISSIONS · DPAD UP TO RETRY";
                    return;
                }
#endif
                var result = await roomKit.LoadSceneFromDevice();
                if (this == null) return;
                MRUKRoom room = roomKit.GetCurrentRoom();
                if (result != MRUK.LoadDeviceResult.Success || room == null)
                {
                    hud.roomStatus = "ROOM UNAVAILABLE: " + result + " · DPAD UP TO RETRY";
                    return;
                }

                if (collisions == null)
                {
                    var geometry = new GameObject("InvisibleRoomCollisions");
                    geometry.transform.SetParent(transform, false);
                    collisions = geometry.AddComponent<EffectMesh>();
                    collisions.SpawnOnStart = MRUK.RoomFilter.None;
                    collisions.Layer = SurfacePlacement.RoomLayer;
                    collisions.Colliders = true;
                    collisions.MeshMaterial = debugMaterial;
                    collisions.HideMesh = true;
                    collisions.CastShadow = false;
                }
                // Clear old effects before rebuilding, avoiding stacked collision shells.
                collisions.DestroyMesh();
                await Task.Yield();
                if (this == null) return;
                bool hasGlobalMesh = room.GlobalMeshAnchor != null &&
                    room.GlobalMeshAnchor.GlobalMesh != null && room.GlobalMeshAnchor.GlobalMesh.vertexCount > 0;
                collisions.Labels = hasGlobalMesh ? MRUKAnchor.SceneLabels.GLOBAL_MESH :
                    (MRUKAnchor.SceneLabels)(~(int)MRUKAnchor.SceneLabels.GLOBAL_MESH);
                collisions.CreateMesh(room);
                Physics.SyncTransforms();

                // Prefer a clear surface in front of the player; then search nearby floor patches.
                Vector3 forward = Vector3.ProjectOnPlane(viewer.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
                Pose launch = default;
                bool found = false;
                for (int ring = 0; ring < 4 && !found; ring++)
                for (int direction = 0; direction < 8 && !found; direction++)
                {
                    Vector3 offset = Quaternion.Euler(0, direction * 45, 0) * forward * (0.6f + ring * 0.3f);
                    found = SurfacePlacement.TryFindPad(new Ray(viewer.position + offset, Vector3.down), out launch);
                }
                if (!found)
                {
                    hud.roomStatus = "NO CLEAR LAUNCH SURFACE · MOVE NEAR A CLEAR AREA · DPAD UP";
                    return;
                }
                spawnPoint.SetPositionAndRotation(launch.position + Vector3.up * 0.05f, launch.rotation);
                vehicle.gameObject.SetActive(true);
                vehicle.ResetAt(spawnPoint.position, spawnPoint.rotation);
                hud.roomStatus = "SCANNED ROOM · DPAD DOWN PLACE TARGET · UP RELOAD";
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (hud != null) hud.roomStatus = "ROOM LOAD FAILED · DPAD UP TO RETRY";
            }
            finally { loading = false; }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static Task<bool> RequestScenePermission()
        {
            string permission = OVRPermissionsRequester.ScenePermission;
            if (Permission.HasUserAuthorizedPermission(permission)) return Task.FromResult(true);
            var completion = new TaskCompletionSource<bool>();
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => completion.TrySetResult(true);
            callbacks.PermissionDenied += _ => completion.TrySetResult(false);
            callbacks.PermissionDeniedAndDontAskAgain += _ => completion.TrySetResult(false);
            Permission.RequestUserPermission(permission, callbacks);
            return completion.Task;
        }
#endif
    }
}
