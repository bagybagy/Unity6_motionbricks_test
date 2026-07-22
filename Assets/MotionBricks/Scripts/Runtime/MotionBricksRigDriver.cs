using System;
using System.Collections.Generic;
using UnityEngine;

namespace MotionBricks.Unity
{
    /// <summary>Applies poses received from the MotionBricks UDP bridge to any assigned rig.</summary>
    public sealed class MotionBricksRigDriver : MonoBehaviour
    {
        [Serializable]
        public sealed class JointBinding
        {
            [Tooltip("The joint key used in the pose JSON message.")]
            public string jointName;
            public Transform transform;
            [Tooltip("Unity-local hinge axis for exact joint-angle messages.")]
            public Vector3 localAxis;
            [Tooltip("Joint's MJCF/body rest rotation. Incoming hinge rotations are composed after it.")]
            public Quaternion restLocalRotation = Quaternion.identity;
        }

        [SerializeField] private Transform rootTransform;
        [SerializeField] private bool applyRootPosition = true;
        [SerializeField] private bool applyRootRotation = true;
        [SerializeField, Min(0f)] private float positionScale = 1f;
        [SerializeField] private List<JointBinding> jointBindings = new();

        private readonly Dictionary<string, JointBinding> joints = new(StringComparer.Ordinal);

        /// <summary>Number of valid joint-name to Transform mappings currently registered.</summary>
        public int BindingCount => joints.Count;

        private void Awake() => RebuildBindings();

        private void OnValidate() => RebuildBindings();

        public void RebuildBindings()
        {
            joints.Clear();
            foreach (var binding in jointBindings)
            {
                if (binding?.transform == null || string.IsNullOrWhiteSpace(binding.jointName))
                    continue;
                joints[binding.jointName] = binding;
            }
        }

        /// <summary>Replaces inspector bindings with generated or programmatic rig bindings.</summary>
        public void SetBindings(IEnumerable<JointBinding> bindings)
        {
            jointBindings = bindings == null ? new List<JointBinding>() : new List<JointBinding>(bindings);
            RebuildBindings();
        }

        public void Apply(PoseMessage pose)
        {
            if (pose == null)
                return;

            var root = rootTransform == null ? transform : rootTransform;
            if (applyRootPosition)
                root.position = pose.GetRootPosition() * positionScale;
            if (applyRootRotation)
                root.rotation = pose.GetRootRotation();

            foreach (var (jointName, binding) in joints)
            {
                var rest = IsZeroQuaternion(binding.restLocalRotation)
                    ? Quaternion.identity
                    : binding.restLocalRotation;

                // JointAngles are the original MuJoCo hinge coordinates, so prefer them
                // over a converted quaternion whenever the server supplies both.
                if (pose.JointAngles != null &&
                    pose.JointAngles.TryGetValue(jointName, out var radians) &&
                    binding.localAxis.sqrMagnitude > .000001f)
                {
                    binding.transform.localRotation = rest * Quaternion.AngleAxis(
                        radians * Mathf.Rad2Deg,
                        binding.localAxis.normalized);
                    continue;
                }

                if (pose.Joints != null &&
                    pose.Joints.TryGetValue(jointName, out var values) &&
                    values is { Length: >= 4 })
                {
                    binding.transform.localRotation = rest * new Quaternion(
                        values[0], values[1], values[2], values[3]);
                }
            }
        }

        private static bool IsZeroQuaternion(Quaternion value) =>
            Mathf.Abs(value.x) + Mathf.Abs(value.y) + Mathf.Abs(value.z) + Mathf.Abs(value.w) < .000001f;
    }
}
