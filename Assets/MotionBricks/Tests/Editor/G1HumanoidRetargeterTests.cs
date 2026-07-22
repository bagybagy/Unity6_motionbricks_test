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
            var savedBuilder = Object.FindFirstObjectByType<SimpleHumanoidDemoBuilder>();
            var savedCamera = Object.FindFirstObjectByType<Camera>();
            Assert.That(savedBuilder, Is.Not.Null);
            Assert.That(savedCamera, Is.Not.Null);

            yield return new EnterPlayMode();
            for (var frame = 0; frame < 5; frame++) yield return null;
            Assert.That(Application.isPlaying, Is.True);

            var model = Object.FindFirstObjectByType<SimpleHumanoidDemoBuilder>()?.CurrentModel;
            Assert.That(model, Is.Not.Null, "The saved demo scene does not contain Unity-chan.");
            Assert.That(Object.FindFirstObjectByType<G1HumanoidRetargeter>().IsHumanoidAvatarValid, Is.True);
            var renderers = model.GetComponentsInChildren<Renderer>();
            Assert.That(renderers, Is.Not.Empty, "Unity-chan has no active renderer.");
            Assert.That(System.Array.Exists(renderers, renderer => renderer.enabled && renderer.gameObject.activeInHierarchy), Is.True);

            yield return new ExitPlayMode();
        }

        [UnityTest, Category("CudaIntegration"), Explicit("Requires the local MotionBricks CUDA server.")]
        public IEnumerator SavedDemoScene_CudaIdleAndWalkDriveCorrectedG1AndUnityChanTogether()
        {
            EditorSceneManager.OpenScene("Assets/MotionBricks/Scenes/MotionBricksDemo.unity");
            yield return new EnterPlayMode();
            for (var frame = 0; frame < 5; frame++) yield return null;

            var client = Object.FindFirstObjectByType<MotionBricksUdpClient>();
            var target = Object.FindFirstObjectByType<MotionBricksTargetController>();
            var retargeter = Object.FindFirstObjectByType<G1HumanoidRetargeter>();
            var builder = Object.FindFirstObjectByType<SimpleHumanoidDemoBuilder>();
            Assert.That(client, Is.Not.Null);
            Assert.That(target, Is.Not.Null);
            Assert.That(retargeter, Is.Not.Null);
            Assert.That(builder?.HasValidHumanoidAvatar, Is.True);

            client.Style = "idle";
            var idleDeadline = Time.realtimeSinceStartup + 8f;
            while (client.ReceivedPoseCount < 20 && Time.realtimeSinceStartup < idleDeadline)
                yield return null;
            if (client.ReceivedPoseCount == 0)
            {
                yield return new ExitPlayMode();
                Assert.Ignore("CUDA MotionBricks server is not running on localhost.");
            }
            for (var frame = 0; frame < 30; frame++) yield return null;
            var g1AxesScreenshotPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                Application.dataPath, "..", "TestResults", "g1-cuda-idle-axes.png"));
            CaptureCharacter(Object.FindFirstObjectByType<Camera>(), client.gameObject,
                client.transform.rotation * Vector3.forward, g1AxesScreenshotPath);
            var idleScreenshotPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                Application.dataPath, "..", "TestResults", "unitychan-cuda-idle.png"));
            yield return new WaitForEndOfFrame();
            CaptureCharacter(Object.FindFirstObjectByType<Camera>(), builder.CurrentModel,
                retargeter.transform.rotation * Vector3.forward, idleScreenshotPath);

            client.Style = "walk";
            var g1Start = client.transform.position;
            var humanStart = retargeter.transform.position;
            var leftToe = builder.Animator.GetBoneTransform(HumanBodyBones.LeftToes);
            var rightToe = builder.Animator.GetBoneTransform(HumanBodyBones.RightToes);
            Assert.That(leftToe, Is.Not.Null);
            Assert.That(rightToe, Is.Not.Null);
            var leftToeReference = leftToe.localRotation;
            var rightToeReference = rightToe.localRotation;
            var maximumLeftToeDrift = 0f;
            var maximumRightToeDrift = 0f;
            target.SetTarget(g1Start + Vector3.forward * 2f, 0f);
            var deadline = Time.realtimeSinceStartup + 12f;
            while ((client.ReceivedPoseCount < 20 ||
                    Vector3.Distance(client.transform.position, g1Start) < .2f) &&
                   Time.realtimeSinceStartup < deadline)
            {
                maximumLeftToeDrift = Mathf.Max(maximumLeftToeDrift,
                    Quaternion.Angle(leftToeReference, leftToe.localRotation));
                maximumRightToeDrift = Mathf.Max(maximumRightToeDrift,
                    Quaternion.Angle(rightToeReference, rightToe.localRotation));
                yield return null;
            }

            var g1Delta = client.transform.position - g1Start;
            var humanDelta = retargeter.transform.position - humanStart;
            Assert.That(g1Delta.magnitude, Is.GreaterThan(.2f));
            Assert.That(Vector3.Distance(g1Delta, humanDelta), Is.LessThan(.05f));
            Assert.That(maximumLeftToeDrift, Is.LessThan(1f), "Left toe must not spin while streamed poses are applied.");
            Assert.That(maximumRightToeDrift, Is.LessThan(1f), "Right toe must not spin while streamed poses are applied.");
            Assert.That(JointAngle(builder.Animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot), Is.InRange(45f, 180f));
            Assert.That(JointAngle(builder.Animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot), Is.InRange(45f, 180f));

            var screenshotPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                Application.dataPath, "..", "TestResults", "unitychan-cuda-walk.png"));
            yield return new WaitForEndOfFrame();
            CaptureCharacter(Object.FindFirstObjectByType<Camera>(), builder.CurrentModel,
                retargeter.transform.rotation * Vector3.forward, screenshotPath);
            Assert.That(System.IO.File.Exists(screenshotPath), Is.True);

            yield return new ExitPlayMode();
        }

        private static void CaptureCamera(Camera camera, string path)
        {
            Assert.That(camera, Is.Not.Null);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var renderTexture = new RenderTexture(1600, 900, 24);
            var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                texture.Apply();
                System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(renderTexture);
            }
        }

        private static void CaptureCharacter(Camera camera, GameObject model, Vector3 front, string path)
        {
            Assert.That(camera, Is.Not.Null);
            Assert.That(model, Is.Not.Null);
            var renderers = model.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.GreaterThan(0));
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);

            var previousPosition = camera.transform.position;
            var previousRotation = camera.transform.rotation;
            var previousFieldOfView = camera.fieldOfView;
            try
            {
                front.y = 0f;
                if (front.sqrMagnitude < .0001f) front = Vector3.back;
                var distance = Mathf.Max(2f, bounds.extents.y * 4.2f);
                camera.transform.position = bounds.center + front.normalized * distance;
                camera.transform.LookAt(bounds.center);
                camera.fieldOfView = 32f;
                CaptureCamera(camera, path);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                camera.fieldOfView = previousFieldOfView;
            }
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
                Assert.That(binding.sign, Is.Not.Zero);
                names.Add(binding.jointName);
            }
            CollectionAssert.AreEquivalent(G1DemoRigBuilder.JointNames, names);
            var muscles = new HashSet<string>();
            foreach (var binding in G1HumanoidRetargeter.DefaultBindings)
            {
                Assert.That(System.Array.IndexOf(HumanTrait.MuscleName, binding.muscleName), Is.GreaterThanOrEqualTo(0), binding.muscleName);
                Assert.That(muscles.Add(binding.muscleName), Is.True, binding.muscleName);
            }
            Assert.That(System.Array.Find(G1HumanoidRetargeter.DefaultBindings,
                item => item.jointName == "left_shoulder_roll_joint").zeroOffsetRadians,
                Is.EqualTo(-1.313f));
            Assert.That(System.Array.Find(G1HumanoidRetargeter.DefaultBindings,
                item => item.jointName == "right_shoulder_roll_joint").zeroOffsetRadians,
                Is.EqualTo(-1.313f));
            Assert.That(System.Array.Find(G1HumanoidRetargeter.DefaultBindings,
                item => item.jointName == "left_ankle_roll_joint").applyToHumanoid,
                Is.False);
            Assert.That(System.Array.Find(G1HumanoidRetargeter.DefaultBindings,
                item => item.jointName == "right_ankle_roll_joint").applyToHumanoid,
                Is.False);
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

        [TestCase("left_hip_pitch_joint", 1f, 0f, 0f)]
        [TestCase("left_hip_roll_joint", 0f, 0f, -1f)]
        [TestCase("waist_yaw_joint", 0f, -1f, 0f)]
        public void ExtractJointRadians_ProjectsOntoTheNamedMjcfToUnityAxis(string joint, float x, float y, float z)
        {
            var axis = new Vector3(x, y, z);
            var rotation = Quaternion.AngleAxis(45f, axis);
            Assert.That(G1HumanoidRetargeter.ExtractJointRadians(joint, rotation), Is.EqualTo(Mathf.PI / 4f).Within(.0001f));
            Assert.That(G1HumanoidRetargeter.ExtractJointRadians(joint, Quaternion.AngleAxis(-45f, axis)), Is.EqualTo(-Mathf.PI / 4f).Within(.0001f));
        }

        [Test]
        public void SimpleHumanoidBuilder_RebuildsBundledUnityChanAvatar()
        {
            var root = new GameObject("humanoid builder test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
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
                Assert.That(builder.CurrentModel, Is.Not.Null);
                Assert.That(builder.CurrentModel.transform.parent, Is.EqualTo(root.transform));
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
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                retargeter.Apply(new PoseMessage
                {
                    RootPosition = new[] { 1f, 0.75f, 3f },
                    RootRotation = new[] { 0f, 0f, 0f, 1f },
                    Joints = new Dictionary<string, float[]>(),
                });

                Assert.That(root.transform.position, Is.EqualTo(new Vector3(3f, 0f, 3f)));
                retargeter.Apply(new PoseMessage
                {
                    RootPosition = new[] { 1.5f, 1f, 4f },
                    RootRotation = new[] { 0f, 0f, 0f, 1f },
                    Joints = new Dictionary<string, float[]>(),
                });
                Assert.That(root.transform.position, Is.EqualTo(new Vector3(3.5f, .25f, 4f)));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Apply_PrefersExactJointAnglesOverConvertedQuaternionAxes()
        {
            var root = new GameObject("exact humanoid angle test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                retargeter.Apply(new PoseMessage
                {
                    Joints = new Dictionary<string, float[]>
                    {
                        ["left_knee_joint"] = new[] { -1f, 0f, 0f, 0f },
                    },
                    JointAngles = new Dictionary<string, float>
                    {
                        ["left_knee_joint"] = .5f,
                    },
                });

                var captured = new Dictionary<string, float>();
                Assert.That(retargeter.TryCaptureJointAngles(captured), Is.True);
                Assert.That(captured["left_knee_joint"], Is.EqualTo(.5f).Within(.0001f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TargetYaw_CanTurnAroundAndWrap()
        {
            var root = new GameObject("target yaw test");
            try
            {
                var target = root.AddComponent<MotionBricksTargetController>();
                target.SetTarget(Vector3.zero, 0f);
                target.SetTargetYaw(target.TargetYaw + 180f);
                Assert.That(target.TargetYaw, Is.EqualTo(180f));
                target.SetTargetYaw(450f);
                Assert.That(target.TargetYaw, Is.EqualTo(90f));
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
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                Assert.That(builder.UsesExternalHumanoid, Is.True);
                Assert.That(builder.HasValidHumanoidAvatar, Is.True);
                Assert.That(retargeter.IsHumanoidAvatarValid, Is.True);
                Assert.That(builder.CurrentModel, Is.Not.Null);
                Assert.That(builder.CurrentModel.name, Is.EqualTo("unitychan Model"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void BundledUnityChan_SourceAssetHasUsableUnmodifiedReferencePose()
        {
            var prefab = Resources.Load<GameObject>("UnityChan/unitychan");
            var instance = Object.Instantiate(prefab);
            try
            {
                var animator = instance.GetComponentInChildren<Animator>(true);
                Assert.That(animator, Is.Not.Null);
                Assert.That(animator.avatar.isHuman && animator.avatar.isValid, Is.True);
                Assert.That(JointAngle(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot), Is.GreaterThan(170f));
                Assert.That(JointAngle(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot), Is.GreaterThan(170f));
                TestContext.WriteLine($"source left arm from down: {ArmAngleFromDown(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm):F3}");
                using var handler = new HumanPoseHandler(animator.avatar, animator.transform);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                var kneeIndex = System.Array.IndexOf(HumanTrait.MuscleName, "Left Lower Leg Stretch");
                var armIndex = System.Array.IndexOf(HumanTrait.MuscleName, "Left Arm Down-Up");
                TestContext.WriteLine($"source left knee muscle: {pose.muscles[kneeIndex]:F6}");
                TestContext.WriteLine($"source left arm down-up muscle: {pose.muscles[armIndex]:F6}");
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void SemanticAngleConversion_UsesUnityHumanoidRange()
        {
            var binding = System.Array.Find(G1HumanoidRetargeter.DefaultBindings, item => item.jointName == "left_knee_joint");
            var index = System.Array.IndexOf(HumanTrait.MuscleName, binding.muscleName);
            var semanticRadians = .5f * binding.sign;
            var muscle = G1HumanoidRetargeter.RadiansToMuscleDelta(semanticRadians, index);
            Assert.That(muscle, Is.LessThan(0f));
            Assert.That(G1HumanoidRetargeter.MuscleDeltaToRadians(muscle, index), Is.EqualTo(semanticRadians).Within(.0001f));
        }

        [Test]
        public void UnityChan_EmptyInputMeansDeterministicG1ZeroAndResetsPose()
        {
            var root = new GameObject("neutral pose test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                var index = System.Array.IndexOf(HumanTrait.MuscleName, "Left Lower Leg Stretch");
                using var handler = new HumanPoseHandler(builder.Avatar, builder.Animator.transform);
                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float>()), Is.True);
                var zero = new HumanPose();
                handler.GetHumanPose(ref zero);

                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float>()), Is.True);
                var afterEmpty = new HumanPose();
                handler.GetHumanPose(ref afterEmpty);
                Assert.That(afterEmpty.muscles[index], Is.EqualTo(zero.muscles[index]).Within(.001f));

                retargeter.ApplyJointAngles(new Dictionary<string, float> { ["left_knee_joint"] = .5f });
                var flexed = new HumanPose();
                handler.GetHumanPose(ref flexed);
                Assert.That(flexed.muscles[index], Is.LessThan(zero.muscles[index]));

                retargeter.ApplyJointAngles(new Dictionary<string, float>());
                var reset = new HumanPose();
                handler.GetHumanPose(ref reset);
                Assert.That(reset.muscles[index], Is.EqualTo(zero.muscles[index]).Within(.001f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void UnityChan_RepeatedIdenticalPoseDoesNotAccumulateToeRotation()
        {
            var root = new GameObject("toe stability test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                var angles = new Dictionary<string, float>
                {
                    ["left_ankle_pitch_joint"] = -.2f,
                    ["left_ankle_roll_joint"] = .08f,
                    ["right_ankle_pitch_joint"] = -.15f,
                    ["right_ankle_roll_joint"] = -.06f,
                };
                Assert.That(retargeter.ApplyJointAngles(angles), Is.True);
                var leftToe = builder.Animator.GetBoneTransform(HumanBodyBones.LeftToes);
                var rightToe = builder.Animator.GetBoneTransform(HumanBodyBones.RightToes);
                Assert.That(leftToe, Is.Not.Null);
                Assert.That(rightToe, Is.Not.Null);
                var leftReference = leftToe.rotation;
                var rightReference = rightToe.rotation;

                for (var frame = 0; frame < 600; frame++)
                    Assert.That(retargeter.ApplyJointAngles(angles), Is.True);

                Assert.That(Quaternion.Angle(leftReference, leftToe.rotation), Is.LessThan(.05f));
                Assert.That(Quaternion.Angle(rightReference, rightToe.rotation), Is.LessThan(.05f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void UnityChan_G1AnkleRollMovesFootWithoutChangingToeLocalRotation()
        {
            var root = new GameObject("ankle roll semantics test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();

                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float>()), Is.True);
                var foot = builder.Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var toe = builder.Animator.GetBoneTransform(HumanBodyBones.LeftToes);
                var footReference = foot.rotation;
                var toeLocalReference = toe.localRotation;
                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float>
                {
                    ["left_ankle_roll_joint"] = .2f,
                }), Is.True);

                Assert.That(Quaternion.Angle(footReference, foot.rotation), Is.GreaterThan(10f));
                Assert.That(Quaternion.Angle(toeLocalReference, toe.localRotation), Is.LessThan(.05f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void UnityChan_ZeroG1PoseMatchesOfficialG1RestGeometry()
        {
            var root = new GameObject("unity chan zero pose geometry test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                var zeros = new Dictionary<string, float>();
                foreach (var joint in G1DemoRigBuilder.JointNames) zeros[joint] = 0f;
                Assert.That(retargeter.ApplyJointAngles(zeros), Is.True);

                var leftKnee = JointAngle(builder.Animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
                var rightKnee = JointAngle(builder.Animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
                var leftElbow = JointAngle(builder.Animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
                var rightElbow = JointAngle(builder.Animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
                TestContext.WriteLine($"G1 zero mapped angles: knees {leftKnee:F2}/{rightKnee:F2}, elbows {leftElbow:F2}/{rightElbow:F2}");
                Assert.That(leftKnee, Is.InRange(160f, 180f));
                Assert.That(rightKnee, Is.InRange(160f, 180f));
                Assert.That(leftElbow, Is.InRange(90f, 115f));
                Assert.That(rightElbow, Is.InRange(90f, 115f));
                Assert.That(ArmAngleFromDown(builder.Animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg), Is.LessThan(5f));
                Assert.That(ArmAngleFromDown(builder.Animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg), Is.LessThan(5f));
                Assert.That(ArmAngleFromDown(builder.Animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), Is.LessThan(25f));
                Assert.That(ArmAngleFromDown(builder.Animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm), Is.LessThan(25f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void UnityChan_PositiveG1KneeFlexesAndPositiveG1ElbowExtends()
        {
            var root = new GameObject("G1 flexion direction test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                retargeter.ApplyJointAngles(new Dictionary<string, float>());
                var zeroKnee = JointAngle(builder.Animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
                var zeroElbow = JointAngle(builder.Animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);

                retargeter.ApplyJointAngles(new Dictionary<string, float>
                {
                    ["left_knee_joint"] = .5f,
                    ["left_elbow_joint"] = .5f,
                });

                var flexedKnee = JointAngle(builder.Animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
                var extendedElbow = JointAngle(builder.Animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
                Assert.That(flexedKnee, Is.LessThan(zeroKnee - 10f));
                Assert.That(extendedElbow, Is.GreaterThan(zeroElbow + 10f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void UnityChan_CoreLimbDirectionsMatchCorrectedG1Rig()
        {
            var g1Root = new GameObject("corrected G1 direction reference");
            var humanRoot = new GameObject("Unity-chan direction subject");
            try
            {
                var g1Driver = g1Root.AddComponent<MotionBricksRigDriver>();
                var g1Builder = g1Root.AddComponent<G1DemoRigBuilder>();
                g1Builder.Build();
                var retargeter = humanRoot.AddComponent<G1HumanoidRetargeter>();
                retargeter.SetSourceRig(g1Builder);
                var humanBuilder = humanRoot.AddComponent<SimpleHumanoidDemoBuilder>();
                humanBuilder.Build();

                AssertPositiveJointDirection(g1Root.transform, g1Builder, g1Driver,
                    humanRoot.transform, humanBuilder.Animator, retargeter,
                    "left_hip_pitch_joint", "left_ankle_roll_joint", HumanBodyBones.LeftFoot);
                AssertPositiveJointDirection(g1Root.transform, g1Builder, g1Driver,
                    humanRoot.transform, humanBuilder.Animator, retargeter,
                    "right_hip_pitch_joint", "right_ankle_roll_joint", HumanBodyBones.RightFoot);
                AssertPositiveJointDirection(g1Root.transform, g1Builder, g1Driver,
                    humanRoot.transform, humanBuilder.Animator, retargeter,
                    "left_hip_roll_joint", "left_ankle_roll_joint", HumanBodyBones.LeftFoot);
                AssertPositiveJointDirection(g1Root.transform, g1Builder, g1Driver,
                    humanRoot.transform, humanBuilder.Animator, retargeter,
                    "right_hip_roll_joint", "right_ankle_roll_joint", HumanBodyBones.RightFoot);
                AssertPositiveJointDirection(g1Root.transform, g1Builder, g1Driver,
                    humanRoot.transform, humanBuilder.Animator, retargeter,
                    "left_shoulder_pitch_joint", "left_wrist_roll_joint", HumanBodyBones.LeftHand);
                AssertPositiveJointDirection(g1Root.transform, g1Builder, g1Driver,
                    humanRoot.transform, humanBuilder.Animator, retargeter,
                    "right_shoulder_pitch_joint", "right_wrist_roll_joint", HumanBodyBones.RightHand);
                AssertPositiveJointDirection(g1Root.transform, g1Builder, g1Driver,
                    humanRoot.transform, humanBuilder.Animator, retargeter,
                    "left_shoulder_roll_joint", "left_wrist_roll_joint", HumanBodyBones.LeftHand);
                AssertPositiveJointDirection(g1Root.transform, g1Builder, g1Driver,
                    humanRoot.transform, humanBuilder.Animator, retargeter,
                    "right_shoulder_roll_joint", "right_wrist_roll_joint", HumanBodyBones.RightHand);
            }
            finally
            {
                Object.DestroyImmediate(g1Root);
                Object.DestroyImmediate(humanRoot);
            }
        }

        [Test]
        public void UnityChan_MultiAxisFootOrientationMatchesCorrectedG1CompositeRotation()
        {
            var g1Root = new GameObject("corrected G1 composite reference");
            var humanRoot = new GameObject("Unity-chan composite subject");
            try
            {
                var g1Driver = g1Root.AddComponent<MotionBricksRigDriver>();
                var g1Builder = g1Root.AddComponent<G1DemoRigBuilder>();
                g1Builder.Build();
                var retargeter = humanRoot.AddComponent<G1HumanoidRetargeter>();
                retargeter.SetSourceRig(g1Builder);
                var humanBuilder = humanRoot.AddComponent<SimpleHumanoidDemoBuilder>();
                humanBuilder.Build();

                var zero = new Dictionary<string, float>();
                foreach (var jointName in G1DemoRigBuilder.JointNames) zero[jointName] = 0f;
                g1Driver.Apply(new PoseMessage { JointAngles = zero });
                retargeter.ApplyJointAngles(zero);

                var pairs = new[]
                {
                    ("left_ankle_roll_joint", HumanBodyBones.LeftFoot),
                    ("right_ankle_roll_joint", HumanBodyBones.RightFoot),
                };
                var sourceReference = new Dictionary<string, Quaternion>();
                var targetReference = new Dictionary<HumanBodyBones, Quaternion>();
                foreach (var (jointName, humanBone) in pairs)
                {
                    sourceReference[jointName] = RootSpaceRotation(g1Root.transform, g1Builder.FindJoint(jointName));
                    targetReference[humanBone] = RootSpaceRotation(humanRoot.transform, humanBuilder.Animator.GetBoneTransform(humanBone));
                }

                var pose = new Dictionary<string, float>(zero)
                {
                    ["left_hip_pitch_joint"] = .22f, ["left_hip_roll_joint"] = .13f, ["left_hip_yaw_joint"] = .24f,
                    ["left_knee_joint"] = .42f, ["left_ankle_pitch_joint"] = -.18f, ["left_ankle_roll_joint"] = .07f,
                    ["right_hip_pitch_joint"] = -.17f, ["right_hip_roll_joint"] = -.11f, ["right_hip_yaw_joint"] = -.21f,
                    ["right_knee_joint"] = .35f, ["right_ankle_pitch_joint"] = -.14f, ["right_ankle_roll_joint"] = -.06f,
                    ["left_shoulder_pitch_joint"] = .18f, ["left_shoulder_roll_joint"] = .24f, ["left_shoulder_yaw_joint"] = -.31f,
                    ["left_elbow_joint"] = .38f, ["left_wrist_roll_joint"] = .12f, ["left_wrist_pitch_joint"] = -.09f, ["left_wrist_yaw_joint"] = .16f,
                    ["right_shoulder_pitch_joint"] = -.15f, ["right_shoulder_roll_joint"] = -.22f, ["right_shoulder_yaw_joint"] = .28f,
                    ["right_elbow_joint"] = .33f, ["right_wrist_roll_joint"] = -.11f, ["right_wrist_pitch_joint"] = .08f, ["right_wrist_yaw_joint"] = -.14f,
                };
                g1Driver.Apply(new PoseMessage { JointAngles = pose });
                retargeter.ApplyJointAngles(pose);

                var largestError = 0f;
                var largestErrorPair = string.Empty;
                foreach (var (jointName, humanBone) in pairs)
                {
                    var sourceCurrent = RootSpaceRotation(g1Root.transform, g1Builder.FindJoint(jointName));
                    var targetCurrent = RootSpaceRotation(humanRoot.transform, humanBuilder.Animator.GetBoneTransform(humanBone));
                    var sourceDelta = Quaternion.Inverse(sourceReference[jointName]) * sourceCurrent;
                    var targetDelta = Quaternion.Inverse(targetReference[humanBone]) * targetCurrent;
                    var error = Quaternion.Angle(sourceDelta, targetDelta);
                    if (error > largestError)
                    {
                        largestError = error;
                        largestErrorPair = $"{jointName} -> {humanBone}";
                    }
                }
                Assert.That(largestError, Is.LessThan(1f), largestErrorPair);
            }
            finally
            {
                Object.DestroyImmediate(g1Root);
                Object.DestroyImmediate(humanRoot);
            }
        }

        [TestCase("waist_pitch_joint", 1f, 0f, 0f)]
        [TestCase("waist_roll_joint", 0f, 0f, -1f)]
        [TestCase("waist_yaw_joint", 0f, -1f, 0f)]
        public void UnityChan_WaistAxesFollowCharacterAnatomicalAxes(string jointName, float x, float y, float z)
        {
            var root = new GameObject("waist anatomical axis test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float>()), Is.True);
                var spine = builder.Animator.GetBoneTransform(HumanBodyBones.Spine);
                var reference = RootSpaceRotation(root.transform, spine);

                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float> { [jointName] = .2f }), Is.True);
                var current = RootSpaceRotation(root.transform, spine);
                var delta = current * Quaternion.Inverse(reference);
                var axis = ShortestRotationAxis(delta, out var angle);
                Assert.That(Vector3.Dot(axis, new Vector3(x, y, z)), Is.GreaterThan(.98f),
                    $"{jointName}: axis={axis}, angle={angle:F2}");
                Assert.That(angle, Is.EqualTo(.2f * Mathf.Rad2Deg).Within(.1f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase("left", "left_wrist_roll_joint")]
        [TestCase("left", "left_wrist_pitch_joint")]
        [TestCase("left", "left_wrist_yaw_joint")]
        [TestCase("right", "right_wrist_roll_joint")]
        [TestCase("right", "right_wrist_pitch_joint")]
        [TestCase("right", "right_wrist_yaw_joint")]
        public void UnityChan_WristAxisUsesAnatomicalForearmFrame(string side, string jointName)
        {
            var root = new GameObject("wrist anatomical axis test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(root, retargeter);
                var source = root.GetComponentInChildren<G1DemoRigBuilder>();
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float>()), Is.True);

                var isLeft = side == "left";
                var upper = builder.Animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var lower = builder.Animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var hand = builder.Animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                Assert.That(source.TryGetJointRootPositions($"{side}_shoulder_yaw_joint", out var sourceUpper, out _), Is.True);
                Assert.That(source.TryGetJointRootPositions($"{side}_elbow_joint", out var sourceLower, out _), Is.True);
                Assert.That(source.TryGetJointRootPositions($"{side}_wrist_roll_joint", out var sourceHand, out _), Is.True);
                var sourceFrame = AnatomicalLimbFrame(sourceUpper, sourceLower, sourceHand);
                var targetFrame = AnatomicalLimbFrame(
                    root.transform.InverseTransformPoint(upper.position),
                    root.transform.InverseTransformPoint(lower.position),
                    root.transform.InverseTransformPoint(hand.position));
                Assert.That(source.TryGetJointRootRotations($"{side}_wrist_yaw_joint", out var sourceReference, out _), Is.True);
                var targetReference = RootSpaceRotation(root.transform, hand);

                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float> { [jointName] = .2f }), Is.True);
                Assert.That(source.TryGetJointRootRotations($"{side}_wrist_yaw_joint", out var sourceCurrent, out _), Is.True);
                var targetCurrent = RootSpaceRotation(root.transform, hand);
                var sourceAxis = ShortestRotationAxis(sourceCurrent * Quaternion.Inverse(sourceReference), out var sourceAngle);
                var targetAxis = ShortestRotationAxis(targetCurrent * Quaternion.Inverse(targetReference), out var targetAngle);
                var expectedAxis = (targetFrame * Quaternion.Inverse(sourceFrame)) * sourceAxis;
                Assert.That(Vector3.Dot(targetAxis, expectedAxis), Is.GreaterThan(.95f),
                    $"{jointName}: expected anatomical axis={expectedAxis}, actual={targetAxis}");
                Assert.That(targetAngle, Is.EqualTo(sourceAngle).Within(.5f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ParallelDisplayOffsetDoesNotChangeSpineOrWristAngles()
        {
            var first = new GameObject("humanoid at origin");
            var second = new GameObject("humanoid offset in parallel");
            second.transform.position = new Vector3(2f, 0f, 0f);
            try
            {
                var firstRetargeter = first.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(first, firstRetargeter);
                var firstBuilder = first.AddComponent<SimpleHumanoidDemoBuilder>();
                firstBuilder.Build();
                var secondRetargeter = second.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(second, secondRetargeter);
                var secondBuilder = second.AddComponent<SimpleHumanoidDemoBuilder>();
                secondBuilder.Build();
                var pose = new Dictionary<string, float>
                {
                    ["waist_yaw_joint"] = .18f,
                    ["waist_roll_joint"] = -.12f,
                    ["waist_pitch_joint"] = .2f,
                    ["left_wrist_roll_joint"] = .22f,
                    ["left_wrist_pitch_joint"] = -.16f,
                    ["left_wrist_yaw_joint"] = .11f,
                    ["right_wrist_roll_joint"] = -.19f,
                    ["right_wrist_pitch_joint"] = .14f,
                    ["right_wrist_yaw_joint"] = -.1f,
                };
                Assert.That(firstRetargeter.ApplyJointAngles(pose), Is.True);
                Assert.That(secondRetargeter.ApplyJointAngles(pose), Is.True);

                foreach (var bone in new[] { HumanBodyBones.Spine, HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                {
                    var firstRotation = RootSpaceRotation(first.transform, firstBuilder.Animator.GetBoneTransform(bone));
                    var secondRotation = RootSpaceRotation(second.transform, secondBuilder.Animator.GetBoneTransform(bone));
                    Assert.That(Quaternion.Angle(firstRotation, secondRotation), Is.LessThan(.01f), bone.ToString());
                }
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void UnityChan_RecordedCudaIdleKeepsFeetSeparatedAndForward()
        {
            var root = new GameObject("recorded CUDA idle stance test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float>()), Is.True);

                var leftFoot = builder.Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var rightFoot = builder.Animator.GetBoneTransform(HumanBodyBones.RightFoot);
                var leftToe = builder.Animator.GetBoneTransform(HumanBodyBones.LeftToes);
                var rightToe = builder.Animator.GetBoneTransform(HumanBodyBones.RightToes);
                var leftKnee = builder.Animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                var rightKnee = builder.Animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                var zeroFootDelta = root.transform.InverseTransformPoint(leftFoot.position).x -
                                    root.transform.InverseTransformPoint(rightFoot.position).x;
                var zeroKneeSeparation = Mathf.Abs(root.transform.InverseTransformPoint(leftKnee.position).x -
                                                   root.transform.InverseTransformPoint(rightKnee.position).x);
                var sourceRig = root.GetComponentInChildren<G1DemoRigBuilder>();
                Assert.That(sourceRig, Is.Not.Null);
                Assert.That(sourceRig.TryGetJointRootPositions("left_ankle_roll_joint", out var sourceLeftZero, out _), Is.True);
                Assert.That(sourceRig.TryGetJointRootPositions("right_ankle_roll_joint", out var sourceRightZero, out _), Is.True);
                var sourceZeroFootDelta = sourceLeftZero.x - sourceRightZero.x;

                var recordedIdle = new Dictionary<string, float>
                {
                    ["left_hip_pitch_joint"] = .145826f, ["left_hip_roll_joint"] = .007840f,
                    ["left_hip_yaw_joint"] = .262765f, ["left_knee_joint"] = -.010896f,
                    ["left_ankle_pitch_joint"] = -.083540f, ["left_ankle_roll_joint"] = .011528f,
                    ["right_hip_pitch_joint"] = .130692f, ["right_hip_roll_joint"] = -.013352f,
                    ["right_hip_yaw_joint"] = -.268654f, ["right_knee_joint"] = .002641f,
                    ["right_ankle_pitch_joint"] = -.100882f, ["right_ankle_roll_joint"] = -.004442f,
                };
                Assert.That(retargeter.ApplyJointAngles(recordedIdle), Is.True);

                var idleFootDelta = root.transform.InverseTransformPoint(leftFoot.position).x -
                                    root.transform.InverseTransformPoint(rightFoot.position).x;
                var idleKneeSeparation = Mathf.Abs(root.transform.InverseTransformPoint(leftKnee.position).x -
                                                   root.transform.InverseTransformPoint(rightKnee.position).x);
                Assert.That(sourceRig.TryGetJointRootPositions("left_ankle_roll_joint", out var sourceLeftIdle, out _), Is.True);
                Assert.That(sourceRig.TryGetJointRootPositions("right_ankle_roll_joint", out var sourceRightIdle, out _), Is.True);
                var sourceIdleFootDelta = sourceLeftIdle.x - sourceRightIdle.x;
                var leftToeAngle = GroundAngleFromForward(root.transform, leftFoot, leftToe);
                var rightToeAngle = GroundAngleFromForward(root.transform, rightFoot, rightToe);
                TestContext.WriteLine($"idle stance: feet {zeroFootDelta:F3}->{idleFootDelta:F3}, knees {zeroKneeSeparation:F3}->{idleKneeSeparation:F3}, toe yaw {leftToeAngle:F1}/{rightToeAngle:F1}");
                TestContext.WriteLine($"G1 ankle width: {sourceZeroFootDelta:F3}->{sourceIdleFootDelta:F3}");
                Assert.That(Mathf.Sign(idleFootDelta), Is.EqualTo(Mathf.Sign(zeroFootDelta)),
                    $"Feet crossed sides: zero={zeroFootDelta:F3}, idle={idleFootDelta:F3}");
                var sourceWidthRatio = Mathf.Abs(sourceIdleFootDelta / sourceZeroFootDelta);
                var targetWidthRatio = Mathf.Abs(idleFootDelta / zeroFootDelta);
                Assert.That(targetWidthRatio, Is.EqualTo(sourceWidthRatio).Within(.15f),
                    $"Stance-width ratio was not preserved: G1={sourceWidthRatio:F3}, Humanoid={targetWidthRatio:F3}");
                Assert.That(Mathf.Abs(idleFootDelta), Is.GreaterThan(Mathf.Abs(zeroFootDelta) * .6f),
                    $"Feet collapsed inward: zero={zeroFootDelta:F3}, idle={idleFootDelta:F3}");
                Assert.That(Mathf.Abs(idleFootDelta), Is.LessThan(Mathf.Abs(zeroFootDelta) * 1.6f),
                    $"Feet spread unnaturally: zero={zeroFootDelta:F3}, idle={idleFootDelta:F3}");
                Assert.That(idleKneeSeparation, Is.GreaterThan(zeroKneeSeparation * .6f),
                    $"Knees collapsed inward: zero={zeroKneeSeparation:F3}, idle={idleKneeSeparation:F3}");
                Assert.That(idleKneeSeparation, Is.LessThan(zeroKneeSeparation * 1.8f),
                    $"Knees spread unnaturally: zero={zeroKneeSeparation:F3}, idle={idleKneeSeparation:F3}");
                Assert.That(leftToeAngle, Is.LessThan(30f), $"Left toe yaw={leftToeAngle:F1}");
                Assert.That(rightToeAngle, Is.LessThan(30f), $"Right toe yaw={rightToeAngle:F1}");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static float GroundAngleFromForward(Transform root, Transform foot, Transform toe)
        {
            var direction = root.InverseTransformDirection(toe.position - foot.position);
            direction.y = 0f;
            return direction.sqrMagnitude < .000001f ? 0f : Vector3.Angle(Vector3.forward, direction.normalized);
        }

        private static Quaternion RootSpaceRotation(Transform root, Transform bone) =>
            Quaternion.Inverse(root.rotation) * bone.rotation;

        private static Quaternion AnatomicalLimbFrame(Vector3 proximal, Vector3 middle, Vector3 end)
        {
            var forward = (end - middle).normalized;
            var right = Vector3.Cross(middle - proximal, end - middle).normalized;
            Assert.That(forward.sqrMagnitude, Is.GreaterThan(.99f));
            Assert.That(right.sqrMagnitude, Is.GreaterThan(.99f));
            var up = Vector3.Cross(forward, right).normalized;
            return Quaternion.LookRotation(forward, up);
        }

        private static Vector3 ShortestRotationAxis(Quaternion rotation, out float angle)
        {
            rotation.ToAngleAxis(out angle, out var axis);
            if (angle > 180f)
            {
                angle = 360f - angle;
                axis = -axis;
            }
            return axis.normalized;
        }

        private static void AttachSourceRig(GameObject owner, G1HumanoidRetargeter retargeter)
        {
            var sourceObject = new GameObject("G1 kinematic source");
            sourceObject.transform.SetParent(owner.transform, false);
            sourceObject.AddComponent<MotionBricksRigDriver>();
            var source = sourceObject.AddComponent<G1DemoRigBuilder>();
            source.Build();
            retargeter.SetSourceRig(source);
        }

        private static void AssertPositiveJointDirection(
            Transform g1Root,
            G1DemoRigBuilder g1Builder,
            MotionBricksRigDriver g1Driver,
            Transform humanRoot,
            Animator animator,
            G1HumanoidRetargeter retargeter,
            string jointName,
            string g1EndpointName,
            HumanBodyBones humanEndpoint)
        {
            var zeroAngles = new Dictionary<string, float>();
            g1Driver.Apply(new PoseMessage { JointAngles = zeroAngles });
            retargeter.ApplyJointAngles(zeroAngles);
            var g1Before = g1Root.InverseTransformPoint(g1Builder.FindJoint(g1EndpointName).position);
            var humanBefore = humanRoot.InverseTransformPoint(animator.GetBoneTransform(humanEndpoint).position);
            var positive = new Dictionary<string, float> { [jointName] = .25f };
            g1Driver.Apply(new PoseMessage { JointAngles = positive });
            retargeter.ApplyJointAngles(positive);
            var g1Delta = g1Root.InverseTransformPoint(g1Builder.FindJoint(g1EndpointName).position) - g1Before;
            var humanDelta = humanRoot.InverseTransformPoint(animator.GetBoneTransform(humanEndpoint).position) - humanBefore;
            Assert.That(g1Delta.magnitude, Is.GreaterThan(.001f), $"G1 {jointName}");
            Assert.That(humanDelta.magnitude, Is.GreaterThan(.001f), $"Humanoid {jointName}");
            Assert.That(Vector3.Dot(g1Delta.normalized, humanDelta.normalized), Is.GreaterThan(.5f),
                $"{jointName}: corrected G1 delta={g1Delta}, Humanoid delta={humanDelta}");
        }

        private static float JointAngle(Animator animator, HumanBodyBones proximal, HumanBodyBones joint, HumanBodyBones distal)
        {
            var proximalTransform = animator.GetBoneTransform(proximal);
            var jointTransform = animator.GetBoneTransform(joint);
            var distalTransform = animator.GetBoneTransform(distal);
            Assert.That(proximalTransform, Is.Not.Null, proximal.ToString());
            Assert.That(jointTransform, Is.Not.Null, joint.ToString());
            Assert.That(distalTransform, Is.Not.Null, distal.ToString());
            return Vector3.Angle(proximalTransform.position - jointTransform.position, distalTransform.position - jointTransform.position);
        }

        private static float ArmAngleFromDown(Animator animator, HumanBodyBones upperArm, HumanBodyBones lowerArm)
        {
            var upper = animator.GetBoneTransform(upperArm);
            var lower = animator.GetBoneTransform(lowerArm);
            Assert.That(upper, Is.Not.Null, upperArm.ToString());
            Assert.That(lower, Is.Not.Null, lowerArm.ToString());
            return Vector3.Angle(lower.position - upper.position, Vector3.down);
        }

        [Test]
        public void InvalidGenericPrefab_IsRejectedWithoutProceduralReplacement()
        {
            var root = new GameObject("invalid prefab test");
            var genericPrefab = new GameObject("Generic");
            try
            {
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.SetHumanoidPrefab(genericPrefab);
                builder.Build();
                Assert.That(builder.HasValidHumanoidAvatar, Is.False);
                Assert.That(builder.CurrentModel, Is.Null);
                Assert.That(builder.LastError, Does.Contain("valid Humanoid"));
                Assert.That(builder.CurrentModel, Is.Null);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(genericPrefab); }
        }

        [Test]
        public void InvalidSwap_KeepsTheCurrentValidHumanoid()
        {
            var root = new GameObject("transactional swap test");
            var genericPrefab = new GameObject("Generic");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                var original = builder.CurrentModel;
                var originalAnimator = builder.Animator;

                builder.SetHumanoidPrefab(genericPrefab);
                builder.Build();

                Assert.That(builder.CurrentModel, Is.SameAs(original));
                Assert.That(builder.Animator, Is.SameAs(originalAnimator));
                Assert.That(builder.HasValidHumanoidAvatar, Is.True);
                Assert.That(retargeter.IsHumanoidAvatarValid, Is.True);
                Assert.That(builder.LastError, Does.Contain("valid Humanoid"));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(genericPrefab); }
        }

        [Test]
        public void Rebuild_RebindsRetargeterToNewValidAnimator()
        {
            var root = new GameObject("rebind test");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                var first = builder.Animator;
                builder.Build();
                Assert.That(builder.Animator, Is.Not.SameAs(first));
                Assert.That(retargeter.IsHumanoidAvatarValid, Is.True);
                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float>()), Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SourceRigSwap_PreservesTheHumanoidReferencePose()
        {
            var root = new GameObject("source swap humanoid");
            var replacementObject = new GameObject("replacement G1 source");
            try
            {
                var retargeter = root.AddComponent<G1HumanoidRetargeter>();
                AttachSourceRig(root, retargeter);
                var builder = root.AddComponent<SimpleHumanoidDemoBuilder>();
                builder.Build();
                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float>()), Is.True);
                var leftFoot = builder.Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var reference = root.transform.InverseTransformPoint(leftFoot.position);

                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float>
                {
                    ["left_hip_pitch_joint"] = .35f,
                    ["left_hip_roll_joint"] = .2f,
                    ["left_knee_joint"] = .5f,
                }), Is.True);
                Assert.That(Vector3.Distance(reference, root.transform.InverseTransformPoint(leftFoot.position)),
                    Is.GreaterThan(.01f));

                replacementObject.AddComponent<MotionBricksRigDriver>();
                var replacement = replacementObject.AddComponent<G1DemoRigBuilder>();
                replacement.Build();
                retargeter.SetSourceRig(replacement);
                Assert.That(retargeter.ApplyJointAngles(new Dictionary<string, float>()), Is.True);
                Assert.That(Vector3.Distance(reference, root.transform.InverseTransformPoint(leftFoot.position)),
                    Is.LessThan(.001f), "Changing only the G1 source must not redefine the target's reference pose.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(replacementObject);
            }
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
