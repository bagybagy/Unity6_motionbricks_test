using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MotionBricks.Unity
{
    /// <summary>Renders the selected target, predicted root plan, and generated terminal G1 pose.</summary>
    [DisallowMultipleComponent]
    public sealed class MotionBricksTargetVisualizer : MonoBehaviour
    {
        [SerializeField] private MotionBricksTargetController targetController;
        [SerializeField] private Color targetColor = new(1f, .68f, .08f, 1f);
        [SerializeField] private Color planColor = new(.1f, .85f, 1f, 1f);
        [SerializeField] private Color ghostColor = new(.3f, .8f, 1f, .3f);

        private Transform targetMarker;
        private LineRenderer planLine;
        private MotionBricksRigDriver goalDriver;
        private readonly List<Material> ownedMaterials = new();

        private void Awake()
        {
            targetController ??= GetComponent<MotionBricksTargetController>();
            CreateTargetMarker();
            CreatePlanLine();
            CreateGoalGhost();
        }

        private void Update()
        {
            var visible = targetController != null && targetController.HasTarget;
            if (targetMarker != null)
            {
                targetMarker.gameObject.SetActive(visible);
                if (visible)
                {
                    targetMarker.position = targetController.TargetPosition;
                    targetMarker.rotation = Quaternion.Euler(0f, targetController.TargetYaw, 0f);
                }
            }
            if (!visible)
            {
                if (planLine != null)
                    planLine.positionCount = 0;
                if (goalDriver != null)
                    goalDriver.gameObject.SetActive(false);
            }
        }

        public void Apply(PoseMessage pose)
        {
            if (pose == null)
                return;
            if (targetController == null || !targetController.HasTarget)
            {
                if (planLine != null)
                    planLine.positionCount = 0;
                if (goalDriver != null)
                    goalDriver.gameObject.SetActive(false);
                return;
            }
            if (planLine != null && pose.PlanRootPositions is { Length: > 0 })
            {
                planLine.positionCount = pose.PlanRootPositions.Length;
                for (var index = 0; index < pose.PlanRootPositions.Length; index++)
                {
                    var point = pose.PlanRootPositions[index];
                    planLine.SetPosition(index, point is { Length: >= 3 }
                        ? new Vector3(point[0], point[1], point[2])
                        : Vector3.zero);
                }
            }
            else if (planLine != null)
                planLine.positionCount = 0;

            if (goalDriver == null || pose.GoalRootPosition is not { Length: >= 3 })
                return;
            goalDriver.gameObject.SetActive(true);
            goalDriver.Apply(new PoseMessage
            {
                RootPosition = pose.GoalRootPosition,
                RootRotation = pose.GoalRootRotation,
                Joints = pose.GoalJoints,
            });
        }

        private void OnDestroy()
        {
            foreach (var material in ownedMaterials)
            {
                if (material == null)
                    continue;
                if (Application.isPlaying)
                    Object.Destroy(material);
                else
                    Object.DestroyImmediate(material);
            }
            ownedMaterials.Clear();
        }

        private void CreateTargetMarker()
        {
            var marker = new GameObject("MotionBricks Target Marker").transform;
            marker.SetParent(transform, false);
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Target Disc";
            disc.transform.SetParent(marker, false);
            disc.transform.localPosition = new Vector3(0f, .015f, 0f);
            disc.transform.localScale = new Vector3(.5f, .015f, .5f);
            Tint(disc, targetColor);
            Object.Destroy(disc.GetComponent<Collider>());

            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arrow.name = "Target Direction";
            arrow.transform.SetParent(marker, false);
            arrow.transform.localPosition = new Vector3(0f, .06f, .42f);
            arrow.transform.localScale = new Vector3(.14f, .08f, .72f);
            Tint(arrow, targetColor);
            Object.Destroy(arrow.GetComponent<Collider>());
            targetMarker = marker;
            marker.gameObject.SetActive(false);
        }

        private void CreatePlanLine()
        {
            planLine = new GameObject("MotionBricks Predicted Plan").AddComponent<LineRenderer>();
            planLine.transform.SetParent(transform, false);
            planLine.useWorldSpace = true;
            planLine.widthMultiplier = .035f;
            planLine.positionCount = 0;
            planLine.material = new Material(Shader.Find("Sprites/Default"));
            ownedMaterials.Add(planLine.material);
            planLine.startColor = planColor;
            planLine.endColor = planColor;
        }

        private void CreateGoalGhost()
        {
            var ghost = new GameObject("MotionBricks Goal Pose Ghost");
            // Keep generated visuals owned by this host, so disabling or destroying the
            // player cannot leave a detached terminal-pose ghost in the scene. RigDriver
            // applies root.position in world space, preserving the planned goal location.
            ghost.transform.SetParent(transform, false);
            var builder = ghost.AddComponent<G1DemoRigBuilder>();
            goalDriver = ghost.GetComponent<MotionBricksRigDriver>();
            foreach (var renderer in ghost.GetComponentsInChildren<Renderer>())
            {
                Tint(renderer.gameObject, ghostColor);
                var material = renderer.material;
                ownedMaterials.Add(material);
                ConfigureTransparent(material);
            }
            foreach (var collider in ghost.GetComponentsInChildren<Collider>())
                Object.Destroy(collider);
            _ = builder;
            ghost.SetActive(false);
        }

        private static void Tint(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        private static void ConfigureTransparent(Material material)
        {
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 3f);
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
