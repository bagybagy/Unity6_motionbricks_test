"""MuJoCo (right-handed, Z-up) to Unity (left-handed, Y-up) conversions."""

from __future__ import annotations

import math
from typing import Sequence

from .protocol import Quaternion, Vector3


def mujoco_position_to_unity(position: Sequence[float]) -> Vector3:
    """Map MuJoCo +X forward/+Y left/+Z up to Unity +Z forward/+X right/+Y up."""
    return -float(position[1]), float(position[2]), float(position[0])


def unity_position_to_mujoco(position: Sequence[float]) -> Vector3:
    """Inverse of :func:`mujoco_position_to_unity`."""
    return float(position[2]), -float(position[0]), float(position[1])


def unity_yaw_to_mujoco_heading(yaw_degrees: float) -> float:
    """Map Unity's Y-up yaw in degrees to MotionBricks' MuJoCo heading."""
    return -math.radians(float(yaw_degrees))


def mujoco_root_quaternion_to_unity(quaternion_wxyz: Sequence[float]) -> Quaternion:
    """Convert a MuJoCo scalar-first root quaternion into Unity x/y/z/w order."""
    w, x, y, z = (float(value) for value in quaternion_wxyz)
    return y, -z, -x, w


def mujoco_axis_to_unity(axis: Sequence[float]) -> Vector3:
    """Map a MuJoCo axial vector (rotation axis) into Unity coordinates.

    Axial vectors gain an extra sign under the handedness-reflecting position
    transform; using the position-vector mapping here reverses every hinge.
    """
    return float(axis[1]), -float(axis[2]), -float(axis[0])


def axis_angle_to_quaternion(axis: Sequence[float], angle: float) -> Quaternion:
    x, y, z = (float(value) for value in axis)
    norm = math.sqrt(x * x + y * y + z * z)
    if norm <= 1e-8:
        return 0.0, 0.0, 0.0, 1.0
    half = float(angle) * 0.5
    scale = math.sin(half) / norm
    return x * scale, y * scale, z * scale, math.cos(half)
