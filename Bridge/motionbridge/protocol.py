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
    type: str = field(default="control", init=False)


@dataclass(frozen=True, slots=True)
class PoseMessage:
    seq: int
    timestamp: float
    root_position: Vector3
    root_rotation: Quaternion
    joints: Mapping[str, Quaternion]
    type: str = field(default="pose", init=False)


def decode_control(payload: bytes) -> ControlMessage:
    raw = json.loads(payload.decode("utf-8"))
    if not isinstance(raw, dict) or raw.get("type") != "control":
        raise ValueError("expected a control message")

    style = str(raw.get("style", "default")).strip() or "default"
    if len(style) > 64:
        raise ValueError("style is too long")

    return ControlMessage(
        seq=int(raw["seq"]),
        move_x=max(-1.0, min(1.0, _finite(raw.get("move_x", 0.0), "move_x"))),
        move_y=max(-1.0, min(1.0, _finite(raw.get("move_y", 0.0), "move_y"))),
        look_yaw=_finite(raw.get("look_yaw", 0.0), "look_yaw"),
        style=style,
    )


def encode_pose(message: PoseMessage) -> bytes:
    root_position = _vector(message.root_position, 3, "root_position")
    root_rotation = _vector(message.root_rotation, 4, "root_rotation")
    joints = {
        str(name): _vector(rotation, 4, f"joints.{name}")
        for name, rotation in message.joints.items()
    }
    payload = asdict(message)
    payload["root_position"] = root_position
    payload["root_rotation"] = root_rotation
    payload["joints"] = joints
    return json.dumps(payload, separators=(",", ":"), allow_nan=False).encode("utf-8")
