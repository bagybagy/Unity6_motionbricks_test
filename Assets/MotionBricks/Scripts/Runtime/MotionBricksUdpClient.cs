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

        private void Awake()
        {
            if (rigDriver == null)
                rigDriver = GetComponent<MotionBricksRigDriver>();
        }

        private void OnEnable() => StartTransport();

        private void OnDisable() => StopTransport();

        private void Update()
        {
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
            }
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
            accumulatedLookYaw += Input.GetAxisRaw("Mouse X") * lookSensitivity;
            var message = new ControlMessage
            {
                Sequence = Interlocked.Increment(ref controlSequence),
                MoveX = Input.GetAxisRaw("Horizontal"),
                MoveY = Input.GetAxisRaw("Vertical"),
                LookYaw = accumulatedLookYaw,
                Style = string.IsNullOrWhiteSpace(style) ? "default" : style,
            };
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
            GUI.Box(new Rect(12, 12, 330, 94), "MotionBricks UDP bridge");
            GUI.Label(new Rect(22, 38, 310, 20), $"{status}: controls {bridgeHost}:{controlPort}, poses :{posePort}");
            GUI.Label(new Rect(22, 58, 310, 20), $"WASD: move | Mouse X: yaw | poses: {ReceivedPoseCount}");
            GUI.Label(new Rect(22, 78, 310, 20), string.IsNullOrEmpty(lastReceiveError)
                ? $"Last pose timestamp: {lastPoseTimestamp:F3}"
                : $"UDP: {lastReceiveError}");
        }
    }
}
