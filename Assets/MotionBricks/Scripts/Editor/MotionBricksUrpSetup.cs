using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MotionBricks.Editor
{
    /// <summary>Creates the checked-in URP settings and repairs the bundled Unity-chan UTS materials.</summary>
    public static class MotionBricksUrpSetup
    {
        public const string PipelineAssetPath = "Assets/MotionBricks/Rendering/MotionBricksURP.asset";
        public const string RendererAssetPath = "Assets/MotionBricks/Rendering/MotionBricksRenderer.asset";
        private const string UnityChanMaterialsPath = "Assets/MotionBricks/Resources/UnityChan/Materials";

        [MenuItem("MotionBricks/Configure URP and Unity-chan Materials")]
        public static void ConfigureProject()
        {
            var pipeline = EnsurePipelineAssets();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            ConvertUnityChanMaterials();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("MotionBricks URP and Unity-chan Toon materials are configured.");
        }

        private static UniversalRenderPipelineAsset EnsurePipelineAssets()
        {
            EnsureFolder("Assets/MotionBricks/Rendering");
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererAssetPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "MotionBricksRenderer";
                AssetDatabase.CreateAsset(renderer, RendererAssetPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "MotionBricksURP";
                AssetDatabase.CreateAsset(pipeline, PipelineAssetPath);
            }
            return pipeline;
        }

        private static void ConvertUnityChanMaterials()
        {
            var toonShader = Shader.Find("Toon/Toon");
            if (toonShader == null)
                throw new InvalidOperationException("Unity Toon Shader is not installed or failed to compile (Toon/Toon not found).");

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { UnityChanMaterialsPath }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) continue;

                material.shader = toonShader;
                material.DisableKeyword("UTS_RP_BUILTIN");

                var clippingMode = material.HasProperty("_ClippingMode")
                    ? Mathf.RoundToInt(material.GetFloat("_ClippingMode"))
                    : 0;
                SetExclusiveClippingKeyword(material, clippingMode);

                var transparent = material.HasProperty("_TransparentEnabled") &&
                                  material.GetFloat("_TransparentEnabled") > .5f;
                if (transparent)
                {
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = (int)RenderQueue.Transparent;
                    SetIfPresent(material, "_ZWriteMode", 0f);
                    SetIfPresent(material, "_ZOverDrawMode", 1f);
                }
                else if (clippingMode != 0)
                {
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                    SetIfPresent(material, "_ZWriteMode", 1f);
                    SetIfPresent(material, "_ZOverDrawMode", 0f);
                }
                else
                {
                    material.SetOverrideTag("RenderType", "Opaque");
                    material.renderQueue = (int)RenderQueue.Geometry;
                    SetIfPresent(material, "_ZWriteMode", 1f);
                    SetIfPresent(material, "_ZOverDrawMode", 0f);
                }
                SetIfPresent(material, "_AutoRenderQueue", 0f);
                EditorUtility.SetDirty(material);
            }
        }

        private static void SetExclusiveClippingKeyword(Material material, int clippingMode)
        {
            material.DisableKeyword("_IS_CLIPPING_OFF");
            material.DisableKeyword("_IS_CLIPPING_MODE");
            material.DisableKeyword("_IS_CLIPPING_TRANSMODE");
            material.EnableKeyword(clippingMode switch
            {
                1 => "_IS_CLIPPING_MODE",
                2 => "_IS_CLIPPING_TRANSMODE",
                _ => "_IS_CLIPPING_OFF",
            });
        }

        private static void SetIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static void EnsureFolder(string path)
        {
            var segments = path.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }
    }
}
