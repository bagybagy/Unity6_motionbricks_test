using System;
using System.Collections.Generic;
using UnityEngine;

namespace MotionBricks.Unity
{
    /// <summary>
    /// Builds a lightweight visible approximation of the official G1 29-DoF MJCF hierarchy.
    /// Attach this component to an empty GameObject with a MotionBricksRigDriver.
    /// </summary>
    [AddComponentMenu("MotionBricks/G1 Demo Rig Builder")]
    [DisallowMultipleComponent]
    public sealed class G1DemoRigBuilder : MonoBehaviour
    {
        public const int JointCount = 29;

        // Names are intentionally identical to motionbricks/assets/skeletons/g1/g1_29dof.xml.
        public static readonly string[] JointNames =
        {
            "left_hip_pitch_joint", "left_hip_roll_joint", "left_hip_yaw_joint", "left_knee_joint", "left_ankle_pitch_joint", "left_ankle_roll_joint",
            "right_hip_pitch_joint", "right_hip_roll_joint", "right_hip_yaw_joint", "right_knee_joint", "right_ankle_pitch_joint", "right_ankle_roll_joint",
            "waist_yaw_joint", "waist_roll_joint", "waist_pitch_joint",
            "left_shoulder_pitch_joint", "left_shoulder_roll_joint", "left_shoulder_yaw_joint", "left_elbow_joint", "left_wrist_roll_joint", "left_wrist_pitch_joint", "left_wrist_yaw_joint",
            "right_shoulder_pitch_joint", "right_shoulder_roll_joint", "right_shoulder_yaw_joint", "right_elbow_joint", "right_wrist_roll_joint", "right_wrist_pitch_joint", "right_wrist_yaw_joint",
        };

        [SerializeField] private MotionBricksRigDriver rigDriver;
        [SerializeField] private bool buildOnAwake = true;
        [SerializeField, Min(0.1f)] private float scale = 1.35f;
        [SerializeField] private Color bodyColor = new(0.16f, 0.2f, 0.24f, 1f);
        [SerializeField] private Color jointColor = new(0.72f, 0.76f, 0.8f, 1f);

        private Transform generatedRoot;
        private readonly Dictionary<string, Transform> generatedJoints = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, Transform> GeneratedJoints => generatedJoints;

        private void Awake()
        {
            if (buildOnAwake)
                Build();
        }

        [ContextMenu("Build G1 29-DoF Demo Rig")]
        public void Build()
        {
            ClearGeneratedRig();
            rigDriver ??= GetComponent<MotionBricksRigDriver>();
            rigDriver ??= gameObject.AddComponent<MotionBricksRigDriver>();

            generatedRoot = new GameObject("G1_29DOF_Demo").transform;
            generatedRoot.SetParent(transform, false);
            AddBody(generatedRoot, PrimitiveType.Capsule, Vector3.zero, new Vector3(.22f, .42f, .18f), bodyColor);

            var pelvis = generatedRoot;
            var leftLeg = BuildLeg(pelvis, true);
            var rightLeg = BuildLeg(pelvis, false);
            var torso = BuildTorso(pelvis);
            BuildArm(torso, true);
            BuildArm(torso, false);

            // Keep these roots alive in the generated hierarchy; their names aid inspector debugging.
            _ = leftLeg;
            _ = rightLeg;
            rigDriver.SetBindings(CreateBindings());
        }

        [ContextMenu("Clear G1 29-DoF Demo Rig")]
        public void ClearGeneratedRig()
        {
            generatedJoints.Clear();
            if (generatedRoot == null)
                generatedRoot = transform.Find("G1_29DOF_Demo");
            if (generatedRoot == null)
                return;
            if (Application.isPlaying)
                Destroy(generatedRoot.gameObject);
            else
                DestroyImmediate(generatedRoot.gameObject);
            generatedRoot = null;
        }

        public Transform FindJoint(string jointName)
        {
            return generatedJoints.TryGetValue(jointName, out var joint) ? joint : null;
        }

        private IEnumerable<MotionBricksRigDriver.JointBinding> CreateBindings()
        {
            foreach (var name in JointNames)
            {
                if (generatedJoints.TryGetValue(name, out var joint))
                    yield return new MotionBricksRigDriver.JointBinding { jointName = name, transform = joint };
            }
        }

        private Transform BuildLeg(Transform pelvis, bool left)
        {
            var side = left ? 1f : -1f;
            var prefix = left ? "left" : "right";
            var hipPitch = AddJoint(pelvis, $"{prefix}_hip_pitch_joint", M(0, side * .064452f, -.1027f), .11f);
            var hipRoll = AddJoint(hipPitch, $"{prefix}_hip_roll_joint", M(0, side * .052f, -.030465f), .10f);
            var hipYaw = AddJoint(hipRoll, $"{prefix}_hip_yaw_joint", M(.025001f, 0, -.12412f), .17f);
            var knee = AddJoint(hipYaw, $"{prefix}_knee_joint", M(-.078273f, side * .0021489f, -.17734f), .29f);
            var anklePitch = AddJoint(knee, $"{prefix}_ankle_pitch_joint", M(0, side * .000094445f, -.30001f), .20f);
            var ankleRoll = AddJoint(anklePitch, $"{prefix}_ankle_roll_joint", M(0, 0, -.017558f), .12f);
            AddBody(ankleRoll, PrimitiveType.Cube, M(.06f, 0, -.03f), new Vector3(.24f, .08f, .11f), bodyColor);
            return hipPitch;
        }

        private Transform BuildTorso(Transform pelvis)
        {
            var yaw = AddJoint(pelvis, "waist_yaw_joint", Vector3.zero, .10f);
            var roll = AddJoint(yaw, "waist_roll_joint", M(-.0039635f, 0, .035f), .08f);
            var pitch = AddJoint(roll, "waist_pitch_joint", M(0, 0, .019f), .30f);
            AddBody(pitch, PrimitiveType.Cube, M(.0f, 0, .16f), new Vector3(.30f, .25f, .38f), bodyColor);
            AddBody(pitch, PrimitiveType.Sphere, M(.02f, 0, .40f), new Vector3(.18f, .18f, .18f), jointColor);
            return pitch;
        }

        private void BuildArm(Transform torso, bool left)
        {
            var side = left ? 1f : -1f;
            var prefix = left ? "left" : "right";
            var shoulderPitch = AddJoint(torso, $"{prefix}_shoulder_pitch_joint", M(.0039563f, side * .10022f, .23778f), .12f);
            var shoulderRoll = AddJoint(shoulderPitch, $"{prefix}_shoulder_roll_joint", M(0, side * .038f, -.013831f), .12f);
            var shoulderYaw = AddJoint(shoulderRoll, $"{prefix}_shoulder_yaw_joint", M(0, side * .00624f, -.1032f), .15f);
            var elbow = AddJoint(shoulderYaw, $"{prefix}_elbow_joint", M(.015783f, 0, -.080518f), .20f);
            var wristRoll = AddJoint(elbow, $"{prefix}_wrist_roll_joint", M(.1f, side * .00188791f, -.01f), .11f);
            var wristPitch = AddJoint(wristRoll, $"{prefix}_wrist_pitch_joint", M(.038f, 0, 0), .08f);
            var wristYaw = AddJoint(wristPitch, $"{prefix}_wrist_yaw_joint", M(.046f, 0, 0), .07f);
            AddBody(wristYaw, PrimitiveType.Sphere, M(.07f, 0, 0), new Vector3(.10f, .075f, .075f), jointColor);
        }

        private Transform AddJoint(Transform parent, string jointName, Vector3 localPosition, float radius)
        {
            var joint = new GameObject(jointName).transform;
            joint.SetParent(parent, false);
            joint.localPosition = localPosition * scale;
            generatedJoints.Add(jointName, joint);
            AddBody(joint, PrimitiveType.Sphere, Vector3.zero, Vector3.one * radius, jointColor);
            AddConnector(parent, joint.localPosition, radius * .48f);
            return joint;
        }

        private void AddConnector(Transform parent, Vector3 localEnd, float thickness)
        {
            if (localEnd.sqrMagnitude < .000001f)
                return;
            var connector = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            connector.name = "link";
            connector.transform.SetParent(parent, false);
            connector.transform.localPosition = localEnd * .5f;
            connector.transform.localRotation = Quaternion.FromToRotation(Vector3.up, localEnd.normalized);
            connector.transform.localScale = new Vector3(thickness, localEnd.magnitude * .5f, thickness);
            Tint(connector, bodyColor);
        }

        private void AddBody(Transform parent, PrimitiveType primitive, Vector3 localPosition, Vector3 localScale, Color color)
        {
            var body = GameObject.CreatePrimitive(primitive);
            body.name = "visual";
            body.transform.SetParent(parent, false);
            body.transform.localPosition = localPosition * scale;
            body.transform.localScale = localScale * scale;
            Tint(body, color);
        }

        private static void Tint(GameObject gameObject, Color color)
        {
            var renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null)
                return;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        // Match the bridge: MuJoCo (X-forward/Y-left/Z-up) -> Unity (X-right/Y-up/Z-forward).
        private static Vector3 M(float x, float y, float z) => new(-y, z, x);
    }
}
