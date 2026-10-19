"""Connected five-digit puppet hands: explicit palm webs, thumb, tips and wrist.

Executed in the character builder's Blender namespace. The hand is authored as
one continuous surface, rather than retaining cut fragments of the source scan.
"""
hand_frames={}

def define_hand_digits(stem,h,d):
    a=Vector((0,1,0));a=(a-d*a.dot(d)).normalized();n=d.cross(a).normalized()
    frame={'origin':h,'forward':d,'across':a,'normal':n,'digits':{}}
    arm.edit_bones[stem+'Hand'].align_roll(-n)
    for finger,spread,length in [('Little',-.03525,.077),('Ring',-.01175,.099),('Middle',.01175,.108),('Index',.03525,.096)]:
        start=h+d*.088+a*spread
        points=[start+d*length*f for f in [0,.42,.73,1]]
        frame['digits'][finger]={'points':points,'length':length,'spread':spread}
    start=h+d*.044+a*.041
    direction=(d*.46+a*.888).normalized()
    points=[start+direction*f for f in [0,.028,.051,.071]]
    frame['digits']['Thumb']={'points':points,'length':.071}
    for finger,info in frame['digits'].items():
        parent=stem+'Hand'
        for j,joint in enumerate(['Proximal','Intermediate','Distal']):
            name=stem+finger+joint
            bone(name,info['points'][j],info['points'][j+1],parent).align_roll(-n)
            parent=name
    hand_frames[stem]=frame

def build_complete_hands(character):
    for stem,frame in hand_frames.items():
        h,d,a,n=[frame[k] for k in ['origin','forward','across','normal']]
        vertices=[];faces=[];weights=[];cache={}
        def point(u,v,w):return h+d*u+a*v+n*w
        def vert(p,weight):
            key=tuple(round(x,7) for x in p)
            if key in cache:return cache[key]
            index=len(vertices);vertices.append(p);weights.append(weight);cache[key]=index;return index
        def palm_weight(u):
            hand=max(0,min(1,(u+.032)/.057));hand=hand*hand*(3-2*hand)
            return {stem+'Forearm':1-hand,stem+'Hand':hand}
        def digit_weight(finger,distance):
            info=frame['digits'][finger];p=info['points']
            lengths=[0]+[(p[i]-p[0]).length for i in range(1,4)]
            names=[stem+'Hand']+[stem+finger+j for j in ['Proximal','Intermediate','Distal']]
            transitions=[0,lengths[1],lengths[2]]
            # Overlapping influence ramps preserve joint volume and keep each
            # digit on its own chain; neighboring fingers never share weights.
            for i,center in enumerate(transitions):
                width=.009 if i==0 else .006
                if distance<center-width:return {names[i]:1}
                if distance<center+width:
                    t=max(0,min(1,(distance-center+width)/(2*width)));t=t*t*(3-2*t)
                    return {names[i]:1-t,names[i+1]:t}
            return {names[-1]:1}
        rows=[(-.095,.020,.020),(-.060,.0205,.020),(-.026,.020,.018),(0,.023,.019),(.026,.036,.021),(.044,.043,.021),(.062,.047,.018),(.088,.047,.015)]
        top={};bottom={};side_mid={}
        for i,(u,width,depth) in enumerate(rows):
            for j in range(9):
                v=width*(j/4-1);z=depth*(.74+.26*(1-(j/4-1)**2))
                top[i,j]=vert(point(u,v,z),palm_weight(u));bottom[i,j]=vert(point(u,v,-z*.88),palm_weight(u))
            for side in [-1,1]:side_mid[i,side]=vert(point(u,side*width,0),palm_weight(u))
        for i in range(len(rows)-1):
            for j in range(8):
                faces.append((top[i,j],top[i+1,j],top[i+1,j+1],top[i,j+1]))
                faces.append((bottom[i,j],bottom[i,j+1],bottom[i+1,j+1],bottom[i+1,j]))
            for side,j in [(-1,0),(1,8)]:
                if side==1 and i in [4,5]:continue # Anatomical thumb opening.
                faces.append((top[i,j],side_mid[i,side],side_mid[i+1,side],top[i+1,j]))
                faces.append((side_mid[i,side],bottom[i,j],bottom[i+1,j],side_mid[i+1,side]))
        faces.append(tuple([top[0,j] for j in range(9)]+[side_mid[0,1]]+[bottom[0,j] for j in range(8,-1,-1)]+[side_mid[0,-1]]))
        def connect(outer,inner):
            for j in range(8):faces.append((outer[j],outer[(j+1)%8],inner[(j+1)%8],inner[j]))
        def finger_tube(finger,outer,center,forward,breadth,normal,rv,rn):
            length=frame['digits'][finger]['length'];previous=outer
            samples=[(0,1),(.075,1.02),(.34,.94),(.42,.97),(.50,.89),(.67,.84),(.73,.87),(.80,.79),(.93,.70),(.985,.43)]
            for fraction,radius in samples:
                distance=length*fraction
                c=center+forward*distance
                loop=[]
                for j in range(8):
                    angle=math.radians(-135+j*45)
                    p=c+breadth*(rv*radius*math.cos(angle))+normal*(rn*radius*math.sin(angle))
                    loop.append(vert(p,digit_weight(finger,distance)))
                connect(previous,loop);previous=loop
            tip=vert(center+forward*(length+.0015),{stem+finger+'Distal':1})
            for j in range(8):faces.append((previous[j],previous[(j+1)%8],tip))
        last=len(rows)-1
        for cell,finger in enumerate(['Little','Ring','Middle','Index']):
            j=cell*2;info=frame['digits'][finger]
            mid_right=vert(point(.088,(j+2)/4*.047-.047,0),{stem+'Hand':1})
            mid_left=vert(point(.088,j/4*.047-.047,0),{stem+'Hand':1})
            outer=[bottom[last,j],bottom[last,j+1],bottom[last,j+2],mid_right,top[last,j+2],top[last,j+1],top[last,j],mid_left]
            radius=.0085 if finger=='Little' else .0096
            finger_tube(finger,outer,info['points'][0],d,a,n,radius,.011 if finger!='Little' else .0096)
        outer=[bottom[4,8],bottom[5,8],bottom[6,8],side_mid[6,1],top[6,8],top[5,8],top[4,8],side_mid[4,1]]
        info=frame['digits']['Thumb'];direction=(info['points'][1]-info['points'][0]).normalized()
        # Reverse the breadth axis to match the boundary's ordered corners.
        finger_tube('Thumb',outer,info['points'][0],direction,d,n,.0135,.0135)
        mesh=bpy.data.meshes.new(stem+'_Complete_Hand_Surface');mesh.from_pydata(vertices,[],faces);mesh.update()
        ob=bpy.data.objects.new(stem+'_Closed_Carved_Hand',mesh);bpy.context.collection.objects.link(ob)
        bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
        for p in mesh.polygons:p.use_smooth=True
        mesh.materials.append(M['skin'])
        for name in {k for w in weights for k in w}:ob.vertex_groups.new(name=name)
        for i,weight in enumerate(weights):
            for name,value in weight.items():
                if value>0:ob.vertex_groups[name].add([i],value,'REPLACE')
        bpy.context.view_layer.objects.active=ob
        sub=ob.modifiers.new('Puppet_Hand_Rounded_Forms','SUBSURF');sub.levels=2
        bpy.ops.object.modifier_apply(modifier=sub.name)
        bm=bmesh.new();bm.from_mesh(ob.data);bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS');bm.to_mesh(ob.data);bm.free()
        ob.parent=rig;mod=ob.modifiers.new('Humanoid_Skin','ARMATURE');mod.object=rig
        ob['hand_topology']='One connected surface: wrist, palm, thumb and four separate fingers'
        ob['digit_count']=5
        character.append(ob)
