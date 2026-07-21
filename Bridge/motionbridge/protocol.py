"""Shared wire protocol for the Unity client and Python inference process."""

from __future__ import annotations

from dataclasses import asdict, dataclass, field
import json
import math
from typing import Mapping, Sequence


Quaternion = tuple[float, float, float, float]
Vector3 = tuple[float, float, float]


def _finite(value: object, field_name: str) -> float:
    number = float(value)
    if not math.isfinite(number):
        raise ValueError(f"{field_name} must be finite")
    return number


def _vector(values: Sequence[object], length: int, field_name: str) -> tuple[float, ...]:
    if len(values) != length:
        raise ValueError(f"{field_name} must contain {length} values")
    return tuple(_finite(value, field_name) for value in values)


@dataclass(frozen=True, slots=True)
class ControlMessage:
    seq: int
    move_x: float
    move_y: float
    look_yaw: float
    style: str = "default"
    # Optional fixed goal supplied by newer Unity clients.  Keeping defaults
    # makes controls sent by the original WASD-only client valid.
    has_target: bool = False
    target_position: Vector3 = (0.0, 0.0, 0.0)
    target_yaw: float = 0.0
    # Optional G1 hinge-angle target.  Angles are radians and names use the
    # MuJoCo joint names returned in pose messages.
    has_pose_target: bool = False
    target_joint_angles: Mapping[str, float] = field(default_factory=dict)
    type: str = field(default="control", init=False)


@dataclass(frozen=True, slots=True)
class PoseMessage:
    seq: int
    timestamp: float
    root_position: Vector3
    root_rotation: Quaternion
    joints: Mapping[str, Quaternion]
    # The generated buffer is deliberately sampled, so it is small enough for
    # UDP while still showing the motion plan in Unity.
    plan_root_positions: tuple[Vector3, ...] = ()
    goal_root_position: Vector3 = (0.0, 0.0, 0.0)
    goal_root_rotation: Quaternion = (0.0, 0.0, 0.0, 1.0)
    goal_joints: Mapping[str, Quaternion] = field(default_factory=dict)
    type: str = field(default="pose", init=False)


def decode_control(payload: bytes) -> ControlMessage:
    raw = json.loads(payload.decode("utf-8"))
    if not isinstance(raw, dict) or raw.get("type") != "control":
        raise ValueError("expected a control message")

    style = str(raw.get("style", "default")).strip() or "default"
    if len(style) > 64:
        raise ValueError("style is too long")

    has_target = bool(raw.get("has_target", False))
    raw_target_position = raw.get("target_position")
    if has_target:
        if raw_target_position is None:
            raise ValueError("target_position is required when has_target is true")
        target_position = _vector(raw_target_position, 3, "target_position")
    else:
        # Unity emits null after Escape/clear. It is equivalent to the field
        # being omitted for legacy WASD controls.
        target_position = (0.0, 0.0, 0.0) if raw_target_position is None else _vector(
            raw_target_position, 3, "target_position"
        )
    has_pose_target = bool(raw.get("has_pose_target", False))
    raw_joint_angles = raw.get("target_joint_angles")
    if raw_joint_angles is None:
        joint_angles: dict[str, float] = {}
    elif not isinstance(raw_joint_angles, dict):
        raise ValueError("target_joint_angles must be a mapping")
    else:
        if len(raw_joint_angles) > 29:
            raise ValueError("target_joint_angles may contain at most 29 joints")
        joint_angles = {
            str(name): _finite(angle, f"target_joint_angles.{name}")
            for name, angle in raw_joint_angles.items()
        }
    if has_pose_target and raw_joint_angles is None:
        raise ValueError("target_joint_angles is required when has_pose_target is true")

    return ControlMessage(
        seq=int(raw["seq"]),
        move_x=max(-1.0, min(1.0, _finite(raw.get("move_x", 0.0), "move_x"))),
        move_y=max(-1.0, min(1.0, _finite(raw.get("move_y", 0.0), "move_y"))),
        look_yaw=_finite(raw.get("look_yaw", 0.0), "look_yaw"),
        style=style,
        has_target=has_target,
        target_position=target_position,
        target_yaw=_finite(raw.get("target_yaw", 0.0), "target_yaw"),
        has_pose_target=has_pose_target,
        target_joint_angles=joint_angles,
    )


def encode_pose(message: PoseMessage) -> bytes:
    root_position = _vector(message.root_position, 3, "root_position")
    root_rotation = _vector(message.root_rotation, 4, "root_rotation")
    joints = {
        str(name): _vector(rotation, 4, f"joints.{name}")
        for name, rotation in message.joints.items()
    }
    plan_root_positions = [
        _vector(position, 3, "plan_root_positions") for position in message.plan_root_positions
    ]
    goal_root_position = _vector(message.goal_root_position, 3, "goal_root_position")
    goal_root_rotation = _vector(message.goal_root_rotation, 4, "goal_root_rotation")
    goal_joints = {
        str(name): _vector(rotation, 4, f"goal_joints.{name}")
        for name, rotation in message.goal_joints.items()
    }
    payload = asdict(message)
    payload["root_position"] = root_position
    payload["root_rotation"] = root_rotation
    payload["joints"] = joints
    payload["plan_root_positions"] = plan_root_positions
    payload["goal_root_position"] = goal_root_position
    payload["goal_root_rotation"] = goal_root_rotation
    payload["goal_joints"] = goal_joints
    return json.dumps(payload, separators=(",", ":"), allow_nan=False).encode("utf-8")
