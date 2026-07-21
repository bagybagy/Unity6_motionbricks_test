using System;
using System.Collections.Generic;
using UnityEngine;

namespace MotionBricks.Unity
{
    /// <summary>Runtime control panel for official modes and optional G1 joint-space goals.</summary>
    [DisallowMultipleComponent]
    public sealed class MotionBricksPoseController : MonoBehaviour
    {
        public static readonly string[] OfficialModes = { "idle", "slow_walk", "walk", "hand_crawling", "walk_boxing", "elbow_crawling", "stealth_walk", "injured_walk", "walk_stealth", "walk_happy_dance", "walk_zombie", "walk_gun", "walk_scared", "walk_left", "walk_right" };
        [SerializeField] private MotionBricksUdpClient udpClient;
        [SerializeField] private G1HumanoidRetargeter humanoidRetargeter;
        [SerializeField] private bool showPanel = true;
        [SerializeField] private bool hasPoseTarget;
        [SerializeField] private string selectedMode = "idle";
        private readonly Dictionary<string, float> targetJointAngles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> latestReceivedJointAngles = new(StringComparer.Ordinal);
        private Vector2 scroll;

        public bool HasPoseTarget => hasPoseTarget;
        public string SelectedMode => selectedMode;
        public IReadOnlyDictionary<string, float> TargetJointAngles => targetJointAngles;
        public void SetHumanoidRetargeter(G1HumanoidRetargeter value) => humanoidRetargeter = value;

        private void Awake()
        {
            udpClient ??= GetComponent<MotionBricksUdpClient>();
            humanoidRetargeter ??= GetComponent<G1HumanoidRetargeter>();
            foreach (var joint in G1DemoRigBuilder.JointNames) targetJointAngles.TryAdd(joint, 0f);
            SetMode(selectedMode);
        }

        public Dictionary<string, float> CopyTargetJointAngles() => new(targetJointAngles, StringComparer.Ordinal);
        public void SetPoseTargetEnabled(bool enabled) => hasPoseTarget = enabled;
        public void SetJointAngle(string jointName, float radians) { if (!string.IsNullOrWhiteSpace(jointName)) targetJointAngles[jointName] = radians; }
        // Keep neutral entries so the editor remains usable immediately after Clear.
        public void ClearPoseTarget() { hasPoseTarget = false; foreach (var joint in G1DemoRigBuilder.JointNames) targetJointAngles[joint] = 0f; }
        public void SetNeutralPose() { foreach (var joint in G1DemoRigBuilder.JointNames) targetJointAngles[joint] = 0f; hasPoseTarget = true; ApplyPreview(); }
        public void CaptureCurrent()
        {
            foreach (var joint in G1DemoRigBuilder.JointNames) targetJointAngles.TryAdd(joint, 0f);
            foreach (var (joint, radians) in latestReceivedJointAngles) targetJointAngles[joint] = radians;
            humanoidRetargeter?.TryCaptureJointAngles(targetJointAngles);
            hasPoseTarget = true;
            ApplyPreview();
        }

        /// <summary>Caches the latest bridge pose so Capture Current works without an Avatar.</summary>
        public void RecordReceivedPose(PoseMessage message)
        {
            if (message?.Joints == null) return;
            foreach (var (joint, rotation) in message.Joints)
                if (rotation is { Length: >= 4 })
                    latestReceivedJointAngles[joint] = G1HumanoidRetargeter.ExtractJointRadians(joint, new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]));
        }
        public void SetMode(string mode)
        {
            if (Array.IndexOf(OfficialModes, mode) < 0) return;
            selectedMode = mode;
            if (udpClient != null) udpClient.Style = mode;
        }

        private void Update()
        {
            for (var i = 0; i < OfficialModes.Length; i++) if (Input.GetKeyDown((KeyCode)((int)KeyCode.F1 + i))) SetMode(OfficialModes[i]);
            if (hasPoseTarget) ApplyPreview();
        }
        private void ApplyPreview() => humanoidRetargeter?.ApplyJointAngles(targetJointAngles);

        private void OnGUI()
        {
            if (!showPanel) return;
            GUILayout.BeginArea(new Rect(Screen.width - 340, 12, 328, Screen.height - 24), "MotionBricks modes / G1 pose", GUI.skin.window);
            GUILayout.Label($"Selected mode: {selectedMode} (F1-F15)");
            for (var i = 0; i < OfficialModes.Length; i++) if (GUILayout.Button($"F{i + 1}: {OfficialModes[i]}")) SetMode(OfficialModes[i]);
            GUILayout.Space(6); hasPoseTarget = GUILayout.Toggle(hasPoseTarget, "Enable joint-space target");
            GUILayout.BeginHorizontal(); if (GUILayout.Button("Clear")) ClearPoseTarget(); if (GUILayout.Button("Neutral")) SetNeutralPose(); if (GUILayout.Button("Capture Current")) CaptureCurrent(); GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var joint in G1DemoRigBuilder.JointNames)
            {
                targetJointAngles.TryGetValue(joint, out var value);
                var updated = GUILayout.HorizontalSlider(value, -3.14159f, 3.14159f);
                if (!Mathf.Approximately(value, updated)) SetJointAngle(joint, updated);
                GUILayout.Label($"{joint}: {value:F2} rad");
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
