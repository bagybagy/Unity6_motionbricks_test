using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace MotionBricks.Unity
{
    /// <summary>Sends player controls and receives poses over the MotionBricks localhost UDP protocol.</summary>
    [DisallowMultipleComponent]
    public sealed class MotionBricksUdpClient : MonoBehaviour
    {
        [Header("Bridge")]
        [SerializeField] private string bridgeHost = "127.0.0.1";
        [SerializeField, Min(1)] private int controlPort = 5005;
        [SerializeField, Min(1)] private int posePort = 5006;
        [SerializeField, Min(1f)] private float controlsPerSecond = 30f;
        [SerializeField, Min(0f)] private float lookSensitivity = 3f;
        [SerializeField] private string style = "default";

        [Header("Scene")]
        [SerializeField] private MotionBricksRigDriver rigDriver;
        [SerializeField] private MotionBricksTargetController targetController;
        [SerializeField] private MotionBricksTargetVisualizer targetVisualizer;
        [SerializeField] private MotionBricksPoseController poseController;
        [SerializeField] private G1HumanoidRetargeter humanoidRetargeter;
        [SerializeField] private bool showDebugOverlay = true;

        private readonly ConcurrentQueue<PoseMessage> receivedPoses = new();
        private CancellationTokenSource cancellation;
        private UdpClient sender;
        private UdpClient receiver;
        private IPEndPoint remoteEndpoint;
        private long controlSequence;
        private long receivedPoseCount;
        private float nextControlTime;
        private float accumulatedLookYaw;
        private string lastReceiveError;
        private double lastPoseTimestamp;

        public long ReceivedPoseCount => Interlocked.Read(ref receivedPoseCount);
        public string LastReceiveError => lastReceiveError;
        public bool IsRunning => cancellation != null && !cancellation.IsCancellationRequested;
        public string Style { get => style; set => style = string.IsNullOrWhiteSpace(value) ? "idle" : value; }
        public void SetHumanoidRetargeter(G1HumanoidRetargeter value) => humanoidRetargeter = value;

        private void Awake()
        {
            if (rigDriver == null)
                rigDriver = GetComponent<MotionBricksRigDriver>();
            // These are added here so the existing generated demo scene gains goal controls
            // without requiring a scene migration.
            targetController ??= GetComponent<MotionBricksTargetController>();
            targetController ??= gameObject.AddComponent<MotionBricksTargetController>();
            targetVisualizer ??= GetComponent<MotionBricksTargetVisualizer>();
            targetVisualizer ??= gameObject.AddComponent<MotionBricksTargetVisualizer>();
            poseController ??= GetComponent<MotionBricksPoseController>();
            poseController ??= gameObject.AddComponent<MotionBricksPoseController>();
            humanoidRetargeter ??= GetComponent<G1HumanoidRetargeter>();
        }

        private void OnEnable() => StartTransport();

        private void OnDisable() => StopTransport();

        private void Update()
        {
            UpdateStyleShortcuts();
            if (Time.unscaledTime >= nextControlTime)
            {
                SendControls();
                nextControlTime = Time.unscaledTime + 1f / controlsPerSecond;
            }

            PoseMessage newest = null;
            while (receivedPoses.TryDequeue(out var pose))
                newest = pose;
            if (newest != null)
            {
                lastPoseTimestamp = newest.Timestamp;
                rigDriver?.Apply(newest);
                poseController?.RecordReceivedPose(newest);
                humanoidRetargeter?.Apply(newest);
                targetVisualizer?.Apply(newest);
            }
        }

        private void UpdateStyleShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) style = "default";
            else if (Input.GetKeyDown(KeyCode.Alpha2)) style = "slow";
            else if (Input.GetKeyDown(KeyCode.Alpha3)) style = "zombie";
            else if (Input.GetKeyDown(KeyCode.Alpha4)) style = "injured";
            else if (Input.GetKeyDown(KeyCode.Alpha5)) style = "stealth";
            else if (Input.GetKeyDown(KeyCode.Alpha6)) style = "crouch";
            else if (Input.GetKeyDown(KeyCode.Alpha7)) style = "happy";
            else if (Input.GetKeyDown(KeyCode.Alpha8)) style = "gun";
            else if (Input.GetKeyDown(KeyCode.Alpha9)) style = "scared";
        }

        private void StartTransport()
        {
            if (IsRunning)
                return;
            try
            {
                remoteEndpoint = new IPEndPoint(IPAddress.Parse(bridgeHost), controlPort);
                sender = new UdpClient();
                receiver = new UdpClient(posePort);
                cancellation = new CancellationTokenSource();
                _ = ReceiveLoopAsync(cancellation.Token);
                lastReceiveError = null;
            }
            catch (Exception exception)
            {
                lastReceiveError = exception.Message;
                StopTransport();
            }
        }

        private void StopTransport()
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
            sender?.Dispose();
            sender = null;
            receiver?.Dispose();
            receiver = null;
        }

        private void SendControls()
        {
            if (sender == null || remoteEndpoint == null)
                return;
            var message = CreateControlMessage();
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
                sender.Send(bytes, bytes.Length, remoteEndpoint);
            }
            catch (SocketException exception)
            {
                lastReceiveError = exception.Message;
            }
        }

        /// <summary>Creates the outgoing message. Exposed to keep protocol behavior testable.</summary>
        public ControlMessage CreateControlMessage()
        {
            // EditMode callers (including tests and inspector tooling) must not invoke the
            // legacy input backend. Runtime input remains exactly as before.
            var mouseX = Application.isPlaying ? Input.GetAxisRaw("Mouse X") : 0f;
            var horizontal = Application.isPlaying ? Input.GetAxisRaw("Horizontal") : 0f;
            var vertical = Application.isPlaying ? Input.GetAxisRaw("Vertical") : 0f;
            accumulatedLookYaw += mouseX * lookSensitivity;
            var hasTarget = targetController != null && targetController.HasTarget;
            return new ControlMessage
            {
                Sequence = Interlocked.Increment(ref controlSequence),
                // Direct controls remain available until a user has placed a fixed target.
                MoveX = hasTarget ? 0f : horizontal,
                MoveY = hasTarget ? 0f : vertical,
                LookYaw = accumulatedLookYaw,
                Style = string.IsNullOrWhiteSpace(style) ? "default" : style,
                HasTarget = hasTarget,
                TargetPosition = hasTarget ? targetController.TargetPositionArray : null,
                TargetYaw = hasTarget ? targetController.TargetYaw : 0f,
                HasPoseTarget = poseController != null && poseController.HasPoseTarget,
                TargetJointAngles = poseController != null && poseController.HasPoseTarget
                    ? poseController.CopyTargetJointAngles() : null,
            };
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && receiver != null)
                {
                    // UdpClient in Unity's target profile has no cancellation-token overload.
                    // Disposing it in StopTransport unblocks this await with ObjectDisposedException.
                    var result = await receiver.ReceiveAsync();
                    var json = Encoding.UTF8.GetString(result.Buffer);
                    if (PoseMessage.TryParse(json, out var pose))
                    {
                        receivedPoses.Enqueue(pose);
                        Interlocked.Increment(ref receivedPoseCount);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception exception)
            {
                lastReceiveError = exception.Message;
            }
        }

        private void OnGUI()
        {
            if (!showDebugOverlay)
                return;
            var status = IsRunning ? "Listening" : "Stopped";
            GUI.Box(new Rect(12, 12, 520, 116), "MotionBricks UDP bridge");
            GUI.Label(new Rect(22, 38, 310, 20), $"{status}: controls {bridgeHost}:{controlPort}, poses :{posePort}");
            GUI.Label(new Rect(22, 58, 500, 20), targetController != null && targetController.HasTarget
                ? "Target: WASD adjust | Q/E yaw | Esc clears target"
                : "Click ground: set target | WASD: direct move | Mouse X: yaw");
            GUI.Label(new Rect(22, 78, 500, 20), $"Mode: {style} | pose target: {(poseController != null && poseController.HasPoseTarget ? "on" : "off")}");
            GUI.Label(new Rect(22, 98, 400, 20), string.IsNullOrEmpty(lastReceiveError)
                ? $"Last pose timestamp: {lastPoseTimestamp:F3}"
                : $"UDP: {lastReceiveError}");
        }
    }
}
