using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public static class MusicianSurfaceExport
{
    [Serializable] public class Surface { public Vector3[] vertices; public int[] triangles; }
    [Serializable] public class Pose { public Surface sleeves,hands,instrument,costume; public Vector3 rightDistal,strumPoint; }
    sealed class Builder
    {
        readonly List<Vector3> points=new List<Vector3>();
        public void Add(Vector3 a,Vector3 b,Vector3 c){points.Add(a);points.Add(b);points.Add(c);}
        public Surface Finish()=>new Surface{vertices=points.ToArray(),triangles=Enumerable.Range(0,points.Count).ToArray()};
    }
    // Exposed hand vs cloth: wrist skin intentionally continues inside the cuff.
    // The dense instrument audit separately includes the complete wrist surfaces.
    static bool Hand(string name)=>name.EndsWith("Hand")||new[]{"Thumb","Index","Middle","Ring","Little"}.Any(name.Contains);
    static bool Arm(string name)=>name.EndsWith("UpperArm")||name.EndsWith("Forearm")||Hand(name);
    static bool Weighted(BoneWeight w,Transform[] b,Func<string,bool> f)=>(w.weight0>.3f&&f(b[w.boneIndex0].name))||(w.weight1>.3f&&f(b[w.boneIndex1].name))||(w.weight2>.3f&&f(b[w.boneIndex2].name))||(w.weight3>.3f&&f(b[w.boneIndex3].name));
    public static void Capture(GameObject actor,string path)
    {
        var sleeves=new Builder();var hands=new Builder();var instrument=new Builder();
        bool wholeCostume=path.Contains("MUSIC_PLAY_")&&(path.Contains("160")||path.Contains("LOOP"));
        var costume=new Builder();
        foreach(var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            // Compensate the imported FBX scale before applying local-to-world.
            var mesh=new Mesh();renderer.BakeMesh(mesh,true);var vertices=mesh.vertices.Select(renderer.transform.TransformPoint).ToArray();var weights=renderer.sharedMesh.boneWeights;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                bool skin=renderer.sharedMaterials[sub].name.Contains("Porcelain");var triangles=mesh.GetTriangles(sub);
                Func<string,bool> predicate=skin?(Func<string,bool>)Hand:Arm;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                    if(wholeCostume&&!skin)costume.Add(vertices[a],vertices[b],vertices[c]);
                    if(Weighted(weights[a],renderer.bones,predicate)||Weighted(weights[b],renderer.bones,predicate)||Weighted(weights[c],renderer.bones,predicate))
                        (skin?hands:sleeves).Add(vertices[a],vertices[b],vertices[c]);
                }
            }
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        var prop=actor.GetComponentsInChildren<Transform>().First(t=>t.name=="Yueqin_Independent");
        foreach(var filter in prop.GetComponentsInChildren<MeshFilter>())
        {
            var v=filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();var t=filter.sharedMesh.triangles;
            for(int i=0;i<t.Length;i+=3)instrument.Add(v[t[i]],v[t[i+1]],v[t[i+2]]);
        }
        File.WriteAllText(path,JsonUtility.ToJson(new Pose{sleeves=sleeves.Finish(),hands=hands.Finish(),instrument=instrument.Finish(),costume=wholeCostume?costume.Finish():null,rightDistal=actor.GetComponentsInChildren<Transform>().First(t=>t.name=="RightIndexDistal").position,strumPoint=prop.GetComponentsInChildren<Transform>().First(t=>t.name=="Yueqin_StrumPoint").position}));
    }
}
