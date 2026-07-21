using MotionBricks.Unity;
using NUnit.Framework;
using UnityEngine;

namespace MotionBricks.Tests.Editor
{
    public sealed class MotionBricksTargetControllerTests
    {
        [Test]
        public void SetAndClearTarget_TracksWorldPositionAndYaw()
        {
            var root = new GameObject("target controller test");
            try
            {
                var controller = root.AddComponent<MotionBricksTargetController>();
                controller.SetTarget(new Vector3(3f, 8f, -2f), 450f);

                Assert.That(controller.HasTarget, Is.True);
                Assert.That(controller.TargetPosition, Is.EqualTo(new Vector3(3f, 0f, -2f)));
                Assert.That(controller.TargetYaw, Is.EqualTo(90f));
                Assert.That(controller.TargetPositionArray, Is.EqualTo(new[] { 3f, 0f, -2f }));

                controller.ClearTarget();
                Assert.That(controller.HasTarget, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
