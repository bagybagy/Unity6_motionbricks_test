using MotionBricks.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MotionBricks.Editor
{
    [CustomEditor(typeof(SimpleHumanoidDemoBuilder))]
    public sealed class SimpleHumanoidDemoBuilderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var builder = (SimpleHumanoidDemoBuilder)target;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Humanoid Prefab に Humanoid Import 済みの FBX／Prefab を指定し、下のボタンで反映します。Generic は個別アダプターなしでは使用できません。",
                MessageType.Info);

            if (GUILayout.Button("Build / Swap Humanoid"))
            {
                Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Swap MotionBricks Humanoid");
                builder.Build();
                EditorUtility.SetDirty(builder);
                if (!Application.isPlaying)
                    EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
            }

            if (!string.IsNullOrEmpty(builder.LastError))
                EditorGUILayout.HelpBox(builder.LastError, MessageType.Error);
            else if (!string.IsNullOrEmpty(builder.Status))
                EditorGUILayout.HelpBox(builder.Status, MessageType.None);
        }
    }
}
