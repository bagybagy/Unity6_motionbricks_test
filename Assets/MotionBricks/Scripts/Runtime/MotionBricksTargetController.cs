using UnityEngine;

namespace MotionBricks.Unity
{
    /// <summary>Lets the user place a fixed, world-space MotionBricks navigation target.</summary>
    [DisallowMultipleComponent]
    public sealed class MotionBricksTargetController : MonoBehaviour
    {
        [SerializeField] private float groundHeight;
        [SerializeField, Min(0.01f)] private float keyboardMoveSpeed = 2f;
        [SerializeField, Min(1f)] private float keyboardYawSpeed = 90f;

        public bool HasTarget { get; private set; }
        public Vector3 TargetPosition { get; private set; }
        public float TargetYaw { get; private set; }
        public float[] TargetPositionArray => new[] { TargetPosition.x, TargetPosition.y, TargetPosition.z };

        private void Update()
        {
            if (Input.GetMouseButtonDown(0) && (!HasTarget || !PointerIsOverControls()) && TryGetGroundPoint(out var point))
            {
                SetTarget(point, TargetYaw);
            }

            if (!HasTarget)
                return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ClearTarget();
                return;
            }

            var movement = new Vector3(
                (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                0f,
                (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
            if (movement.sqrMagnitude > 0f)
                TargetPosition += movement.normalized * (keyboardMoveSpeed * Time.unscaledDeltaTime);

            var yawInput = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            TargetYaw = Mathf.Repeat(TargetYaw + yawInput * keyboardYawSpeed * Time.unscaledDeltaTime, 360f);
        }

        public void SetTarget(Vector3 worldPosition, float yawDegrees)
        {
            TargetPosition = new Vector3(worldPosition.x, groundHeight, worldPosition.z);
            TargetYaw = Mathf.Repeat(yawDegrees, 360f);
            HasTarget = true;
        }

        public void SetTargetYaw(float yawDegrees) => TargetYaw = Mathf.Repeat(yawDegrees, 360f);

        public void ClearTarget() => HasTarget = false;

        private static bool PointerIsOverControls()
        {
            var pointer = Input.mousePosition;
            return new Rect(12f, 12f, 520f, 156f).Contains(new Vector2(pointer.x, Screen.height - pointer.y));
        }

        private bool TryGetGroundPoint(out Vector3 point)
        {
            point = default;
            var camera = Camera.main;
            if (camera == null)
                return false;
            var plane = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!plane.Raycast(ray, out var distance))
                return false;
            point = ray.GetPoint(distance);
            return true;
        }
    }
}
