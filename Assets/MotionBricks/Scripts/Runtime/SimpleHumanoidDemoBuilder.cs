using System;
using System.Collections.Generic;
using UnityEngine;

namespace MotionBricks.Unity
{
    /// <summary>Builds the side-by-side Humanoid retarget preview, with a procedural fallback for tests.</summary>
    [DisallowMultipleComponent]
    public sealed class SimpleHumanoidDemoBuilder : MonoBehaviour
    {
        [SerializeField] private G1HumanoidRetargeter retargeter;
        [SerializeField] private GameObject humanoidPrefab;
        [SerializeField] private bool buildOnAwake = true;
        [SerializeField] private Color color = new(0.35f, 0.72f, 0.42f, 1f);
        private readonly Dictionary<string, Transform> bones = new(StringComparer.Ordinal);
        private GameObject generatedModel;
        private bool ownsAvatar;
        private bool useProceduralFallback;

        public Animator Animator { get; private set; }
        public Avatar Avatar { get; private set; }
        public bool HasValidHumanoidAvatar => Avatar != null && Avatar.isHuman && Avatar.isValid;
        public bool UsesExternalHumanoid => generatedModel != null;

        public void SetHumanoidPrefab(GameObject value) => humanoidPrefab = value;
        public void UseProceduralFallback() { useProceduralFallback = true; humanoidPrefab = null; }

        private void Awake() { if (buildOnAwake) Build(); }

        [ContextMenu("Build Simple Humanoid Demo")]
        public void Build()
        {
            // Rebuilding must be synchronous so duplicate bone names cannot leak into AvatarBuilder.
            ClearGenerated(immediate: true);
            if (!useProceduralFallback)
                humanoidPrefab ??= Resources.Load<GameObject>("UnityChan/unitychan");
            if (humanoidPrefab != null && BuildExternalHumanoid())
                return;

            var root = new GameObject("Hips").transform;
            root.SetParent(transform, false);
            bones["Hips"] = root;
            Add("Spine", "Hips", new Vector3(0, .32f, 0));
            Add("Chest", "Spine", new Vector3(0, .26f, 0));
            Add("Neck", "Chest", new Vector3(0, .22f, 0));
            Add("Head", "Neck", new Vector3(0, .18f, 0));
            Add("LeftShoulder", "Chest", new Vector3(-.18f, .16f, 0));
            Add("LeftUpperArm", "LeftShoulder", new Vector3(-.24f, 0, 0));
            Add("LeftLowerArm", "LeftUpperArm", new Vector3(-.25f, 0, 0));
            Add("LeftHand", "LeftLowerArm", new Vector3(-.16f, 0, 0));
            Add("RightShoulder", "Chest", new Vector3(.18f, .16f, 0));
            Add("RightUpperArm", "RightShoulder", new Vector3(.24f, 0, 0));
            Add("RightLowerArm", "RightUpperArm", new Vector3(.25f, 0, 0));
            Add("RightHand", "RightLowerArm", new Vector3(.16f, 0, 0));
            Add("LeftUpperLeg", "Hips", new Vector3(-.12f, -.34f, 0));
            Add("LeftLowerLeg", "LeftUpperLeg", new Vector3(0, -.38f, 0));
            Add("LeftFoot", "LeftLowerLeg", new Vector3(0, -.34f, .07f));
            Add("LeftToes", "LeftFoot", new Vector3(0, 0, .19f));
            Add("RightUpperLeg", "Hips", new Vector3(.12f, -.34f, 0));
            Add("RightLowerLeg", "RightUpperLeg", new Vector3(0, -.38f, 0));
            Add("RightFoot", "RightLowerLeg", new Vector3(0, -.34f, .07f));
            Add("RightToes", "RightFoot", new Vector3(0, 0, .19f));

            Avatar = AvatarBuilder.BuildHumanAvatar(gameObject, CreateDescription());
            ownsAvatar = true;
            var animatorComponent = gameObject.GetComponent<Animator>();
            if (animatorComponent == null) animatorComponent = gameObject.AddComponent<Animator>();
            Animator = animatorComponent;
            Animator.avatar = Avatar;
            retargeter ??= GetComponent<G1HumanoidRetargeter>();
            retargeter?.SetAnimator(Animator);
        }

        [ContextMenu("Clear Simple Humanoid Demo")]
        public void Clear() => ClearGenerated(immediate: !Application.isPlaying);

        private void OnDestroy()
        {
            if (Avatar == null || !ownsAvatar) return;
            if (Application.isPlaying) Destroy(Avatar); else DestroyImmediate(Avatar);
            Avatar = null;
        }

        private void ClearGenerated(bool immediate)
        {
            if (Avatar != null && ownsAvatar)
            {
                if (immediate) DestroyImmediate(Avatar); else Destroy(Avatar);
            }
            Avatar = null;
            ownsAvatar = false;
            Animator = null;
            bones.Clear();
            if (generatedModel != null)
            {
                if (immediate) DestroyImmediate(generatedModel); else Destroy(generatedModel);
                generatedModel = null;
            }
            var old = transform.Find("Hips");
            if (old != null)
            {
                if (immediate) DestroyImmediate(old.gameObject); else Destroy(old.gameObject);
            }
        }

        private bool BuildExternalHumanoid()
        {
            generatedModel = Instantiate(humanoidPrefab, transform, false);
            generatedModel.name = "UnityChan Model";
            generatedModel.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            generatedModel.transform.localScale = Vector3.one;
            Animator = generatedModel.GetComponentInChildren<Animator>();
            Avatar = Animator != null ? Animator.avatar : null;
            if (!HasValidHumanoidAvatar)
            {
                DestroyImmediate(generatedModel);
                generatedModel = null;
                Animator = null;
                Avatar = null;
                return false;
            }

            Animator.applyRootMotion = false;
            retargeter ??= GetComponent<G1HumanoidRetargeter>();
            retargeter?.SetAnimator(Animator);
            return true;
        }

        private void Add(string name, string parentName, Vector3 position)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(bones[parentName], false);
            bone.localPosition = position;
            bones[name] = bone;
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "visual";
            visual.transform.SetParent(bone, false);
            visual.transform.localScale = Vector3.one * .11f;
            var renderer = visual.GetComponent<Renderer>();
            var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", color); block.SetColor("_Color", color); renderer.SetPropertyBlock(block);
        }

        private HumanDescription CreateDescription()
        {
            var humans = new List<HumanBone>();
            foreach (var name in bones.Keys)
                humans.Add(new HumanBone { boneName = name, humanName = name, limit = new HumanLimit { useDefaultValues = true } });
            var skeleton = new List<SkeletonBone>();
            foreach (var (name, bone) in bones)
                skeleton.Add(new SkeletonBone { name = name, position = bone.localPosition, rotation = bone.localRotation, scale = bone.localScale });
            return new HumanDescription { human = humans.ToArray(), skeleton = skeleton.ToArray(), upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f, armStretch = .05f, legStretch = .05f, feetSpacing = 0f, hasTranslationDoF = false };
        }
    }
}
