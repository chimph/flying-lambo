"""Refine the licensed donor into the higher-detail, articulated RC conversion.
Run in Blender; adapt_lamborghini.py remains the untouched low-detail recipe.
"""
import bpy, bmesh, json, math, runpy, numpy as np
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
ROOT = Path(__file__).resolve().parents[1]
base = runpy.run_path(str(ROOT / 'Art/adapt_lamborghini.py'))
root, scene, manifest = base['root'], base['scene'], base['manifest']
OUT = base['OUT']

def V(p): return Vector((p[0], -p[2], p[1]))
def addmat(name, rgb, metal=0, rough=.4, emission=0):
    mat = bpy.data.materials.new('AstraHD_' + name); mat.use_nodes=True
    p=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value=(*rgb,1)
    p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
    if emission:
        p.inputs['Emission Color'].default_value=(*rgb,1);p.inputs['Emission Strength'].default_value=emission
    manifest['materials'].append(dict(sourceName=mat.name,assetName='LamboHD'+name,color=[*rgb,1],metallic=metal,smoothness=1-rough,albedo='',normal='',emission='',unlit=bool(emission)))
    return mat
rubber=addmat('Rubber',(.017,.019,.022),0,.83)
alloy=addmat('ForgedAlloy',(.35,.39,.44),.92,.2)
chrome=addmat('PolishedLip',(.7,.74,.78),.95,.13)
black=addmat('SatinCarbon',(.013,.019,.024),.32,.36)
steel=addmat('BrakeSteel',(.31,.32,.34),.85,.4)
red=addmat('Caliper',(.58,.012,.009),.28,.28)
glass=addmat('SmokedGlass',(.018,.03,.044),.08,.16)
led=addmat('HeadlightLED',(.78,.91,1),0,.25,1)
tail=addmat('TailLED',(.85,.008,.006),0,.2,1)
cyan=addmat('ThrusterRing',(.06,.63,.88),.2,.25,1)

# Discard the low-poly donor's baked seam/edge pixels. Preserve the large dark
# regions as actual material boundaries, and use continuous pearl paint elsewhere.
body=base['objects']['Body']
old_paint=next(m for m in manifest['materials'] if m['assetName']=='LamboBody')
image=bpy.data.images.load(str(ROOT/'Unity'/old_paint['albedo']),check_existing=True)
width,height=image.size
pixels=np.empty(width*height*4,dtype=np.float32);image.pixels.foreach_get(pixels)
pixels=pixels.reshape(height,width,4)
uvs=body.data.uv_layers.active.data
for polygon in body.data.polygons:
    if polygon.material_index!=0:continue
    uv=sum((uvs[i].uv for i in polygon.loop_indices),Vector((0,0)))/polygon.loop_total
    rgb=pixels[min(height-1,max(0,int(uv.y*height))),min(width-1,max(0,int(uv.x*width))),:3]
    if min(rgb[0],rgb[1])-rgb[2]<.035:polygon.material_index=1
manifest['materials']=[m for m in manifest['materials'] if m['assetName']!='LamboBody']
pearl=addmat('PearlPaint',(.95,.95,.95),.24,.28)
manifest['materials'][-1]['assetName']='LamboBody';manifest['materials'][-1]['paintMask']=True
body.data.materials[0]=pearl;body.data.materials[1]=black
for material in manifest['materials']:
    if material['assetName'] in ['LamboDetails','LamboCarbon','LamboTailLamp']:
        material['albedo']=material['normal']=material['emission']=''
        material['color']=[.65,.012,.007,1] if material['assetName']=='LamboTailLamp' else [.018,.023,.029,1]
for obj in [base['objects']['Details']]:
    for index,material in enumerate(obj.data.materials):
        shader=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        for socket in ['Base Color','Normal']:
            for link in list(shader.inputs[socket].links):material.node_tree.links.remove(link)
        shader.inputs['Base Color'].default_value=(.65,.012,.007,1) if index else (.018,.023,.029,1)

# Finish meshes in physical RC units. Creases preserve the wedge and panel edges.
donor_wheel_centers=[]
for name in ['F_L-wheel','F_R-wheel','R_L-wheel','R_R-wheel']:
    coords=[v.co for v in base['objects'][name].data.vertices]
    donor_wheel_centers.append(Vector([(min(v[i] for v in coords)+max(v[i] for v in coords))/2 for i in range(3)]))
for name in ['LP640_Body','LP640_Glass','LP640_Details']:
    obj=base['objects'][name.removeprefix('LP640_')]
    bm=bmesh.new();bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000005)
    crease=bm.edges.layers.float.get('crease_edge') or bm.edges.layers.float.new('crease_edge')
    for edge in bm.edges:
        edge[crease]=.7 if edge.is_boundary else (.65 if edge.calc_face_angle(0)>.75 else 0)
        if name=='LP640_Body' and edge.is_boundary:
            mid=(edge.verts[0].co+edge.verts[1].co)/2
            for center in donor_wheel_centers:
                if abs(mid.x)>.043 and abs(mid.x-center.x)<.02 and .019<(mid-center).yz.length<.03:
                    edge[crease]=0
    bm.to_mesh(obj.data);bm.free()
    sub=obj.modifiers.new('Curved surface refinement','SUBSURF');sub.levels=3 if name=='LP640_Body' else 2 if name=='LP640_Glass' else 1
    sub.boundary_smooth='ALL'
    sub.render_levels=sub.levels
    if name=='LP640_Body':
        bevel=obj.modifiers.new('Paint edge highlights','BEVEL');bevel.width=.00016;bevel.segments=3
    if name=='LP640_Glass':
        obj.data.materials.clear();obj.data.materials.append(glass)
        for p in obj.data.polygons:p.material_index=0
    for p in obj.data.polygons:p.use_smooth=True
# Replace the original opaque headlamp covers with modelled projector assemblies.
bpy.data.objects.remove(base['objects']['GlassLT'],do_unlink=True)

parts=[]
def mesh(name,verts,faces,mat,parent=root):
    data=bpy.data.meshes.new(name);data.from_pydata([V(v) for v in verts],[],faces);data.update()
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.parent=parent;data.materials.append(mat)
    for p in data.polygons:p.use_smooth=True
    parts.append(obj);return obj

def lathe(name,profile,mat,axis=(1,0,0),center=(0,0,0),parent=root,n=96):
    if len(profile)==2 and abs(profile[0][0]-profile[1][0])<1e-7 and profile[0][1]==0:
        d,r=profile[1];profile=[(d-.00008,0),(d-.00008,r),(d,r),(d,0)]
    axis=Vector(axis).normalized();a=axis.cross(Vector((0,1,0)))
    if a.length<.01:a=axis.cross(Vector((1,0,0)))
    a.normalize();b=axis.cross(a);c=Vector(center)
    verts=[c+axis*distance+radius*(a*math.cos(2*math.pi*j/n)+b*math.sin(2*math.pi*j/n)) for distance,radius in profile for j in range(n)]
    faces=[]
    for k in range(len(profile)-1):
        for j in range(n):faces.append((k*n+j,k*n+(j+1)%n,(k+1)*n+(j+1)%n,(k+1)*n+j))
    return mesh(name,verts,faces,mat,parent)

def tube(name,points,radius,mat,parent=root,closed=False):
    data=bpy.data.curves.new(name,'CURVE');data.dimensions='3D';data.resolution_u=2;data.bevel_depth=radius;data.bevel_resolution=2
    spline=data.splines.new('POLY');spline.points.add(len(points)-1)
    for p,v in zip(spline.points,points):p.co=(*V(v),1)
    spline.use_cyclic_u=closed
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.parent=parent;data.materials.append(mat);parts.append(obj);return obj

def block(name,center,size,mat,parent=root,bevel=.0004):
    bpy.ops.mesh.primitive_cube_add(size=1,location=V(center));obj=bpy.context.object;obj.name=name
    obj.scale=(size[0],size[2],size[1]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    obj.parent=parent;obj.data.materials.append(mat)
    mod=obj.modifiers.new('Machined edges','BEVEL');mod.width=bevel;mod.segments=3
    for p in obj.data.polygons:p.use_smooth=True
    parts.append(obj);return obj

# Fine details are physical surfaces, with no raster seam texture to shimmer.
bpy.context.view_layer.update()
surface=BVHTree.FromObject(body,bpy.context.evaluated_depsgraph_get())
def project(point,direction):
    location,normal,_,_=surface.ray_cast(V(point),V(direction),.4)
    if location is None:return None
    location+=normal*.00028
    return Vector((location.x,location.z,-location.y))
def catmull(points,steps=12):
    points=[Vector(p) for p in points];result=[]
    for i in range(len(points)-1):
        a,b,c,d=points[max(0,i-1)],points[i],points[i+1],points[min(len(points)-1,i+2)]
        for j in range(steps):
            t=j/steps
            result.append(.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t))
    return result+[points[-1]]
seam=addmat('PanelGap',(.045,.052,.06),0,.65,1)
for sign in [-1,1]:
    profile=[(.014,.045),(-.004,.047),(-.023,.036),(-.023,-.026),(.010,-.037),(.018,-.025)]
    line=[project((sign*.13,p[0],p[1]),(-sign,0,0)) for p in catmull(profile)]
    line=[p for p in line if p is not None]
    if len(line)>4:tube('Continuous door shut line',line,.00016,seam)
hood=[(-.031,.079),(-.026,.101),(-.019,.120),(0,.123),(.019,.120),(.026,.101),(.031,.079)]
line=[project((p[0],.13,p[1]),(0,-1,0)) for p in catmull(hood)]
line=[p for p in line if p is not None]
if len(line)>4:tube('Continuous bonnet shut line',line,.00016,seam)
louver=addmat('CoolingBlade',(.065,.075,.09),.55,.38)
detail_surface=BVHTree.FromObject(base['objects']['Details'],bpy.context.evaluated_depsgraph_get())
for z in np.linspace(-.047,-.085,9):
    p=project((0,.13,float(z)),(0,-1,0))
    top,_,_,_=detail_surface.ray_cast(V((0,.13,float(z))),V((0,-1,0)),.4)
    if p is not None:
        if top is not None:p.y=max(p.y,top.z)
        block('Raised engine cooling louver',p+Vector((0,.0005,0)),(.048,.00065,.0027),louver,bevel=.0002)
grille=addmat('GrilleMetal',(.065,.076,.09),.6,.38)
for sign in [-1,1]:
    for row in range(4):
        for col in range(8):
            cx=sign*.038+(col-3.5)*.0025+(row%2)*.00125
            cy=-.014+(row-1.5)*.0022
            line=[]
            for corner in range(6):
                a=2*math.pi*corner/6
                p=project((cx+.00125*math.cos(a),cy+.00125*math.sin(a),.2),(0,0,-1))
                if p is not None and p.z>.12:line.append(p)
            if len(line)==6:tube('Recessed honeycomb intake',line,.00013,grille,closed=True)

wheels=[]
for name in ['F_L-wheel','F_R-wheel','R_L-wheel','R_R-wheel']:
    old=base['objects'][name];coords=[v.co for v in old.data.vertices]
    lo=Vector([min(v[i] for v in coords) for i in range(3)]);hi=Vector([max(v[i] for v in coords) for i in range(3)])
    center=(lo+hi)/2; c=Vector((center.x,center.z,-center.y));radius=(hi.z-lo.z)/2;width=hi.x-lo.x
    front=name.startswith('F');tag=name[:3];sign=1 if c.x>0 else -1
    pivot=bpy.data.objects.new('Wheel_'+tag,None);scene.collection.objects.link(pivot);pivot.parent=root;pivot.location=V(c)
    wheels.append(dict(name=pivot.name,position=list(c),radius=radius,front=front))
    profile=[(-.5,.73),(-.49,.84),(-.43,.94),(-.32,.995),(-.26,1),(-.24,.982),(-.21,1),(-.03,1),(-.02,.98),(.02,.98),(.03,1),(.21,1),(.24,.982),(.26,1),(.32,.995),(.43,.94),(.49,.84),(.5,.73)]
    lathe('Sculpted tyre '+tag,[(x*width,r*radius) for x,r in profile],rubber,parent=pivot)
    outer=width*.49*sign
    lathe('Diamond cut rim '+tag,[(outer,.74*radius),(outer+sign*.0004,.75*radius),(outer+sign*.0009,.72*radius),(outer+sign*.0005,.69*radius),(outer-sign*.002,.69*radius)],chrome,parent=pivot)
    lathe('Inner barrel '+tag,[(-width*.4,.7*radius),(width*.4,.7*radius)],black,parent=pivot)
    lathe('Vented brake disc '+tag,[(outer-sign*.0014,.2*radius),(outer-sign*.0014,.62*radius),(outer-sign*.002,.62*radius),(outer-sign*.002,.2*radius)],steel,parent=pivot)
    for spoke in range(5):
        for split in [-1,1]:
            angle=2*math.pi*spoke/5+split*.08
            # Two curved spokes per arm, with actual bevelled metal thickness.
            pts=[]
            for r,t in [(.18,0),(.42,.05*split),(.69,.08*split)]:
                pts.append((outer+sign*.0006,r*radius*math.cos(angle+t),r*radius*math.sin(angle+t)))
            tube('Split spoke '+tag,pts,.00085,alloy,pivot)
        a=2*math.pi*spoke/5
        lathe('Titanium lug '+tag,[(outer+sign*.001,.00045),(outer+sign*.0017,.00045)],chrome,center=(0,.19*radius*math.cos(a),.19*radius*math.sin(a)),parent=pivot,n=12)
    lathe('Center cap '+tag,[(outer+sign*.0008,0),(outer+sign*.0008,radius*.125),(outer+sign*.0012,radius*.125),(outer+sign*.0012,0)],black,parent=pivot,n=48)
    for row in [.4,.53]:
        for j in range(24):
            a=2*math.pi*j/24+row
            lathe('Drilled rotor '+tag,[(outer-sign*.0013,0),(outer-sign*.0013,.00035)],black,center=(0,row*radius*math.cos(a),row*radius*math.sin(a)),parent=pivot,n=8)
    block('Caliper_'+tag,(c.x+outer-sign*.0025,c.y,c.z-radius*.48),(.003,.014,.005),red)
    bpy.data.objects.remove(old,do_unlink=True)
for name,obj in base['objects'].items():
    if 'caliper' in name or 'break' in name or 'bereak' in name:bpy.data.objects.remove(obj,do_unlink=True)
manifest['wheels']=wheels
# Front lamp plates follow the actual slope of the donor's headlamp opening.
for sign in [-1,1]:
    x=sign*.0384
    points=[(x-.0088,-.0002,.120),(x+.0088,-.0002,.120),(x+.0088,.0115,.0915),(x-.0088,.0115,.0915)]
    mesh('Recessed projector housing',points,[(0,1,2,3)],black)
    tube('Headlight bezel',points,.0004,chrome,closed=True)
    normal=Vector((0,.93,.365))
    for z,r in [(.102,.0036),(.113,.0026)]:
        y=.0115+(.0915-z)*.41;c=Vector((x,y,z))+normal*.0005
        lathe('Projector bezel',[(0,r),(.0006,r),(.0008,r*.78)],chrome,normal,c,n=48)
        lathe('Projector lens',[(.00085,0),(.00085,r*.75)],glass,normal,c,n=48)
        lathe('Projector illuminator',[(.0009,0),(.0009,r*.38)],led,normal,c,n=32)
    tube('Daytime running LED',[(x-sign*.0069,.0008,.117),(x-sign*.0069,.0095,.095)],.00038,led)
    for row in range(3):
        tube('Rear LED blade',[(sign*.030,.013+row*.002,-.125),(sign*.048,.013+row*.002,-.118)],.00055,tail)
# Replace blue cylinders with machined, recessed nozzles and annular cyan rings.
for obj in list(root.children_recursive):
    if obj.name.split('.')[0].endswith(('_Housing','_Rim','_Core')):bpy.data.objects.remove(obj,do_unlink=True)
for mount in manifest['mounts']:
    marker=scene.objects.get(mount['name']);p=Vector((marker.location.x,marker.location.z,-marker.location.y))
    direction=-Vector(mount['force']).normalized();r=.0044 if 'RearMain' in mount['name'] else .0031
    lathe('Nozzle titanium lip',[(-.002,r),(-.0003,r),(.0003,r*.93),(.0003,r*.73),(-.0012,r*.62)],alloy,direction,p,n=48)
    lathe('Nozzle dark bore',[(-.0013,0),(-.0013,r*.65)],black,direction,p,n=48)
    lathe('Nozzle ignition ring',[(-.0005,r*.66),(-.0002,r*.66),(-.0002,r*.56)],cyan,direction,p,n=48)
# Resolve modifiers/curves once, join static meshes by material and each wheel into
# a separate articulated mesh. Runtime detail does not multiply draw submissions.
for obj in list(root.children_recursive):
    if obj.type not in {'MESH','CURVE'}:continue
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.object.convert(target='MESH')
for wheel in wheels:
    pivot=scene.objects.get(wheel['name']);children=[o for o in pivot.children if o.type=='MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in children:o.select_set(True)
    bpy.context.view_layer.objects.active=children[0];bpy.ops.object.join();children[0].name='WheelMesh_'+wheel['name']
# Keep body, glass and detail source meshes distinct; batch added static fittings.
static=[o for o in root.children if o.type=='MESH' and not o.name.startswith(('LP640_','Caliper_'))]
by_material={}
for obj in static:by_material.setdefault(obj.data.materials[0].name,[]).append(obj)
for key,group in by_material.items():
    bpy.ops.object.select_all(action='DESELECT')
    for obj in group:obj.select_set(True)
    bpy.context.view_layer.objects.active=group[0];bpy.ops.object.join();group[0].name='HD_Fittings_'+key
triangle_count=0
for obj in root.children_recursive:
    if obj.type=='MESH':obj.data.calc_loop_triangles();triangle_count+=len(obj.data.loop_triangles)
manifest['triangles']=triangle_count;manifest['quality']='High detail, modelled wheels, lamps and machined nozzles'
(OUT/'DonorVehicle.json').write_text(json.dumps(manifest,indent=2)+'\n')
bpy.ops.object.select_all(action='DESELECT')
for obj in [root,*root.children_recursive]:obj.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=str(OUT/'DonorVehicle.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',path_mode='RELATIVE',bake_anim=False,add_leaf_bones=False)
scene.render.filepath=str(ROOT/'Art/AstraLP640_HD.png')
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Art/AstraLP640_HD.blend'))
print(json.dumps({'triangles':triangle_count,'wheels':wheels,'materials':len(manifest['materials'])}))
