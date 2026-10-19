"""Rebuild YueXian directly from the original authored opera puppet geometry."""
from pathlib import Path
exec(compile(Path(__file__).with_name('yuexian_modeling.py').read_text(encoding='utf8'),'yuexian_modeling.py','exec'))

exec(compile(Path(__file__).with_name('yuexian_hands.py').read_text(encoding='utf8'),'yuexian_hands.py','exec'))

source=os.path.join(ROOT,'Assets','Models','SwordCharacter','sword_01_testblend_colored.fbx')
if not os.path.isfile(source):source=os.path.join(os.path.abspath(args.unity_project),'Assets','Models','SwordCharacter','sword_01_testblend_colored.fbx')
bpy.ops.import_scene.fbx(filepath=source)
original_rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
original_rig.animation_data_clear()
for p in original_rig.pose.bones:p.matrix_basis.identity()
bpy.context.view_layer.update()
body=bpy.data.objects['char1'];body.name='YueXian_OriginalPuppet_CarvedBody'
source_bones={b.name:(original_rig.matrix_world@b.head_local,original_rig.matrix_world@b.tail_local) for b in original_rig.data.bones}
original_groups={g.index:g.name for g in body.vertex_groups}
bpy.context.view_layer.objects.active=body
for mod in list(body.modifiers):
    if mod.type=='ARMATURE':bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.context.view_layer.update()
world=body.matrix_world.copy();body.data.transform(world);body.parent=None;body.matrix_world=Matrix.Identity(4)
lift=-min(v.co.z for v in body.data.vertices)
for v in body.data.vertices:v.co.z+=lift
for h,t in source_bones.values():h.z+=lift;t.z+=lift
# The source hand-end helpers point away from the carved fingers. Align the new
# hand/finger chains to the actual retained finger silhouette before skinning.
for side,stem in [(1,'Left'),(-1,'Right')]:
    h=source_bones[stem+'Hand'][0]
    direction=(Vector((side*.842,-.128,1.314+lift))-h).normalized()
    source_bones[stem+'Hand']=(h,h+direction*.20)
for o in list(bpy.context.scene.objects):
    if o!=body:bpy.data.objects.remove(o,do_unlink=True)

# Retain the original face and costume, but remove the martial headdress.
# A fitted half-up hair cap closes the scalp underneath the new crescent crown.
bm=bmesh.new();bm.from_mesh(body.data)
remove=[]
deform=bm.verts.layers.deform.active
head_indices={i for i,n in original_groups.items() if n.startswith('Head') or n.startswith('head')}
for f in bm.faces:
    c=f.calc_center_median();z=c.z-lift
    head_weight=sum(sum(w for i,w in v[deform].items() if i in head_indices) for v in f.verts)/len(f.verts)
    face=f.material_index in [3,7,9] and abs(c.x)<.099 and c.y<-.165 and z<1.582
    if z>1.59 or (head_weight>.50 and z>1.40 and not face):remove.append(f)
bmesh.ops.delete(bm,geom=remove,context='FACES');bm.to_mesh(body.data);bm.free();body.data.update()
old_materials=[p.material_index for p in body.data.polygons]
original_skin_vertices={i for p in body.data.polygons if p.material_index==3 for i in p.vertices}
# Tailor the retained sculpted sleeves behind the wrist. Cloth vertices never
# inherit hand/finger rotations. Complete hands are built separately below.
cloth_vertices={};hand_vertices={};sleeve_frames={}
for stem in ['Left','Right']:
    a,b=source_bones[stem+'ForeArm'];axis=(b-a).normalized();length=(b-a).length
    hh,ht=source_bones[stem+'Hand'];hand_axis=(ht-hh).normalized()
    sleeve_frames[stem]=(a,b,axis,length)
    candidates=[]
    for v in body.data.vertices:
        local=v.co-a;t=local.dot(axis);radial=local-axis*t
        if t<-.025 or t>length+.14 or radial.length>.26:continue
        if (stem=='Left' and v.co.x<.32) or (stem=='Right' and v.co.x>-.31):continue
        hand_local=v.co-hh;u=hand_local.dot(hand_axis)
        hand_weight=sum(g.weight for g in v.groups if original_groups[g.group]==stem+'Hand')
        is_hand=v.index in original_skin_vertices and u>-.008 and (hand_local-hand_axis*u).length<.10
        if is_hand:hand_vertices[v.index]=stem
        else:candidates.append((v.index,t,radial))
    max_t=max(t for _,t,_ in candidates)
    cuff_end=length-.075
    for index,t,radial in candidates:
        blend=max(0,min(1,(t+.005)/.15));blend=blend*blend*(3-2*blend)
        nt=t if t<.025 else .025+(t-.025)/(max_t-.025)*(cuff_end-.025)
        radius=radial.length
        scale=1-blend*.62
        if radius>0:scale=min(scale,(.105*(1-blend)+.047*blend)/radius)
        body.data.vertices[index].co=a+axis*nt+radial*scale
        cloth_vertices[index]=(stem,blend)

M['blue']=mat('YX_Ref_Azure_Brocade',(.027,.115,.175),.04,.57)
M['silver']=mat('YX_Ref_Silver_Embroidery',(.24,.34,.35),.42,.46)
M['white']=mat('YX_Ref_MoonWhite_Silk',(.54,.65,.65),.01,.64)
M['skin']=mat('YX_Ref_Carved_Porcelain',(.60,.385,.275),0,.58)
M['hair']=mat('YX_Ref_Ink_Hair',(.014,.022,.028),.02,.61)
M['gold']=mat('YX_Ref_PaleGold_Thread',(.36,.29,.17),.42,.48)
keys=list(M);body.data.materials.clear()
for k in keys:body.data.materials.append(M[k])
for poly,mi in zip(body.data.polygons,old_materials):
    center=sum((body.data.vertices[i].co for i in poly.vertices),Vector())/len(poly.vertices)
    x,y,z=center;z-=lift
    key={0:'blue',1:'silver',2:'white',3:'skin',4:'silver',5:'white',6:'ink',7:'eye',8:'gold',9:'eye',10:'ink',11:'gold',12:'silver'}.get(mi,'blue')
    if mi==2 and abs(x)<.26 and z>.59:key='hair'
    if mi in [0,1,2,8,12] and abs(x)<.27 and z>1.435:key='hair'
    if mi==0 and abs(x)<.125 and 1.05<z<1.40:key='white'
    if z>1.655 and mi not in [3,7,9]:key='silver' if mi in [1,4,8] else 'hair'
    hand_weight=sum(sum(g.weight for g in body.data.vertices[i].groups if original_groups[g.group] in ['LeftHand','RightHand']) for i in poly.vertices)/len(poly.vertices)
    if hand_weight>.60:key='skin'
    elif mi==3 and abs(x)>.27:key='white'
    if sum(i in cloth_vertices for i in poly.vertices)>len(poly.vertices)*.5 and key=='skin':key='white'
    if sum(i in hand_vertices for i in poly.vertices)>len(poly.vertices)*.5:key='skin'
    poly.material_index=keys.index(key)
    poly.use_smooth=True

# Build the same 52-bone Humanoid family around the original puppet's joints.
arm=bpy.data.armatures.new('YueXian_Humanoid');rig=bpy.data.objects.new('YueXianRig',arm);bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
bone_specs={}
def bone(name,h,t,parent=None,deform=True):
    b=arm.edit_bones.new(name);b.head=h;b.tail=t;b.use_deform=deform
    if parent:b.parent=arm.edit_bones[parent]
    bone_specs[name]={'head':list(h),'tail':list(t),'parent':parent};return b
bone('Root',(0,0,0),(0,0,.15),None,False)
bone('Pelvis',source_bones['Hips'][0],source_bones['Spine02'][0],'Root')
bone('Spine',source_bones['Spine02'][0],source_bones['Spine01'][0],'Pelvis')
bone('Chest',source_bones['Spine01'][0],source_bones['neck'][0],'Spine')
bone('Neck',*source_bones['neck'],'Chest')
bone('Head',source_bones['Head'][0],source_bones['head_end'][0],'Neck')
mapping={'Hips':'Pelvis','Spine02':'Spine','Spine01':'Chest','Spine':'Chest','neck':'Neck','Head':'Head','head_end':'Head','head_end_end':'Head','headfront':'Head','headfront_end':'Head'}
finger_names=['Index','Middle','Ring','Little']
for side,stem in [(1,'Left'),(-1,'Right')]:
    pairs=[('Shoulder','Shoulder','Chest'),('UpperArm','Arm',stem+'Shoulder'),('Forearm','ForeArm',stem+'UpperArm'),('UpperLeg','UpLeg','Pelvis'),('LowerLeg','Leg',stem+'UpperLeg'),('Foot','Foot',stem+'LowerLeg'),('Toes','ToeBase',stem+'Foot')]
    for new,old,parent in pairs:
        bone(stem+new,*source_bones[stem+old],parent);mapping[stem+old]=stem+new
    h,t=source_bones[stem+'Hand'];direction=(t-h).normalized()
    across=Vector((0,1,0));across=(across-direction*across.dot(direction)).normalized()
    bone(stem+'Hand',h,h+direction*.09,stem+'Forearm')
    mapping[stem+'Hand']=stem+'Hand';mapping[stem+'Hand_end']=stem+'Hand';mapping[stem+'ToeBase_end']=stem+'Toes'
    define_hand_digits(stem,h,direction)
bpy.ops.object.mode_set(mode='OBJECT')

weights=[]
for v in body.data.vertices:
    w={}
    for g in v.groups:
        dest=mapping.get(original_groups[g.group],'Chest');w[dest]=w.get(dest,0)+g.weight
    total=sum(w.values());w={n:x/total for n,x in w.items()} if total else {'Chest':1}
    if v.index in cloth_vertices:
        stem,blend=cloth_vertices[v.index]
        if blend>.15:w={stem+'Forearm':1}
    if v.index in hand_vertices:w={hand_vertices[v.index]+'Hand':1}
    # Keep wrist and palm weights; extend the original finger silhouettes onto
    # the closest phalanx with a short blend at the knuckle.
    for stem in ['Left','Right']:
        hand_weight=w.get(stem+'Hand',0)
        if hand_weight<.6:continue
        h=Vector(bone_specs[stem+'Hand']['head']);d=(Vector(bone_specs[stem+'Hand']['tail'])-h).normalized()
        along=(v.co-h).dot(d);blend=max(0,min(1,(along-.075)/.045))
        if blend<=0:continue
        def segment_distance(spec):
            a=Vector(spec['head']);b=Vector(spec['tail']);ab=b-a
            q=a+ab*max(0,min(1,(v.co-a).dot(ab)/ab.length_squared));return (v.co-q).length_squared
        candidates=[(n,s) for n,s in bone_specs.items() if n.startswith(stem) and any(f in n for f in finger_names+['Thumb'])]
        name=min(candidates,key=lambda p:segment_distance(p[1]))[0]
        w[stem+'Hand']=hand_weight*(1-blend);w[name]=hand_weight*blend
    weights.append(w)
body.vertex_groups.clear()
for name in bone_specs:body.vertex_groups.new(name=name)
for i,w in enumerate(weights):
    for name,value in w.items():
        if value>0:body.vertex_groups[name].add([i],value,'REPLACE')
body.parent=rig;mod=body.modifiers.new('Original_Puppet_Humanoid_Skin','ARMATURE');mod.object=rig

# A closed cranial volume backs the original carved face at every angle.
ellipse('Head_Closed_Skull',(0,-.118,1.493+lift),(.088,.094,.130),'skin','Head',segments=64,rings=40)
for side in [-1,1]:
    ellipse('Head_Closed_Ears',(side*.087,-.108,1.472+lift),(.015,.016,.029),'skin','Head',segments=24,rings=16)

# Watertight hair shell: outer and inner surfaces, single-vertex poles, sealed
# lower rim. The curved hairline reaches the forehead, temples and nape.
cap_v=[];cap_f=[];nr=28;ns=64
for inner in [False,True]:
    inset=.004 if inner else 0
    base=len(cap_v);cap_v.append((0,-.118,1.500+lift+.148-inset))
    for j in range(1,nr+1):
        for i in range(ns):
            a=math.tau*i/ns;t=(1.05+.75*(math.sin(a)+1))*j/nr
            cap_v.append(((.101-inset)*math.sin(t)*math.cos(a),-.125+(.122-inset)*math.sin(t)*math.sin(a),1.500+lift+(.148-inset)*math.cos(t)))
    for i in range(ns):cap_f.append((base,base+1+i,base+1+(i+1)%ns))
    for j in range(nr-1):
        for i in range(ns):a=base+1+j*ns+i;b=base+1+j*ns+(i+1)%ns;cap_f.append((a,b,b+ns,a+ns))
inner_base=1+nr*ns
for i in range(ns):
    a=1+(nr-1)*ns+i;b=1+(nr-1)*ns+(i+1)%ns
    cap_f.append((a,b,b+inner_base,a+inner_base))
add('Hair_Closed_HalfUp_Shell',cap_v,cap_f,'hair','Head')
for side in [-1,1]:
    tube('Hair_Joined_Temple_Locks',[(side*.080,-.17,1.566+lift),(side*.096,-.19,1.493+lift),(side*.126,-.205,1.418+lift),(side*.139,-.224,1.365+lift)],[.012,.013,.011,.004],'hair','Head',sides=10)
for k in range(9):
    x=(k-4)*.017
    tube('Hair_Closed_Back_Locks',[(x,-.058,1.575+lift),(x*1.12,-.010,1.465+lift),(x*1.2,.014,1.29+lift),(x*1.05,.003,1.15+lift)],[.015,.015,.012,.003],'hair','Head',sides=10)
for stem,(a,b,axis,length) in sleeve_frames.items():
    # A hollow sewn cuff covers the repaired edge without closing the wrist opening.
    cuff_v=[];cuff_f=[];u=axis.cross(Vector((0,0,1))).normalized();v=axis.cross(u).normalized()
    for along,radius in [(length-.095,.049),(length-.070,.046),(length-.070,.036),(length-.095,.039)]:
        for i in range(48):cuff_v.append(a+axis*along+radius*(u*math.cos(i*math.tau/48)+v*math.sin(i*math.tau/48)))
    for j in range(4):
        for i in range(48):cuff_f.append((j*48+i,j*48+(i+1)%48,((j+1)%4)*48+(i+1)%48,((j+1)%4)*48+i))
    add(stem+'_Tailored_Cuff_Rim',cuff_v,cuff_f,'white',stem+'Forearm')
crest_z=1.659+lift
ellipse('Hair_HalfUp_Knot',(0,-.065,crest_z-.035),(.041,.035,.037),'hair','Head',segments=28,rings=16)
crescent('Small_Crescent_HairCrown',(0,-.14,crest_z+.015),.048,'silver','Head',depth=.005)
ellipse('Crown_Jade_Clasp',(0,-.145,crest_z-.019),(.021,.009,.015),'jade','Head',segments=24,rings=12)
for side in [-1,1]:
    tube('Short_Hair_Ribbons',[(side*.035,-.015,crest_z-.028),(side*.071,.025,crest_z-.10),(side*.076,.03,crest_z-.17)],[.009,.009,.004],'white','Head',sides=8)
    # Quiet jade pendants replace the aggressive metal emphasis at the waist.
    x=side*.16
    tube('Jade_Pendant_Cord',[(x,-.33,1.03),(x,-.355,.94)],.0028,'ink','Pelvis')
    ellipse('Jade_Pendant',(x,-.357,.917),(.017,.007,.028),'jade','Pelvis',segments=24,rings=16)
    for k in range(6):tube('Short_Waist_Tassel',[(x+(k-2.5)*.0025,-.355,.89),(x+(k-2.5)*.003,-.353,.84)],.0015,'white','Pelvis',sides=6)

# Front-direction and contact markers make the imported reference geometry
# independent of its authoring axes, while keeping Humanoid bone names clean.
front_marker=bpy.data.objects.new('YueXian_FaceForward',None);bpy.context.collection.objects.link(front_marker)
front_marker.location=(0,-1,0);front_marker.parent=rig

exec(compile(Path(__file__).with_name('yueqin_geometry.py').read_text(encoding='utf8'),'yueqin_geometry.py','exec'))

def repair_reference_surface(character):
    # The original sleeve surface spans cloth and palm vertices. Remove those
    # bridging triangles after tailoring; the new cuff and wrist cover the seam.
    bm=bmesh.new();bm.from_mesh(body.data);bm.verts.ensure_lookup_table()
    remove=[]
    for f in bm.faces:
        hand=any(any(n.endswith('Hand') or any(k in n for k in finger_names+['Thumb']) for n,w in weights[v.index].items() if w>.5) for v in f.verts)
        if hand:remove.append(f)
    bmesh.ops.delete(bm,geom=remove,context='FACES');bm.to_mesh(body.data);bm.free();body.data.update()
    # Join the retained carved face to its cranial backing. Voxel repair seals
    # the original scan's floating fragments while preserving its sculpted form.
    selected=[]
    head_index=body.vertex_groups['Head'].index
    for p in body.data.polygons:
        c=sum((body.data.vertices[i].co for i in p.vertices),Vector())/len(p.vertices)
        hw=sum(sum(g.weight for g in body.data.vertices[i].groups if g.group==head_index) for i in p.vertices)/len(p.vertices)
        if hw>.5 and c.z>1.39+lift and abs(c.x)<.12 and c.y<-.15:selected.append(p.index)
    mesh=bpy.data.meshes.new('Retained_Carved_Face');mesh.from_pydata([v.co for v in body.data.vertices],[],[tuple(body.data.polygons[i].vertices) for i in selected]);mesh.update()
    face=bpy.data.objects.new('Retained_Carved_Face',mesh);bpy.context.collection.objects.link(face)
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS');bm.to_mesh(mesh);bm.free()
    bpy.context.view_layer.objects.active=face;face.select_set(True)
    solid=face.modifiers.new('Face_Interior','SOLIDIFY');solid.thickness=.026;solid.offset=-1;bpy.ops.object.modifier_apply(modifier=solid.name)
    bm=bmesh.new();bm.from_mesh(body.data);bm.faces.ensure_lookup_table();bmesh.ops.delete(bm,geom=[bm.faces[i] for i in selected],context='FACES');bm.to_mesh(body.data);bm.free()
    skull=bpy.data.objects['Head_Closed_Skull'];ears=bpy.data.objects['Head_Closed_Ears']
    bpy.ops.object.select_all(action='DESELECT')
    for ob in [skull,ears,face]:
        ob.modifiers.clear();ob.select_set(True)
    bpy.context.view_layer.objects.active=skull;bpy.ops.object.join();character.remove(ears)
    skull.data.remesh_voxel_size=.0018;skull.data.use_remesh_preserve_volume=True;bpy.ops.object.voxel_remesh()
    smooth=skull.modifiers.new('Close_Scan_Seams','SMOOTH');smooth.factor=.7;smooth.iterations=3;bpy.ops.object.modifier_apply(modifier=smooth.name)
    dec=skull.modifiers.new('Realtime_Head','DECIMATE');dec.ratio=.55;bpy.ops.object.modifier_apply(modifier=dec.name)
    skull.data.materials.clear();skull.data.materials.append(M['skin'])
    for p in skull.data.polygons:p.material_index=0;p.use_smooth=True
    skull.vertex_groups.clear();skull.vertex_groups.new(name='Head').add(list(range(len(skull.data.vertices))),1,'REPLACE')
    skull.parent=rig;mod=skull.modifiers.new('Humanoid_Skin','ARMATURE');mod.object=rig
    build_complete_hands(character)
    return character

tail=Path(__file__).with_name('yuexian_export_preview.py').read_text(encoding='utf8')
tail=tail.replace('character,instrument=realize()', 'character,instrument=realize()\ncharacter.insert(0,body)\nrepair_reference_surface(character)')
tail=tail.replace('[rig]+character)', '[rig,front_marker]+character)')
tail=tail.replace("distance=min(d.length,.493)", "distance=min(d.length,upper.length+fore.length-.0001)")
tail=tail.replace("targets={'Left':(.257,-.235,1.408),'Right':(-.116,-.277,1.137)}", "targets={'Left':(.245,-.435,1.424),'Right':(-.215,-.50,1.16)}")
tail=tail.replace("aim_bone('RightHand',(.90,-.12,-.35))", "aim_bone('RightHand',(.97,-.20,-.15))")
tail=tail.replace("aim_bone('LeftHand',(-.85,-.22,.40))", "aim_bone('LeftHand',(-.43,-.32,.845))")
tail=tail.replace("(.43 if stem=='Left' else -.39,-.015,1.13)", "(.47 if stem=='Left' else -.45,-.15,1.12)")
tail=tail.replace('math.radians(12 if p.name.startswith(\'Left\') else 7)', 'math.radians(6 if p.name.startswith(\'Left\') else 4)')
tail=tail.replace('prop_root.location=(.015,-.233,1.07)', 'prop_root.location=(.015,-.46,1.10)')
tail=tail.replace("'instrument_world_position':[.015,-.233,1.07]", "'instrument_world_position':[.015,-.46,1.10]")
tail=tail.replace("'character_vertices':sum", "'reference_source':source,'reference_geometry_retained':True,'reference_body_vertices':len(body.data.vertices),'character_vertices':sum")
tail=tail.replace("'head_height_metres':.304,'height_metres':1.86,'head_ratio_approx':5.9", "'proportion_source':'original authored puppet','height_metres':crest_z+.063")
tail=tail.replace("cam.data.ortho_scale=2.22", "cam.data.ortho_scale=2.32")
tail=tail.replace("aim(cam,(0,-.035,.92))", "aim(cam,(0,-.18,.96))")
tail=tail.replace("aim(cam,(0,-.10,1.39))", "aim(cam,(0,-.24,1.43))")
exec(compile(tail,'yuexian_export_preview.py','exec'))
