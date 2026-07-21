using MotionBricks.Unity;
using NUnit.Framework;
using UnityEngine;

namespace MotionBricks.Tests.Editor
{
    public sealed class MotionBricksProtocolTests
    {
        [Test]
        public void PoseMessage_TryParse_ReadsRootAndJoints()
        {
            const string json = "{\"type\":\"pose\",\"seq\":7,\"timestamp\":12.5,\"root_position\":[1,2,3],\"root_rotation\":[0,0,0,1],\"joints\":{\"hip\":[0,0.5,0,0.5]}}";

            var parsed = PoseMessage.TryParse(json, out var pose);

            Assert.That(parsed, Is.True);
            Assert.That(pose.Sequence, Is.EqualTo(7));
            Assert.That(pose.GetRootPosition(), Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(pose.Joints["hip"], Has.Length.EqualTo(4));
        }

        [Test]
        public void PoseMessage_TryParse_RejectsNonPoseMessages()
        {
            Assert.That(PoseMessage.TryParse("{\"type\":\"control\"}", out _), Is.False);
        }

        [Test]
        public void PoseMessage_TryParse_ReadsOptionalPlanAndGoal()
        {
            const string json = "{\"type\":\"pose\",\"plan_root_positions\":[[1,2,3]],\"goal_root_position\":[4,5,6],\"goal_root_rotation\":[0,0,0,1],\"goal_joints\":{\"hip\":[0,0,0,1]}}";

            Assert.That(PoseMessage.TryParse(json, out var pose), Is.True);
            Assert.That(pose.PlanRootPositions, Has.Length.EqualTo(1));
            Assert.That(pose.GetGoalRootPosition(), Is.EqualTo(new Vector3(4, 5, 6)));
            Assert.That(pose.GoalJoints["hip"], Has.Length.EqualTo(4));
        }

        [Test]
        public void ControlMessage_StoresFixedTargetFields()
        {
            var message = new ControlMessage
            {
                HasTarget = true,
                TargetPosition = new[] { 1f, 2f, 3f },
                TargetYaw = 135f,
            };

            Assert.That(message.HasTarget, Is.True);
            Assert.That(message.TargetPosition, Is.EqualTo(new[] { 1f, 2f, 3f }));
            Assert.That(message.TargetYaw, Is.EqualTo(135f));
        }
    }
}
