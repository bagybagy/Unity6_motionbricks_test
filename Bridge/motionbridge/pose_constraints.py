"""Bridge-side G1 pose-target adaptation without modifying MotionBricks."""

from __future__ import annotations

from dataclasses import dataclass
from types import MethodType
from typing import Mapping, Sequence


TARGET_FRAME_COUNT = 4


@dataclass(frozen=True, slots=True)
class HingeLimit:
    """The qpos slot and inclusive MJCF range for one G1 hinge joint."""

    name: str
    qpos_address: int
    minimum: float
    maximum: float


def clamp_pose_target(
    target_joint_angles: Mapping[str, float], hinges: Sequence[HingeLimit]
) -> dict[int, float]:
    """Return known hinge targets, clamped to their MJCF ranges.

    Unknown names are deliberately ignored here: the wire protocol stays
    forward-compatible with newer G1 variants while a runtime only consumes
    joints it can prove exist in its loaded model.
    """
    by_name = {hinge.name: hinge for hinge in hinges}
    return {
        hinge.qpos_address: max(hinge.minimum, min(hinge.maximum, float(angle)))
        for name, angle in target_joint_angles.items()
        if (hinge := by_name.get(name)) is not None
    }


def constrained_qpos(
    base_qpos: Sequence[float], targets: Mapping[int, float]
) -> list[float]:
    """Copy qpos and apply already-validated qpos-address targets."""
    result = [float(value) for value in base_qpos]
    for address, angle in targets.items():
        result[address] = angle
    return result


class PoseTargetAdapter:
    """Inject optional qpos-derived pose targets into an official full_agent.

    The adapter retains MotionBricks' clip-produced root path and heading. It
    replaces only target joint transforms with FK from a constrained G1 qpos.
    """

    def __init__(self, full_agent, torch, hinges: Sequence[HingeLimit], base_qpos) -> None:
        hook = getattr(full_agent, "_generate_target_joint_transforms", None)
        if not callable(hook):
            raise RuntimeError("MotionBricks full_agent target-transform hook is unavailable")
        frame_count = getattr(full_agent, "NUM_FRAMES_PER_TOKEN", None)
        if frame_count != TARGET_FRAME_COUNT:
            raise RuntimeError(
                f"MotionBricks target frame count must be {TARGET_FRAME_COUNT}, got {frame_count!r}"
            )
        self._agent = full_agent
        self._torch = torch
        self._hinges = tuple(hinges)
        self._base_qpos = base_qpos
        self._active_targets: dict[int, float] | None = None
        self._original = hook
        full_agent._generate_target_joint_transforms = MethodType(self._generate_target_joint_transforms, full_agent)

    def set_active(self, target_joint_angles: Mapping[str, float]) -> None:
        self._active_targets = clamp_pose_target(target_joint_angles, self._hinges)

    def clear_active(self) -> None:
        self._active_targets = None

    def _generate_target_joint_transforms(self, _agent, input):
        positions, rotations, root_positions = self._original(input)
        self._validate_target_transforms(positions, rotations, root_positions)
        if self._active_targets is None:
            return positions, rotations, root_positions

        qpos = constrained_qpos(self._base_qpos(), self._active_targets)
        # Use the released converter's FK implementation. Repeat all four
        # target frames so constraints are stable across the target token.
        target_qpos = self._torch.tensor([[qpos] * TARGET_FRAME_COUNT], dtype=positions.dtype,
                                         device=positions.device)
        constrained_positions, constrained_rotations = \
            self._agent._converter.convert_mujoco_qpos_to_motion_transforms(target_qpos)

        # The clip calculation above owns root translation/heading. Align FK
        # transforms to that root so only the requested articulated pose is
        # substituted.
        source_root_rotation = constrained_rotations[:, :, :1]
        target_root_rotation = rotations[:, :, :1]
        correction = self._torch.matmul(target_root_rotation, source_root_rotation.transpose(-1, -2))
        relative_positions = constrained_positions - constrained_positions[:, :, :1]
        positions = self._torch.matmul(correction, relative_positions[..., None])[..., 0] + positions[:, :, :1]
        rotations = self._torch.matmul(correction, constrained_rotations)
        return positions, rotations, root_positions

    @staticmethod
    def _validate_target_transforms(positions, rotations, root_positions) -> None:
        """Fail early if an upstream private full_agent contract changes."""
        try:
            position_shape = positions.shape
            rotation_shape = rotations.shape
            root_shape = root_positions.shape
        except AttributeError as error:
            raise RuntimeError("MotionBricks target transforms must expose tensor shapes") from error
        if (
            len(position_shape) != 4
            or len(rotation_shape) != 5
            or len(root_shape) != 3
            or position_shape[1] != TARGET_FRAME_COUNT
            or rotation_shape[1] != TARGET_FRAME_COUNT
            or root_shape[1] != TARGET_FRAME_COUNT
            or position_shape[:3] != rotation_shape[:3]
        ):
            raise RuntimeError(
                "Unexpected MotionBricks target-transform shapes: "
                f"positions={tuple(position_shape)}, rotations={tuple(rotation_shape)}, "
                f"root_positions={tuple(root_shape)}"
            )
