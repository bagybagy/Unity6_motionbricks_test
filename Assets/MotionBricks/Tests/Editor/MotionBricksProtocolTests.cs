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
    }
}
