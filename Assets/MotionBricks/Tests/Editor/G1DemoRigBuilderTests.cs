using MotionBricks.Unity;
using NUnit.Framework;
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
    }
}
