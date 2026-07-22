using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace MotionBricks.Unity
{
    [Serializable]
    public sealed class ControlMessage
    {
        [JsonProperty("type")] public string Type = "control";
        [JsonProperty("session_id")] public string SessionId;
        [JsonProperty("seq")] public long Sequence;
        [JsonProperty("move_x")] public float MoveX;
        [JsonProperty("move_y")] public float MoveY;
        [JsonProperty("look_yaw")] public float LookYaw;
        [JsonProperty("style")] public string Style = "default";
        // Optional fixed navigation goal. Omitted/false retains the original WASD protocol.
        [JsonProperty("has_target")] public bool HasTarget;
        [JsonProperty("target_position")] public float[] TargetPosition;
        [JsonProperty("target_yaw")] public float TargetYaw;
        // Optional joint-space target. Angles use the G1 hinge convention (radians).
        [JsonProperty("has_pose_target")] public bool HasPoseTarget;
        [JsonProperty("target_joint_angles")] public Dictionary<string, float> TargetJointAngles;
    }

    [Serializable]
    public sealed class PoseMessage
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("seq")] public long Sequence;
        [JsonProperty("timestamp")] public double Timestamp;
        [JsonProperty("root_position")] public float[] RootPosition;
        [JsonProperty("root_rotation")] public float[] RootRotation;
        [JsonProperty("joints")] public Dictionary<string, float[]> Joints;
        [JsonProperty("joint_angles")] public Dictionary<string, float> JointAngles;
        [JsonProperty("plan_root_positions")] public float[][] PlanRootPositions;
        [JsonProperty("goal_root_position")] public float[] GoalRootPosition;
        [JsonProperty("goal_root_rotation")] public float[] GoalRootRotation;
        [JsonProperty("goal_joints")] public Dictionary<string, float[]> GoalJoints;

        public bool IsPose => string.Equals(Type, "pose", StringComparison.Ordinal);

        public Vector3 GetRootPosition()
        {
            return RootPosition is { Length: >= 3 }
                ? new Vector3(RootPosition[0], RootPosition[1], RootPosition[2])
                : Vector3.zero;
        }

        public Quaternion GetRootRotation()
        {
            return RootRotation is { Length: >= 4 }
                ? new Quaternion(RootRotation[0], RootRotation[1], RootRotation[2], RootRotation[3])
                : Quaternion.identity;
        }

        public Vector3 GetGoalRootPosition()
        {
            return GoalRootPosition is { Length: >= 3 }
                ? new Vector3(GoalRootPosition[0], GoalRootPosition[1], GoalRootPosition[2])
                : Vector3.zero;
        }

        public Quaternion GetGoalRootRotation()
        {
            return GoalRootRotation is { Length: >= 4 }
                ? new Quaternion(GoalRootRotation[0], GoalRootRotation[1], GoalRootRotation[2], GoalRootRotation[3])
                : Quaternion.identity;
        }

        public static bool TryParse(string json, out PoseMessage pose)
        {
            pose = null;
            try
            {
                var parsed = JsonConvert.DeserializeObject<PoseMessage>(json);
                if (parsed == null || !parsed.IsPose)
                    return false;
                pose = parsed;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
