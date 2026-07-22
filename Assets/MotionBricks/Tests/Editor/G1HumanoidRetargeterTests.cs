using System.Collections;
using System.Collections.Generic;
using MotionBricks.Unity;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace MotionBricks.Tests.Editor
{
    public sealed class G1HumanoidRetargeterTests
    {
        [UnityTest]
        public IEnumerator SavedDemoScene_ShowsActiveUnityChanOnPlay()
        {
            EditorSceneManager.OpenScene("Assets/MotionBricks/Scenes/MotionBricksDemo.unity");
            var savedModel = GameObject.Find("UnityChan Model");
            var savedCamera = Object.FindFirstObjectByType<Camera>();
            Assert.That(savedModel, Is.Not.Null);
            Assert.That(savedCamera, Is.Not.Null);
            var savedRenderers = savedModel.GetComponentsInChildren<Renderer>();
            var frustum = GeometryUtility.CalculateFrustumPlanes(savedCamera);
            Assert.That(System.Array.Exists(savedRenderers, renderer => GeometryUtility.TestPlanesAABB(frustum, renderer.bounds)), Is.True,
                "Unity-chan is outside the saved demo camera view.");

            yield return new EnterPlayMode();
            for (var frame = 0; frame < 5; frame++) yield return null;
            Assert.That(Application.isPlaying, Is.True);

            var model = GameObject.Find("UnityChan Model");
            Assert.That(model, Is.Not.Null, "The saved demo scene does not contain Unity-chan.");
            Assert.That(Object.FindFirstObjectByType<G1HumanoidRetargeter>().IsHumanoidAvatarValid, Is.True);
            var renderers = model.GetComponentsInChildren<Renderer>();
            Assert.That(renderers, Is.Not.Empty, "Unity-chan has no active renderer.");
            Assert.That(System.Array.Exists(renderers, renderer => renderer.enabled && renderer.gameObject.activeInHierarchy), Is.True);

            yield return new ExitPlayMode();
        }

        [Test]
        public void PoseController_DefaultsToPlayableWalkMode()
        {
            var root = new GameObject("pose controller default test");
            try
            {
                var controller = root.AddComponent<MotionBricksPoseController>();
                Assert.That(controller.SelectedMode, Is.EqualTo("walk"));
                Assert.That(MotionBricksPoseController.OfficialModes, Has.Length.EqualTo(15));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void DefaultBindings_CoverAll29OfficialG1Axes()
        {
            Assert.That(G1HumanoidRetargeter.DefaultBindings, Has.Length.EqualTo(G1DemoRigBuilder.JointCount));
            var names = new HashSet<string>();
            foreach (var binding in G1HumanoidRetargeter.DefaultBindings)
            {
                Assert.That(binding.muscleName, Is.Not.Empty);
                Assert.That(binding.maxRadians, Is.GreaterThan(0f));
                Assert.That(binding.minRadians, Is.LessThan(0f));
                names.Add(binding.jointName);
            }
            CollectionAssert.AreEquivalent(G1DemoRigBuilder.JointNames, names);
            var muscles = new HashSet<string>();
            foreach (var binding in G1HumanoidRetargeter.DefaultBindings)
            {
                Assert.That(System.Array.IndexOf(HumanTrait.MuscleName, binding.muscleName), Is.GreaterThanOrEqualTo(0), binding.muscleName);
                Assert.That(muscles.Add(binding.muscleName), Is.True, binding.muscleName);
            }
        }

        [Test]
        public void MissingOrNonHumanoidAvatar_IsRejectedWithoutApplying()
        {
            var root = new GameObject("retargeter test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                Assert.That(retargeter.IsHumanoidAvatarValid, Is.False);
                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float> { ["left_knee_joint"] = 1f }), Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase("left_hip_pitch_joint", -1f, 0f, 0f)]
        [TestCase("left_hip_roll_joint", 0f, 0f, 1f)]
        [TestCase("waist_yaw_joint", 0f, 1f, 0f)]
        public void ExtractJointRadians_ProjectsOntoTheNamedMjcfToUnityAxis(string joint, float x, float y, float z)
        {
            var axis = new Vector3(x, y, z);
            var rotation = Quaternion.AngleAxis(45f, axis);
            Assert.That(G1HumanoidRetargeter.ExtractJointRadians(joint, rotation), Is.EqualTo(Mathf.PI / 4f).Within(.0001f));
            Assert.That(G1HumanoidRetargeter.ExtractJointRadians(joint, Quaternion.AngleAxis(-45f, axis)), Is.EqualTo(-Mathf.PI / 4f).Within(.0001f));
        }

        [Test]
        public void SimpleHumanoidBuilder_CreatesValidHumanAvatar()
        {
            var root = new GameObject("humanoid builder test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.UseProceduralFallback();
                builder.Build();
                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float>
                {
                    ["left_knee_joint"] = .5f,
                }), Is.True);
                builder.Build();
                Assert.That(builder.HasValidHumanoidAvatar, Is.True);
                Assert.That(retargeter.IsHumanoidAvatarValid, Is.True);
                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float>
                {
                    ["left_knee_joint"] = .25f,
                }), Is.True);
                Assert.That(root.transform.Find("Hips"), Is.Not.Null);
                Assert.That(root.transform.childCount, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Apply_PreservesSideBySideOffsetWhileFollowingStreamedRoot()
        {
            var root = new GameObject("humanoid root follow test");
            try
            {
                root.transform.position = new Vector3(2f, 0f, 0f);
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.UseProceduralFallback();
                builder.Build();
                retargeter.Apply(new PoseMessage
                {
                    RootPosition = new[] { 1f, 0.75f, 3f },
                    RootRotation = new[] { 0f, 0f, 0f, 1f },
                    Joints = new Dictionary<string, float[]>(),
                });

                Assert.That(root.transform.position, Is.EqualTo(new Vector3(3f, .75f, 3f)));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void BundledUnityChan_CreatesValidExternalHumanoidAvatar()
        {
            var root = new GameObject("unity chan humanoid test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                Assert.That(builder.UsesExternalHumanoid, Is.True);
                Assert.That(builder.HasValidHumanoidAvatar, Is.True);
                Assert.That(retargeter.IsHumanoidAvatarValid, Is.True);
                Assert.That(root.transform.Find("UnityChan Model"), Is.Not.Null);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void MuscleConversion_ReversedSignPreservesAsymmetricJointRange()
        {
            var binding = new G1HumanoidRetargeter.MuscleBinding
            {
                jointName = "test",
                muscleName = "test",
                minRadians = -.5f,
                maxRadians = 3f,
                sign = -1f,
            };

            var positiveMuscle = G1HumanoidRetargeter.JointRadiansToMuscle(.5f, binding);
            Assert.That(positiveMuscle, Is.EqualTo(-1f / 6f).Within(.0001f));
            Assert.That(
                G1HumanoidRetargeter.MuscleToJointRadians(positiveMuscle, binding),
                Is.EqualTo(.5f).Within(.0001f));
            Assert.That(G1HumanoidRetargeter.JointRadiansToMuscle(-.5f, binding), Is.EqualTo(1f));
            Assert.That(G1HumanoidRetargeter.MuscleToJointRadians(1f, binding), Is.EqualTo(-.5f));
        }

        [Test]
        public void ClearPoseTarget_DisablesButRetainsAllNeutralJointEntries()
        {
            var root = new GameObject("pose controller test");
            try
            {
                var controller = root.AddComponent<MotionBricksPoseController>();
                controller.SetJointAngle("left_knee_joint", 1f);
                controller.ClearPoseTarget();
                Assert.That(controller.HasPoseTarget, Is.False);
                Assert.That(controller.TargetJointAngles.Count, Is.EqualTo(G1DemoRigBuilder.JointCount));
                Assert.That(controller.TargetJointAngles["left_knee_joint"], Is.Zero);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
