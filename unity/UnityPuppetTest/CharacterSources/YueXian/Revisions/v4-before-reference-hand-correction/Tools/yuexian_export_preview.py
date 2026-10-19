def realize():
    character=[];instrument=[]
    for (name,prop),(vs,fs,ms,ws,ss) in groups.items():
        mesh=bpy.data.meshes.new(name);mesh.from_pydata(vs,[],fs);mesh.update()
        ob=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(ob)
        keys=list(dict.fromkeys(ms))
        for key in keys:mesh.materials.append(M[key])
        for p,key,smooth in zip(mesh.polygons,ms,ss):p.material_index=keys.index(key);p.use_smooth=smooth
        bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free();mesh.update()
        if prop:instrument.append(ob)
        else:
            bones={b for w in ws for b,v in w.items() if v>0}
            for b in bones:ob.vertex_groups.new(name=b)
            for i,w in enumerate(ws):
                for b,value in w.items():
                    if value>0:ob.vertex_groups[b].add([i],value,'REPLACE')
            ob.parent=rig;mod=ob.modifiers.new('Humanoid_Skin','ARMATURE');mod.object=rig
            if name in ['MoonWhite_UnderRobe','Azure_OuterRobe','MoonWhite_CrossCollar'] or 'MoonWhite_Cuff' in name:
                sol=ob.modifiers.new('Silk_Thickness','SOLIDIFY');sol.thickness=.002
            character.append(ob)
    return character,instrument
character,instrument=realize()
prop_root=bpy.data.objects.new('Yueqin',None);bpy.context.collection.objects.link(prop_root)
for ob in instrument:ob.parent=prop_root
markers=[]
for name,position in [('Yueqin_FaceForward',(0,-.15,0)),('Yueqin_NeckTip',(0,0,.582)),('Yueqin_LeftGrip',(0,-.030,.390)),('Yueqin_StrumPoint',(0,-.052,.035))]:
    marker=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(marker);marker.parent=prop_root;marker.location=position;markers.append(marker)
def export(path,objects):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects:ob.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.export_scene.fbx(filepath=path,use_selection=True,object_types={'MESH','ARMATURE','EMPTY'},add_leaf_bones=False,bake_anim=False,use_mesh_modifiers=True,axis_forward='-Z',axis_up='Y',path_mode='AUTO')
export(os.path.join(ASSETS,'YueXian.fbx'),[rig]+character)
export(os.path.join(ASSETS,'Yueqin.fbx'),[prop_root]+instrument+markers)

# Author the performance pose using a two-bone analytic solve in Blender world
# coordinates, and record local rotations for exactly reproducible Unity setup.
def aim_bone(name,direction):
    p=rig.pose.bones[name];current=(p.tail-p.head).normalized();q=current.rotation_difference(Vector(direction).normalized())
    rotation=q.to_matrix().to_4x4();origin=p.head.copy();p.matrix=Matrix.Translation(origin)@rotation@Matrix.Translation(-origin)@p.matrix;bpy.context.view_layer.update()
def arm_target(stem,target,pole):
    upper=rig.pose.bones[stem+'UpperArm'];fore=rig.pose.bones[stem+'Forearm'];shoulder=upper.head.copy();target=Vector(target)
    d=target-shoulder;distance=min(d.length,.493);direction=d.normalized();a=upper.length;b=fore.length
    along=(a*a-b*b+distance*distance)/(2*distance);height=math.sqrt(max(0,a*a-along*along))
    bend=Vector(pole)-shoulder;bend=(bend-direction*bend.dot(direction)).normalized()
    elbow=shoulder+direction*along+bend*height
    aim_bone(stem+'UpperArm',elbow-shoulder);aim_bone(stem+'Forearm',target-rig.pose.bones[stem+'Forearm'].head)
    return list(elbow)
targets={'Left':(.257,-.235,1.408),'Right':(-.116,-.277,1.137)}
elbows={stem:arm_target(stem,t,(.43 if stem=='Left' else -.39,-.015,1.13)) for stem,t in targets.items()}
aim_bone('LeftHand',(-.85,-.22,.40));aim_bone('RightHand',(.90,-.12,-.35))
# Slightly curled fingers hover over the frets / string pairs.
for p in rig.pose.bones:
    if any(k in p.name for k in ['Index','Middle','Ring','Little']):
        p.rotation_mode='XYZ';p.rotation_euler.x=math.radians(12 if p.name.startswith('Left') else 7)
    if 'Thumb' in p.name:p.rotation_mode='XYZ';p.rotation_euler.x=math.radians(-15)
bpy.context.view_layer.update()
prop_root.location=(.015,-.233,1.07);prop_root.rotation_euler=(0,math.radians(23),0)
bpy.context.view_layer.update()
world_prop=prop_root.matrix_world.copy();prop_root.parent=rig;prop_root.parent_type='BONE';prop_root.parent_bone='Chest';bpy.context.view_layer.update();prop_root.matrix_world=world_prop
pose={'local_rotations':{p.name:list(p.matrix_basis.to_quaternion()) for p in rig.pose.bones},'hand_targets':targets,'elbows':elbows,'instrument_world_position':[.015,-.233,1.07],'instrument_y_rotation_degrees':23,'bone_specs':bone_specs,'head_height_metres':.304,'height_metres':1.86,'head_ratio_approx':5.9,'character_vertices':sum(len(o.data.vertices) for o in character),'instrument_separate':True,'character_meshes':len(character),'instrument_meshes':len(instrument)}
json.dump(pose,open(os.path.join(ASSETS,'YueXianPose.json'),'w'),indent=2)
json.dump(pose,open(os.path.join(ROOT,'Previews/model_report.json'),'w'),indent=2)

def aim(ob,target):ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()
# Neutral studio presentation for artist review, with a second face/hand detail.
floor_mat=mat('Studio_MoonSlate',(.019,.034,.043),.18,.46)
bpy.ops.mesh.primitive_cylinder_add(vertices=128,radius=.71,depth=.055,location=(0,0,-.035));floor=bpy.context.object;floor.name='Preview_Plinth';floor.data.materials.append(floor_mat)
bev=floor.modifiers.new('Rounded_Base','BEVEL');bev.width=.012;bev.segments=3
bpy.ops.mesh.primitive_torus_add(major_segments=100,minor_segments=8,location=(0,0,-.006),major_radius=.67,minor_radius=.0025);bpy.context.object.data.materials.append(M['silver'])
bpy.ops.object.camera_add(location=(2.0,-6.0,2.7));cam=bpy.context.object;aim(cam,(0,-.035,.92));cam.data.type='ORTHO';cam.data.ortho_scale=2.22;bpy.context.scene.camera=cam
for name,pos,power,size,color in [('MoonKey',(-2.3,-3.4,4.0),350,3.1,(.81,.9,1)),('WarmFill',(2,-2,2.5),230,2.8,(1,.86,.69)),('HairRim',(0,2,3.2),480,2,(.53,.77,1))]:
    bpy.ops.object.light_add(type='AREA',location=pos);light=bpy.context.object;light.name=name;light.data.energy=power;light.data.shape='DISK';light.data.size=size;light.data.color=color;aim(light,(0,0,1))
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=48;s.cycles.use_denoising=True;s.render.resolution_x=1400;s.render.resolution_y=1600;s.render.resolution_percentage=100
s.world=bpy.data.worlds.new('Moonlit_Studio');s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.028,.045,.065,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=.4
s.render.image_settings.file_format='PNG';s.render.filepath=os.path.join(ROOT,'Previews/YueXian.png')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT,'Source/YueXian_and_Yueqin.blend'))
bpy.ops.render.render(write_still=True)
cam.location=(.8,-4,2.01);aim(cam,(0,-.10,1.39));cam.data.ortho_scale=1.10;s.render.resolution_x=1400;s.render.resolution_y=1400;s.render.filepath=os.path.join(ROOT,'Previews/YueXian_Detail.png');bpy.ops.render.render(write_still=True)
print('YUEXIAN_MODEL_BUILD_COMPLETE')
