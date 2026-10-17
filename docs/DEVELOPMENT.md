# Developing Flying Lambo

## Requirements

- Unity **6000.6.0f1**, with Android Build Support, SDK/NDK and OpenJDK installed through Unity Hub.
- Quest 3 with developer mode enabled, a room scan and spatial-data permission.
- A Bluetooth Xbox controller for headset play. Keyboard input is available in the Editor.
- Blender is optional: the runtime model and editable source are included. The art scripts were created with Blender 5.2.1 and use its bundled NumPy.

Open `Unity` in Unity Hub. Package versions and the Meta XR registry are pinned in `Unity/Packages/manifest.json` and `packages-lock.json`.

## Editor play

Open `Assets/AstroQuest/Scenes/TestRoom.unity`, press Play, and focus the Game view. The car starts directly on the floor; the optional target pad is on a table. There is no automatic floor launch pad. Connect a gamepad or use these keyboard controls:

| Keyboard | Action |
| --- | --- |
| Space | Launch |
| W / S | Forward/backward thrust; accelerate/brake then reverse on the ground |
| A / D | Sideways thrust; steer on the ground |
| Q / E | Down/up thrust |
| Down / Up arrows | Pitch nose-up / nose-down |
| Left / Right arrows | Yaw |
| Z / C | Roll |
| H | Toggle stop-and-level with position hold |
| Hold left Shift | Counter-thrust brake in flight; handbrake on the ground |
| B | Toggle spin damping |
| Hold R for 0.8 seconds | Reset |
| Escape | Pause/resume |
| F1 | Show/hide all text |

The Xbox View/F1 preference is saved between sessions. Hidden mode disables all text renderers, including speed/state, room errors, pause messages, credits and pad labels. The toggle remains available while paused or waiting for room data.

## Build and install

Choose **Flying Lambo → Build Quest APK** for a regular build, or **Build development Quest APK** for a development build. Both write `Builds/FlyingLambo.apk` at the repository root. `Assets/AstroQuest/Scenes/QuestMR.unity` is the headset scene.

Regular builds disable the Meta DevAgent and clear its local connection settings after the SDK's build processor runs. Development builds retain those settings for local tooling. Generated DevAgent settings, test resources and build caches are excluded from Git.

Close the interactive Editor before using the command-line scripts:

```sh
bash scripts/unity.sh build
bash scripts/unity.sh test
bash scripts/install-quest.sh
```

`build-development`, `generate` and `preview` are also supported. Scripts default to the standard macOS Unity Hub paths. Set `LAMBO_UNITY_EDITOR` and `LAMBO_ADB` to your Unity and ADB executables on other installations. Set `LAMBO_QUEST_SERIAL` when several devices are attached. The older `ASTRO_UNITY_EDITOR`, `ASTRO_QUEST_ADB` and `ASTRO_QUEST_SERIAL` variables remain supported.

To install a downloaded release without the project:

```sh
adb install -r FlyingLambo.apk
adb shell am start -n com.astroquest.prototype/com.unity3d.player.UnityPlayerActivity
```

The Android package ID remains `com.astroquest.prototype` so the renamed app updates earlier installations. Public APKs use Android debug signing; builds made on another machine may require uninstalling an existing copy before installation.

## Room setup

Pair the Xbox controller in the headset's Bluetooth settings. Launch Flying Lambo from Unknown Sources, allow spatial-data access, and complete room setup if prompted. Press Menu to resume if a system dialog paused the game. The game finds a clear, horizontal launch surface in front of the viewer and places the car directly on it, without adding a pad. Y launches the car. In flight, RT/LT provide forward/reverse thrust, the left stick provides sideways/up/down thrust, and pulling back on the right stick raises the nose.

Optional hand tracking allows Horizon to launch the app while Touch controllers are asleep. Hand gestures do not control the car. Controller loss or application focus loss pauses the vehicle and clears thrust.

Look at a clear horizontal furniture surface and press D-pad down to place a pad. Placement checks nine support points, flatness, edge clearance and overhead space. D-pad up reloads the room. Rescan moved furniture in the headset's room setup before reloading.

Room colliders use the global mesh when available and semantic surfaces otherwise. They are invisible. Visual depth occlusion and continuous updates for moving objects are not implemented.

## Physics

Translation uses vehicle-relative axes. Turning the car leaves its existing momentum unchanged. Spin damping leaves linear drift alone. Launching adds an upward impulse and disables gravity. A slow, upright underside contact restores Earth gravity; wall and roof contacts do not count as landings. Leaving a ledge returns the car to zero gravity.

Stop-and-level captures world position and heading. A bounded position/velocity controller commands counter-thrust, while an orientation controller levels pitch/roll and preserves heading. The allocator drives 17 actual nozzle directions with bounded, nonnegative forces. `Rigidbody.AddForceAtPosition` produces translation and torque through their lever arms, and exhaust follows applied force. Stopping takes time and distance; pose and velocity are never snapped to the target. Manual flight input cancels the hold.

Ground driving applies traction-limited tyre forces at four sampled contacts, with finite braking, rolling resistance and front steering. Ground colliders carry the weight; low collider friction avoids double-counting tyre grip. Wheels visibly steer and spin, while brake calipers stay fixed to their steering pivots.

## Vehicle art

- `Art/AstraLP640_HD.blend`: current editable high-detail white vehicle.
- `Art/refine_lamborghini.py`: high-detail conversion recipe; calls `adapt_lamborghini.py` for the donor import.
- `Art/Donor/original/source/Lamborgini/`: unmodified donor Blender source and textures.
- `Unity/Assets/AstroQuest/Art/DonorVehicle.fbx`: ready-to-use runtime export.
- `Unity/Assets/AstroQuest/Art/DonorVehicle.json`: material, wheel and thruster metadata.
- `Art/AstraLP640_Unity.png`: Unity render used in the README.

The donor is [Ajay Gawde's Lamborghini Murcielago LP-640](https://sketchfab.com/3d-models/lamborghini-murcielago-lp-640-low-poly-1822aaadc6f34060a38ddf46f0c84ec3), under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Preserve [CREDITS.md](../CREDITS.md) when redistributing the model or derived assets.

The conversion replaces baked seam pixels with pearl-white paint and geometric material boundaries, refines body curvature and arches, and adds modelled seams, grilles, louvers, wheels, brakes, lamps and recessed nozzles. It exports **248,972 triangles** at **26.7 cm length**, with **17 thruster ports**. Runtime textures use up to 2048 px and Android ASTC 4×4 compression. A neutral studio reflection cubemap supplies paint/glass/metal reflections; it does not sample the physical room.

Run the recipe from Blender's Scripting workspace, or with Blender's command line:

```sh
blender --background --python Art/refine_lamborghini.py
```

The scripts append their own scenes and preserve earlier scenes. They overwrite the generated FBX, metadata and conversion files; save hand-authored changes separately before regenerating. After importing art changes in Unity, choose **Flying Lambo → Generate prototype scenes** to rebuild the prefab, materials and both scenes. This also overwrites generated Unity scene changes.

The earlier `AstraLP640.blend` conversion and original `AstraWedge.blend` placeholder remain available. Internal `AstroQuest` names and asset paths are retained to preserve Unity references.

## Verification

Run the Play Mode suite under `Assets/AstroQuest/Tests/PlayMode`. Its twelve tests exercise inertial coasting, momentum after rotation, finite braking, launch/landing transitions, pause recovery, rejected landings, pad clearance, imported wheel contacts, physical stop-and-level/manual cancellation, ground acceleration/braking/steering/reverse/launch, and actual gamepad trigger/vertical-thrust and pull-back-to-pitch-up behavior. CLI results go to ignored `flight-test-results.xml`; logs go to ignored `Unity/Logs`.

The build validates vehicle scale, white paint, triangle count, shader compilation and nozzle count. `AstroQuestBuilder.GenerateAndCaptureVehiclePreviews` renders the vehicle and test room, and checks that hiding text leaves zero visible text renderers across active, paused and room-unavailable states, then restores them. All quality tiers and runtime startup enforce 4× MSAA and full texture resolution.

All twelve Play Mode tests passed on 18 October 2026 after removing the automatic floor pad and updating the gamepad controls. The input tests verify RT/LT forward/reverse flight without lift, left-stick vertical thrust, and pull-back nose-up motion on the imported vehicle. The preceding build was installed and launched on Quest 3 on 17 October 2026. Its startup confirmed 4× MSAA and detected four room anchors. Interactive checks remain for controller mappings/reconnect, passthrough appearance, room permission/reload behavior, furniture collisions and landings, text hiding, flight hold, and sustained frame rate/thermals. Sound, collision damage and strict landing scoring are not implemented.
