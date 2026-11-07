using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.Integration;

public static class GestureAnimationRefinement
{
    public static void Rebuild()
    {
        var paths = new[] { "Bow", "Landscape", "Drink", "Alert", "Attack" }
            .Select(n => "Assets/Animations/" + n + ".anim")
            .Concat(new[] { "Greeting", "SlowIntro", "SlowLoop", "Pause", "Challenge", "FastIntro", "FastLoop", "FastExit" }
                .Select(n => MusicianActionBuilder.Base + "/Animations/" + n + ".anim")).ToArray();
        var guids = paths.Select(AssetDatabase.AssetPathToGUID).ToArray();
        PuppetProjectBootstrap.RebuildGestureClips();
        MusicianActionBuilder.RebuildGestureClips();
        for (int i = 0; i < paths.Length; i++)
        {
            if (guids[i] != AssetDatabase.AssetPathToGUID(paths[i]))
                throw new InvalidOperationException("Animation reference changed: " + paths[i]);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(paths[i]);
            if (!clip || clip.length <= 0) throw new InvalidOperationException("Empty clip: " + paths[i]);
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Transform) || !binding.propertyName.StartsWith("m_LocalRotation."))
                    throw new InvalidOperationException("Unexpected transform binding: " + paths[i]);
                foreach (var key in AnimationUtility.GetEditorCurve(clip, binding).keys)
                    if (float.IsNaN(key.value) || float.IsInfinity(key.value))
                        throw new InvalidOperationException("Nonfinite animation key: " + paths[i]);
            }
        }
        var attack = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Attack.anim");
        var bindings = AnimationUtility.GetCurveBindings(attack);
        if (!bindings.Any(b => b.path.EndsWith("/LeftArm")) || bindings.Any(b => b.path.EndsWith("/RightArm")))
            throw new InvalidOperationException("Attack must animate the sword arm, not the jug arm.");
        foreach (var path in new[] { HybridTheatreBuilder.SoloScene, HybridTheatreBuilder.DuetScene })
        {
            var scene = EditorSceneManager.OpenScene(path);
            foreach (var actor in UnityEngine.Object.FindObjectsByType<HybridPuppetActor>())
            {
                if (actor.musician) continue;
                var action = actor.actions.Single(a => a.actionId == "ATTACK");
                action.mask = HybridTheatreBuilder.WarriorMask("ATTACK");
                HybridTheatreBuilder.Audit(actor);
                EditorUtility.SetDirty(actor);
            }
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("../../reports/motion-gesture-refined");
        File.WriteAllText("../../reports/motion-gesture-refined/Build.txt",
            "PASS: 13 clips; asset GUIDs preserved; finite rotation-only keys; left sword arm; two hybrid masks updated.\n" + string.Join("\n", paths));
        Debug.Log("GESTURE_ANIMATION_REFINEMENT_PASS");
    }
}
