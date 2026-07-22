using System;
using System.Collections.Generic;
using UnityEngine;

namespace MotionBricks.Unity
{
    /// <summary>
    /// Retargets G1's generated FK hierarchy to a valid Unity Humanoid Avatar.
    /// The Humanoid pose is used once to establish a stable zero pose; live motion is
    /// applied as root-space bone rotation deltas so it preserves G1's rotation order.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class G1HumanoidRetargeter : MonoBehaviour
    {
        [Serializable]
        public sealed class MuscleBinding
        {
            public string jointName;
            public string muscleName;
            [Tooltip("Maps positive G1 qpos to the Humanoid muscle's anatomical direction.")]
            public float sign = 1f;
            [Tooltip("Semantic Humanoid angle at G1 qpos=0, in radians.")]
            public float zeroOffsetRadians;
            [Tooltip("False when Unity Humanoid has no semantically equivalent muscle.")]
            public bool applyToHumanoid = true;
        }

        // Kept as the zero-pose definition. Live retargeting deliberately does not map
        // each hinge to a HumanPose muscle: G1's compound links have a different axis
        // hierarchy from a Humanoid Avatar.
        public static readonly MuscleBinding[] DefaultBindings =
        {
            B("left_hip_pitch_joint", "Left Upper Leg Front-Back"), B("left_hip_roll_joint", "Left Upper Leg In-Out"), B("left_hip_yaw_joint", "Left Upper Leg Twist In-Out"), B("left_knee_joint", "Left Lower Leg Stretch", -1f, -.1598f), B("left_ankle_pitch_joint", "Left Foot Up-Down"), Unsupported("left_ankle_roll_joint", "Left Foot Twist In-Out"),
            B("right_hip_pitch_joint", "Right Upper Leg Front-Back"), B("right_hip_roll_joint", "Right Upper Leg In-Out", -1f), B("right_hip_yaw_joint", "Right Upper Leg Twist In-Out", -1f), B("right_knee_joint", "Right Lower Leg Stretch", -1f, -.1598f), B("right_ankle_pitch_joint", "Right Foot Up-Down"), Unsupported("right_ankle_roll_joint", "Right Foot Twist In-Out"),
            B("waist_yaw_joint", "Spine Twist Left-Right", -1f), B("waist_roll_joint", "Spine Left-Right"), B("waist_pitch_joint", "Spine Front-Back", -1f),
            B("left_shoulder_pitch_joint", "Left Arm Front-Back", 1f, .0819f), B("left_shoulder_roll_joint", "Left Arm Down-Up", 1f, -1.313f), B("left_shoulder_yaw_joint", "Left Arm Twist In-Out"), B("left_elbow_joint", "Left Forearm Stretch", 1f, -1.3877f), B("left_wrist_roll_joint", "Left Forearm Twist In-Out"), B("left_wrist_pitch_joint", "Left Hand Down-Up", -1f), B("left_wrist_yaw_joint", "Left Hand In-Out"),
            B("right_shoulder_pitch_joint", "Right Arm Front-Back", 1f, .0819f), B("right_shoulder_roll_joint", "Right Arm Down-Up", -1f, -1.313f), B("right_shoulder_yaw_joint", "Right Arm Twist In-Out", -1f), B("right_elbow_joint", "Right Forearm Stretch", 1f, -1.3877f), B("right_wrist_roll_joint", "Right Forearm Twist In-Out", -1f), B("right_wrist_pitch_joint", "Right Hand Down-Up", -1f), B("right_wrist_yaw_joint", "Right Hand In-Out", -1f),
        };

        private readonly struct BoneBinding
        {
            public readonly string sourceJoint;
            public readonly HumanBodyBones targetBone;
            public BoneBinding(string sourceJoint, HumanBodyBones targetBone)
            {
                this.sourceJoint = sourceJoint;
                this.targetBone = targetBone;
            }
        }

        private readonly struct LimbData
        {
            public readonly Vector3 sourceProximalCurrent;
            public readonly Vector3 sourceProximalZero;
            public readonly Vector3 sourceMidCurrent;
            public readonly Vector3 sourceMidZero;
            public readonly Vector3 sourceEndCurrent;
            public readonly Vector3 sourceEndZero;
            public readonly Vector3 targetProximalZero;
            public readonly Vector3 targetMidZero;
            public readonly Vector3 targetEndZero;
            public readonly Transform targetProximal;
            public readonly Transform targetMid;
            public readonly Transform targetEnd;

            public LimbData(
                Vector3 sourceProximalCurrent, Vector3 sourceProximalZero, Vector3 sourceMidCurrent, Vector3 sourceMidZero,
                Vector3 sourceEndCurrent, Vector3 sourceEndZero, Vector3 targetProximalZero,
                Vector3 targetMidZero, Vector3 targetEndZero, Transform targetProximal,
                Transform targetMid, Transform targetEnd)
            {
                this.sourceProximalCurrent = sourceProximalCurrent;
                this.sourceProximalZero = sourceProximalZero;
                this.sourceMidCurrent = sourceMidCurrent;
                this.sourceMidZero = sourceMidZero;
                this.sourceEndCurrent = sourceEndCurrent;
                this.sourceEndZero = sourceEndZero;
                this.targetProximalZero = targetProximalZero;
                this.targetMidZero = targetMidZero;
                this.targetEndZero = targetEndZero;
                this.targetProximal = targetProximal;
                this.targetMid = targetMid;
                this.targetEnd = targetEnd;
            }
        }

        private readonly struct BodyBasis
        {
            public readonly Vector3 lateral;
            public readonly Vector3 up;
            public readonly Vector3 forward;

            public BodyBasis(Vector3 lateral, Vector3 up, Vector3 forward)
            {
                this.lateral = lateral;
                this.up = up;
                this.forward = forward;
            }
        }

        // Parent-to-child ordering is essential: every target orientation is expressed in
        // target-root space, and a parent is set before its descendants.
        private static readonly BoneBinding[] CompositeBindings =
        {
            new("left_hip_yaw_joint", HumanBodyBones.LeftUpperLeg),
            new("right_hip_yaw_joint", HumanBodyBones.RightUpperLeg),
            new("left_knee_joint", HumanBodyBones.LeftLowerLeg),
            new("right_knee_joint", HumanBodyBones.RightLowerLeg),
            new("left_ankle_roll_joint", HumanBodyBones.LeftFoot),
            new("right_ankle_roll_joint", HumanBodyBones.RightFoot),
            new("waist_pitch_joint", HumanBodyBones.Spine),
            new("left_shoulder_yaw_joint", HumanBodyBones.LeftUpperArm),
            new("right_shoulder_yaw_joint", HumanBodyBones.RightUpperArm),
            new("left_elbow_joint", HumanBodyBones.LeftLowerArm),
            new("right_elbow_joint", HumanBodyBones.RightLowerArm),
            new("left_wrist_yaw_joint", HumanBodyBones.LeftHand),
            new("right_wrist_yaw_joint", HumanBodyBones.RightHand),
        };

        [SerializeField] private Animator animator;
        [SerializeField] private G1DemoRigBuilder sourceRig;
        [SerializeField] private bool applyReceivedPoses = true;
        [SerializeField] private bool applyRootTransform = true;

        private readonly Dictionary<string, int> muscleIndices = new(StringComparer.Ordinal);
        private readonly Dictionary<HumanBodyBones, Transform> targetBones = new();
        private readonly Dictionary<HumanBodyBones, Quaternion> targetReferenceRootRotations = new();
        private readonly Dictionary<HumanBodyBones, Vector3> targetReferenceRootPositions = new();
        private readonly Dictionary<string, float> lastJointAngles = new(StringComparer.Ordinal);
        private HumanPoseHandler poseHandler;
        private float[] g1ZeroMuscles;
        private Vector3 comparisonOffset;
        private bool comparisonOffsetCaptured;
        private float streamedRootBaseY;
        private bool streamedRootBaseYCaptured;

        public bool IsHumanoidAvatarValid => animator != null && animator.avatar != null && animator.avatar.isHuman && animator.avatar.isValid && animator.isHuman;
        public int BindingCount => DefaultBindings.Length;

        public void SetAnimator(Animator value)
        {
            poseHandler?.Dispose();
            poseHandler = null;
            g1ZeroMuscles = null;
            targetBones.Clear();
            targetReferenceRootRotations.Clear();
            targetReferenceRootPositions.Clear();
            lastJointAngles.Clear();
            animator = value;
            streamedRootBaseY = 0f;
            streamedRootBaseYCaptured = false;
        }

        /// <summary>Overrides automatic source-rig discovery and invalidates captured references.</summary>
        public void SetSourceRig(G1DemoRigBuilder value)
        {
            sourceRig = value;
            lastJointAngles.Clear();
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
            if (jointAngles == null || !EnsureReady()) return false;
            var complete = new Dictionary<string, float>(G1DemoRigBuilder.JointCount, StringComparer.Ordinal);
            foreach (var jointName in G1DemoRigBuilder.JointNames)
            {
                jointAngles.TryGetValue(jointName, out var radians);
                complete[jointName] = radians;
                lastJointAngles[jointName] = radians;
            }

            if (!sourceRig.ApplyJointAngles(complete))
                return false;
            ApplyCompositeBoneRotations();
            return true;
        }

        /// <summary>Returns the complete last applied 29-axis qpos dictionary.</summary>
        public bool TryCaptureJointAngles(IDictionary<string, float> jointAngles)
        {
            if (jointAngles == null || !EnsureReady()) return false;
            foreach (var jointName in G1DemoRigBuilder.JointNames)
            {
                lastJointAngles.TryGetValue(jointName, out var radians);
                jointAngles[jointName] = radians;
            }
            return true;
        }

        public void Apply(PoseMessage message)
        {
            if (!applyReceivedPoses || message == null) return;
            if (applyRootTransform && !comparisonOffsetCaptured) CaptureComparisonOffset();
            if (applyRootTransform && message.RootPosition is { Length: >= 3 })
            {
                var streamedRoot = message.GetRootPosition();
                if (!streamedRootBaseYCaptured)
                {
                    streamedRootBaseY = streamedRoot.y;
                    streamedRootBaseYCaptured = true;
                }
                streamedRoot.y -= streamedRootBaseY;
                transform.position = comparisonOffset + streamedRoot;
                transform.rotation = message.GetRootRotation();
            }

            IReadOnlyDictionary<string, float> angles = message.JointAngles;
            if (angles == null || angles.Count == 0)
            {
                var extracted = new Dictionary<string, float>(StringComparer.Ordinal);
                if (message.Joints != null)
                    foreach (var (joint, rotation) in message.Joints)
                        if (rotation is { Length: >= 4 })
                            extracted[joint] = ExtractJointRadians(joint, new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]));
                angles = extracted;
            }
            ApplyJointAngles(angles);
        }

        private bool EnsureReady()
        {
            if (!EnsureHandler()) return false;
            if (sourceRig == null)
            {
                var candidates = FindObjectsByType<G1DemoRigBuilder>(FindObjectsSortMode.None);
                if (candidates.Length != 1) return false;
                sourceRig = candidates[0];
            }
            if (sourceRig == null) return false;
            if (!sourceRig.HasGeneratedRig) sourceRig.Build();
            if (!sourceRig.HasGeneratedRig) return false;
            return CaptureTargetReferences();
        }

        private bool CaptureTargetReferences()
        {
            if (targetReferenceRootRotations.Count == CompositeBindings.Length &&
                targetReferenceRootPositions.Count == CompositeBindings.Length)
                return true;
            targetBones.Clear();
            targetReferenceRootRotations.Clear();
            targetReferenceRootPositions.Clear();
            var root = animator.transform;
            var inverseRoot = Quaternion.Inverse(root.rotation);
            foreach (var binding in CompositeBindings)
            {
                var bone = animator.GetBoneTransform(binding.targetBone);
                if (bone == null) return false;
                targetBones[binding.targetBone] = bone;
                targetReferenceRootRotations[binding.targetBone] = inverseRoot * bone.rotation;
                targetReferenceRootPositions[binding.targetBone] = inverseRoot * (bone.position - root.position);
            }
            return true;
        }

        private void ApplyCompositeBoneRotations()
        {
            var targetRootRotation = animator.transform.rotation;
            foreach (var binding in CompositeBindings)
            {
                if (!sourceRig.TryGetJointRootRotations(binding.sourceJoint, out var current, out var zero) ||
                    !targetBones.TryGetValue(binding.targetBone, out var target) ||
                    !targetReferenceRootRotations.TryGetValue(binding.targetBone, out var reference))
                    continue;
                // Transfer the source rotation in its reference-bone frame. Pre-multiplying
                // a source-root-space delta would assume source and target bind axes match.
                target.rotation = targetRootRotation * reference * Quaternion.Inverse(zero) * current;
            }
            ApplyAnatomicalSpineRotation(targetRootRotation);
            ApplyLimbPositionIk();
            ApplyAnatomicalWristRotation("left", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, targetRootRotation);
            ApplyAnatomicalWristRotation("right", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, targetRootRotation);
        }

        private void ApplyAnatomicalSpineRotation(Quaternion targetRootRotation)
        {
            if (!sourceRig.TryGetJointRootRotations("waist_pitch_joint", out var current, out var zero) ||
                !targetBones.TryGetValue(HumanBodyBones.Spine, out var spine) ||
                !targetReferenceRootRotations.TryGetValue(HumanBodyBones.Spine, out var reference))
                return;

            // Waist axes are defined in G1 root/anatomical space. Applying this spatial
            // delta before the target bind rotation avoids depending on an FBX bone's local axes.
            var spatialDelta = current * Quaternion.Inverse(zero);
            spine.rotation = targetRootRotation * spatialDelta * reference;
        }

        private void ApplyAnatomicalWristRotation(
            string side, HumanBodyBones upperBone, HumanBodyBones lowerBone, HumanBodyBones handBone,
            Quaternion targetRootRotation)
        {
            if (!sourceRig.TryGetJointRootPositions($"{side}_shoulder_yaw_joint", out var sourceUpperCurrent, out var sourceUpperZero) ||
                !sourceRig.TryGetJointRootPositions($"{side}_elbow_joint", out var sourceLowerCurrent, out var sourceLowerZero) ||
                !sourceRig.TryGetJointRootPositions($"{side}_wrist_roll_joint", out var sourceHandCurrent, out var sourceHandZero) ||
                !sourceRig.TryGetJointRootRotations($"{side}_wrist_yaw_joint", out var sourceRotationCurrent, out var sourceRotationZero) ||
                !targetBones.TryGetValue(upperBone, out var upper) ||
                !targetBones.TryGetValue(lowerBone, out var lower) ||
                !targetBones.TryGetValue(handBone, out var hand) ||
                !targetReferenceRootPositions.TryGetValue(upperBone, out var targetUpperZero) ||
                !targetReferenceRootPositions.TryGetValue(lowerBone, out var targetLowerZero) ||
                !targetReferenceRootPositions.TryGetValue(handBone, out var targetHandZero) ||
                !targetReferenceRootRotations.TryGetValue(handBone, out var targetRotationZero) ||
                !TryCreateAnatomicalLimbFrame(sourceUpperZero, sourceLowerZero, sourceHandZero, out var sourceFrameZero) ||
                !TryCreateAnatomicalLimbFrame(sourceUpperCurrent, sourceLowerCurrent, sourceHandCurrent, out var sourceFrameCurrent) ||
                !TryCreateAnatomicalLimbFrame(targetUpperZero, targetLowerZero, targetHandZero, out var targetFrameZero))
                return;

            var inverseTargetRoot = Quaternion.Inverse(targetRootRotation);
            var targetUpperCurrent = inverseTargetRoot * (upper.position - animator.transform.position);
            var targetLowerCurrent = inverseTargetRoot * (lower.position - animator.transform.position);
            var targetHandCurrent = inverseTargetRoot * (hand.position - animator.transform.position);
            if (!TryCreateAnatomicalLimbFrame(targetUpperCurrent, targetLowerCurrent, targetHandCurrent, out var targetFrameCurrent))
                return;

            var sourceRelativeZero = Quaternion.Inverse(sourceFrameZero) * sourceRotationZero;
            var sourceRelativeCurrent = Quaternion.Inverse(sourceFrameCurrent) * sourceRotationCurrent;
            var targetRelativeZero = Quaternion.Inverse(targetFrameZero) * targetRotationZero;
            var bindOffset = Quaternion.Inverse(sourceRelativeZero) * targetRelativeZero;
            hand.rotation = targetRootRotation * targetFrameCurrent * sourceRelativeCurrent * bindOffset;
        }

        private static bool TryCreateAnatomicalLimbFrame(
            Vector3 proximal, Vector3 middle, Vector3 end, out Quaternion frame)
        {
            var forward = end - middle;
            var right = Vector3.Cross(middle - proximal, forward);
            if (forward.sqrMagnitude < .00000001f || right.sqrMagnitude < .00000001f)
            {
                frame = Quaternion.identity;
                return false;
            }
            forward.Normalize();
            right.Normalize();
            var up = Vector3.Cross(forward, right).normalized;
            frame = Quaternion.LookRotation(forward, up);
            return true;
        }

        // The G1 link axes and proportions do not match every Humanoid. Keep the bind-frame
        // orientation transfer above, then solve each terminal position from measured bind poses.
        private void ApplyLimbPositionIk()
        {
            ApplyLimbPairIk(
                "left_hip_pitch_joint", "left_knee_joint", "left_ankle_roll_joint",
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                "right_hip_pitch_joint", "right_knee_joint", "right_ankle_roll_joint",
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
            ApplyLimbPairIk(
                "left_shoulder_pitch_joint", "left_elbow_joint", "left_wrist_roll_joint",
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                "right_shoulder_pitch_joint", "right_elbow_joint", "right_wrist_roll_joint",
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        }

        private void ApplyLimbPairIk(
            string sourceProximal, string sourceMid, string sourceEnd,
            HumanBodyBones targetProximal, HumanBodyBones targetMid, HumanBodyBones targetEnd,
            string oppositeSourceProximal, string oppositeSourceMid, string oppositeSourceEnd,
            HumanBodyBones oppositeTargetProximal, HumanBodyBones oppositeTargetMid, HumanBodyBones oppositeTargetEnd)
        {
            if (!TryGetLimbData(sourceProximal, sourceMid, sourceEnd, targetProximal, targetMid, targetEnd, out var limb) ||
                !TryGetLimbData(oppositeSourceProximal, oppositeSourceMid, oppositeSourceEnd,
                    oppositeTargetProximal, oppositeTargetMid, oppositeTargetEnd, out var opposite))
                return;

            if (!TryCreateBodyBasis(limb.sourceEndZero, opposite.sourceEndZero,
                    (limb.sourceProximalZero + opposite.sourceProximalZero) * .5f,
                    (limb.sourceEndZero + opposite.sourceEndZero) * .5f, out var sourceBasis) ||
                !TryCreateBodyBasis(limb.targetEndZero, opposite.targetEndZero,
                    (limb.targetProximalZero + opposite.targetProximalZero) * .5f,
                    (limb.targetEndZero + opposite.targetEndZero) * .5f, out var targetBasis))
                return;

            var sourceLateralLength = (limb.sourceEndZero - opposite.sourceEndZero).magnitude;
            var targetLateralLength = (limb.targetEndZero - opposite.targetEndZero).magnitude;
            var sourceLimbLength = ((limb.sourceEndZero - limb.sourceProximalZero).magnitude +
                                    (opposite.sourceEndZero - opposite.sourceProximalZero).magnitude) * .5f;
            var targetLimbLength = ((limb.targetEndZero - limb.targetProximalZero).magnitude +
                                    (opposite.targetEndZero - opposite.targetProximalZero).magnitude) * .5f;
            if (sourceLateralLength < .00001f || sourceLimbLength < .00001f)
                return;

            var lateralScale = targetLateralLength / sourceLateralLength;
            var axialScale = targetLimbLength / sourceLimbLength;
            var targetRootRotation = animator.transform.rotation;
            ApplyTwoBoneIk(limb, sourceBasis, targetBasis, lateralScale, axialScale, targetRootRotation);
            ApplyTwoBoneIk(opposite, sourceBasis, targetBasis, lateralScale, axialScale, targetRootRotation);
        }

        private bool TryGetLimbData(
            string sourceProximal, string sourceMid, string sourceEnd,
            HumanBodyBones targetProximal, HumanBodyBones targetMid, HumanBodyBones targetEnd, out LimbData limb)
        {
            limb = default;
            if (!sourceRig.TryGetJointRootPositions(sourceProximal, out var sourceProximalCurrent, out var sourceProximalZero) ||
                !sourceRig.TryGetJointRootPositions(sourceMid, out var sourceMidCurrent, out var sourceMidZero) ||
                !sourceRig.TryGetJointRootPositions(sourceEnd, out var sourceEndCurrent, out var sourceEndZero) ||
                !targetBones.TryGetValue(targetProximal, out var proximal) ||
                !targetBones.TryGetValue(targetMid, out var mid) ||
                !targetBones.TryGetValue(targetEnd, out var end) ||
                !targetReferenceRootPositions.TryGetValue(targetProximal, out var targetProximalZero) ||
                !targetReferenceRootPositions.TryGetValue(targetMid, out var targetMidZero) ||
                !targetReferenceRootPositions.TryGetValue(targetEnd, out var targetEndZero))
                return false;
            limb = new LimbData(sourceProximalCurrent, sourceProximalZero, sourceMidCurrent, sourceMidZero, sourceEndCurrent,
                sourceEndZero, targetProximalZero, targetMidZero, targetEndZero, proximal, mid, end);
            return true;
        }

        private static bool TryCreateBodyBasis(
            Vector3 leftEnd, Vector3 rightEnd, Vector3 proximalCenter, Vector3 endCenter, out BodyBasis basis)
        {
            var lateral = leftEnd - rightEnd;
            var up = proximalCenter - endCenter;
            var forward = Vector3.Cross(up, lateral);
            if (lateral.sqrMagnitude < .00000001f || up.sqrMagnitude < .00000001f || forward.sqrMagnitude < .00000001f)
            {
                basis = default;
                return false;
            }
            up.Normalize();
            forward.Normalize();
            lateral = Vector3.Cross(forward, up).normalized;
            basis = new BodyBasis(lateral, up, forward);
            return true;
        }

        private void ApplyTwoBoneIk(
            LimbData limb, BodyBasis sourceBasis, BodyBasis targetBasis,
            float lateralScale, float axialScale, Quaternion targetRootRotation)
        {
            var endDisplacement = limb.sourceEndCurrent - limb.sourceEndZero;
            var goalRoot = limb.targetEndZero + MapBodyVector(endDisplacement, sourceBasis, targetBasis, lateralScale, axialScale);
            var goal = animator.transform.position + targetRootRotation * goalRoot;
            // Map the middle joint's displacement, not its absolute bend offset. This makes
            // the reference pole exact at qpos=0, so the analytic branch preserves the bind mid.
            var poleRoot = limb.targetMidZero + MapBodyVector(
                limb.sourceMidCurrent - limb.sourceMidZero, sourceBasis, targetBasis, lateralScale, axialScale);
            var pole = animator.transform.position + targetRootRotation * poleRoot;

            var upperLength = (limb.targetMidZero - limb.targetProximalZero).magnitude;
            var lowerLength = (limb.targetEndZero - limb.targetMidZero).magnitude;
            if (upperLength < .00001f || lowerLength < .00001f)
                return;

            // Foot/hand orientation comes from the bind-frame retarget, not from positional IK.
            var desiredEndRotation = limb.targetEnd.rotation;
            var toGoal = goal - limb.targetProximal.position;
            var goalDistance = toGoal.magnitude;
            if (goalDistance < .00001f)
                return;
            var minDistance = Mathf.Max(.00001f, Mathf.Abs(upperLength - lowerLength) + .00001f);
            var distance = Mathf.Clamp(goalDistance, minDistance, upperLength + lowerLength - .00001f);
            var direction = toGoal / goalDistance;
            var reachableGoal = limb.targetProximal.position + direction * distance;
            var poleDirection = Vector3.ProjectOnPlane(pole - limb.targetProximal.position, direction);
            if (poleDirection.sqrMagnitude < .00000001f)
                poleDirection = Vector3.ProjectOnPlane(targetRootRotation * (limb.targetMidZero - limb.targetProximalZero), direction);
            if (poleDirection.sqrMagnitude < .00000001f)
                return;
            poleDirection.Normalize();
            var along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
            var height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            var solvedMid = limb.targetProximal.position + direction * along + poleDirection * height;

            var currentUpper = limb.targetMid.position - limb.targetProximal.position;
            if (currentUpper.sqrMagnitude < .00000001f)
                return;
            limb.targetProximal.rotation = Quaternion.FromToRotation(currentUpper, solvedMid - limb.targetProximal.position) * limb.targetProximal.rotation;
            var currentLower = limb.targetEnd.position - limb.targetMid.position;
            if (currentLower.sqrMagnitude < .00000001f)
                return;
            limb.targetMid.rotation = Quaternion.FromToRotation(currentLower, reachableGoal - limb.targetMid.position) * limb.targetMid.rotation;
            limb.targetEnd.rotation = desiredEndRotation;
        }

        private static Vector3 MapBodyVector(
            Vector3 value, BodyBasis source, BodyBasis target, float lateralScale, float axialScale)
        {
            return target.lateral * (Vector3.Dot(value, source.lateral) * lateralScale) +
                   target.up * (Vector3.Dot(value, source.up) * axialScale) +
                   target.forward * (Vector3.Dot(value, source.forward) * axialScale);
        }

        private void CaptureComparisonOffset()
        {
            comparisonOffset = transform.position;
            comparisonOffsetCaptured = true;
        }

        /// <summary>Extracts the signed twist about the named G1 hinge axis in Unity space.</summary>
        public static float ExtractJointRadians(string jointName, Quaternion rotation) => ExtractSignedRadians(rotation, GetUnityAxis(jointName));

        /// <summary>MJCF-to-Unity axial mapping: pitch (+X), roll (-Z), yaw (-Y).</summary>
        public static Vector3 GetUnityAxis(string jointName)
        {
            if (string.IsNullOrEmpty(jointName)) return Vector3.right;
            if (jointName.Contains("roll")) return Vector3.back;
            if (jointName.Contains("yaw")) return Vector3.down;
            return Vector3.right;
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
            if (poseHandler != null) return g1ZeroMuscles != null;
            try
            {
                poseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
                var pose = new HumanPose();
                poseHandler.GetHumanPose(ref pose);
                g1ZeroMuscles = (float[])pose.muscles.Clone();
                foreach (var binding in DefaultBindings)
                {
                    if (!binding.applyToHumanoid || !muscleIndices.TryGetValue(binding.muscleName, out var index)) continue;
                    g1ZeroMuscles[index] = Mathf.Clamp(g1ZeroMuscles[index] + RadiansToMuscleDelta(binding.zeroOffsetRadians, index), -1f, 1f);
                }
                pose.muscles = (float[])g1ZeroMuscles.Clone();
                poseHandler.SetHumanPose(ref pose);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        private static Quaternion Normalize(Quaternion value)
        {
            var inverseMagnitude = 1f / Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            return new Quaternion(value.x * inverseMagnitude, value.y * inverseMagnitude, value.z * inverseMagnitude, value.w * inverseMagnitude);
        }

        public static float RadiansToMuscleDelta(float radians, int muscleIndex)
        {
            var degrees = radians * Mathf.Rad2Deg;
            var range = degrees >= 0f ? HumanTrait.GetMuscleDefaultMax(muscleIndex) : -HumanTrait.GetMuscleDefaultMin(muscleIndex);
            return range > .00001f ? Mathf.Clamp(degrees / range, -1f, 1f) : 0f;
        }

        public static float MuscleDeltaToRadians(float muscle, int muscleIndex)
        {
            var clamped = Mathf.Clamp(muscle, -1f, 1f);
            var degrees = clamped >= 0f ? clamped * HumanTrait.GetMuscleDefaultMax(muscleIndex) : clamped * -HumanTrait.GetMuscleDefaultMin(muscleIndex);
            return degrees * Mathf.Deg2Rad;
        }

        private static MuscleBinding B(string joint, string muscle, float sign = 1f, float zeroOffsetRadians = 0f) =>
            new() { jointName = joint, muscleName = muscle, sign = sign, zeroOffsetRadians = zeroOffsetRadians };

        private static MuscleBinding Unsupported(string joint, string closestMuscle) =>
            new() { jointName = joint, muscleName = closestMuscle, applyToHumanoid = false };
    }
}
