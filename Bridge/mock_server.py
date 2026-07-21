"""Dependency-free bridge simulator used before enabling MotionBricks inference."""

from __future__ import annotations

import argparse
import math
import socket
import time

from motionbridge.protocol import ControlMessage, PoseMessage, decode_control, encode_pose


def _axis_angle(axis: tuple[float, float, float], angle: float) -> tuple[float, float, float, float]:
    half = angle * 0.5
    scale = math.sin(half)
    return axis[0] * scale, axis[1] * scale, axis[2] * scale, math.cos(half)


def _mock_pose(control: ControlMessage, now: float, seq: int) -> PoseMessage:
    speed = min(1.0, math.hypot(control.move_x, control.move_y))
    phase = now * (2.0 + speed * 5.0)
    stride = math.sin(phase) * speed * 0.45
    knee = max(0.0, math.sin(phase)) * speed * 0.7
    opposite_knee = max(0.0, -math.sin(phase)) * speed * 0.7
    yaw = math.radians(control.look_yaw)

    return PoseMessage(
        seq=seq,
        timestamp=now,
        root_position=(control.move_x * 0.1, 1.0, control.move_y * 0.1),
        root_rotation=_axis_angle((0.0, 1.0, 0.0), yaw),
        joints={
            "left_hip_pitch_joint": _axis_angle((1.0, 0.0, 0.0), stride),
            "right_hip_pitch_joint": _axis_angle((1.0, 0.0, 0.0), -stride),
            "left_knee_joint": _axis_angle((1.0, 0.0, 0.0), knee),
            "right_knee_joint": _axis_angle((1.0, 0.0, 0.0), opposite_knee),
            "left_shoulder_pitch_joint": _axis_angle((1.0, 0.0, 0.0), -stride * 0.7),
            "right_shoulder_pitch_joint": _axis_angle((1.0, 0.0, 0.0), stride * 0.7),
        },
    )


def run(control_host: str, control_port: int, unity_host: str, unity_port: int, fps: float) -> None:
    receiver = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    receiver.bind((control_host, control_port))
    receiver.setblocking(False)
    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (unity_host, unity_port)
    control = ControlMessage(seq=0, move_x=0.0, move_y=0.0, look_yaw=0.0)
    period = 1.0 / fps
    sequence = 0

    print(f"Listening for Unity controls on udp://{control_host}:{control_port}")
    print(f"Sending mock poses to udp://{unity_host}:{unity_port} at {fps:g} FPS")
    try:
        while True:
            frame_start = time.perf_counter()
            while True:
                try:
                    payload, _ = receiver.recvfrom(65535)
                except BlockingIOError:
                    break
                try:
                    candidate = decode_control(payload)
                    if candidate.seq >= control.seq:
                        control = candidate
                except (KeyError, TypeError, ValueError) as error:
                    print(f"Ignored invalid control message: {error}")

            sequence += 1
            now = time.monotonic()
            sender.sendto(encode_pose(_mock_pose(control, now, sequence)), target)
            remaining = period - (time.perf_counter() - frame_start)
            if remaining > 0:
                time.sleep(remaining)
    except KeyboardInterrupt:
        print("Bridge stopped")
    finally:
        receiver.close()
        sender.close()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--control-host", default="127.0.0.1")
    parser.add_argument("--control-port", type=int, default=5005)
    parser.add_argument("--unity-host", default="127.0.0.1")
    parser.add_argument("--unity-port", type=int, default=5006)
    parser.add_argument("--fps", type=float, default=60.0)
    args = parser.parse_args()
    if args.fps <= 0:
        parser.error("--fps must be positive")
    run(args.control_host, args.control_port, args.unity_host, args.unity_port, args.fps)


if __name__ == "__main__":
    main()
