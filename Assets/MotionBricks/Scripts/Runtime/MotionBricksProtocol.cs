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
        [JsonProperty("seq")] public long Sequence;
        [JsonProperty("move_x")] public float MoveX;
        [JsonProperty("move_y")] public float MoveY;
        [JsonProperty("look_yaw")] public float LookYaw;
        [JsonProperty("style")] public string Style = "default";
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
