"""Create the RC thruster conversion from Ajay Gawde's CC BY 4.0 donor.

Run through Blender MCP or in Blender. Only appends data from the donor file;
the original archive and previous scenes are preserved.
"""
import bpy
import json
import math
import shutil
from pathlib import Path
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Art/Donor/original/source/Lamborgini/Lamborgini_mercy.blend"
OUT = ROOT / "Unity/Assets/AstroQuest/Art"
TEXTURES = OUT / "DonorTextures"
TEXTURES.mkdir(parents=True, exist_ok=True)

scene = bpy.data.scenes.new("AstroQuest_LP640_Thruster")
bpy.context.window.scene = scene
with bpy.data.libraries.load(str(SOURCE), link=False) as (origin, loaded):
    source_names = list(origin.objects)
    loaded.objects = list(source_names)
objects = dict(zip(source_names, loaded.objects))
for obj in objects.values():
    if obj is not None:
        scene.collection.objects.link(obj)
bpy.context.view_layer.update()

coords = [obj.matrix_world @ Vector(corner) for obj in objects.values()
          if obj.type == "MESH" for corner in obj.bound_box]
low = Vector(tuple(min(v[i] for v in coords) for i in range(3)))
high = Vector(tuple(max(v[i] for v in coords) for i in range(3)))
scale = 0.267 / (high.y - low.y)
center = (low + high) / 2
normalization = Matrix.Translation((0, 0, -0.0305)) @ Matrix.Scale(scale, 4) @ \
    Matrix.Translation((-center.x, -center.y, -low.z))
wheel_centers = {}
for name in ["F_L-wheel", "F_R-wheel", "R_L-wheel", "R_R-wheel"]:
    p = normalization @ objects[name].matrix_world.translation
    wheel_centers[name] = [p.x, p.z, -p.y]

root = bpy.data.objects.new("Astra_LP640", None)
scene.collection.objects.link(root)
root["source_author"] = "Ajay Gawde"
root["source_url"] = "https://sketchfab.com/3d-models/lamborghini-murcielago-lp-640-low-poly-1822aaadc6f34060a38ddf46f0c84ec3"
root["license"] = "CC BY 4.0"
root["modifications"] = "RC scale, static meshes, opaque windows, runtime materials, reaction-control and lift nozzles"

manifest = {"author": "Ajay Gawde", "license": "CC BY 4.0",
            "sourceUrl": root["source_url"],
            "licenseUrl": "https://creativecommons.org/licenses/by/4.0/",
            "materials": [], "mounts": [], "wheelCenters": wheel_centers}
materials = {}

def donor_material(original, semantic):
    if semantic in materials:
        return materials[semantic]
    mat = original.copy()
    mat.name = "AstraLP640_" + semantic
    node = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    metallic = 0.38 if semantic == "Body" else 0.2
    smoothness = 0.65 if semantic == "Body" else 0.45
    color = [1, 1, 1, 1]
    if semantic == "Glass":
        color = [0.015, 0.04, 0.055, 1]
        metallic, smoothness = 0.35, 0.8
    elif semantic == "Carbon":
        color = [0.022, 0.026, 0.03, 1]
    elif semantic == "TailLamp":
        color = [0.8, 0.015, 0.005, 1]
    elif semantic == "Body":
        color = [0.95, 0.95, 0.95, 1]
    if node:
        node.inputs["Roughness"].default_value = 1 - smoothness
        node.inputs["Alpha"].default_value = 1
        if "Transmission Weight" in node.inputs:
            node.inputs["Transmission Weight"].default_value = 0
        if semantic == "Glass":
            node.inputs["Base Color"].default_value = color
    record = {"sourceName": mat.name, "assetName": "Lambo" + semantic,
              "color": color, "metallic": metallic, "smoothness": smoothness,
              "albedo": "", "normal": "", "emission": "", "paintMask": semantic == "Body"}
    for n in mat.node_tree.nodes:
        if n.type != "TEX_IMAGE" or not n.image:
            continue
        source = SOURCE.parent / "Textures" / Path(bpy.path.abspath(n.image.filepath)).name
        if not source.is_file():
            raise FileNotFoundError(source)
        n.image.filepath = str(source)
        n.image.reload()
        texture_type = "albedo" if "BaseColor" in source.name or source.name == "Texture.jpg" else \
            "normal" if "Normal" in source.name else "emission" if "Emissive" in source.name else None
        if texture_type:
            target = TEXTURES / source.name
            shutil.copy2(source, target)
            record[texture_type] = "Assets/AstroQuest/Art/DonorTextures/" + target.name
    if semantic == "Body" and node and node.inputs["Base Color"].is_linked:
        # Select the donor's yellow paint in the shader, preserving dark details.
        color_socket = node.inputs["Base Color"].links[0].from_socket
        links = mat.node_tree.links
        for link in list(node.inputs["Base Color"].links): links.remove(link)
        separate = mat.node_tree.nodes.new("ShaderNodeSeparateColor")
        links.new(color_socket, separate.inputs[0])
        minimum = mat.node_tree.nodes.new("ShaderNodeMath"); minimum.operation = "MINIMUM"
        links.new(separate.outputs[0], minimum.inputs[0]); links.new(separate.outputs[1], minimum.inputs[1])
        difference = mat.node_tree.nodes.new("ShaderNodeMath"); difference.operation = "SUBTRACT"
        links.new(minimum.outputs[0], difference.inputs[0]); links.new(separate.outputs[2], difference.inputs[1])
        selection = mat.node_tree.nodes.new("ShaderNodeMapRange")
        selection.clamp = True
        selection.inputs[1].default_value = 0.05
        selection.inputs[2].default_value = 0.18
        links.new(difference.outputs[0], selection.inputs[0])
        mix = mat.node_tree.nodes.new("ShaderNodeMixRGB")
        mix.inputs[2].default_value = color
        links.new(selection.outputs[0], mix.inputs[0]); links.new(color_socket, mix.inputs[1])
        links.new(mix.outputs[0], node.inputs["Base Color"])
    materials[semantic] = mat
    manifest["materials"].append(record)
    return mat

depsgraph = bpy.context.evaluated_depsgraph_get()
for name, obj in objects.items():
    evaluated = obj.evaluated_get(depsgraph)
    mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=depsgraph)
    mesh.transform(normalization @ obj.matrix_world)
    original_materials = list(obj.data.materials)
    obj.modifiers.clear()
    obj.data = mesh
    obj.matrix_world = Matrix.Identity(4)
    obj.parent = root
    obj.name = "LP640_" + name
    material_indices = [p.material_index for p in mesh.polygons]
    mesh.materials.clear()
    for slot, original in enumerate(original_materials):
        semantic = "Body" if name == "Body" and slot == 0 else \
            "Carbon" if name == "Body" else \
            "Details" if name == "Details" and slot == 0 else \
            "TailLamp" if name == "Details" else \
            "Glass" if name.startswith("Glass") else \
            "FrontWheel" if name.startswith("F_") and name.endswith("wheel") else \
            "RearWheel" if name.endswith("wheel") else \
            "Caliper" if name.endswith("caliper") else "BrakeDisc"
        mesh.materials.append(donor_material(original, semantic))
    for polygon, index in zip(mesh.polygons, material_indices): polygon.material_index = index

def make_material(name, color, metallic, roughness, emission=0):
    mat = bpy.data.materials.new("AstraLP640_" + name)
    mat.use_nodes = True
    node = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    node.inputs["Base Color"].default_value = (*color, 1)
    node.inputs["Metallic"].default_value = metallic
    node.inputs["Roughness"].default_value = roughness
    if emission:
        node.inputs["Emission Color"].default_value = (*color, 1)
        node.inputs["Emission Strength"].default_value = emission
    mat.diffuse_color = (*color, 1)
    manifest["materials"].append({"sourceName": mat.name, "assetName": "Lambo" + name,
        "color": [*color, 1], "metallic": metallic, "smoothness": 1-roughness,
        "albedo": "", "normal": "", "emission": "", "unlit": emission > 0})
    return mat

housing = make_material("ThrusterHousing", (0.025, 0.035, 0.045), 0.6, 0.35)
rim = make_material("ThrusterRim", (0.32, 0.38, 0.42), 0.85, 0.27)
core = make_material("ThrusterCore", (0.12, 0.75, 1), 0.2, 0.3, 1.2)

def blender_vector(unity_vector):
    x, y, z = unity_vector
    return Vector((x, -z, y))

def port(name, position, force, torque=(0, 0, 0), radius=0.0037):
    p = blender_vector(position)
    exhaust = -blender_vector(force).normalized()
    # A shallow housing and dark bore keep all underbody hardware above the tyres.
    for label, rad, depth, offset, mat in [
        ("Housing", radius*1.32, 0.004, -0.001, housing),
        ("Rim", radius, 0.001, 0.0011, rim),
        ("Core", radius*0.65, 0.00115, 0.0013, core)]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=rad, depth=depth,
            location=p + exhaust*offset)
        obj = bpy.context.object
        obj.name = name + "_" + label
        obj.rotation_euler = exhaust.to_track_quat("Z", "Y").to_euler()
        obj.parent = root
        obj.data.materials.append(mat)
    marker = bpy.data.objects.new("FX_" + name, None)
    marker.location = p + exhaust*0.002
    marker.parent = root
    scene.collection.objects.link(marker)
    manifest["mounts"].append({"name": marker.name, "force": list(force), "torque": list(torque)})

port("RearMain", (0,-0.011,-0.13), (0,0,1), radius=0.0055)
port("NoseBrake", (0,-0.013,0.127), (0,0,-1), radius=0.0045)
port("LeftTranslate", (-0.058,-0.008,0), (1,0,0))
port("RightTranslate", (0.058,-0.008,0), (-1,0,0))
port("RoofTranslate", (0,0.035,0), (0,-1,0), radius=0.0035)
for x in [-1,1]:
    for z in [-1,1]:
        port(f"Lift_{x}_{z}", (x*0.044,-0.024,z*0.076),
            (0,1,0), (-z*0.7,0,x*0.7), radius=0.0043)
        port(f"AttitudeTop_{x}_{z}", (x*0.05,0.011,z*0.105),
            (0,-1,0), (z*0.7,0,-x*0.7), radius=0.0027)
        port(f"Yaw_{x}_{z}", (x*0.059,-0.008,z*0.105),
            (-x,0,0), (0,-x*z,0), radius=0.0027)

# Use metadata for in-game material mapping and physically aligned exhaust effects.
triangle_count = 0
for obj in root.children_recursive:
    if obj.type == "MESH":
        obj.data.calc_loop_triangles()
        triangle_count += len(obj.data.loop_triangles)
manifest["triangles"] = triangle_count
(OUT / "DonorVehicle.json").write_text(json.dumps(manifest, indent=2) + "\n")
for obj in scene.objects:
    obj.select_set(False)
for obj in [root, *root.children_recursive]:
    obj.select_set(True)
bpy.context.view_layer.objects.active = root
fbx_options = bpy.ops.export_scene.fbx.get_rna_type().properties
assert "RELATIVE" in [item.identifier for item in fbx_options["path_mode"].enum_items]
bpy.ops.export_scene.fbx(filepath=str(OUT / "DonorVehicle.fbx"), use_selection=True,
    object_types={"MESH","EMPTY"}, axis_forward="-Z", axis_up="Y", path_mode="RELATIVE",
    bake_anim=False, add_leaf_bones=False, use_mesh_modifiers=True)

world = bpy.data.worlds.new("LP640_Studio")
world.use_nodes = True
background = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
background.inputs[0].default_value = (0.08,0.12,0.17,1)
background.inputs[1].default_value = 0.45
scene.world = world
for name, location, energy, size in [
    ("LP640_Key",(0.25,-0.25,0.4),20,0.35),
    ("LP640_Rim",(-0.2,0.1,0.25),14,0.3)]:
    data = bpy.data.lights.new(name, "AREA")
    data.energy, data.size = energy, size
    light = bpy.data.objects.new(name, data)
    scene.collection.objects.link(light)
    light.location = location
    light.rotation_euler = (-light.location).to_track_quat("-Z","Y").to_euler()
camera_data = bpy.data.cameras.new("LP640_Camera")
camera = bpy.data.objects.new("LP640_Camera", camera_data)
scene.collection.objects.link(camera)
camera.location = (0.3,-0.38,0.2)
camera.rotation_euler = (Vector((0,0,0.003))-camera.location).to_track_quat("-Z","Y").to_euler()
camera_data.type = "ORTHO"
camera_data.ortho_scale = 0.39
scene.camera = camera
scene.render.resolution_x, scene.render.resolution_y = 1200, 800
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = str(ROOT / "Art/AstraLP640.png")
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == "VIEW_3D":
            area.spaces.active.clip_start = 0.001
            area.spaces.active.region_3d.view_location = (0,0,0)
            area.spaces.active.region_3d.view_distance = 0.4
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "Art/AstraLP640.blend"))
print(json.dumps({"triangles": triangle_count, "materials": len(manifest["materials"]),
    "thrusterPorts": len(manifest["mounts"]), "fbx": str(OUT/"DonorVehicle.fbx")}))
