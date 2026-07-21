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
        }

        [SerializeField] private Transform rootTransform;
        [SerializeField] private bool applyRootPosition = true;
        [SerializeField] private bool applyRootRotation = true;
        [SerializeField, Min(0f)] private float positionScale = 1f;
        [SerializeField] private List<JointBinding> jointBindings = new();

        private readonly Dictionary<string, Transform> joints = new(StringComparer.Ordinal);

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
                joints[binding.jointName] = binding.transform;
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

            if (pose.Joints == null)
                return;
            foreach (var (jointName, values) in pose.Joints)
            {
                if (!joints.TryGetValue(jointName, out var joint) || values is not { Length: >= 4 })
                    continue;
                joint.localRotation = new Quaternion(values[0], values[1], values[2], values[3]);
            }
        }
    }
}
