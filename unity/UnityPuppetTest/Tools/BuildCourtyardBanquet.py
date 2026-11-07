"""Author an independent low banquet and courtyard prop set in background Blender."""
import bpy
import json
import math
import os
import random
import sys
from mathutils import Vector

root = os.path.abspath(sys.argv[sys.argv.index('--') + 1])
assets = os.path.join(root, 'Assets', 'Environment', 'CourtyardBanquet')
source = os.path.join(root, 'ArtSources', 'CourtyardBanquet')
for folder in (assets, source, os.path.join(assets, 'Textures')):
    os.makedirs(folder, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
random.seed(27)
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 1
scene.cycles.device = 'CPU'
scene.render.bake.use_clear = True
scene.render.bake.margin = 4
materials = {}

def solid(name, color, rough=.65, metal=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Roughness'].default_value = rough
    shader.inputs['Metallic'].default_value = metal
    materials[name] = mat
    return mat

def textured(name, dark, light, kind):
    mat = solid(name, light)
    tree = mat.node_tree
    nodes, links = tree.nodes, tree.links
    shader = nodes.get('Principled BSDF')
    coordinate = nodes.new('ShaderNodeTexCoord')
    noise = nodes.new('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value = 9 if kind == 'wood' else 35
    noise.inputs['Detail'].default_value = 3
    links.new(coordinate.outputs['Generated'], noise.inputs['Vector'])
    if kind in ('wood', 'reed', 'linen'):
        wave = nodes.new('ShaderNodeTexWave')
        wave.wave_type = 'BANDS'
        wave.bands_direction = 'Y'
        wave.inputs['Scale'].default_value = 5 if kind == 'wood' else 30
        wave.inputs['Distortion'].default_value = 18 if kind == 'wood' else .6
        wave.inputs['Detail Scale'].default_value = 4 if kind == 'wood' else 2
        links.new(coordinate.outputs['Generated'], wave.inputs['Vector'])
        mix = nodes.new('ShaderNodeMixRGB')
        mix.blend_type = 'MULTIPLY'
        mix.inputs[0].default_value = .55
        links.new(wave.outputs['Color'], mix.inputs[1])
        links.new(noise.outputs['Fac'], mix.inputs[2])
        value = mix.outputs[0]
    else:
        value = noise.outputs['Fac']
    ramp = nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].color = (*dark, 1)
    ramp.color_ramp.elements[1].color = (*light, 1)
    links.new(value, ramp.inputs[0])
    links.new(ramp.outputs[0], shader.inputs['Base Color'])
    bump = nodes.new('ShaderNodeBump')
    bump.inputs['Strength'].default_value = .22
    bump.inputs['Distance'].default_value = .025 if kind == 'wood' else .008
    links.new(value, bump.inputs['Height'])
    links.new(bump.outputs[0], shader.inputs['Normal'])
    # Bake procedural surfaces to portable UV textures for the Unity materials.
    bpy.ops.mesh.primitive_plane_add(size=2)
    plane = bpy.context.object
    plane.name = 'Temporary texture bake plane'
    plane.data.materials.append(mat)
    texture = nodes.new('ShaderNodeTexImage')
    nodes.active = texture
    emission = nodes.new('ShaderNodeEmission')
    links.new(ramp.outputs[0], emission.inputs['Color'])
    output = nodes.get('Material Output')
    links.new(emission.outputs[0], output.inputs['Surface'])
    for channel in ('albedo', 'normal'):
        image = bpy.data.images.new(name + '_' + channel, width=512, height=512)
        if channel == 'normal':
            image.colorspace_settings.name = 'Non-Color'
            links.new(shader.outputs[0], output.inputs['Surface'])
        texture.image = image
        bpy.ops.object.bake(type='EMIT' if channel == 'albedo' else 'NORMAL')
        image.filepath_raw = os.path.join(assets, 'Textures', name + '_' + channel + '.png')
        image.file_format = 'PNG'
        image.save()
    links.new(shader.outputs[0], output.inputs['Surface'])
    nodes.remove(emission)
    nodes.remove(texture)
    bpy.data.objects.remove(plane, do_unlink=True)
    return mat

wood = textured('BanquetWood', (.085, .037, .015), (.22, .095, .035), 'wood')
reed = textured('BanquetReed', (.20, .105, .032), (.65, .46, .19), 'reed')
ceramic = textured('BanquetCeramic', (.075, .027, .012), (.27, .11, .04), 'ceramic')
linen = textured('BanquetLinen', (.30, .23, .14), (.65, .55, .36), 'linen')
ivory = solid('BanquetIvory', (.72, .69, .57), .38)
dark = solid('BanquetDark', (.045, .025, .014))
edge = solid('BanquetBinding', (.16, .065, .023))
red = solid('BanquetFruitRed', (.48, .028, .009), .4)
orange = solid('BanquetFruitGold', (.67, .23, .015), .5)
nuts = solid('BanquetNuts', (.44, .23, .07))
green = solid('BanquetGreens', (.065, .20, .03))
food = solid('BanquetFood', (.39, .16, .035), .5)
stone = solid('BanquetStone', (.27, .285, .26), .88)
foliage = solid('BanquetPine', (.045, .14, .055), .88)
bark = solid('BanquetBark', (.15, .065, .025), .9)
glow = solid('BanquetGlow', (1, .45, .07), .7)
cover = solid('BanquetCover', (.31, .055, .025), .85)
glow.node_tree.nodes['Principled BSDF'].inputs['Emission Color'].default_value = (1, .25, .025, 1)
glow.node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = 3

def empty(name, location=(0, 0, 0), parent=None):
    obj = bpy.data.objects.new(name, None)
    scene.collection.objects.link(obj)
    obj.location = location
    if parent:
        obj.parent = parent
    return obj

def finish(obj, name, material, parent=None):
    obj.name = name
    obj.data.materials.append(material)
    if parent:
        obj.parent = parent
    return obj

def cube(name, location, size, mat, bevel=.01, parent=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = finish(bpy.context.object, name, mat, parent)
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        modifier = obj.modifiers.new('Soft worn edges', 'BEVEL')
        modifier.width = bevel
        modifier.segments = 2
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.modifiers.new('Weighted corner normals', 'WEIGHTED_NORMAL')
    return obj

def sphere(name, location, scale, mat, parent=None):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=1, location=location)
    obj = finish(bpy.context.object, name, mat, parent)
    obj.scale = scale
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj

def cylinder_between(name, start, end, radius, mat, parent=None, vertices=8):
    a, b = Vector(start), Vector(end)
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=(b-a).length, location=(a+b)/2)
    obj = finish(bpy.context.object, name, mat, parent)
    obj.rotation_euler = (b-a).to_track_quat('Z', 'Y').to_euler()
    return obj

def lathe(name, location, profile, mat, parent=None, segments=24):
    vertices = [(r*math.cos(2*math.pi*j/segments), r*math.sin(2*math.pi*j/segments), h)
                for r, h in profile for j in range(segments)]
    faces = []
    for i in range(len(profile)-1):
        for j in range(segments):
            a = i*segments+j
            b = i*segments+(j+1)%segments
            faces.append((a,b,b+segments,a+segments))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    obj.location = location
    finish(obj, name, mat, parent)
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    # Cylindrical UVs give ceramic surface variation rather than a flat color.
    uv = mesh.uv_layers.new()
    height = max(p[1] for p in profile) or 1
    for polygon in mesh.polygons:
        for li in polygon.loop_indices:
            vi = mesh.loops[li].vertex_index
            uv.data[li].uv = ((vi % segments)/segments, profile[vi//segments][1]/height)
    return obj

def torus(name, location, major, minor, mat, rotation=(0,0,0), parent=None):
    bpy.ops.mesh.primitive_torus_add(major_segments=24, minor_segments=6, location=location,
        major_radius=major, minor_radius=minor, rotation=rotation)
    return finish(bpy.context.object, name, mat, parent)

def cloth_cover(name, height, radius, parent):
    count=24
    vertices=[(0,0,height+.02)]
    for ring in (0,1):
        for j in range(count):
            a=2*math.pi*j/count
            r=radius*(.83 if ring==0 else 1)
            h=height+.015 if ring==0 else height-.04+.009*math.sin(j*math.pi/3)
            vertices.append((r*math.cos(a),r*math.sin(a),h))
    faces=[(0,1+j,1+(j+1)%count) for j in range(count)]
    faces.extend((1+j,1+(j+1)%count,1+count+(j+1)%count,1+count+j) for j in range(count))
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(vertices,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);scene.collection.objects.link(obj)
    finish(obj,name,cover,parent)

banquet = empty('Courtyard Banquet Assembly')
table = empty('Low Table', parent=banquet)
# Double the previous .4375 m tabletop height, keeping the feet on the mat.
TABLE_RAISE = .4975
for i in range(5):
    cube('Table top plank %02d' % i, (0, -.52+i*.26, .34+TABLE_RAISE), (2.16, .256, .075), wood, .012, table)
for x in (-.96,.96):
    cube('Table rim', (x,0,.31+TABLE_RAISE), (.055,1.35,.12), wood, .008, table)
for y in (-.60,.60):
    cube('Carved apron rail', (0,y,.24+TABLE_RAISE), (1.95,.055,.13), wood, .012, table)
    cube('Apron inset', (0,y*1.02,.24+TABLE_RAISE), (1.7,.015,.045), edge, .007, table)
for x in (-.88,.88):
    for y in (-.48,.48):
        cube('Short table foot', (x,y,.15+TABLE_RAISE/2), (.14,.14,.28+TABLE_RAISE), wood, .015, table)
for x in (-.88,.88):
    cube('Side stretcher', (x,0,.10+TABLE_RAISE/2), (.07,1.02,.055), wood, .006, table)

def mat(name, x, y, width, depth, base, raised=False):
    group = empty(name, (x,y,base), banquet)
    thickness = .045 if raised else .018
    cube(name+' woven body', (0,0,thickness/2), (width,depth,thickness), reed, .012, group)
    for xx in (-width/2,width/2):
        cube(name+' binding', (xx,0,thickness*.65), (.045,depth,.025), edge, .008, group)
    for yy in (-depth/2,depth/2):
        cube(name+' binding', (0,yy,thickness*.65), (width,.04,.025), edge, .006, group)
    # Low polygon ridges catch grazing light; baked texture supplies fine weave.
    for i in range(int(depth/.025)):
        yy=-depth/2+.022+i*.025
        cylinder_between('Reed course', (-width/2+.035,yy,thickness), (width/2-.035,yy,thickness), .005, reed, group, 6)
    return group

mat('Large Banquet Floor Mat',0,0,4.25,1.8,.0)
mat('Left Kneeling Mat',-1.6,0,.77,1.25,.026,True)
mat('Right Kneeling Mat',1.6,0,.77,1.25,.026,True)

top=.385+TABLE_RAISE
def plate(name,x,y,radius=.19):
    return lathe(name,(x,y,top),[(0,0),(.07,.0),(radius,.018),(radius,.045),(radius-.018,.045),(.08,.018),(0,.018)],ivory,banquet)
def cup(name,x,y):
    group=empty(name,(x,y,top),banquet)
    lathe('Cup bowl',(0,0,0),[(0,0),(.04,0),(.057,.065),(.047,.066),(.03,.015),(0,.015)],ivory,group)
    lathe('Dark wine',(0,0,.045),[(0,0),(.043,0)],dark,group)
    return group
for i,(x,y) in enumerate([(-.86,-.36),(.86,-.35),(-.82,.43),(.84,.4)]): cup('Wine Cup %d'%i,x,y)

ewer=empty('Wine Ewer',(-.65,.28,top),banquet)
lathe('Wine Ewer body',(0,0,0),[(0,0),(.075,0),(.11,.045),(.12,.13),(.075,.23),(.04,.25),(.035,.32),(.047,.325),(.047,.34),(.028,.34),(.028,.28)],ceramic,ewer)
lathe('Ewer lid',(0,0,.34),[(0,0),(.052,0),(.04,.015),(0,.02)],ceramic,ewer)
sphere('Lid knob',(0,0,.37),(.018,.018,.018),edge,ewer)
cylinder_between('Ewer tapered spout',(.07,0,.16),(.20,0,.28),.028,ceramic,ewer,12)
torus('Ewer loop handle',(-.105,0,.22),.075,.018,ceramic,(math.pi/2,0,0),ewer)
jar=empty('Table Wine Jar',(.64,.28,top),banquet)
lathe('Wine jar body',(0,0,0),[(0,0),(.08,0),(.12,.08),(.11,.16),(.055,.21),(.055,.26),(.065,.26),(.065,.275),(.035,.275),(.035,.23)],ceramic,jar)
cloth_cover('Wine jar cloth seal',.282,.087,jar)
torus('Wine jar neck binding',(0,0,.245),.06,.006,edge,parent=jar)

for name,x,y,kind in [('Fruit Dish',0,.31,'fruit'),('Roast Dish',-.43,-.11,'roast'),('Nut Dish',.12,-.36,'nuts'),('Green Dish',.51,-.13,'green'),('Small Side Dish',-.05,-.02,'nuts')]:
    radius=.18 if name!='Small Side Dish' else .115
    plate(name,x,y,radius)
    count=7 if kind in ('fruit','roast') else 14
    for i in range(count):
        angle=i*2.399
        rr=radius*.65*math.sqrt((i+1)/count)
        px,py=x+math.cos(angle)*rr,y+math.sin(angle)*rr
        if kind=='fruit':
            sphere('Fruit',(px,py,top+.07+random.random()*.02),(.046,.043,.045),red if i%2 else orange,banquet)
        elif kind=='roast':
            piece=cube('Sliced roast',(px,py,top+.055),(.09,.055,.04),food,.012,banquet)
            piece.rotation_euler.z=angle
        elif kind=='nuts': sphere('Nuts',(px,py,top+.045),(.02,.014,.014),nuts,banquet)
        else: sphere('Leaf vegetables',(px,py,top+.05),(.04,.022,.018),green,banquet)
# Small soft-folded cloth, kept on the tabletop rather than an intersecting drape.
for i in range(4):
    cube('Folded linen cloth',(-.72,-.40+i*.035,top+.006+i*.002),(.22,.04,.012),linen,.006,banquet)

def storage_jar(name, location, scale):
    group=empty(name,location,banquet)
    group.scale=(scale,scale,scale)
    lathe('Storage jar vessel',(0,0,0),[(0,0),(.14,0),(.22,.10),(.24,.27),(.19,.40),(.12,.46),(.12,.49),(.10,.49),(.10,.45)],ceramic,group)
    cloth_cover('Cloth tied lid',.5,.18,group)
    torus('Tied rope',(0,0,.465),.133,.009,edge,parent=group)
    return group
tray=empty('Wine Jar Storage Tray',(-3.0,-.02,.03),banquet)
cube('Wood carrying base',(0,0,0),(.95,.76,.07),wood,.015,tray)
for y in (-.36,.36):cube('Tray lip',(0,y,.07),(.98,.035,.12),wood,.006,tray)
storage_jar('Large Floor Wine Jar',(-3.15,.16,.08),1.05)
storage_jar('Medium Floor Wine Jar',(-2.76,-.12,.08),.8)
storage_jar('Small Floor Wine Jar',(-3.4,-.24,.08),.65)

for x in (-2.45,2.45):
    lamp=empty('Low Courtyard Lantern '+str(x),(x,-.84,.025),banquet)
    cube('Lantern plinth',(0,0,.04),(.39,.36,.08),stone,.018,lamp)
    cube('Lantern base',(0,0,.13),(.30,.28,.10),stone,.01,lamp)
    cube('Lantern glowing insert',(0,0,.29),(.18,.17,.22),glow,.005,lamp)
    for xx in (-.12,.12):
        for yy in (-.11,.11):cube('Lantern stone post',(xx,yy,.30),(.04,.04,.27),stone,.007,lamp)
    cube('Lantern roof',(0,0,.46),(.42,.38,.07),stone,.018,lamp)
    lathe('Lantern roof finial',(0,0,.50),[(0,0),(.045,0),(.025,.04),(0,.07)],stone,lamp)

pine=empty('Pine In Stone Planter',(3.1,.08,0),banquet)
cube('Stone planter',(0,0,.15),(.85,.61,.26),stone,.06,pine)
cube('Planter dark soil',(0,0,.29),(.73,.49,.022),dark,.03,pine)
points=[(0,0,.29),(-.06,0,.48),(.04,.01,.65),(.14,.015,.83),(.10,.02,1.02)]
for i in range(len(points)-1):cylinder_between('Twisted pine trunk',points[i],points[i+1],.06-i*.01,bark,pine,10)
for i,(x,y,z,spread) in enumerate([(-.32,0,.61,.36),(.37,.06,.76,.34),(-.17,-.05,.86,.33),(.18,.0,1.02,.29)]):
    cylinder_between('Pine branch',(.04,0,z-.14),(x,y,z),.025,bark,pine)
    for j in range(5):
        angle=j*2.4
        cx=x+math.cos(angle)*spread*.45
        cy=y+math.sin(angle)*spread*.35
        sphere('Pine foliage core',(cx,cy,z), (spread*.27,spread*.22,.032),foliage,pine)
        vertices=[];faces=[]
        for tuft in range(16):
            a=tuft*2.399
            radius=spread*.55*math.sqrt((tuft+1)/16)
            origin=Vector((cx+math.cos(a)*radius,cy+math.sin(a)*radius,z+random.uniform(-.025,.025)))
            for needle in range(7):
                na=needle*2*math.pi/7+a
                tip=origin+Vector((math.cos(na)*.065,math.sin(na)*.065,random.uniform(.01,.055)))
                side=Vector((-math.sin(na)*.004,math.cos(na)*.004,0))
                index=len(vertices)
                vertices.extend([origin-side,origin+side,tip])
                faces.append((index,index+1,index+2))
        mesh=bpy.data.meshes.new('Pine needle sprays');mesh.from_pydata(vertices,[],faces);mesh.update()
        obj=bpy.data.objects.new('Pine needle sprays',mesh);scene.collection.objects.link(obj)
        finish(obj,'Pine needle sprays',foliage,pine)

# Preserve all independent prop transforms in FBX; apply edge modifiers first.
bpy.ops.object.select_all(action='SELECT')
for obj in list(scene.objects):
    if obj.type=='MESH':
        bpy.context.view_layer.objects.active=obj
        for modifier in list(obj.modifiers):
            bpy.ops.object.modifier_apply(modifier=modifier.name)

def join_meshes(objects, name):
    if len(objects)<2:
        return
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.join()
    bpy.context.object.name=name

# Keep vessels/cups independently addressable, but combine static detail within
# each prop. Individual reed courses and foliage pads must not become draw calls.
for group in [o for o in list(scene.objects) if o.type=='EMPTY' and o!=banquet]:
    join_meshes([o for o in list(group.children) if o.type=='MESH'],group.name+' Mesh')
for material in (red,orange,nuts,food,green,linen):
    join_meshes([o for o in list(banquet.children) if o.type=='MESH' and
                 len(o.data.materials)==1 and o.data.materials[0]==material],material.name+' Tabletop Details')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(source,'courtyard_banquet_v1.blend'))
# Flatten only the export copy. Blender's experimental space baking can multiply
# nested empty unit conversions; the editable blend keeps its logical hierarchy.
for obj in [o for o in scene.objects if o.type=='MESH']:
    matrix=obj.matrix_world.copy()
    obj.parent=None
    obj.matrix_world=matrix
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active=obj
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
bpy.ops.export_scene.fbx(filepath=os.path.join(assets,'courtyard_banquet_v1.fbx'),
    use_selection=False, object_types={'MESH'}, axis_forward='-Z',axis_up='Y',
    apply_unit_scale=True,bake_space_transform=False,add_leaf_bones=False,
    bake_anim=False,path_mode='STRIP',mesh_smooth_type='FACE')
meshes=[o for o in scene.objects if o.type=='MESH']
triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes)
manifest={'generator':'BuildCourtyardBanquet.py','objects':len(meshes),'triangles':triangles,
    'materials':[{'name':n,'color':list(m.diffuse_color),'roughness':m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value,
                  'metallic':m.node_tree.nodes['Principled BSDF'].inputs['Metallic'].default_value}
                 for n,m in materials.items()],
    'source':'Locally authored Blender geometry and baked procedural textures; no third-party meshes.',
    'scope':'Static scene props only. No character animation or control changes.'}
with open(os.path.join(assets,'banquet_manifest.json'),'w',encoding='utf-8') as f:json.dump(manifest,f,indent=2)
print('BANQUET_BUILD_PASS',len(meshes),'meshes',triangles,'triangles')
