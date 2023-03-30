using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RuntimeRagdollCreator
{
    public FullBodyBones bones;
    public float totalMass = 20f;
    public float strength = 0.0f;
    public bool flipForward;
    private Vector3 right = Vector3.right;
    private Vector3 up = Vector3.up;
    private Vector3 forward = Vector3.forward;
    private Vector3 worldRight = Vector3.right;
    private Vector3 worldUp = Vector3.up;
    private Vector3 worldForward = Vector3.forward;
    private ArrayList bones;
    private BoneInfo rootBone;


    public void SetBones(FullBodyBones _bones)
    {
        bones = _bones;
    }

    public void CreateRagdoll()
    {
        if (!ValidateCreation()) return;
        Cleanup();
        BuildCapsules();
        AddBreastColliders();
        AddHeadCollider();
        BuildBodies();
        BuildJoints();
        CalculateMass();
    }

    private string CheckConsistency()
    {
        PrepareBones();
        Hashtable hashtable = new();
        foreach (BoneInfo bone in bones)
        {
            if (!bone.anchor) continue;
            if (hashtable[(object)bone.anchor] != null)
            {
                BoneInfo boneInfo = (BoneInfo)hashtable[(object)bone.anchor];
                return $"{(object)bone.name} and {(object)boneInfo.name} may not be assigned to the same bone.";
            }

            hashtable[(object)bone.anchor] = (object)bone;
        }

        foreach (BoneInfo bone in bones)
        {
            if (bone.anchor == null)
                return $"{(object)bone.name} has not been assigned yet.\n";
        }

        return "";
    }

    private void DecomposeVector(
        out Vector3 normalCompo,
        out Vector3 tangentCompo,
        Vector3 outwardDir,
        Vector3 outwardNormal)
    {
        outwardNormal = outwardNormal.normalized;
        normalCompo = outwardNormal * Vector3.Dot(outwardDir, outwardNormal);
        tangentCompo = outwardDir - normalCompo;
    }

    private void CalculateAxes()
    {
        if (bones.head is null && bones.pelvis is null)
            up = CalculateDirectionAxis(bones.pelvis.InverseTransformPoint(bones.head.position));
        if (bones.rightElbow is null && bones.pelvis is null)
        {
            DecomposeVector(out Vector3 _, out Vector3 tangentCompo, bones.pelvis.InverseTransformPoint(bones.rightElbow.position),
                up);
            right = CalculateDirectionAxis(tangentCompo);
        }

        forward = Vector3.Cross(right, up);
        if (!flipForward)
            return;
        forward = -forward;
    }

    private bool ValidateCreation()
    {
        var errorString = CheckConsistency();
        CalculateAxes();
        if (errorString.Length != 0) Debug.LogError(errorString);
        return errorString.Length == 0;
    }

    private void PrepareBones()
    {
        if (bones.pelvis)
        {
            worldRight = bones.pelvis.TransformDirection(right);
            worldUp = bones.pelvis.TransformDirection(up);
            worldForward = bones.pelvis.TransformDirection(forward);
        }

        bones = new ArrayList();
        rootBone = new BoneInfo
        {
            name = "Pelvis",
            anchor = bones.pelvis,
            parent = null,
            density = 2.5f
        };
        bones.Add((object)rootBone);
        AddMirroredJoint("Hips", bones.leftHips, bones.rightHips, "Pelvis", worldRight, worldForward, -20f, 70f, 30f,
            typeof(CapsuleCollider), 0.3f, 1.5f);
        AddMirroredJoint("Knee", bones.leftKnee, bones.rightKnee, "Hips", worldRight, worldForward, -80f, 0.0f, 0.0f,
            typeof(CapsuleCollider), 0.25f, 1.5f);
        AddJoint("Middle Spine", bones.middleSpine, "Pelvis", worldRight, worldForward, -20f, 20f, 10f, null, 1f, 2.5f);
        AddMirroredJoint("Arm", bones.leftArm, bones.rightArm, "Middle Spine", worldUp, worldForward, -70f, 10f, 50f,
            typeof(CapsuleCollider), 0.25f, 1f);
        AddMirroredJoint("Elbow", bones.leftElbow, bones.rightElbow, "Arm", worldForward, worldUp, -90f, 0.0f, 0.0f,
            typeof(CapsuleCollider), 0.2f, 1f);
        AddJoint("Head", bones.head, "Middle Spine", worldRight, worldForward, -40f, 25f, 25f, null, 1f, 1f);
    }


    private BoneInfo FindBone(string name)
    {
        return bones.Cast<BoneInfo>().FirstOrDefault(bone => bone.name == name);
    }

    private void AddMirroredJoint(
        string name,
        Transform leftAnchor,
        Transform rightAnchor,
        string parent,
        Vector3 worldTwistAxis,
        Vector3 worldSwingAxis,
        float minLimit,
        float maxLimit,
        float swingLimit,
        System.Type colliderType,
        float radiusScale,
        float density)
    {
        AddJoint("Left " + name, leftAnchor, parent, worldTwistAxis, worldSwingAxis, minLimit, maxLimit, swingLimit,
            colliderType, radiusScale, density);
        AddJoint("Right " + name, rightAnchor, parent, worldTwistAxis, worldSwingAxis, minLimit, maxLimit, swingLimit,
            colliderType, radiusScale, density);
    }

    private void AddJoint(
        string name,
        Transform anchor,
        string parent,
        Vector3 worldTwistAxis,
        Vector3 worldSwingAxis,
        float minLimit,
        float maxLimit,
        float swingLimit,
        System.Type colliderType,
        float radiusScale,
        float density)
    {
        BoneInfo boneInfo = new()
        {
            name = name,
            anchor = anchor,
            axis = worldTwistAxis,
            normalAxis = worldSwingAxis,
            minLimit = minLimit,
            maxLimit = maxLimit,
            swingLimit = swingLimit,
            density = density,
            colliderType = colliderType,
            radiusScale = radiusScale
        };
        if (FindBone(parent) != null)
            boneInfo.parent = FindBone(parent);
        else if (name.StartsWith("Left"))
            boneInfo.parent = FindBone("Left " + parent);
        else if (name.StartsWith("Right"))
            boneInfo.parent = FindBone("Right " + parent);
        boneInfo.parent.children.Add(boneInfo);
        bones.Add(boneInfo);
    }

    private void BuildCapsules()
    {
        foreach (BoneInfo bone in bones)
        {
            if (bone.colliderType != typeof(CapsuleCollider)) continue;
            int direction;
            float distance;
            if (bone.children.Count == 1)
            {
                Vector3 position = ((BoneInfo)bone.children[0]).anchor.position;
                CalculateDirection(bone.anchor.InverseTransformPoint(position), out direction, out distance);
            }
            else
            {
                Vector3 position = bone.anchor.position - bone.parent.anchor.position + bone.anchor.position;
                CalculateDirection(bone.anchor.InverseTransformPoint(position), out direction, out distance);
                var childTransforms = bone.anchor.GetComponentsInChildren(typeof(Transform));

                if (childTransforms.Length > 1)
                {
                    Bounds bounds = new Bounds();
                    foreach (Component component in childTransforms)
                    {
                        Transform componentsInChild = (Transform)component;
                        bounds.Encapsulate(bone.anchor.InverseTransformPoint(componentsInChild.position));
                    }

                    distance = (double)distance <= 0.0 ? bounds.min[direction] : bounds.max[direction];
                }
            }

            CapsuleCollider capsuleCollider = bone.anchor.gameObject.AddComponent<CapsuleCollider>();
            capsuleCollider.direction = direction;
            Vector3 zero = Vector3.zero;
            zero[direction] = distance * 0.5f;
            capsuleCollider.center = zero;
            capsuleCollider.height = Mathf.Abs(distance);
            capsuleCollider.radius = Mathf.Abs(distance * bone.radiusScale);
        }
    }

    private void Cleanup()
    {
        foreach (BoneInfo bone in bones)
        {
            if (!(bool)(Object)bone.anchor) continue;
            foreach (Component componentsInChild in bone.anchor.GetComponentsInChildren(typeof(Joint)))
                Object.DestroyImmediate(componentsInChild);
            foreach (Component componentsInChild in bone.anchor.GetComponentsInChildren(typeof(Rigidbody)))
                Object.DestroyImmediate(componentsInChild);
            foreach (Component componentsInChild in bone.anchor.GetComponentsInChildren(typeof(Collider)))
                Object.DestroyImmediate(componentsInChild);
        }
    }

    private void BuildBodies()
    {
        foreach (BoneInfo bone in bones)
        {
            bone.anchor.gameObject.AddComponent<Rigidbody>();
            bone.anchor.GetComponent<Rigidbody>().mass = bone.density;
        }
    }

    private void BuildJoints()
    {
        foreach (BoneInfo bone in bones)
        {
            if (bone.parent != null)
            {
                CharacterJoint characterJoint = bone.anchor.gameObject.AddComponent<CharacterJoint>();
                bone.joint = characterJoint;
                characterJoint.axis = CalculateDirectionAxis(bone.anchor.InverseTransformDirection(bone.axis));
                characterJoint.swingAxis =
                    CalculateDirectionAxis(bone.anchor.InverseTransformDirection(bone.normalAxis));
                characterJoint.anchor = Vector3.zero;
                characterJoint.connectedBody = bone.parent.anchor.GetComponent<Rigidbody>();
                characterJoint.enablePreprocessing = false;
                SoftJointLimit softJointLimit = new()
                {
                    contactDistance = 0.0f,
                    limit = bone.minLimit
                };
                characterJoint.lowTwistLimit = softJointLimit;
                softJointLimit.limit = bone.maxLimit;
                characterJoint.highTwistLimit = softJointLimit;
                softJointLimit.limit = bone.swingLimit;
                characterJoint.swing1Limit = softJointLimit;
                softJointLimit.limit = 0.0f;
                characterJoint.swing2Limit = softJointLimit;
            }
        }
    }

    private void CalculateMass()
    {
        CalculateMassRecurse(rootBone);
        float num = totalMass / rootBone.summedMass;
        foreach (BoneInfo bone in bones)
            bone.anchor.GetComponent<Rigidbody>().mass *= num;
        CalculateMassRecurse(rootBone);
    }

    private static void CalculateMassRecurse(BoneInfo bone)
    {
        float mass = bone.anchor.GetComponent<Rigidbody>().mass;
        foreach (BoneInfo child in bone.children)
        {
            CalculateMassRecurse(child);
            mass += child.summedMass;
        }

        bone.summedMass = mass;
    }

       private Bounds Clip(
        Bounds bounds,
        Transform relativeTo,
        Transform clipTransform,
        bool below)
    {
        int index = LargestComponent(bounds.size);
        if ((double)Vector3.Dot(worldUp, relativeTo.TransformPoint(bounds.max)) >
            (double)Vector3.Dot(worldUp, relativeTo.TransformPoint(bounds.min)) == below)
        {
            Vector3 min = bounds.min;
            min[index] = relativeTo.InverseTransformPoint(clipTransform.position)[index];
            bounds.min = min;
        }
        else
        {
            Vector3 max = bounds.max;
            max[index] = relativeTo.InverseTransformPoint(clipTransform.position)[index];
            bounds.max = max;
        }

        return bounds;
    }

    private Bounds GetBreastBounds(Transform relativeTo)
    {
        Bounds breastBounds = new Bounds();
        breastBounds.Encapsulate(relativeTo.InverseTransformPoint(bones.leftHips.position));
        breastBounds.Encapsulate(relativeTo.InverseTransformPoint(bones.rightHips.position));
        breastBounds.Encapsulate(relativeTo.InverseTransformPoint(bones.leftArm.position));
        breastBounds.Encapsulate(relativeTo.InverseTransformPoint(bones.rightArm.position));
        Vector3 size = breastBounds.size;
        size[SmallestComponent(breastBounds.size)] = size[LargestComponent(breastBounds.size)] / 2f;
        breastBounds.size = size;
        return breastBounds;
    }

    private void AddBreastColliders()
    {
        if (bones.middleSpine is null && bones.pelvis is null)
        {
            Bounds bounds1 = Clip(GetBreastBounds(bones.pelvis), bones.pelvis, bones.middleSpine, false);
            BoxCollider boxCollider1 = bones.pelvis.gameObject.AddComponent<BoxCollider>();
            boxCollider1.center = bounds1.center;
            boxCollider1.size = bounds1.size;
            Bounds bounds2 = Clip(GetBreastBounds(bones.middleSpine), bones.middleSpine, bones.middleSpine, true);
            BoxCollider boxCollider2 = bones.middleSpine.gameObject.AddComponent<BoxCollider>();
            boxCollider2.center = bounds2.center;
            boxCollider2.size = bounds2.size;
        }
        else
        {
            Bounds bounds = new();
            bounds.Encapsulate(bones.pelvis.InverseTransformPoint(bones.leftHips.position));
            bounds.Encapsulate(bones.pelvis.InverseTransformPoint(bones.rightHips.position));
            bounds.Encapsulate(bones.pelvis.InverseTransformPoint(bones.leftArm.position));
            bounds.Encapsulate(bones.pelvis.InverseTransformPoint(bones.rightArm.position));
            Vector3 size = bounds.size;
            size[SmallestComponent(bounds.size)] = size[LargestComponent(bounds.size)] / 2f;
            BoxCollider boxCollider = bones.pelvis.gameObject.AddComponent<BoxCollider>();
            boxCollider.center = bounds.center;
            boxCollider.size = size;
        }
    }

    private void AddHeadCollider()
    {
        if (bones.head.TryGetComponent(out Collider col))
            Object.Destroy(col);
        float num = Vector3.Distance(bones.leftArm.transform.position, bones.rightArm.transform.position) / 4f;
        SphereCollider sphereCollider = bones.head.gameObject.AddComponent<SphereCollider>();
        sphereCollider.radius = num;
        Vector3 zero = Vector3.zero;
        CalculateDirection(bones.head.InverseTransformPoint(bones.pelvis.position), out int direction, out float distance);
        zero[direction] = (double)distance <= 0.0 ? num : -num;
        sphereCollider.center = zero;
    }

    private static void CalculateDirection(Vector3 point, out int direction, out float distance)
    {
        direction = 0;
        if ((double)Mathf.Abs(point[1]) > (double)Mathf.Abs(point[0]))
            direction = 1;
        if ((double)Mathf.Abs(point[2]) > (double)Mathf.Abs(point[direction]))
            direction = 2;
        distance = point[direction];
    }

    private static Vector3 CalculateDirectionAxis(Vector3 point)
    {
        CalculateDirection(point, out int direction, out float distance);
        Vector3 zero = Vector3.zero;
        zero[direction] = (double)distance <= 0.0 ? -1f : 1f;
        return zero;
    }

    private static int SmallestComponent(Vector3 point)
    {
        int index = 0;
        if ((double)Mathf.Abs(point[1]) < (double)Mathf.Abs(point[0]))
            index = 1;
        if ((double)Mathf.Abs(point[2]) < (double)Mathf.Abs(point[index]))
            index = 2;
        return index;
    }

    private static int LargestComponent(Vector3 point)
    {
        int index = 0;
        if ((double)Mathf.Abs(point[1]) > (double)Mathf.Abs(point[0]))
            index = 1;
        if ((double)Mathf.Abs(point[2]) > (double)Mathf.Abs(point[index]))
            index = 2;
        return index;
    }

    private static int SecondLargestComponent(Vector3 point)
    {
        int num1 = SmallestComponent(point);
        int num2 = LargestComponent(point);
        if (num1 < num2)
        {
            (num2, num1) = (num1, num2);
        }

        if (num1 == 0 && num2 == 1)
            return 2;
        return num1 == 0 && num2 == 2 ? 1 : 0;
    }

    private class BoneInfo
    {
        public string name;
        public Transform anchor;
        public CharacterJoint joint;
        public BoneInfo parent;
        public float minLimit;
        public float maxLimit;
        public float swingLimit;
        public Vector3 axis;
        public Vector3 normalAxis;
        public float radiusScale;
        public System.Type colliderType;
        public ArrayList children = new();
        public float density;
        public float summedMass;
    }
}

public class FullBodyBones
{
    public Transform pelvis;
    public Transform leftHips;
    public Transform leftKnee;
    public Transform leftFoot;
    public Transform rightHips;
    public Transform rightKnee;
    public Transform rightFoot;
    public Transform leftArm;
    public Transform leftElbow;
    public Transform rightArm;
    public Transform rightElbow;
    public Transform middleSpine;
    public Transform head;
}