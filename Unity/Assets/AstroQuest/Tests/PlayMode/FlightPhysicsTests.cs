using System.Collections;
using System.Collections.Generic;
using AstroQuest;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class FlightPhysicsTests
{
    readonly List<GameObject> objects = new();

    ThrusterVehicle Ship(Vector3 position)
    {
        var root = new GameObject("Test vehicle");
        objects.Add(root);
        root.transform.position = position;
        root.AddComponent<BoxCollider>().size = new Vector3(0.12f, 0.06f, 0.26f);
        root.AddComponent<Rigidbody>();
        var vehicle = root.AddComponent<ThrusterVehicle>();
        vehicle.attitudeAssist = false;
        return vehicle;
    }

    GameObject Surface(Vector3 size)
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        objects.Add(floor);
        floor.layer = SurfacePlacement.RoomLayer;
        floor.transform.position = Vector3.down * 0.05f;
        floor.transform.localScale = size;
        Physics.SyncTransforms();
        return floor;
    }

    static IEnumerator Steps(int count)
    {
        for (int i = 0; i < count; i++) yield return new WaitForFixedUpdate();
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        foreach (var obj in objects) Object.Destroy(obj);
        objects.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator ReleasingThrustPreservesVelocityAndTurningPreservesWorldMomentum()
    {
        var ship = Ship(Vector3.up * 5);
        ship.Launch();
        ship.Command = new FlightCommand { Translation = Vector3.forward };
        yield return Steps(30);
        ship.Command = default;
        yield return Steps(2);
        Vector3 velocity = ship.Body.linearVelocity;
        Assert.That(velocity.z, Is.GreaterThan(0.1f));
        ship.Body.rotation = Quaternion.Euler(0, 90, 0);
        yield return Steps(30);
        Assert.That(Vector3.Distance(ship.Body.linearVelocity, velocity), Is.LessThan(0.005f));
        Assert.That(ship.Body.useGravity, Is.False);
    }

    [UnityTest]
    public IEnumerator BrakeAppliesFiniteCounterThrustWithoutInstantlyStopping()
    {
        var ship = Ship(Vector3.up * 5);
        ship.Launch();
        yield return Steps(1);
        ship.Body.linearVelocity = new Vector3(0, 0, 0.8f);
        ship.Command = new FlightCommand { Brake = true };
        yield return Steps(1);
        Assert.That(ship.Body.linearVelocity.z, Is.InRange(0.7f, 0.8f));
        yield return Steps(120);
        Assert.That(ship.Body.linearVelocity.magnitude, Is.LessThan(0.02f));
    }

    [UnityTest]
    public IEnumerator LaunchAndGentleLandingSwitchBetweenZeroAndEarthGravity()
    {
        Surface(new Vector3(3, 0.1f, 3));
        var ship = Ship(new Vector3(0, 0.034f, 0));
        yield return Steps(50);
        Assert.That(ship.State, Is.EqualTo(ThrusterVehicle.FlightState.Grounded));
        Assert.That(ship.Body.useGravity, Is.True);
        ship.Launch();
        yield return Steps(70);
        Assert.That(ship.State, Is.EqualTo(ThrusterVehicle.FlightState.FreeFlight));
        Assert.That(ship.Body.useGravity, Is.False);
        ship.Body.position = new Vector3(0, 0.065f, 0);
        ship.Body.linearVelocity = Vector3.down * 0.15f;
        yield return Steps(90);
        Assert.That(ship.State, Is.EqualTo(ThrusterVehicle.FlightState.Grounded));
        Assert.That(ship.SuccessfulLandings, Is.EqualTo(1));
        Assert.That(ship.Body.useGravity, Is.True);
    }

    [UnityTest]
    public IEnumerator PausingPreservesMomentumAndClearsStaleThrust()
    {
        var ship = Ship(Vector3.up * 5);
        ship.Launch();
        yield return Steps(1);
        Vector3 velocity = new Vector3(0.2f, 0.1f, -0.3f);
        ship.Body.linearVelocity = velocity;
        ship.Command = new FlightCommand { Translation = Vector3.forward };
        ship.SetPaused(true);
        Vector3 position = ship.Body.position;
        yield return Steps(5);
        Assert.That(Vector3.Distance(ship.Body.position, position), Is.LessThan(0.001f));
        ship.SetPaused(false);
        Assert.That(ship.Command.Translation, Is.EqualTo(Vector3.zero));
        Assert.That(Vector3.Distance(ship.Body.linearVelocity, velocity), Is.LessThan(0.001f));
    }

#if UNITY_EDITOR
    ThrusterVehicle ImportedShip(Vector3 position)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AstroQuest/Prefabs/AstraVehicle.prefab");
        var instance = Object.Instantiate(prefab, position, Quaternion.identity);
        objects.Add(instance);
        instance.GetComponent<VehicleInput>().enabled = false;
        var ship = instance.GetComponent<ThrusterVehicle>();
        ship.SetPaused(false);
        return ship;
    }

    [UnityTest]
    public IEnumerator StopAndLevelUsesBoundedJetsAndReturnsToCapturedPosition()
    {
        var ship = ImportedShip(Vector3.up * 2);
        ship.Launch();
        yield return Steps(2);
        ship.Body.rotation = Quaternion.Euler(25, 40, -20);
        ship.Body.linearVelocity = new Vector3(0.3f, 0.15f, 0.2f);
        ship.Body.angularVelocity = new Vector3(0.3f, -0.2f, 0.4f);
        ship.SetStopAndLevel(true);
        Vector3 target = ship.HoldPosition;
        Quaternion initialRotation = ship.Body.rotation;
        Vector3 initialVelocity = ship.Body.linearVelocity;
        yield return Steps(1);
        Assert.That(Vector3.Distance(ship.Body.linearVelocity, initialVelocity), Is.LessThan(0.06f), "Braking must take time.");
        Assert.That(Quaternion.Angle(ship.Body.rotation, initialRotation), Is.LessThan(5), "Levelling must take time.");
        bool firing = false;
        foreach (var jet in ship.GetComponentsInChildren<ThrusterVisuals>())
        {
            Assert.That(jet.AppliedForce, Is.InRange(0, jet.maximumForce));
            firing |= jet.AppliedForce > 0.0001f;
        }
        Assert.That(firing, Is.True);
        yield return Steps(800);
        Assert.That(ship.StopAndLevelActive, Is.True);
        Assert.That(Vector3.Distance(ship.Body.position, target), Is.LessThan(0.035f));
        Assert.That(ship.Body.linearVelocity.magnitude, Is.LessThan(0.025f));
        Assert.That(Vector3.Dot(ship.transform.up, Vector3.up), Is.GreaterThan(0.997f));
        Assert.That(ship.Body.angularVelocity.magnitude, Is.LessThan(0.03f));
        Assert.That(ship.Body.useGravity, Is.False);
    }

    [UnityTest]
    public IEnumerator ManualFlightInputCancelsPositionHoldWithoutRemovingMomentum()
    {
        var ship = ImportedShip(Vector3.up * 2);
        ship.Launch();
        yield return Steps(2);
        ship.SetStopAndLevel(true);
        ship.Body.linearVelocity = Vector3.forward * 0.3f;
        ship.Command = new FlightCommand { Translation = Vector3.right };
        yield return Steps(2);
        Assert.That(ship.StopAndLevelActive, Is.False);
        Assert.That(ship.Body.linearVelocity.z, Is.GreaterThan(0.28f));
        Assert.That(ship.Body.linearVelocity.x, Is.GreaterThan(0));
    }

    [UnityTest]
    public IEnumerator GroundTyresAccelerateSteerBrakeReverseAndThenLaunch()
    {
        Surface(new Vector3(6, 0.1f, 6));
        var ship = ImportedShip(Vector3.up * 0.05f);
        yield return Steps(100);
        var drive = ship.GetComponent<GroundDrive>();
        Assert.That(drive.SupportedWheels, Is.EqualTo(4));
        ship.Command = new FlightCommand { Throttle = 1 };
        yield return Steps(100);
        Assert.That(ship.State, Is.EqualTo(ThrusterVehicle.FlightState.Grounded));
        Assert.That(ship.Body.linearVelocity.z, Is.GreaterThan(0.3f));
        Assert.That(ship.Body.useGravity, Is.True);
        float beforeBraking = ship.Body.linearVelocity.z;
        ship.Command = new FlightCommand { GroundBrake = 1 };
        yield return Steps(15);
        Assert.That(ship.Body.linearVelocity.z, Is.LessThan(beforeBraking - 0.05f));
        ship.Command = new FlightCommand { Throttle = 0.5f, Steering = 0.7f };
        yield return Steps(70);
        Assert.That(Vector3.Angle(ship.transform.forward, Vector3.forward), Is.GreaterThan(5));
        ship.Command = new FlightCommand { Handbrake = true };
        yield return Steps(130);
        Assert.That(ship.Body.linearVelocity.magnitude, Is.LessThan(0.08f));
        foreach (var jet in ship.GetComponentsInChildren<ThrusterVisuals>()) Assert.That(jet.AppliedForce, Is.Zero);
        ship.Command = new FlightCommand { GroundBrake = 1 };
        yield return Steps(70);
        Assert.That(Vector3.Dot(ship.Body.linearVelocity, ship.transform.forward), Is.LessThan(-0.1f));
        ship.Command = default;
        ship.Launch();
        yield return Steps(25);
        Assert.That(ship.State, Is.EqualTo(ThrusterVehicle.FlightState.FreeFlight));
        Assert.That(ship.Body.useGravity, Is.False);
    }

    [UnityTest]
    public IEnumerator ImportedVehiclePrefabRestsOnItsWheelsAndLaunches()
    {
        Surface(new Vector3(3, 0.1f, 3));
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AstroQuest/Prefabs/AstraVehicle.prefab");
        Assert.That(prefab, Is.Not.Null);
        var instance = Object.Instantiate(prefab, new Vector3(0, 0.05f, 0), Quaternion.identity);
        objects.Add(instance);
        instance.GetComponent<VehicleInput>().enabled = false;
        var ship = instance.GetComponent<ThrusterVehicle>();
        ship.SetPaused(false);
        yield return Steps(90);
        Assert.That(ship.State, Is.EqualTo(ThrusterVehicle.FlightState.Grounded));
        Assert.That(ship.Body.position.y, Is.InRange(0.025f, 0.05f));
        float restingHeight = ship.Body.position.y;
        ship.Launch();
        yield return Steps(30);
        Assert.That(ship.State, Is.EqualTo(ThrusterVehicle.FlightState.FreeFlight));
        Assert.That(ship.Body.useGravity, Is.False);
        Assert.That(ship.Body.position.y, Is.GreaterThan(restingHeight + 0.05f));
    }
#endif

    [Test]
    public void WallInvertedAndHardContactsAreRejectedAsLandings()
    {
        Assert.That(ThrusterVehicle.IsLandingContact(Vector3.right, Vector3.up, 0.1f, 0, true), Is.False);
        Assert.That(ThrusterVehicle.IsLandingContact(Vector3.up, Vector3.down, 0.1f, 0, true), Is.False);
        Assert.That(ThrusterVehicle.IsLandingContact(Vector3.up, Vector3.up, 1.1f, 0, true), Is.False);
        Assert.That(ThrusterVehicle.IsLandingContact(Vector3.up, Vector3.up, 0.1f, 0, false), Is.False);
        Assert.That(ThrusterVehicle.IsLandingContact(Vector3.up, Vector3.up, 0.1f, 0, true), Is.True);
    }

    [Test]
    public void PadPlacementRejectsEdgesAndOverheadObstructions()
    {
        Surface(new Vector3(1, 0.1f, 1));
        Assert.That(SurfacePlacement.TryFindPad(new Ray(Vector3.up, Vector3.down), out _), Is.True);
        Assert.That(SurfacePlacement.TryFindPad(new Ray(new Vector3(0.45f, 1, 0), Vector3.down), out _), Is.False);
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        objects.Add(obstacle);
        obstacle.layer = SurfacePlacement.RoomLayer;
        obstacle.transform.position = new Vector3(0.15f, 0.11f, 0);
        obstacle.transform.localScale = new Vector3(0.06f, 0.18f, 0.2f);
        Physics.SyncTransforms();
        Assert.That(SurfacePlacement.TryFindPad(new Ray(Vector3.up, Vector3.down), out _), Is.False);
    }
}
