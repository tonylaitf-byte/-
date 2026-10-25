using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.Integration;
using PalmTheatre.DuetGestures;
using PalmTheatre.MusicianActions;

public static class HybridTheatreBuilder
{
    public const string SoloScene = IntegratedTheatreBuilder.Root + "/Scenes/SoloHybrid.unity";
    public const string DuetScene = IntegratedTheatreBuilder.Root + "/Scenes/DuetHybrid.unity";
    static readonly string[] Ids = { "BOW", "LANDSCAPE", "DRINK", "ALERT", "ATTACK" };
    static readonly string[] ClipNames = { "Bow", "Landscape", "Drink", "Alert", "Attack" };

    public static void Prepare()
    {
        PrepareSolo();
        PrepareDuet();
    }

    static void PrepareSolo()
    {
        var scene = EditorSceneManager.OpenScene(IntegratedTheatreBuilder.Scenes[2]);
        var mapping = UnityEngine.Object.FindAnyObjectByType<PuppetBoneTest>();
        if (!mapping) throw new InvalidOperationException("Solo mapping rig is missing.");
        RemoveAll<ActionDebugPanel>();
        RemoveAll<UnityBridge>();
        RemoveAll<GameManager>();
        RemoveAll<ActionManager>();
        RemoveAll<ActionVoicePresenter>();
        RemoveAll<PuppetManager>();
        RemoveAll<PuppetController>();
        foreach (var source in mapping.GetComponents<AudioSource>()) UnityEngine.Object.DestroyImmediate(source);
        var actor = Configure(mapping.gameObject, mapping.GetComponentInChildren<Animator>(), null,
            "俠客", -3f, 3f, SoloLines());
        actor.mapping.horizontalMove = 1.25f;
        actor.mapping.depthMove = .55f;
        actor.maximumDepth = .55f;
        CreateSession(actor, null);
        EditorSceneManager.SaveScene(scene, SoloScene);
    }

    static void PrepareDuet()
    {
        var scene = EditorSceneManager.OpenScene(IntegratedTheatreBuilder.Scenes[3]);
        var old = UnityEngine.Object.FindAnyObjectByType<DuetGestureSession>();
        if (!old) throw new InvalidOperationException("Duet gesture source is missing.");
        var left = old.leftActor;
        var right = old.rightActor;
        var leftLines = left.lines;
        var rightLines = right.lines;
        RemoveAll<DuetGestureView>();
        UnityEngine.Object.DestroyImmediate(old.gameObject);
        var hero = Configure(left.gameObject, left.animator, null, "俠客",
            left.minimumX, left.maximumX, leftLines);

        // Mapping's forward axis is the stage-facing frame, not the FBX axis.
        var frame = new GameObject("Hybrid musician stage root");
        frame.transform.SetPositionAndRotation(right.transform.position, Quaternion.Euler(0, 180, 0));
        right.transform.SetParent(frame.transform, true);
        var musician = Configure(frame, right.animator, right.music, "琴師",
            right.minimumX, right.maximumX, rightLines);
        musician.instrument = Find(right.transform, "Yueqin_Independent").gameObject;
        musician.instrument.SetActive(false);
        UnityEngine.Object.DestroyImmediate(left);
        UnityEngine.Object.DestroyImmediate(right);
        CreateSession(hero, musician);
        EditorSceneManager.SaveScene(scene, DuetScene);
    }

    static void CreateSession(HybridPuppetActor left, HybridPuppetActor right)
    {
        var session = new GameObject("Hybrid theatre session").AddComponent<HybridTheatreSession>();
        session.leftActor = left;
        session.rightActor = right;
        session.voice = session.gameObject.AddComponent<AudioSource>();
        session.voice.playOnAwake = false;
        session.backgroundMusic = UnityEngine.Object.FindAnyObjectByType<BackgroundMusicPlayer>();
        foreach (var receiver in UnityEngine.Object.FindObjectsByType<PuppetHttpReceiver>()) receiver.enabled = false;
        if (Camera.main) Camera.main.rect = new Rect(0, 0, 1, 1);
    }

    static HybridPuppetActor Configure(GameObject root, Animator animator, MusicianActionManager music,
        string role, float minimumX, float maximumX, DuetGestureLine[] lines)
    {
        if (!animator || lines == null || lines.Length != 5) throw new InvalidOperationException("Incomplete hybrid action source.");
        var actor = root.AddComponent<HybridPuppetActor>();
        actor.role = role;
        actor.animator = animator;
        actor.musician = music;
        actor.minimumX = minimumX;
        actor.maximumX = maximumX;
        var mapping = root.GetComponent<PuppetBoneTest>();
        if (!mapping) mapping = root.AddComponent<PuppetBoneTest>();
        actor.mapping = mapping;
        mapping.head = Find(animator.transform, "Head");
        mapping.spine = Find(animator.transform, "Spine");
        mapping.leftShoulder = Find(animator.transform, "LeftShoulder");
        mapping.rightShoulder = Find(animator.transform, "RightShoulder");
        mapping.leftArm = Find(animator.transform, music ? "LeftUpperArm" : "LeftArm");
        mapping.rightArm = Find(animator.transform, music ? "RightUpperArm" : "RightArm");
        mapping.leftForeArm = Find(animator.transform, music ? "LeftForearm" : "LeftForeArm");
        mapping.rightForeArm = Find(animator.transform, music ? "RightForearm" : "RightForeArm");
        mapping.leftHand = Find(animator.transform, "LeftHand");
        mapping.rightHand = Find(animator.transform, "RightHand");
        mapping.useAutomaticDemo = false;
        mapping.lockFeetToGround = true;
        mapping.horizontalMove = .95f;
        mapping.depthMove = .15f;
        var receiver = root.GetComponent<PuppetHttpReceiver>();
        if (!receiver) receiver = root.AddComponent<PuppetHttpReceiver>();
        receiver.target = mapping;
        receiver.enabled = false;
        animator.enabled = true;
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        if (music) music.automaticUpdate = false;

        Transform skeleton = Find(animator.transform, music ? "Root" : "Hips");
        Transform prop = music ? Find(animator.transform, "Yueqin_Independent") : null;
        var source = music ? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/YueXian/Mapping/Prefabs/YueXianMappedPuppet.prefab") : null;
        var rests = source ? source.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name)
            .ToDictionary(g => g.Key, g => g.First()) : new Dictionary<string, Transform>();
        actor.bones = skeleton.GetComponentsInChildren<Transform>(true)
            .Where(t => !prop || (t != prop && !t.IsChildOf(prop)))
            .Select(t => {
                Transform rest = rests.TryGetValue(t.name, out var found) ? found : t;
                return new HybridBone {
                    bone = t, path = AnimationUtility.CalculateTransformPath(t, animator.transform),
                    part = Part(t, skeleton), restRotation = rest.localRotation, restPosition = rest.localPosition
                };
            }).ToArray();
        foreach (var bone in actor.bones)
        {
            bone.bone.localRotation = bone.restRotation;
            bone.bone.localPosition = bone.restPosition;
        }
        actor.actions = Enumerable.Range(0, 5).Select(i => new HybridAction {
            actionId = Ids[i], line = lines[i], mask = music ? HybridBodyPart.UpperBody : WarriorMask(Ids[i]),
            cooldown = 3f,
            blendIn = music ? new[] { .45f, .4f, .42f, .34f, .32f }[i] : new[] { .34f, .4f, .38f, .3f, .28f }[i],
            blendOut = music ? new[] { .6f, .55f, .6f, .5f, .48f }[i] : new[] { .55f, .6f, .58f, .56f, .54f }[i],
            loopBlend = i == 4 ? .16f : .22f,
            poseSmoothing = music ? new[] { .04f, .035f, .04f, .025f, .02f }[i] : new[] { .035f, .04f, .04f, .025f, .02f }[i]
        }).ToArray();
        Audit(actor);
        return actor;
    }

    public static HybridBodyPart WarriorMask(string id)
    {
        switch (id)
        {
            case "BOW": return HybridBodyPart.UpperBody;
            case "DRINK": return HybridBodyPart.Pelvis | HybridBodyPart.Torso | HybridBodyPart.Head | HybridBodyPart.RightArm;
            case "LANDSCAPE": return HybridBodyPart.Torso | HybridBodyPart.Head | HybridBodyPart.RightArm;
            case "ALERT": return HybridBodyPart.Torso | HybridBodyPart.Head | HybridBodyPart.LeftArm;
            case "ATTACK": return HybridBodyPart.Torso | HybridBodyPart.RightArm;
            default: throw new ArgumentException("Unknown hybrid action: " + id);
        }
    }

    static HybridBodyPart Part(Transform bone, Transform skeleton)
    {
        for (Transform cursor = bone; cursor; cursor = cursor.parent)
        {
            string name = cursor.name.ToLowerInvariant();
            if (name == "leftshoulder") return HybridBodyPart.LeftArm;
            if (name == "rightshoulder") return HybridBodyPart.RightArm;
            if (name == "head" || name == "neck") return HybridBodyPart.Head;
            if (name.Contains("leg") || name.Contains("foot") || name.Contains("toe")) return HybridBodyPart.Legs;
            if (name.StartsWith("spine") || name == "chest") return HybridBodyPart.Torso;
            if (cursor == skeleton) break;
        }
        return HybridBodyPart.Pelvis;
    }

    public static void Audit(HybridPuppetActor actor)
    {
        if (actor.bones.Any(b => !b.bone || b.bone == actor.transform) ||
            actor.bones.Select(b => b.path).Distinct().Count() != actor.bones.Length)
            throw new InvalidOperationException("Invalid hybrid bone paths: " + actor.role);
        var clips = actor.musician ? actor.musician.actions.SelectMany(a => new[] { a.mainClip, a.loopClip, a.exitClip }).Where(c => c).Distinct() :
            actor.actions.Select(a => a.line.animation);
        var paths = new HashSet<string>(actor.bones.Select(b => b.path));
        foreach (var clip in clips)
        {
            if (!clip) throw new InvalidOperationException("Hybrid animation is missing.");
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.type != typeof(Transform) || !binding.propertyName.StartsWith("m_LocalRotation.") ||
                    !paths.Contains(binding.path) || !actor.animator.transform.Find(binding.path))
                    throw new InvalidOperationException("Unsupported hybrid curve: " + clip.name + ": " + binding.path + "/" + binding.propertyName);
        }
        foreach (var action in actor.actions)
            if (action.line == null || !action.line.voice || action.mask == HybridBodyPart.None)
                throw new InvalidOperationException("Hybrid action/voice/mask is missing: " + action.actionId);
    }

    static DuetGestureLine[] SoloLines()
    {
        string[] resources = { "greeting", "landscape", "drink", "alert", "sword" };
        string[] subtitles = { "久仰久仰，很高興見到你！", "明月高掛天際，今晚真是一番好景色。", "來！我們一起喝一杯！", "嗯？你想做什麼？", "劍出無情。接招！" };
        return Enumerable.Range(0, 5).Select(i => new DuetGestureLine {
            actionId = Ids[i], displayName = ClipNames[i], subtitle = subtitles[i],
            voice = Resources.Load<AudioClip>("Audio/Voice/SinglePuppetDenoised/" + resources[i]),
            animation = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/" + ClipNames[i] + ".anim")
        }).ToArray();
    }

    static Transform Find(Transform root, string name)
    {
        var result = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        if (!result) throw new InvalidOperationException("Hybrid rig bone missing: " + name);
        return result;
    }

    static void RemoveAll<T>() where T : Component
    {
        foreach (var item in UnityEngine.Object.FindObjectsByType<T>()) UnityEngine.Object.DestroyImmediate(item);
    }
}
