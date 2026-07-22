using MotionBricks.Unity;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace MotionBricks.Tests.Editor
{
    public sealed class G1DemoRigBuilderTests
    {
        [Test]
        public void Build_CreatesAndBindsAllOfficialG1_29DofJoints()
        {
            var root = new GameObject("G1 test root");
            try
            {
                var driver = root.AddComponent<MotionBricksRigDriver>();
                var builder = root.AddComponent<G1DemoRigBuilder>();
                builder.Build();

                Assert.That(builder.GeneratedJoints.Count, Is.EqualTo(G1DemoRigBuilder.JointCount));
                Assert.That(driver.BindingCount, Is.EqualTo(G1DemoRigBuilder.JointCount));
                foreach (var jointName in G1DemoRigBuilder.JointNames)
                    Assert.That(builder.FindJoint(jointName), Is.Not.Null, jointName);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Build_ZeroPoseMatchesOfficialMjcfForwardKinematics()
        {
            var root = new GameObject("G1 zero-pose test root");
            try
            {
                var builder = root.AddComponent<G1DemoRigBuilder>();
                builder.Build();

                AssertLocalPosition(root.transform, builder, "left_knee_joint",
                    new Vector3(-.118601f, -.439296f, -.000002f) * 1.35f);
                AssertLocalPosition(root.transform, builder, "left_ankle_roll_joint",
                    new Vector3(-.118506f, -.756864f, -.000002f) * 1.35f);
                AssertLocalPosition(root.transform, builder, "right_knee_joint",
                    new Vector3(.118601f, -.439296f, -.000002f) * 1.35f);
                AssertLocalPosition(root.transform, builder, "right_ankle_roll_joint",
                    new Vector3(.118506f, -.756864f, -.000002f) * 1.35f);
                AssertLocalPosition(root.transform, builder, "waist_pitch_joint",
                    new Vector3(0f, .044f, -.003964f) * 1.35f);
                AssertLocalPosition(root.transform, builder, "left_elbow_joint",
                    new Vector3(-.146808f, .105243f, .015774f) * 1.35f);
                AssertLocalPosition(root.transform, builder, "right_elbow_joint",
                    new Vector3(.146798f, .105243f, .015774f) * 1.35f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Apply_PrefersExactMjcfKneeAngleAndBendsLowerLegBackward()
        {
            var root = new GameObject("G1 hinge test root");
            try
            {
                var driver = root.AddComponent<MotionBricksRigDriver>();
                var builder = root.AddComponent<G1DemoRigBuilder>();
                builder.Build();
                var ankle = builder.FindJoint("left_ankle_roll_joint");
                var initial = root.transform.InverseTransformPoint(ankle.position);

                driver.Apply(new PoseMessage
                {
                    JointAngles = new Dictionary<string, float> { ["left_knee_joint"] = .7f },
                    // Deliberately opposite: exact qpos must win over this compatibility field.
                    Joints = new Dictionary<string, float[]>
                    {
                        ["left_knee_joint"] = new[] { -Mathf.Sin(.35f), 0f, 0f, Mathf.Cos(.35f) },
                    },
                });

                var bent = root.transform.InverseTransformPoint(ankle.position);
                Assert.That(bent.z, Is.LessThan(initial.z - .1f), "Positive G1 knee flexion must move the shin backward, not into a reverse knee.");
                var knee = builder.FindJoint("left_knee_joint");
                var expected = new Quaternion(.0873386f, 0f, 0f, .996179f).normalized *
                               Quaternion.AngleAxis(.7f * Mathf.Rad2Deg, Vector3.right);
                Assert.That(Quaternion.Angle(knee.localRotation, expected), Is.LessThan(.01f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Apply_All29JointNamesUseTheOfficialConvertedAxisDirection()
        {
            var root = new GameObject("G1 all-axis test root");
            try
            {
                var builder = root.AddComponent<G1DemoRigBuilder>();
                builder.Build();
                var rollJoints = new HashSet<string>
                {
                    "left_hip_roll_joint", "left_ankle_roll_joint", "right_hip_roll_joint", "right_ankle_roll_joint",
                    "waist_roll_joint", "left_shoulder_roll_joint", "left_wrist_roll_joint",
                    "right_shoulder_roll_joint", "right_wrist_roll_joint",
                };
                var yawJoints = new HashSet<string>
                {
                    "left_hip_yaw_joint", "right_hip_yaw_joint", "waist_yaw_joint",
                    "left_shoulder_yaw_joint", "left_wrist_yaw_joint",
                    "right_shoulder_yaw_joint", "right_wrist_yaw_joint",
                };

                foreach (var jointName in G1DemoRigBuilder.JointNames)
                {
                    Assert.That(builder.ApplyJointAngles(new Dictionary<string, float>()), Is.True);
                    var joint = builder.FindJoint(jointName);
                    var reference = joint.localRotation;
                    Assert.That(builder.ApplyJointAngles(new Dictionary<string, float> { [jointName] = .2f }), Is.True);
                    var delta = Quaternion.Inverse(reference) * joint.localRotation;
                    delta.ToAngleAxis(out var angle, out var axis);
                    var expected = rollJoints.Contains(jointName)
                        ? Vector3.back
                        : yawJoints.Contains(jointName) ? Vector3.down : Vector3.right;
                    Assert.That(Vector3.Dot(axis.normalized, expected), Is.GreaterThan(.999f), jointName);
                    Assert.That(angle, Is.EqualTo(.2f * Mathf.Rad2Deg).Within(.01f), jointName);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void AssertLocalPosition(
            Transform root,
            G1DemoRigBuilder builder,
            string jointName,
            Vector3 expected)
        {
            var actual = root.InverseTransformPoint(builder.FindJoint(jointName).position);
            // Reference values are rounded MuJoCo FK output (six decimals).
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.0005f), jointName);
        }
    }
}
