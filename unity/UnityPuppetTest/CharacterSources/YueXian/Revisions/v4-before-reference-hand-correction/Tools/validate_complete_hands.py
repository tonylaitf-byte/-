"""Inspect hand shape, connectivity, cross sections, and multiple close views."""
import bpy,bmesh,json,math,sys
from pathlib import Path
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
root=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(root/'Source/YueXian_and_Yueqin.blend'))
rig=bpy.data.objects['YueXianRig'];scene=bpy.context.scene;camera=scene.camera
saved_pose={p.name:p.matrix_basis.copy() for p in rig.pose.bones}
report={'hands':{},'passed':True}
def components(mesh):
    adj=[[] for v in mesh.vertices]
    for e in mesh.edges:
        a,b=e.vertices;adj[a].append(b);adj[b].append(a)
    pending=set(range(len(adj)));sizes=[]
    while pending:
        stack=[pending.pop()];count=0
        while stack:
            i=stack.pop();count+=1
            for j in adj[i]:
                if j in pending:pending.remove(j);stack.append(j)
        sizes.append(count)
    return sizes
def section_loops(mesh,origin,normal,offset):
    edges={};vertices=[v.co for v in mesh.vertices]
    def key(p):return tuple(round(x,5) for x in p)
    for face in mesh.polygons:
        cuts=[]
        for i,j in zip(face.vertices,list(face.vertices[1:])+[face.vertices[0]]):
            a,b=vertices[i],vertices[j];da=(a-origin).dot(normal)-offset;db=(b-origin).dot(normal)-offset
            if da*db<0:cuts.append(key(a+(b-a)*da/(da-db)))
        if len(cuts)==2 and cuts[0]!=cuts[1]:
            a,b=cuts;edges.setdefault(a,set()).add(b);edges.setdefault(b,set()).add(a)
    remaining=set(edges);count=0
    while remaining:
        stack=[remaining.pop()];count+=1
        while stack:
            for other in edges[stack.pop()]:
                if other in remaining:remaining.remove(other);stack.append(other)
    return count,all(len(v)==2 for v in edges.values())
for stem in ['Left','Right']:
    hand=bpy.data.objects[stem+'_Closed_Carved_Hand'];mesh=hand.data
    bone=rig.data.bones[stem+'Hand'];h=bone.head_local;d=(bone.tail_local-h).normalized()
    a=Vector((0,1,0));a=(a-d*a.dot(d)).normalized()
    bm=bmesh.new();bm.from_mesh(mesh)
    closed=all(e.is_manifold for e in bm.edges)
    sizes=components(mesh);four,loops=section_loops(mesh,h,d,.12817);thumb,tloop=section_loops(mesh,h,a,.07943)
    result={'vertices':len(mesh.vertices),'connected_components':len(sizes),'boundary_edges':sum(e.is_boundary for e in bm.edges),'non_manifold_edges':sum(not e.is_manifold for e in bm.edges),'four_finger_sections':four,'thumb_sections':thumb,'sections_closed':loops and tloop,'euler_characteristic':len(mesh.vertices)-len(mesh.edges)+len(mesh.polygons)}
    result['passed']=closed and len(sizes)==1 and four==4 and thumb==1 and loops and tloop and result['euler_characteristic']==2
    report['hands'][stem]=result;report['passed'] &= result['passed'];bm.free()
(root/'Previews/CompleteHandsGeometryQA.json').write_text(json.dumps(report,indent=2))
print('COMPLETE_HANDS_QA',json.dumps(report),flush=True)
if '--check-only' in sys.argv:sys.exit(0 if report['passed'] else 1)
scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.cycles.samples=24
hidden={o.name:o.hide_render for o in scene.objects}
for o in scene.objects:
    if o.type in ['MESH','LIGHT']:o.hide_render=True
lights=[]
for power,size in [(28,.35),(16,.40),(25,.25)]:
    bpy.ops.object.light_add(type='AREA');light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=size;lights.append(light)
for p in rig.pose.bones:p.matrix_basis.identity()
bpy.context.view_layer.update()
for stem in ['Left','Right']:
    hand=bpy.data.objects[stem+'_Closed_Carved_Hand'];hand.hide_render=False
    bone=rig.data.bones[stem+'Hand'];h=bone.head_local;d=(bone.tail_local-h).normalized()
    a=Vector((0,1,0));a=(a-d*a.dot(d)).normalized();n=d.cross(a).normalized()
    center=h+d*.052+a*.021
    for label,side in [('Back',1),('Palm',-1),('Edge',0)]:
        back=n*side if side else a
        right=d.cross(back).normalized()
        camera.location=center+back*.6;camera.rotation_euler=Matrix((right,d,back)).transposed().to_euler();camera.data.ortho_scale=.355
        for light,position in zip(lights,[center+back*.45+right*.2+d*.2,center+back*.25-right*.3,center-back*.3+d*.1]):
            light.location=position;light.rotation_euler=(center-position).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(root/'Previews'/f'Hand_{stem}_{label}.png');bpy.ops.render.render(write_still=True)
    hand.hide_render=True
for o in scene.objects:
    if o.name in hidden:o.hide_render=hidden[o.name]
for light in lights:light.hide_render=True
for p in rig.pose.bones:p.matrix_basis=saved_pose[p.name]
bpy.context.view_layer.update()
for stem in ['Left','Right']:
    hand=rig.pose.bones[stem+'Hand'];target=hand.head+(hand.tail-hand.head)*.8
    camera.location=target+Vector((-.06,-1.0,.12));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=.40
    scene.render.filepath=str(root/'Previews'/f'Hand_{stem}_Holding.png');bpy.ops.render.render(write_still=True)
print('COMPLETE_HANDS_VIEWS_DONE',flush=True)
