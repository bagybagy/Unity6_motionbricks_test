using System.Collections.Generic;
using UnityEngine;

namespace MotionBricks.Unity
{
    /// <summary>Instantiates an untouched prefab that already has a valid Unity Humanoid Avatar.</summary>
    [DisallowMultipleComponent]
    public sealed class SimpleHumanoidDemoBuilder : MonoBehaviour
    {
        [SerializeField] private G1HumanoidRetargeter retargeter;
        [Tooltip("Any prefab with a valid Humanoid Animator in its child hierarchy.")]
        [SerializeField] private GameObject humanoidPrefab;
        [SerializeField] private bool buildOnAwake = true;
        [SerializeField, HideInInspector] private GameObject generatedModel;

        public GameObject HumanoidPrefab => humanoidPrefab;
        public Animator Animator { get; private set; }
        public Avatar Avatar { get; private set; }
        public GameObject CurrentModel => generatedModel;
        public string LastError { get; private set; }
        public string Status { get; private set; }
        public bool HasValidHumanoidAvatar => Avatar != null && Avatar.isHuman && Avatar.isValid;
        public bool UsesExternalHumanoid => generatedModel != null;

        public void SetHumanoidPrefab(GameObject value) => humanoidPrefab = value;

        private void Awake() { if (buildOnAwake) Build(); }

        [ContextMenu("Build Simple Humanoid Demo")]
        public void Build()
        {
            LastError = null;
            Status = null;
            var prefab = humanoidPrefab != null ? humanoidPrefab : Resources.Load<GameObject>("UnityChan/unitychan");
            if (prefab == null)
            {
                LastError = "No Humanoid prefab is assigned and the default UnityChan resource was not found.";
                Status = "Build failed";
                return;
            }
            BuildExternalHumanoid(prefab);
        }

        [ContextMenu("Clear Simple Humanoid Demo")]
        public void Clear() => ClearGenerated(immediate: !Application.isPlaying);

        private void ClearGenerated(bool immediate)
        {
            Avatar = null;
            Animator = null;
            retargeter ??= GetComponent<G1HumanoidRetargeter>();
            retargeter?.SetAnimator(null);
            if (generatedModel != null)
            {
                if (immediate) DestroyImmediate(generatedModel); else Destroy(generatedModel);
                generatedModel = null;
            }
        }

        private bool BuildExternalHumanoid(GameObject prefab)
        {
            // Validate an isolated candidate first. A bad Generic/Legacy asset must not
            // remove the currently working Humanoid from the scene.
            var candidate = Instantiate(prefab, transform, false);
            candidate.name = prefab.name + " Model";
            candidate.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            var candidateAnimator = candidate.GetComponentInChildren<Animator>(true);
            var candidateAvatar = candidateAnimator != null ? candidateAnimator.avatar : null;
            var candidateIsValid = candidateAnimator != null && candidateAnimator.isHuman &&
                                   candidateAvatar != null && candidateAvatar.isHuman && candidateAvatar.isValid;
            if (!candidateIsValid)
            {
                LastError = $"'{prefab.name}' does not contain a valid Humanoid Animator/Avatar.";
                Status = "Build rejected";
                DestroyObject(candidate, immediate: !Application.isPlaying);
                Debug.LogWarning($"MotionBricks Humanoid swap rejected: {LastError}", this);
                return false;
            }

            candidateAnimator.applyRootMotion = false;
            candidateAnimator.runtimeAnimatorController = null;
            ClearGenerated(immediate: !Application.isPlaying);
            generatedModel = candidate;
            Animator = candidateAnimator;
            Avatar = candidateAvatar;
            retargeter ??= GetComponent<G1HumanoidRetargeter>();
            retargeter?.SetAnimator(Animator);
            retargeter?.ApplyJointAngles(new Dictionary<string, float>());
            Status = $"Humanoid prefab '{prefab.name}' built";
            return true;
        }

        private static void DestroyObject(UnityEngine.Object value, bool immediate)
        {
            if (value == null) return;
            if (immediate) DestroyImmediate(value); else Destroy(value);
        }

    }
}
