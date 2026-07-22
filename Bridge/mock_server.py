"""Dependency-free bridge simulator used before enabling MotionBricks inference."""

from __future__ import annotations

import argparse
import math
import socket
import time
from dataclasses import dataclass, field

from motionbridge.protocol import ControlMessage, ControlSessionState, PoseMessage, decode_control, encode_pose


def _axis_angle(axis: tuple[float, float, float], angle: float) -> tuple[float, float, float, float]:
    half = angle * 0.5
    scale = math.sin(half)
    return axis[0] * scale, axis[1] * scale, axis[2] * scale, math.cos(half)


def _joints(phase: float, speed: float) -> dict[str, tuple[float, float, float, float]]:
    stride = math.sin(phase) * speed * 0.45
    knee = max(0.0, math.sin(phase)) * speed * 0.7
    opposite_knee = max(0.0, -math.sin(phase)) * speed * 0.7
    return {
        "left_hip_pitch_joint": _axis_angle((-1.0, 0.0, 0.0), stride),
        "right_hip_pitch_joint": _axis_angle((-1.0, 0.0, 0.0), -stride),
        "left_knee_joint": _axis_angle((-1.0, 0.0, 0.0), knee),
        "right_knee_joint": _axis_angle((-1.0, 0.0, 0.0), opposite_knee),
        "left_shoulder_pitch_joint": _axis_angle((-1.0, 0.0, 0.0), -stride * 0.7),
        "right_shoulder_pitch_joint": _axis_angle((-1.0, 0.0, 0.0), stride * 0.7),
    }


@dataclass
class MockRuntime:
    root_position: list[float] = field(default_factory=lambda: [0.0, 1.0, 0.0])
    last_timestamp: float | None = None

    def next_pose(self, control: ControlMessage, now: float, seq: int) -> PoseMessage:
        dt = min(max(0.0, now - self.last_timestamp), 0.1) if self.last_timestamp else 1.0 / 60.0
        self.last_timestamp = now
        target = control.target_position if control.has_target else None
        if target is not None:
            dx, dz = target[0] - self.root_position[0], target[2] - self.root_position[2]
            distance = math.hypot(dx, dz)
            step = min(distance, 1.4 * dt)
            if distance > 1e-5:
                self.root_position[0] += dx / distance * step
                self.root_position[2] += dz / distance * step
            yaw_degrees = control.target_yaw
            speed = 0.0 if distance <= 0.18 else 1.0
            # The click target is a ground-plane X/Z destination.  Preserve
            # the rig's height so a click with a different Y cannot sink it.
            goal_position = (target[0], self.root_position[1], target[2])
        else:
            speed = min(1.0, math.hypot(control.move_x, control.move_y))
            self.root_position[0] += control.move_x * 1.4 * dt
            self.root_position[2] += control.move_y * 1.4 * dt
            yaw_degrees = control.look_yaw
            goal_position = (
                self.root_position[0] + control.move_x,
                self.root_position[1],
                self.root_position[2] + control.move_y,
            )

        phase = now * (2.0 + speed * 5.0)
        joints = _joints(phase, speed)
        yaw = math.radians(yaw_degrees)
        goal_yaw = math.radians(control.target_yaw if target is not None else control.look_yaw)
        # A small, evenly spaced preview makes the mock usable by the exact
        # same plan/goal visualizer as the CUDA runtime.
        plan = tuple(
            tuple(self.root_position[axis] + (goal_position[axis] - self.root_position[axis]) * i / 7
                  for axis in range(3))
            for i in range(8)
        )
        goal_joints = _joints(phase + 1.0, 0.0 if target is not None else speed)
        if control.has_pose_target:
            # The mock exposes only its six illustrative hinges; unknown G1
            # names are ignored just as the CUDA runtime ignores unknown MJCF joints.
            for name, angle in control.target_joint_angles.items():
                if name in goal_joints:
                    goal_joints[name] = _axis_angle((-1.0, 0.0, 0.0), angle)
        return PoseMessage(
            seq=seq,
            timestamp=now,
            root_position=tuple(self.root_position),
            root_rotation=_axis_angle((0.0, 1.0, 0.0), yaw),
            joints=joints,
            plan_root_positions=plan,
            goal_root_position=goal_position,
            goal_root_rotation=_axis_angle((0.0, 1.0, 0.0), goal_yaw),
            goal_joints=goal_joints,
        )


def _mock_pose(control: ControlMessage, now: float, seq: int) -> PoseMessage:
    """Compatibility helper for callers that do not need accumulated state."""
    return MockRuntime().next_pose(control, now, seq)


def run(control_host: str, control_port: int, unity_host: str, unity_port: int, fps: float) -> None:
    receiver = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    receiver.bind((control_host, control_port))
    receiver.setblocking(False)
    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (unity_host, unity_port)
    control: ControlMessage | None = None
    session = ControlSessionState()
    period = 1.0 / fps
    sequence = 0
    runtime = MockRuntime()

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
                    accepted, new_session = session.receive(candidate, time.monotonic())
                    if accepted:
                        if new_session:
                            runtime = MockRuntime()
                        control = candidate
                except (KeyError, TypeError, ValueError) as error:
                    print(f"Ignored invalid control message: {error}")

            now = time.monotonic()
            if control is not None and session.is_active(now):
                sequence += 1
                sender.sendto(encode_pose(runtime.next_pose(control, now, sequence)), target)
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
