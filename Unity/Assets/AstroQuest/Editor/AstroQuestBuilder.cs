using System;
using System.IO;
using System.Linq;
using AstroQuest;
using Meta.XR.MRUtilityKit;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

public static class AstroQuestBuilder
{
    const string Root = "Assets/AstroQuest";
    public const string TestScene = Root + "/Scenes/TestRoom.unity";
    public const string QuestScene = Root + "/Scenes/QuestMR.unity";
    const string DonorModel = Root + "/Art/DonorVehicle.fbx";

    [Serializable] public class DonorData
    {
        public string author, license, sourceUrl, licenseUrl;
        public DonorMaterial[] materials;
        public DonorMount[] mounts;
        public DonorWheel[] wheels;
    }
    [Serializable] public class DonorMaterial
    {
        public string sourceName, assetName, albedo, normal, emission;
        public float[] color;
        public float metallic, smoothness;
        public bool unlit, paintMask;
    }
    [Serializable] public class DonorMount
    {
        public string name;
        public float[] force, torque;
    }
    [Serializable] public class DonorWheel
    {
        public string name;
        public float[] position;
        public float radius;
        public bool front;
    }

    static DonorData ReadDonorData() => JsonUtility.FromJson<DonorData>(
        File.ReadAllText(Root + "/Art/DonorVehicle.json"));

    [MenuItem("Flying Lambo/Generate prototype scenes")]
    public static void Generate()
    {
        Directory.CreateDirectory(Root + "/Scenes");
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Prefabs");
        Directory.CreateDirectory("Assets/XR/Settings");
        AssetDatabase.Refresh();
        Configure();
        SetupVehicleMaterials();
        CreateScene(false);
        CreateScene(true);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(QuestScene, true) };
        EditorSceneManager.OpenScene(TestScene);
        AssetDatabase.SaveAssets();
        Debug.Log("FLYING LAMBO: generated TestRoom, QuestMR, vehicle prefab and Android/OpenXR settings.");
    }

    static void Configure()
    {
        PlayerSettings.companyName = "Flying Lambo";
        PlayerSettings.productName = "Flying Lambo";
        PlayerSettings.bundleVersion = "0.1.0";
        PlayerSettings.Android.bundleVersionCode = 2;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.astroquest.prototype");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        QualitySettings.antiAliasing = 4;
        QualitySettings.shadows = ShadowQuality.Disable;
        // Android uses a different default quality tier from the Editor. Setting
        // only the active Editor tier left the device at zero MSAA.
        var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        var tiers = quality.FindProperty("m_QualitySettings");
        for (int i = 0; i < tiers.arraySize; i++)
        {
            var tier = tiers.GetArrayElementAtIndex(i);
            tier.FindPropertyRelative("antiAliasing").intValue = 4;
            tier.FindPropertyRelative("shadows").intValue = (int)ShadowQuality.Disable;
            tier.FindPropertyRelative("globalTextureMipmapLimit").intValue = 0;
        }
        quality.ApplyModifiedPropertiesWithoutUndo();
        Time.fixedDeltaTime = 1f / 90f;

        var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        var activeInput = player.FindProperty("activeInputHandler");
        if (activeInput != null) activeInput.intValue = 1;
        player.ApplyModifiedPropertiesWithoutUndo();

        var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        tags.FindProperty("layers").GetArrayElementAtIndex(SurfacePlacement.RoomLayer).stringValue = "RoomGeometry";
        tags.ApplyModifiedPropertiesWithoutUndo();

        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
            out XRGeneralSettingsPerBuildTarget xr))
        {
            xr = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(xr, "Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, xr, true);
        }
        if (!xr.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            xr.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        var manager = xr.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        xr.SettingsForBuildTarget(BuildTargetGroup.Android).InitManagerOnStart = true;
        if (!XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android))
            throw new InvalidOperationException("Could not assign the Android OpenXR loader.");

        var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (openxr == null) throw new InvalidOperationException("Android OpenXR settings were not created.");
        openxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
        foreach (var feature in openxr.GetFeatures<OpenXRFeature>())
        {
            string type = feature.GetType().FullName;
            if (type == "Meta.XR.MetaXRFeature" || type == "Meta.XR.MetaXRSpatialEntityFeature" ||
                type == "UnityEngine.XR.OpenXR.Features.MetaQuestSupport.MetaQuestFeature" ||
                type == "UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile")
            {
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
            }
        }
        EditorUtility.SetDirty(openxr);
        EditorUtility.SetDirty(xr);
        var config = OVRProjectConfig.CachedProjectConfig;
        config.sceneSupport = OVRProjectConfig.FeatureSupport.Required;
        config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
        // Permit launch while Touch controllers are asleep. Flight input comes from
        // the Bluetooth gamepad; hands remain available for Horizon system dialogs.
        config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
        config.targetDeviceTypes = new() { OVRProjectConfig.DeviceType.Quest3 };
        OVRProjectConfig.CommitProjectConfig(config);
    }

    static Material Material(string name, Color color, bool unlit = false)
    {
        string path = Root + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find(unlit ? "Unlit/Color" : "Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        if (!unlit)
        {
            material.SetFloat("_Glossiness", 0.42f);
            material.SetFloat("_Metallic", name is "Titanium" or "Bronze" ? 0.65f : 0);
        }
        return material;
    }

    static void SetupVehicleMaterials()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(DonorModel) != null)
        {
            SetupDonorMaterials();
            return;
        }
        var importer = AssetImporter.GetAtPath(Root + "/Art/AstraWedge.fbx") as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Blender vehicle export is missing.");
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.SaveAndReimport();
        foreach (var original in AssetDatabase.LoadAllAssetsAtPath(importer.assetPath).OfType<Material>())
        {
            Color color = original.name switch
            {
                "Titanium" => new Color(0.42f, 0.49f, 0.54f),
                "Bronze" => new Color(0.58f, 0.34f, 0.12f),
                "SmokedGlass" => new Color(0.025f, 0.065f, 0.08f),
                "Headlight" => new Color(0.75f, 0.94f, 1),
                "TailLight" => new Color(1, 0.04f, 0.015f),
                "JetCyan" => new Color(0.15f, 0.85f, 1),
                _ => new Color(0.025f, 0.03f, 0.035f)
            };
            var replacement = Material(original.name, color,
                original.name is "Headlight" or "TailLight" or "JetCyan");
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(original), replacement);
        }
        importer.SaveAndReimport();
    }

    static void SetupDonorMaterials()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(DonorModel);
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.SaveAndReimport();
        foreach (var entry in ReadDonorData().materials)
        {
            Color color = new Color(entry.color[0], entry.color[1], entry.color[2], entry.color[3]);
            var material = Material(entry.assetName, color, entry.unlit);
            material.shader = Shader.Find(entry.paintMask ? "AstroQuest/Vehicle Paint" :
                entry.unlit ? "Unlit/Color" : "Standard");
            if (material.shader == null) throw new InvalidOperationException("Vehicle shader missing: " + entry.assetName);
            material.color = color;
            if (!entry.unlit)
            {
                material.SetFloat("_Metallic", entry.metallic);
                material.SetFloat("_Glossiness", entry.smoothness);
                if (entry.paintMask) material.SetFloat("_BumpStrength", 0);
                if (entry.paintMask) material.SetFloat("_UseDonorMap", string.IsNullOrEmpty(entry.albedo) ? 0 : 1);
                material.SetTexture("_MainTex", DonorTexture(entry.albedo));
                material.SetTexture("_BumpMap", DonorTexture(entry.normal, true));
                if (!string.IsNullOrEmpty(entry.normal)) material.EnableKeyword("_NORMALMAP");
                var emission = DonorTexture(entry.emission);
                material.SetTexture("_EmissionMap", emission);
                if (emission != null)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", Color.white);
                }
            }
            EditorUtility.SetDirty(material);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.sourceName), material);
        }
        importer.SaveAndReimport();
    }

    static Texture2D DonorTexture(string path, bool normal = false)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing donor texture: " + path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !normal;
        importer.maxTextureSize = 2048;
        importer.anisoLevel = 8;
        importer.mipmapEnabled = true;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name = "Android", overridden = true, maxTextureSize = 2048,
            format = TextureImporterFormat.ASTC_4x4, compressionQuality = 100
        });
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void CreateScene(bool quest)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.69f, 0.79f);
        RenderSettings.ambientEquatorColor = new Color(0.32f, 0.36f, 0.42f);
        RenderSettings.ambientGroundColor = new Color(0.12f, 0.13f, 0.15f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = VehicleReflection();
        RenderSettings.reflectionIntensity = 0.8f;
        var sun = new GameObject("Soft daylight").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        sun.transform.rotation = Quaternion.Euler(45, -35, 0);
        sun.shadows = LightShadows.None;

        Transform viewer;
        if (quest)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab");
            var rigObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var rig = rigObject.GetComponent<OVRCameraRig>();
            var manager = rigObject.GetComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            viewer = rig.centerEyeAnchor;
            foreach (var camera in rigObject.GetComponentsInChildren<Camera>())
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 30;
                camera.allowHDR = false;
            }
            rigObject.AddComponent<OVRPassthroughLayer>();
        }
        else
        {
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 1.65f, -2.4f);
            camera.transform.LookAt(new Vector3(0, 0.65f, 0.5f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.075f, 0.095f);
            camera.nearClipPlane = 0.01f;
            viewer = camera.transform;
            camera.gameObject.AddComponent<AudioListener>();
            CreateTestRoom();
        }

        var vehicle = CreateVehicle();
        var input = vehicle.GetComponent<VehicleInput>();
        input.viewer = viewer;
        bool hasDonor = AssetDatabase.LoadAssetAtPath<GameObject>(DonorModel) != null;
        if (hasDonor)
        {
            var donor = ReadDonorData();
            input.modelSourceUrl = donor.sourceUrl;
            input.modelLicenseUrl = donor.licenseUrl;
        }
        var target = CreatePad("Target pad", "01", vehicle);
        target.position = new Vector3(0.7f, 0.748f, 0.55f);
        var spawn = new GameObject("Validated spawn point").transform;
        spawn.position = new Vector3(-0.4f, 0.066f, -0.35f);
        vehicle.transform.position = spawn.position;
        input.resetPoint = spawn;
        var hud = CreateHud(viewer, vehicle, input);
        if (hasDonor) hud.vehicleCredit = "Car model: Ajay Gawde · CC BY 4.0 · adapted for Flying Lambo";

        if (quest)
        {
            var roomObject = new GameObject("Room Session");
            var kitObject = new GameObject("MR Utility Kit");
            kitObject.SetActive(false);
            var kit = kitObject.AddComponent<MRUK>();
            kit.SceneSettings = new MRUK.MRUKSettings { LoadSceneOnStartup = false, DataSource = MRUK.SceneDataSource.Device };
            kitObject.SetActive(true);
            var session = roomObject.AddComponent<RoomSession>();
            session.roomKit = kit;
            session.vehicle = vehicle;
            session.input = input;
            session.viewer = viewer;
            session.targetPad = target;
            session.spawnPoint = spawn;
            session.hud = hud;
            session.debugMaterial = Material("RoomDebug", new Color(0.2f, 0.7f, 0.8f), true);
            vehicle.gameObject.SetActive(false);
            target.gameObject.SetActive(false);
            hud.roomStatus = "LOADING YOUR ROOM…";
        }
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), quest ? QuestScene : TestScene);
    }

    static ThrusterVehicle CreateVehicle()
    {
        var root = new GameObject("Astra · RC thruster car");
        root.AddComponent<Rigidbody>();
        var body = root.AddComponent<BoxCollider>();
        body.size = new Vector3(0.116f, 0.05f, 0.254f);
        body.center = new Vector3(0, -0.003f, 0);
        // Two small axle boxes approximate the wheel contacts without a concave collider.
        foreach (float z in new[] { -0.087f, 0.087f })
        {
            var axle = new GameObject("Wheel contacts");
            axle.transform.SetParent(root.transform, false);
            var collider = axle.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, -0.017f, z);
            collider.size = new Vector3(0.13f, 0.027f, 0.028f);
        }
        string physicsPath = Root + "/Materials/VehicleContact.physicMaterial";
        var contact = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(physicsPath);
        if (contact == null)
        {
            contact = new PhysicsMaterial("VehicleContact") { dynamicFriction = 0.6f, staticFriction = 0.7f, bounciness = 0.08f };
            AssetDatabase.CreateAsset(contact, physicsPath);
        }
        // GroundDrive supplies tyre friction at the four patches. Collider
        // friction would otherwise pin the small car before motor force can move it.
        contact.dynamicFriction = contact.staticFriction = 0;
        contact.frictionCombine = PhysicsMaterialCombine.Minimum;
        contact.bounciness = 0.015f;
        EditorUtility.SetDirty(contact);
        foreach (var collider in root.GetComponentsInChildren<Collider>()) collider.sharedMaterial = contact;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(DonorModel) ??
            AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Art/AstraWedge.fbx");
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.name = "VehicleVisual · replace with donor model here";
        visual.transform.SetParent(root.transform, false);
        bool hasDonor = AssetDatabase.GetAssetPath(model) == DonorModel;
        if (hasDonor)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float scale = 0.267f / Mathf.Max(bounds.size.z, 0.001f);
                visual.transform.localScale *= scale;
                Vector3 center = bounds.center * scale;
                visual.transform.localPosition = new Vector3(-center.x, -0.0305f - bounds.min.y * scale, -center.z);
            }
            var wheels = visual.GetComponentsInChildren<Renderer>();
            foreach (var collider in root.GetComponentsInChildren<BoxCollider>().Where(c => c.gameObject != root))
            {
                string prefix = collider.center.z > 0 ? "LP640_F_" : "LP640_R_";
                var axleWheels = wheels.Where(w => w.name.StartsWith(prefix) && w.name.Contains("wheel")).ToArray();
                if (axleWheels.Length == 0) continue;
                float z = axleWheels.Average(w => root.transform.InverseTransformPoint(w.bounds.center).z);
                collider.center = new Vector3(0, -0.017f, z);
                collider.size = new Vector3(0.112f, 0.027f, 0.028f);
            }
        }
        var vehicle = root.AddComponent<ThrusterVehicle>();
        var donor = hasDonor ? ReadDonorData() : null;
        if (donor?.wheels != null && donor.wheels.Length == 4)
        {
            var drive = root.AddComponent<GroundDrive>();
            drive.wheels = new GroundDrive.Wheel[donor.wheels.Length];
            var modelTransforms = visual.GetComponentsInChildren<Transform>();
            for (int i = 0; i < donor.wheels.Length; i++)
            {
                var wheel = donor.wheels[i];
                var mesh = modelTransforms.First(t => t.name == wheel.name);
                var pivot = new GameObject(wheel.name + " steering").transform;
                pivot.SetParent(root.transform, false);
                pivot.position = mesh.position;
                var spin = new GameObject(wheel.name + " rotation").transform;
                spin.SetParent(pivot, false);
                mesh.SetParent(spin, true);
                var caliper = modelTransforms.FirstOrDefault(t => t.name == "Caliper_" + wheel.name.Replace("Wheel_", ""));
                if (caliper != null) caliper.SetParent(pivot, true);
                drive.wheels[i] = new GroundDrive.Wheel
                {
                    visual = pivot, center = pivot.localPosition, radius = wheel.radius, front = wheel.front
                };
            }
            foreach (var collider in root.GetComponentsInChildren<BoxCollider>().Where(c => c.gameObject != root))
            {
                bool front = collider.center.z > 0;
                var axle = drive.wheels.Where(w => w.front == front).ToArray();
                collider.center = new Vector3(0, -0.018f, axle.Average(w => w.center.z));
                collider.size = new Vector3(0.108f, 0.025f, 0.023f);
            }
        }
        root.AddComponent<VehicleInput>();
        if (hasDonor)
        {
            var transforms = visual.GetComponentsInChildren<Transform>();
            foreach (var mount in ReadDonorData().mounts)
            {
                var socket = transforms.FirstOrDefault(t => t.name == mount.name);
                if (socket == null) throw new InvalidOperationException("Thruster socket missing: " + mount.name);
                AddJet(root.transform, vehicle, root.transform.InverseTransformPoint(socket.position),
                    new Vector3(mount.force[0], mount.force[1], mount.force[2]),
                    new Vector3(mount.torque[0], mount.torque[1], mount.torque[2]));
            }
        }
        else
        {
            foreach (Vector3 direction in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                AddJet(root.transform, vehicle, Vector3.Scale(-direction, new Vector3(0.066f, 0.028f, 0.13f)), direction, Vector3.zero);
            foreach (float sign in new[] { -1f, 1f })
            {
                AddJet(root.transform, vehicle, new Vector3(0, 0, 0.105f), Vector3.down * sign, Vector3.right * sign, true);
                AddJet(root.transform, vehicle, new Vector3(0, 0, -0.105f), Vector3.up * sign, Vector3.right * sign, true);
                AddJet(root.transform, vehicle, new Vector3(0, 0, 0.105f), Vector3.right * sign, Vector3.up * sign, true);
                AddJet(root.transform, vehicle, new Vector3(0, 0, -0.105f), Vector3.left * sign, Vector3.up * sign, true);
                AddJet(root.transform, vehicle, new Vector3(0.06f, 0, 0), Vector3.up * sign, Vector3.forward * sign, true);
                AddJet(root.transform, vehicle, new Vector3(-0.06f, 0, 0), Vector3.down * sign, Vector3.forward * sign, true);
            }
        }
        PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/AstraVehicle.prefab");
        return vehicle;
    }

    static void AddJet(Transform parent, ThrusterVehicle vehicle, Vector3 position,
        Vector3 force, Vector3 torque, bool rotationOnly = false)
    {
        var socket = new GameObject(rotationOnly ? "Attitude jet" : "Translation jet");
        socket.transform.SetParent(parent, false);
        socket.transform.localPosition = position;
        socket.transform.localRotation = Quaternion.FromToRotation(Vector3.up, -force);
        var plume = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        UnityEngine.Object.DestroyImmediate(plume.GetComponent<Collider>());
        plume.transform.SetParent(socket.transform, false);
        plume.transform.localPosition = new Vector3(0, 0.015f, 0);
        plume.GetComponent<Renderer>().sharedMaterial = Material("Exhaust", new Color(0.3f, 0.85f, 1), true);
        var jet = socket.AddComponent<ThrusterVisuals>();
        jet.vehicle = vehicle;
        jet.forceDirection = force;
        jet.torqueDirection = torque;
        jet.plume = plume.transform;
        plume.SetActive(false);
    }

    static Transform CreatePad(string name, string number, ThrusterVehicle vehicle)
    {
        var root = new GameObject(name);
        var pad = root.AddComponent<LandingPad>();
        pad.vehicle = vehicle;
        pad.label = number;
        var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseObject.name = "Pad surface";
        baseObject.transform.SetParent(root.transform, false);
        baseObject.transform.localScale = new Vector3(0.38f, 0.004f, 0.38f);
        baseObject.GetComponent<Renderer>().sharedMaterial = Material("PadCarbon", new Color(0.05f, 0.075f, 0.09f));
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Status disc";
        ring.transform.SetParent(root.transform, false);
        ring.transform.localPosition = new Vector3(0, 0.005f, 0);
        ring.transform.localScale = new Vector3(0.31f, 0.0008f, 0.31f);
        UnityEngine.Object.DestroyImmediate(ring.GetComponent<Collider>());
        pad.statusRing = ring.GetComponent<Renderer>();
        pad.statusRing.sharedMaterial = Material("PadCyan", new Color(0.15f, 0.65f, 0.75f), true);
        var labelObject = new GameObject("Pad number");
        labelObject.transform.SetParent(root.transform, false);
        labelObject.transform.localPosition = new Vector3(0, 0.006f, -0.07f);
        labelObject.transform.localRotation = Quaternion.Euler(90, 0, 0);
        var text = labelObject.AddComponent<TextMesh>();
        text.text = number;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 80;
        text.characterSize = 0.008f;
        text.anchor = TextAnchor.MiddleCenter;
        text.color = new Color(0.05f, 0.2f, 0.25f);
        text.GetComponent<Renderer>().sharedMaterial = text.font.material;
        return root.transform;
    }

    static FlightHud CreateHud(Transform viewer, ThrusterVehicle vehicle, VehicleInput input)
    {
        var root = new GameObject("Flight status");
        var text = root.AddComponent<TextMesh>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.GetComponent<Renderer>().sharedMaterial = text.font.material;
        text.fontSize = 48;
        text.characterSize = 0.0045f;
        text.anchor = TextAnchor.UpperCenter;
        text.alignment = TextAlignment.Center;
        text.color = new Color(0.7f, 0.92f, 1);
        var hud = root.AddComponent<FlightHud>();
        hud.vehicle = vehicle;
        hud.input = input;
        hud.readout = text;
        hud.viewer = viewer;
        return hud;
    }

    static void CreateTestRoom()
    {
        Box("Floor", new Vector3(0, -0.06f, 0.2f), new Vector3(4.4f, 0.12f, 4.4f), new Color(0.18f, 0.23f, 0.27f));
        Box("Back wall", new Vector3(0, 1.3f, 2.35f), new Vector3(4.4f, 2.6f, 0.1f), new Color(0.23f, 0.29f, 0.32f));
        Box("Left wall", new Vector3(-2.15f, 1.3f, 0.2f), new Vector3(0.1f, 2.6f, 4.4f), new Color(0.2f, 0.26f, 0.3f));
        // The other two walls and ceiling are collision-only, keeping the test view open.
        Box("Right boundary", new Vector3(2.15f, 1.3f, 0.2f), new Vector3(0.1f, 2.6f, 4.4f), Color.clear, false);
        Box("Front boundary", new Vector3(0, 1.3f, -1.95f), new Vector3(4.4f, 2.6f, 0.1f), Color.clear, false);
        Box("Ceiling", new Vector3(0, 2.65f, 0.2f), new Vector3(4.4f, 0.1f, 4.4f), Color.clear, false);
        Box("Table top", new Vector3(0.7f, 0.7f, 0.65f), new Vector3(1.1f, 0.08f, 0.8f), new Color(0.48f, 0.32f, 0.2f));
        foreach (float x in new[] { 0.23f, 1.17f })
        foreach (float z in new[] { 0.33f, 0.97f })
            Box("Table leg", new Vector3(x, 0.34f, z), new Vector3(0.055f, 0.68f, 0.055f), new Color(0.08f, 0.1f, 0.12f));
        Box("Sofa base", new Vector3(-1.15f, 0.22f, 1.3f), new Vector3(1.25f, 0.44f, 0.6f), new Color(0.18f, 0.35f, 0.35f));
        Box("Sofa back", new Vector3(-1.15f, 0.55f, 1.6f), new Vector3(1.25f, 0.65f, 0.15f), new Color(0.17f, 0.31f, 0.32f));
    }

    static void Box(string name, Vector3 position, Vector3 size, Color color, bool visible = true)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.layer = SurfacePlacement.RoomLayer;
        box.transform.position = position;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().enabled = visible;
        if (visible) box.GetComponent<Renderer>().sharedMaterial = Material(name.Replace(" ", ""), color);
    }

    [MenuItem("Flying Lambo/Build Quest APK")]
    public static void BuildApk() => BuildQuestApk(BuildOptions.None);

    [MenuItem("Flying Lambo/Build development Quest APK")]
    public static void BuildDevelopmentApk() => BuildQuestApk(BuildOptions.Development);

    static void BuildQuestApk(BuildOptions options)
    {
        Configure();
        AssetDatabase.SaveAssets();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(DonorModel) != null) ValidateVehicleAsset();
        string output = Path.GetFullPath("../Builds/FlyingLambo.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { QuestScene },
            locationPathName = output,
            target = BuildTarget.Android,
            options = options
        });
        if (result.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Quest APK build failed: " + result.summary.result);
        Debug.Log("FLYING LAMBO APK: " + output);
    }

    public static void GenerateAndBuildApk()
    {
        Generate();
        BuildApk();
    }

    public static void CapturePreview()
    {
        EditorSceneManager.OpenScene(TestScene);
        var camera = Camera.main;
        var target = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
        camera.targetTexture = target;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.GetFullPath("../Art/TestRoom.png"), image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(target);
        Debug.Log("FLYING LAMBO: rendered Editor test room preview.");
    }

    public static void CaptureVehiclePreviews()
    {
        CapturePreview();
        var vehicle = UnityEngine.Object.FindFirstObjectByType<ThrusterVehicle>();
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            if (!renderer.transform.IsChildOf(vehicle.transform)) renderer.enabled = false;
        var camera = Camera.main;
        camera.transform.position = vehicle.transform.position + new Vector3(0.3f, 0.2f, 0.38f);
        camera.transform.LookAt(vehicle.transform.position + Vector3.up * 0.004f);
        camera.orthographic = true;
        camera.orthographicSize = 0.14f;
        camera.backgroundColor = new Color(0.055f, 0.075f, 0.095f);
        var target = new RenderTexture(1500, 1000, 24) { antiAliasing = 4 };
        camera.targetTexture = target;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(1500, 1000, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1500, 1000), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.GetFullPath("../Art/AstraLP640_Unity.png"), image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(target);
        EditorSceneManager.OpenScene(TestScene);
        ValidateVehicleAsset();
    }

    public static void GenerateAndCaptureVehiclePreviews()
    {
        Generate();
        CaptureVehiclePreviews();
        CaptureTextVisibilityPreview();
    }

    public static void GeneratePreviewAndBuildApk()
    {
        GenerateAndCaptureVehiclePreviews();
        BuildApk();
    }

    public static void CaptureTextVisibilityPreview()
    {
        const string preference = "AstroQuest.HelpVisible";
        bool hadPreference = PlayerPrefs.HasKey(preference);
        int savedPreference = PlayerPrefs.GetInt(preference, 1);
        try
        {
            EditorSceneManager.OpenScene(TestScene);
            var vehicle = UnityEngine.Object.FindFirstObjectByType<ThrusterVehicle>();
            var hud = UnityEngine.Object.FindFirstObjectByType<FlightHud>();
            vehicle.SendMessage("Awake");
            hud.SendMessage("Awake");
            if (hud.HelpVisible) hud.ToggleHelp();
            foreach (int state in new[] { 0, 1, 2 })
            {
                if (state == 1) vehicle.SetPaused(true);
                if (state == 2)
                {
                    vehicle.gameObject.SetActive(false);
                    hud.roomStatus = "ROOM UNAVAILABLE · RETRY";
                }
                hud.SendMessage("LateUpdate");
                if (!string.IsNullOrEmpty(hud.readout.text) ||
                    UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .Any(t => t.GetComponent<Renderer>() != null && t.GetComponent<Renderer>().enabled))
                    throw new InvalidOperationException("Hidden mode must contain zero visible text, including pad labels and status messages.");
            }
            vehicle.gameObject.SetActive(true);
            var camera = Camera.main;
            var target = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
            var previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.GetFullPath("../Art/TextHidden_Unity.png"), image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
            hud.ToggleHelp();
            if (UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Any(t => t.GetComponent<Renderer>() != null && !t.GetComponent<Renderer>().enabled))
                throw new InvalidOperationException("Text could not be restored after hiding it.");
            Debug.Log("FLYING LAMBO TEXT VERIFIED: zero visible text for active vehicle, paused vehicle, unavailable room and pad labels; toggle restores text.");
        }
        finally
        {
            if (hadPreference) PlayerPrefs.SetInt(preference, savedPreference);
            else PlayerPrefs.DeleteKey(preference);
            PlayerPrefs.Save();
            EditorSceneManager.OpenScene(TestScene);
        }
    }

    public static void ValidateVehicleAsset()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/AstraVehicle.prefab");
        var instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            instance.transform.position = Vector3.zero;
            var renderers = instance.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            if (Mathf.Abs(bounds.size.z - 0.267f) > 0.001f || bounds.size.x > 0.15f || bounds.size.y > 0.1f)
                throw new InvalidOperationException("Vehicle export has an incorrect scale: " + bounds.size);
            var jets = instance.GetComponentsInChildren<ThrusterVisuals>();
            if (jets.Length != ReadDonorData().mounts.Length)
                throw new InvalidOperationException("Vehicle exhaust effects do not match its exported sockets.");
            if (renderers.Any(r => r.sharedMaterials.Any(m => m == null || m.shader == null || ShaderUtil.ShaderHasError(m.shader))))
                throw new InvalidOperationException("Vehicle has missing materials or shader errors.");
            var paint = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/LamboBody.mat");
            if (paint == null || paint.shader.name != "AstroQuest/Vehicle Paint" ||
                paint.color.r < 0.9f || paint.color.g < 0.9f || paint.color.b < 0.9f)
                throw new InvalidOperationException("White vehicle paint is not configured.");
            int triangles = instance.GetComponentsInChildren<MeshFilter>()
                .Sum(mesh => mesh.sharedMesh.triangles.Length / 3);
            if (triangles > 300000) throw new InvalidOperationException("Vehicle exceeds the 300k triangle budget.");
            Debug.Log($"FLYING LAMBO VEHICLE VALIDATED: {triangles} triangles, {jets.Length} ports, size {bounds.size}, white paint and shaders OK.");
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
    }

    static Cubemap VehicleReflection()
    {
        const string path = Root + "/Art/VehicleStudioReflection.asset";
        var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        if (cube != null) return cube;
        const int size = 128;
        cube = new Cubemap(size, TextureFormat.RGBAHalf, true) { name = "Neutral softbox reflections" };
        for (int face = 0; face < 6; face++)
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2 - 1, v = (y + 0.5f) / size * 2 - 1;
                Vector3 d = ((CubemapFace)face switch
                {
                    CubemapFace.PositiveX => new Vector3(1, -v, -u),
                    CubemapFace.NegativeX => new Vector3(-1, -v, u),
                    CubemapFace.PositiveY => new Vector3(u, 1, v),
                    CubemapFace.NegativeY => new Vector3(u, -1, -v),
                    CubemapFace.PositiveZ => new Vector3(u, -v, 1),
                    _ => new Vector3(-u, -v, -1)
                }).normalized;
                Color color = Color.Lerp(new Color(0.045f, 0.055f, 0.075f),
                    new Color(0.36f, 0.42f, 0.52f), d.y * 0.5f + 0.5f);
                float ceiling = Mathf.SmoothStep(0, 1, (d.y - 0.55f) / 0.08f) *
                    Mathf.SmoothStep(0, 1, (0.62f - Mathf.Abs(d.x)) / 0.08f);
                float strip = Mathf.Exp(-Mathf.Pow((d.x + 0.65f) / 0.055f, 2)) * Mathf.Clamp01(d.y + 0.5f);
                pixels[y * size + x] = color + Color.white * (ceiling * 1.5f + strip * 0.8f);
            }
            cube.SetPixels(pixels, (CubemapFace)face);
        }
        cube.Apply(true, false);
        AssetDatabase.CreateAsset(cube, path);
        return cube;
    }
}
