using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MotionBricks.Tests.Editor
{
    public sealed class MotionBricksUrpTests
    {
        [Test]
        public void ProjectUsesUrpAndUnityChanUsesUnityToonShader()
        {
            var pipeline = GraphicsSettings.defaultRenderPipeline;
            Assert.That(pipeline, Is.Not.Null);
            Assert.That(pipeline.GetType().Name, Is.EqualTo("UniversalRenderPipelineAsset"));

            var materialGuids = AssetDatabase.FindAssets(
                "t:Material", new[] { "Assets/MotionBricks/Resources/UnityChan/Materials" });
            Assert.That(materialGuids, Is.Not.Empty);
            foreach (var guid in materialGuids)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(material, Is.Not.Null);
                Assert.That(material.shader.name, Is.EqualTo("Toon/Toon"), material.name);
                Assert.That(material.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"), material.name);
            }
        }

        [Test]
        public void UnityChanFaceTransparencySettingsSurviveUrpMigration()
        {
            var cheek = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/MotionBricks/Resources/UnityChan/Materials/mat_cheek.mat");
            var eye = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/MotionBricks/Resources/UnityChan/Materials/eye_L1.mat");
            Assert.That(cheek.GetTag("RenderType", false), Is.EqualTo("Transparent"));
            Assert.That(cheek.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
            Assert.That(eye.GetTag("RenderType", false), Is.EqualTo("TransparentCutout"));
            Assert.That(eye.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));
        }
    }
}
