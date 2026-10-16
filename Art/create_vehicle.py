"""Rebuild the original RC-scale wedge placeholder in a separate Blender scene.

Run in Blender's Python console or through Blender MCP. No downloaded mesh is used.
The existing scene is preserved. Exports an FBX for Unity and an editable .blend.
"""
import bpy
import math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Unity/Assets/AstroQuest/Art"
OUT.mkdir(parents=True, exist_ok=True)
S = 0.058
Z = -0.031

scene = bpy.data.scenes.new("AstroQuest_Wedge")
bpy.context.window.scene = scene
scene.unit_settings.system = "METRIC"

def material(name, rgb, metallic=0, roughness=0.4, emission=0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    node = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    node.inputs["Base Color"].default_value = (*rgb, 1)
    node.inputs["Metallic"].default_value = metallic
    node.inputs["Roughness"].default_value = roughness
    if emission:
        node.inputs["Emission Color"].default_value = (*rgb, 1)
        node.inputs["Emission Strength"].default_value = emission
    mat.diffuse_color = (*rgb, 1)
    return mat

paint = material("Titanium", (0.38, 0.44, 0.48), 0.75, 0.28)
carbon = material("Carbon", (0.014, 0.02, 0.026), 0.25, 0.45)
glass = material("SmokedGlass", (0.018, 0.055, 0.068), 0.6, 0.14)
rubber = material("Rubber", (0.012, 0.013, 0.014), 0, 0.65)
bronze = material("Bronze", (0.55, 0.31, 0.09), 0.8, 0.27)
jet = material("JetCyan", (0.12, 0.8, 1), 0.3, 0.2, 2)
white = material("Headlight", (0.75, 0.92, 1), 0, 0.2, 3)
red = material("TailLight", (1, 0.025, 0.012), 0, 0.2, 2)

root = bpy.data.objects.new("Astra_Wedge", None)
scene.collection.objects.link(root)

def point(v):
    return (v[0]*S, v[1]*S, v[2]*S + Z)

def mesh(name, vertices, faces, mat):
    data = bpy.data.meshes.new(name)
    data.from_pydata([point(v) for v in vertices], [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.parent = root
    obj.data.materials.append(mat)
    return obj

def cube(name, loc, size, mat, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=point(loc))
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = tuple(v*S for v in size)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.parent = root
    obj.data.materials.append(mat)
    if bevel:
        mod=obj.modifiers.new("EdgeHighlights", "BEVEL")
        mod.width=bevel*S
        mod.segments=1
    return obj

# Chamfered longitudinal sections form a low nose, broad shoulders and a short tail.
sections=[(-2.3,.8,.32,.47),(-1.65,.99,.28,.68),(-.65,.95,.27,.73),
          (.55,1.0,.28,.74),(1.55,1.01,.3,.76),(2.25,.88,.33,.66)]
verts=[]
for y,w,b,t in sections:
    verts.extend([(-w*.82,y,b),(-w,y,b+.09),(-w,y,t-.1),(-w*.84,y,t),
                  (w*.84,y,t),(w,y,t-.1),(w,y,b+.09),(w*.82,y,b)])
faces=[tuple(range(7,-1,-1))]
for i in range(len(sections)-1):
    for j in range(8):
        a=i*8+j; b=i*8+(j+1)%8
        faces.append((a,b,b+8,a+8))
faces.append(tuple(range((len(sections)-1)*8,len(sections)*8)))
body=mesh("WedgeBody",verts,faces,paint)

mesh("Canopy",[(-.72,-.78,.735),(.72,-.78,.735),(-.59,-.1,1.11),(.59,-.1,1.11),
               (-.58,.85,1.08),(.58,.85,1.08),(-.73,1.58,.755),(.73,1.58,.755)],
     [(0,1,3,2),(2,3,5,4),(4,5,7,6),(0,2,4,6),(1,7,5,3)],glass)
cube("RoofSpine",(0,.42,1.105),(.08,1.0,.026),carbon)
cube("FrontSplitter",(0,-2.2,.29),(1.72,.34,.07),carbon,.025)
cube("RearDiffuser",(0,2.17,.3),(1.7,.25,.13),carbon,.03)
cube("RearLightBar",(0,2.255,.59),(1.6,.012,.045),red)
for sign in [-1,1]:
    cube("SideSill",(sign*.96,.15,.285),(.09,3.0,.08),carbon,.02)
    cube("FrontLight",(sign*.56,-2.18,.49),(.43,.12,.035),white,.015)
    mesh("SideIntake",[(sign*.96,.32,.39),(sign*1.015,1.08,.43),
                       (sign*1.015,1.08,.67),(sign*.96,.48,.63)],[(0,1,2,3)],carbon)
    for y in [-1.55,1.48]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=20, radius=.345*S, depth=.22*S,
            location=point((sign*1.0,y,.345)), rotation=(0,math.pi/2,0))
        tire=bpy.context.object; tire.name="Wheel"; tire.parent=root
        tire.data.materials.append(rubber)
        bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=.245*S, depth=.012*S,
            location=point((sign*1.118,y,.345)), rotation=(0,math.pi/2,0))
        rim=bpy.context.object; rim.name="AeroWheelCover"; rim.parent=root
        rim.data.materials.append(bronze)
        bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=.125*S, depth=.018*S,
            location=point((sign*1.128,y,.345)), rotation=(0,math.pi/2,0))
        hub=bpy.context.object; hub.name="WheelHub"; hub.parent=root
        hub.data.materials.append(carbon)

# Four underside lift nozzles and fore/aft reaction-control sockets.
for x in [-.69,.69]:
    for y in [-1.4,1.4]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=.115*S, depth=.12*S,
            location=point((x,y,.23)))
        nozzle=bpy.context.object; nozzle.name="LiftNozzle"; nozzle.parent=root
        nozzle.data.materials.append(carbon)
        bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=.075*S, depth=.015*S,
            location=point((x,y,.16)))
        core=bpy.context.object; core.name="LiftCore"; core.parent=root
        core.data.materials.append(jet)
for y in [-2.29,2.24]:
    for x in [-.6,.6]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=.095*S, depth=.10*S,
            location=point((x,y,.4)),rotation=(math.pi/2,0,0))
        nozzle=bpy.context.object; nozzle.name="ReactionNozzle"; nozzle.parent=root
        nozzle.data.materials.append(carbon)
        bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=.06*S, depth=.012*S,
            location=point((x,y + (-.055 if y<0 else .055),.4)),rotation=(math.pi/2,0,0))
        core=bpy.context.object; core.name="ReactionCore"; core.parent=root
        core.data.materials.append(jet)

for obj in [root,*root.children_recursive]:
    obj.select_set(True)
bpy.context.view_layer.objects.active=root
export_types=[i.identifier for i in bpy.ops.export_scene.fbx.get_rna_type().properties["object_types"].enum_items]
bpy.ops.export_scene.fbx(filepath=str(OUT/"AstraWedge.fbx"),use_selection=True,
    object_types={"MESH","EMPTY"},axis_forward="-Z",axis_up="Y",add_leaf_bones=False,
    bake_anim=False,use_mesh_modifiers=True)

world=bpy.data.worlds.new("AstroStudio")
world.use_nodes=True
background=next(n for n in world.node_tree.nodes if n.type=="BACKGROUND")
background.inputs[0].default_value=(.12,.16,.21,1)
background.inputs[1].default_value=.5
scene.world=world

def area(name,loc,energy,size):
    data=bpy.data.lights.new(name,"AREA"); data.energy=energy; data.shape="DISK";data.size=size
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj)
    obj.location=loc; obj.rotation_euler=(-obj.location).to_track_quat("-Z","Y").to_euler()

area("Key",(.25,-.2,.35),30,.35)
area("Rim",(-.18,.16,.25),22,.2)
data=bpy.data.cameras.new("AssetCamera"); camera=bpy.data.objects.new("AssetCamera",data)
scene.collection.objects.link(camera);camera.location=(.28,-.36,.21)
camera.rotation_euler=(Vector((0,0,0))-camera.location).to_track_quat("-Z","Y").to_euler()
data.type="ORTHO";data.ortho_scale=.39;scene.camera=camera
scene.render.resolution_x=1200;scene.render.resolution_y=800;scene.render.resolution_percentage=100
scene.render.image_settings.file_format="PNG"
scene.render.filepath=str(ROOT/"Art/AstraWedge.png")
scene.render.film_transparent=False
scene.view_settings.view_transform="AgX"
for screen in bpy.data.screens:
    for area_view in screen.areas:
        if area_view.type=="VIEW_3D":
            area_view.spaces.active.region_3d.view_distance=.45
            area_view.spaces.active.region_3d.view_location=(0,0,0)
            area_view.spaces.active.clip_start=.001

bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/"Art/AstraWedge.blend"))
print("Saved editable Blender source and exported",OUT/"AstraWedge.fbx")
print("Triangles:",sum(len(p.vertices)-2 for obj in root.children_recursive if obj.type=="MESH" for p in obj.data.polygons))
