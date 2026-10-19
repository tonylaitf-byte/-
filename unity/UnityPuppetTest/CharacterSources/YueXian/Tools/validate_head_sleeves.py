"""Geometry checks and alternate camera views for the authored performance pose."""
import bpy,bmesh,json,math,sys
from pathlib import Path
from mathutils import Vector,Quaternion
from mathutils.bvhtree import BVHTree
root=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(root/'Source/YueXian_and_Yueqin.blend'))
rig=bpy.data.objects['YueXianRig'];body=bpy.data.objects['YueXian_OriginalPuppet_CarvedBody']
report={'closed_meshes':{},'poses':[],'missing_meshes':[]}
for name in ['Head_Closed_Skull','Hair_Closed_HalfUp_Shell','Left_Closed_Carved_Hand','Right_Closed_Carved_Hand']:
    ob=bpy.data.objects.get(name)
    if not ob:report['missing_meshes'].append(name);continue
    bm=bmesh.new();bm.from_mesh(ob.data)
    report['closed_meshes'][name]={'boundary_edges':sum(e.is_boundary for e in bm.edges),'non_manifold_edges':sum(not e.is_manifold for e in bm.edges)}
    bm.free()
def tree(ob,polys=None):
    ev=ob.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh()
    vertices=[ev.matrix_world@v.co for v in me.vertices]
    faces=[tuple(p.vertices) for p in me.polygons if polys is None or p.index in polys]
    result=BVHTree.FromPolygons(vertices,faces,all_triangles=False)
    ev.to_mesh_clear();return result
groups={g.index:g.name for g in body.vertex_groups}
cloth=[];hands=[]
for p in body.data.polygons:
    names={groups[g.group] for i in p.vertices for g in body.data.vertices[i].groups if g.weight>.5}
    skin='Porcelain' in body.data.materials[p.material_index].name
    if not skin and any('Forearm' in n or 'Hand' in n or any(f in n for f in ['Index','Middle','Ring','Little','Thumb']) for n in names):cloth.append(p.index)
    if skin and any(any(f in n for f in ['Index','Middle','Ring','Little','Thumb']) for n in names):hands.append(p.index)
prop=[o for o in bpy.data.objects if o.type=='MESH' and o.parent and o.parent.name=='Yueqin']
rims=[o for o in bpy.data.objects if o.type=='MESH' and 'Tailored_Cuff' in o.name]
closed_hands=[o for o in bpy.data.objects if o.type=='MESH' and 'Closed_Carved_Hand' in o.name]
visible_hand_faces={}
for ob in closed_hands:
    names={g.index:g.name for g in ob.vertex_groups}
    # The continuous new hand includes the forearm hidden inside the sleeve.
    # Check exposed wrist/palm/digits; covered Forearm-only skin is a garment join.
    visible_hand_faces[ob.name]={p.index for p in ob.data.polygons if any(any(g.weight>.3 and (names[g.group].endswith('Hand') or any(d in names[g.group] for d in ['Index','Middle','Ring','Little','Thumb'])) for g in ob.data.vertices[i].groups) for i in p.vertices)}
original=rig.pose.bones['RightHand'].matrix_basis.copy()
for degrees in [-4.5,0,4.5]:
    rig.pose.bones['RightHand'].matrix_basis=original@Quaternion((0,0,1),math.radians(degrees)).to_matrix().to_4x4()
    bpy.context.view_layer.update();ct=tree(body,set(cloth));hand_trees=[tree(o,visible_hand_faces[o.name]) for o in closed_hands]
    if hands:hand_trees.append(tree(body,set(hands)))
    cloth_trees=[ct]+[tree(o) for o in rims]
    intersections={o.name:sum(len(c.overlap(tree(o))) for c in cloth_trees) for o in prop}
    if degrees==0:
        hit_ids={cloth[a] for o in prop for a,b in ct.overlap(tree(o))}
        hits=[]
        for i in sorted(hit_ids):
            p=body.data.polygons[i]
            hits.append({'face':i,'material':body.data.materials[p.material_index].name,'vertices':[{'co':list(body.data.vertices[j].co),'weights':{groups[g.group]:g.weight for g in body.data.vertices[j].groups}} for j in p.vertices]})
        (root/'Previews/SleeveIntersectionDetails.json').write_text(json.dumps(hits,indent=2))
    report['poses'].append({'right_hand_degrees':degrees,'sleeve_instrument_surface_intersections':intersections,'sleeve_finger_surface_intersections':sum(len(c.overlap(h)) for c in cloth_trees for h in hand_trees),'covered_forearm_join_intersections':sum(len(c.overlap(tree(o,set(range(len(o.data.polygons)))-visible_hand_faces[o.name]))) for c in cloth_trees for o in closed_hands)})
rig.pose.bones['RightHand'].matrix_basis=original;bpy.context.view_layer.update()
report['passed']=not report['missing_meshes'] and all(v['non_manifold_edges']==0 for v in report['closed_meshes'].values()) and all(not any(p['sleeve_instrument_surface_intersections'].values()) and p['sleeve_finger_surface_intersections']==0 for p in report['poses'])
(root/'Previews/HeadSleeveGeometryQA.json').write_text(json.dumps(report,indent=2))
print('HEAD_SLEEVE_QA',json.dumps(report),flush=True)
if '--check-only' in sys.argv:sys.exit(0)
s=bpy.context.scene;s.render.resolution_x=950;s.render.resolution_y=950;s.cycles.samples=20
cam=s.camera
for name,position,target,scale in [
    ('Head_Front',(0,-4,1.61),(0,-.12,1.52),.52),
    ('Head_Side',(-3,-.9,1.65),(0,-.10,1.50),.56),
    ('Head_Back',(1,4,1.8),(0,-.05,1.49),.65),
    ('Sleeve_Side',(-3,-1,1.75),(0,-.28,1.30),1.05)]:
    cam.location=position;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    s.render.filepath=str(root/'Previews'/f'{name}.png');bpy.ops.render.render(write_still=True)
print('HEAD_SLEEVE_QA_COMPLETE',flush=True)
