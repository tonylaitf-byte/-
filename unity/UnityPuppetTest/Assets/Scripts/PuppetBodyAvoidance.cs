using UnityEngine;

// Analytic proxies, independent of Physics layers and fixed-update timing.
public sealed class PuppetBodyAvoidance
{
    public struct Capsule
    {
        public Vector3 a, b;
        public float radius;
        public Capsule(Vector3 start, Vector3 end, float size) { a = start; b = end; radius = size; }
    }

    public Capsule torso, skull;
    public float span { get; private set; }
    public bool Ready { get; private set; }
    public bool HasLeftWeapon => leftWeapon != null;
    private Transform hips, leftWeapon;
    private Vector3 weaponA, weaponB;
    private float weaponRadius;

    public void Bind(PuppetBoneTest rig)
    {
        hips = null;
        leftWeapon = null;
        foreach (Transform bone in rig.GetComponentsInChildren<Transform>(true))
            if (bone.name == "Hips") hips = bone;

        // Only accept a rigid mesh actually parented to the controlled hand.
        // Never treat an unrelated body mesh or instrument as a sword.
        if (!rig.leftHand) return;
        foreach (MeshFilter filter in rig.leftHand.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.name != "Mesh1.0" && filter.name.IndexOf("sword", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!filter.sharedMesh) continue;
            Bounds bounds = filter.sharedMesh.bounds;
            Vector3 extents = bounds.extents;
            int axis = extents.x > extents.y ? 0 : 1;
            if (extents.z > extents[axis]) axis = 2;
            Vector3 along = Vector3.zero; along[axis] = extents[axis];
            weaponA = bounds.center - along;
            weaponB = bounds.center + along;
            extents[axis] = 0;
            weaponRadius = extents.magnitude;
            leftWeapon = filter.transform;
            break;
        }
    }

    public void Refresh(PuppetBoneTest rig)
    {
        Ready = rig.leftArm && rig.rightArm && rig.spine && rig.head;
        if (!Ready) return;
        span = Vector3.Distance(rig.leftArm.position, rig.rightArm.position);
        Ready = span > .0001f;
        if (!Ready) return;
        Vector3 shoulders = (rig.leftArm.position + rig.rightArm.position) * .5f;
        Vector3 bottom = hips ? hips.position : rig.spine.position;
        Vector3 up = (shoulders - bottom).normalized;
        if (up.sqrMagnitude < .5f) up = rig.transform.up;
        torso = new Capsule(bottom, shoulders - up * span * .22f,
            span * rig.bodyAvoidanceRadius);
        skull = new Capsule(rig.head.position, rig.head.position + up * span * .22f,
            span * rig.headAvoidanceRadius);
    }

    public float Penetration(Transform upper, Transform lower, Transform hand, bool left, float padding)
    {
        if (!Ready || !upper || !lower || !hand) return 0;
        float radius = span * (.045f + padding);
        // Shoulder attachment itself is intentionally allowed inside the torso.
        Vector3 start = Vector3.Lerp(upper.position, lower.position, .12f);
        float result = Mathf.Max(Overlap(start, lower.position, radius),
            Mathf.Max(Overlap(lower.position, hand.position, radius),
            Overlap(hand.position, hand.position, span * (.075f + padding))));
        if (left && leftWeapon && leftWeapon.gameObject.activeInHierarchy)
        {
            Vector3 scale = leftWeapon.lossyScale;
            float largestScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            result = Mathf.Max(result, Overlap(leftWeapon.TransformPoint(weaponA),
                leftWeapon.TransformPoint(weaponB), weaponRadius * largestScale + span * padding));
        }
        return result;
    }

    private float Overlap(Vector3 a, Vector3 b, float radius)
    {
        return Mathf.Max(0, Mathf.Max(torso.radius + radius - SegmentDistance(a, b, torso.a, torso.b),
            skull.radius + radius - SegmentDistance(a, b, skull.a, skull.b)));
    }

    public static float SegmentDistance(Vector3 p, Vector3 q, Vector3 a, Vector3 b)
    {
        Vector3 d1 = q - p, d2 = b - a, r = p - a;
        float aa = Vector3.Dot(d1, d1), ee = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float s, t;
        if (aa <= 1e-12f && ee <= 1e-12f) return r.magnitude;
        if (aa <= 1e-12f) { s = 0; t = Mathf.Clamp01(f / ee); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (ee <= 1e-12f) { t = 0; s = Mathf.Clamp01(-c / aa); }
            else
            {
                float bb = Vector3.Dot(d1, d2), denominator = aa * ee - bb * bb;
                s = denominator > 1e-12f ? Mathf.Clamp01((bb * f - c * ee) / denominator) : 0;
                t = (bb * s + f) / ee;
                if (t < 0) { t = 0; s = Mathf.Clamp01(-c / aa); }
                else if (t > 1) { t = 1; s = Mathf.Clamp01((bb - c) / aa); }
            }
        }
        return Vector3.Distance(p + d1 * s, a + d2 * t);
    }
}
