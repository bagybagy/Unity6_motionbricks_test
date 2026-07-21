using System;
using System.Collections.Generic;
using UnityEngine;

namespace MotionBricks.Unity
{
    /// <summary>Maps G1's 29 named hinge axes to an assigned valid Unity Humanoid Avatar.</summary>
    [DisallowMultipleComponent]
    public sealed class G1HumanoidRetargeter : MonoBehaviour
    {
        [Serializable]
        public sealed class MuscleBinding
        {
            public string jointName;
            public string muscleName;
            [Tooltip("G1 MJCF hinge limit in radians.")] public float minRadians;
            [Tooltip("G1 MJCF hinge limit in radians.")] public float maxRadians;
            [Tooltip("Allows Avatar-specific direction correction without changing the wire pose.")] public float sign = 1f;
        }

        // Unity Humanoid has no independent wrist twist muscle. The final wrist axis shares
        // the hand twist channel, so a target can still be represented deterministically.
        public static readonly MuscleBinding[] DefaultBindings =
        {
            B("left_hip_pitch_joint", "Left Upper Leg Front-Back", -2.5307f, 2.8798f), B("left_hip_roll_joint", "Left Upper Leg In-Out", -.5236f, 2.9671f), B("left_hip_yaw_joint", "Left Upper Leg Twist In-Out", -2.7576f, 2.7576f), B("left_knee_joint", "Left Lower Leg Stretch", -.087267f, 2.8798f), B("left_ankle_pitch_joint", "Left Foot Up-Down", -.87267f, .5236f), B("left_ankle_roll_joint", "Left Foot Twist In-Out", -.2618f, .2618f),
            B("right_hip_pitch_joint", "Right Upper Leg Front-Back", -2.5307f, 2.8798f), B("right_hip_roll_joint", "Right Upper Leg In-Out", -2.9671f, .5236f), B("right_hip_yaw_joint", "Right Upper Leg Twist In-Out", -2.7576f, 2.7576f), B("right_knee_joint", "Right Lower Leg Stretch", -.087267f, 2.8798f), B("right_ankle_pitch_joint", "Right Foot Up-Down", -.87267f, .5236f), B("right_ankle_roll_joint", "Right Foot Twist In-Out", -.2618f, .2618f),
            B("waist_yaw_joint", "Spine Twist Left-Right", -2.618f, 2.618f), B("waist_roll_joint", "Spine Left-Right", -.52f, .52f), B("waist_pitch_joint", "Spine Front-Back", -.52f, .52f),
            B("left_shoulder_pitch_joint", "Left Arm Front-Back", -3.0892f, 2.6704f), B("left_shoulder_roll_joint", "Left Arm Down-Up", -1.5882f, 2.2515f), B("left_shoulder_yaw_joint", "Left Arm Twist In-Out", -2.618f, 2.618f), B("left_elbow_joint", "Left Forearm Stretch", -1.0472f, 2.0944f), B("left_wrist_roll_joint", "Left Forearm Twist In-Out", -1.97222f, 1.97222f), B("left_wrist_pitch_joint", "Left Hand Down-Up", -1.61443f, 1.61443f), B("left_wrist_yaw_joint", "Left Hand In-Out", -1.61443f, 1.61443f),
            B("right_shoulder_pitch_joint", "Right Arm Front-Back", -3.0892f, 2.6704f), B("right_shoulder_roll_joint", "Right Arm Down-Up", -2.2515f, 1.5882f), B("right_shoulder_yaw_joint", "Right Arm Twist In-Out", -2.618f, 2.618f), B("right_elbow_joint", "Right Forearm Stretch", -1.0472f, 2.0944f), B("right_wrist_roll_joint", "Right Forearm Twist In-Out", -1.97222f, 1.97222f), B("right_wrist_pitch_joint", "Right Hand Down-Up", -1.61443f, 1.61443f), B("right_wrist_yaw_joint", "Right Hand In-Out", -1.61443f, 1.61443f),
        };

        [SerializeField] private Animator animator;
        [SerializeField] private bool applyReceivedPoses = true;
        [SerializeField] private bool applyRootTransform = true;
        [SerializeField] private List<MuscleBinding> bindings = new(DefaultBindings);
        private readonly Dictionary<string, int> muscleIndices = new(StringComparer.Ordinal);
        private HumanPoseHandler poseHandler;
        private Vector3 comparisonOffset;
        private bool comparisonOffsetCaptured;

        public bool IsHumanoidAvatarValid => animator != null && animator.avatar != null && animator.avatar.isHuman && animator.avatar.isValid && animator.isHuman;
        public int BindingCount => bindings?.Count ?? 0;

        public void SetAnimator(Animator value)
        {
            // The same Animator may receive a newly rebuilt runtime Avatar.
            // Always invalidate a handler that could still reference the old Avatar.
            poseHandler?.Dispose();
            poseHandler = null;
            animator = value;
        }

        private void Awake()
        {
            RebuildMuscleIndices();
            CaptureComparisonOffset();
        }
        private void OnDestroy() => poseHandler?.Dispose();
        private void OnValidate() => RebuildMuscleIndices();

        public void RebuildMuscleIndices()
        {
            muscleIndices.Clear();
            for (var i = 0; i < HumanTrait.MuscleCount; i++) muscleIndices[HumanTrait.MuscleName[i]] = i;
        }

        public bool ApplyJointAngles(IReadOnlyDictionary<string, float> jointAngles)
        {
            if (jointAngles == null || !EnsureHandler()) return false;
            var pose = new HumanPose();
            poseHandler.GetHumanPose(ref pose);
            foreach (var binding in bindings)
                if (binding != null && jointAngles.TryGetValue(binding.jointName, out var radians) && muscleIndices.TryGetValue(binding.muscleName, out var index))
                    pose.muscles[index] = JointRadiansToMuscle(radians, binding);
            poseHandler.SetHumanPose(ref pose);
            return true;
        }

        /// <summary>Reads the current Humanoid pose into G1-radian targets when an Avatar is available.</summary>
        public bool TryCaptureJointAngles(IDictionary<string, float> jointAngles)
        {
            if (jointAngles == null || !EnsureHandler()) return false;
            var pose = new HumanPose();
            poseHandler.GetHumanPose(ref pose);
            foreach (var binding in bindings)
                if (binding != null && muscleIndices.TryGetValue(binding.muscleName, out var index))
                    jointAngles[binding.jointName] = MuscleToJointRadians(pose.muscles[index], binding);
            return true;
        }

        public void Apply(PoseMessage message)
        {
            if (!applyReceivedPoses || message?.Joints == null) return;
            if (applyRootTransform && !comparisonOffsetCaptured) CaptureComparisonOffset();
            var angles = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var (joint, rotation) in message.Joints)
            {
                if (rotation is not { Length: >= 4 }) continue;
                angles[joint] = ExtractJointRadians(joint, new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]));
            }
            ApplyJointAngles(angles);
            if (applyRootTransform && message.RootPosition is { Length: >= 3 })
            {
                transform.position = comparisonOffset + message.GetRootPosition();
                transform.rotation = message.GetRootRotation();
            }
        }

        private void CaptureComparisonOffset()
        {
            comparisonOffset = transform.position;
            comparisonOffsetCaptured = true;
        }

        /// <summary>Extracts the signed twist about the named G1 hinge axis in Unity space.</summary>
        public static float ExtractJointRadians(string jointName, Quaternion rotation) => ExtractSignedRadians(rotation, GetUnityAxis(jointName));

        /// <summary>MJCF-to-Unity hinge axes: pitch (-X), roll (+Z), yaw (+Y).</summary>
        public static Vector3 GetUnityAxis(string jointName)
        {
            if (string.IsNullOrEmpty(jointName)) return Vector3.left;
            if (jointName.Contains("roll")) return Vector3.forward;
            if (jointName.Contains("yaw")) return Vector3.up;
            // Knees and elbows are flexion/pitch axes in the G1 description.
            return Vector3.left;
        }

        /// <summary>Pure swing-twist projection; returns the principal signed angle in [-pi, pi].</summary>
        public static float ExtractSignedRadians(Quaternion rotation, Vector3 axis)
        {
            var rotationMagnitudeSquared = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
            if (axis.sqrMagnitude < 0.000001f || rotationMagnitudeSquared < 0.000001f) return 0f;
            axis.Normalize();
            rotation = Normalize(rotation);
            var vector = new Vector3(rotation.x, rotation.y, rotation.z);
            var projection = axis * Vector3.Dot(vector, axis);
            var twist = Normalize(new Quaternion(projection.x, projection.y, projection.z, rotation.w));
            if (twist.w < 0f) twist = new Quaternion(-twist.x, -twist.y, -twist.z, -twist.w);
            return 2f * Mathf.Atan2(Vector3.Dot(new Vector3(twist.x, twist.y, twist.z), axis), twist.w);
        }

        private bool EnsureHandler()
        {
            if (!IsHumanoidAvatarValid) return false;
            if (poseHandler != null) return true;
            try { poseHandler = new HumanPoseHandler(animator.avatar, animator.transform); return true; }
            catch (ArgumentException) { return false; }
        }

        private static Quaternion Normalize(Quaternion value)
        {
            var inverseMagnitude = 1f / Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            return new Quaternion(value.x * inverseMagnitude, value.y * inverseMagnitude, value.z * inverseMagnitude, value.w * inverseMagnitude);
        }
        public static float JointRadiansToMuscle(float radians, MuscleBinding binding)
        {
            var sign = Mathf.Approximately(binding.sign, 0f) ? 1f : binding.sign;
            var range = radians >= 0f ? binding.maxRadians : -binding.minRadians;
            var normalized = range > .00001f ? Mathf.Clamp(radians / range, -1f, 1f) : 0f;
            return Mathf.Clamp(normalized * sign, -1f, 1f);
        }

        public static float MuscleToJointRadians(float muscle, MuscleBinding binding)
        {
            var sign = Mathf.Approximately(binding.sign, 0f) ? 1f : binding.sign;
            var normalized = Mathf.Clamp(muscle / sign, -1f, 1f);
            return normalized >= 0f
                ? normalized * binding.maxRadians
                : normalized * -binding.minRadians;
        }

        private static MuscleBinding B(string joint, string muscle, float minRadians, float maxRadians, float sign = 1f) => new() { jointName = joint, muscleName = muscle, minRadians = minRadians, maxRadians = maxRadians, sign = sign };
    }
}
