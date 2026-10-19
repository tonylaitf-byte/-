"""Yue Xian Gong Zi: editable skinned opera puppet and separate yueqin prop."""
import bpy,bmesh,math,os,json,sys,argparse
from mathutils import Vector,Quaternion,Matrix
ROOT=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
parser=argparse.ArgumentParser()
project_default=ROOT
bundle_project=os.path.abspath(os.path.join(ROOT,'..','..'))
if not os.path.isdir(os.path.join(ROOT,'ProjectSettings')) and os.path.isdir(os.path.join(bundle_project,'ProjectSettings')):project_default=bundle_project
parser.add_argument('--unity-project',default=project_default)
parser.add_argument('--skip-render',action='store_true')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
ASSETS=os.path.join(os.path.abspath(args.unity_project),'Assets','YueXian','Models')
os.makedirs(ASSETS,exist_ok=True)
for folder in ['Source','Previews']:os.makedirs(os.path.join(ROOT,folder),exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
def mat(name,c,metal=0,rough=.45):
    m=bpy.data.materials.new(name);m.diffuse_color=(*c,1);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*c,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
    return m
M={
 'blue':mat('YX_Silk_Azure',(.032,.165,.245),.12,.4),
 'white':mat('YX_Silk_MoonWhite',(.68,.78,.79),.04,.5),
 'ink':mat('YX_Sash_InkBlue',(.009,.035,.064),.08,.46),
 'silver':mat('YX_Embroidery_Silver',(.48,.63,.66),.7,.32),
 'gold':mat('YX_Antique_PaleGold',(.43,.32,.14),.72,.35),
 'jade':mat('YX_Jade_Pendant',(.055,.34,.25),.24,.23),
 'skin':mat('YX_Skin_Porcelain',(.63,.405,.29),0,.52),
 'hair':mat('YX_Hair_BlueBlack',(.009,.018,.030),.08,.36),
 'hairline':mat('YX_Hair_SubtleGlints',(.028,.048,.064),.13,.38),
 'eye':mat('YX_Eye_DarkInk',(.009,.021,.028),.0,.24),
 'eyewhite':mat('YX_Eye_Ivory',(.46,.49,.46),0,.36),
 'lip':mat('YX_Lips_MutedRose',(.30,.103,.092),0,.53),
 'makeup':mat('YX_EyeTail_SoftRose',(.29,.105,.095),0,.56),
 'wood':mat('YQ_Soundboard_HoneyWood',(.32,.142,.055),.05,.47),
 'rim':mat('YQ_Frame_Rosewood',(.071,.020,.012),.16,.35),
 'fret':mat('YQ_Frets_AgedBrass',(.41,.27,.10),.64,.36),
 'strings':mat('YQ_Strings_SilkSilver',(.59,.61,.51),.58,.32),
}
groups={};meshes=[]
def add(name,verts,faces,material,weights=None,prop=False,smooth=True):
    key=(name,prop)
    if key not in groups:groups[key]=[[],[],[],[],[]]
    vs,fs,ms,ws,ss=groups[key];n=len(vs);vs.extend([tuple(v) for v in verts]);fs.extend([tuple(i+n for i in f) for f in faces]);ms.extend([material]*len(faces));ss.extend([smooth]*len(faces))
    if weights is None:weights=[{'Chest':1}]*len(verts)
    elif isinstance(weights,str):weights=[{weights:1}]*len(verts)
    elif isinstance(weights,dict):weights=[weights]*len(verts)
    ws.extend(weights)

def tube(name,points,radius,material,bone='Chest',prop=False,sides=12):
    pts=[Vector(p) for p in points];v=[]
    radii=radius if isinstance(radius,(list,tuple)) else [radius]*len(pts)
    for i,p in enumerate(pts):
        axis=(pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]).normalized();u=axis.cross(Vector((0,1,0)))
        if u.length<.01:u=axis.cross(Vector((1,0,0)))
        u.normalize();w=axis.cross(u).normalized()
        for j in range(sides):v.append(p+radii[i]*(u*math.cos(j*math.tau/sides)+w*math.sin(j*math.tau/sides)))
    faces=[tuple(reversed(range(sides))),tuple((len(pts)-1)*sides+i for i in range(sides))]
    for i in range(len(pts)-1):
        for j in range(sides):a=i*sides+j;b=i*sides+(j+1)%sides;faces.append((a,b,b+sides,a+sides))
    add(name,v,faces,material,bone,prop)

def ellipse(name,center,scale,material,bone='Head',prop=False,segments=40,rings=22):
    c=Vector(center);v=[]
    for i in range(rings+1):
        phi=math.pi*(.001+.998*i/rings)
        for j in range(segments):
            a=j*math.tau/segments;v.append(c+Vector((scale[0]*math.sin(phi)*math.cos(a),scale[1]*math.sin(phi)*math.sin(a),scale[2]*math.cos(phi))))
    f=[]
    for i in range(rings):
        for j in range(segments):a=i*segments+j;b=i*segments+(j+1)%segments;f.append((a,b,b+segments,a+segments))
    f+=[tuple(reversed(range(segments))),tuple(rings*segments+j for j in range(segments))]
    add(name,v,f,material,bone,prop)

def ribbon(name,points,width,material,bone='Chest',prop=False):
    pts=[Vector(p) for p in points];v=[]
    for i,p in enumerate(pts):
        d=(pts[min(i+1,len(pts)-1)]-pts[max(i-1,0)]).normalized();side=Vector((d.z,0,-d.x)).normalized()
        v.extend([p-side*width*.5,p+side*width*.5])
    faces=[(i*2,i*2+1,i*2+3,i*2+2) for i in range(len(pts)-1)]
    add(name,v,faces,material,bone,prop)

def prism(name,poly,y,depth,material,bone='Chest',prop=False):
    n=len(poly);v=[(x,y,z) for x,z in poly]+[(x,y+depth,z) for x,z in poly]
    f=[tuple(reversed(range(n))),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    add(name,v,f,material,bone,prop,False)

def crescent(name,c,r,material,bone='Head',prop=False,depth=.003):
    poly=[]
    for i in range(51):
        a=math.radians(55+250*i/50);poly.append((c[0]+r*math.cos(a),c[2]+r*math.sin(a)))
    for i in range(51):
        a=math.radians(305-250*i/50);poly.append((c[0]+r*.31+r*.73*math.cos(a),c[2]+r*.73*math.sin(a)))
    prism(name,poly,c[1],depth,material,bone,prop)

def cloud(name,c,w,material,bone='Pelvis'):
    # A restrained ruyi-shaped curl, repeated on hems and cuffs.
    pts=[]
    for i in range(65):
        t=i/64;a=math.pi*.4+t*math.pi*2.6;r=w*(.48-.36*t)
        pts.append((c[0]+r*math.cos(a),c[1]-.001*math.sin(a),c[2]+r*.48*math.sin(a)))
    tube(name,pts,.0016,material,bone,False,8)
    tube(name,[(c[0]-w*.65,c[1],c[2]-w*.14),(c[0]-w*.40,c[1],c[2]-w*.22),(c[0]+w*.45,c[1],c[2]-w*.22)],.0014,material,bone,False,8)

