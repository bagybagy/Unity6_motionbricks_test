"""Run NVIDIA MotionBricks inference and stream Unitree G1 poses to Unity."""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import math
import os
from pathlib import Path
import socket
import sys
import time
from types import SimpleNamespace
from typing import Sequence

from motionbridge.pose_conversion import (
    axis_angle_to_quaternion,
    mujoco_axis_to_unity,
    mujoco_position_to_unity,
    mujoco_root_quaternion_to_unity,
    unity_position_to_mujoco,
    unity_yaw_to_mujoco_heading,
)
from motionbridge.pose_constraints import HingeLimit, PoseTargetAdapter
from motionbridge.protocol import ControlMessage, PoseMessage, decode_control, encode_pose


STYLE_ALIASES = {
    "default": "walk",
    "slow": "slow_walk",
    "slow_walk": "slow_walk",
    "crawl": "hand_crawling",
    "hand_crawling": "hand_crawling",
    "boxing": "walk_boxing",
    "walk_boxing": "walk_boxing",
    "elbow_crawling": "elbow_crawling",
    "stealth": "stealth_walk",
    "stealth_walk": "stealth_walk",
    "injured": "injured_walk",
    "injured_walk": "injured_walk",
    "crouch": "walk_stealth",
    "walk_stealth": "walk_stealth",
    "happy": "walk_happy_dance",
    "walk_happy_dance": "walk_happy_dance",
    "zombie": "walk_zombie",
    "walk_zombie": "walk_zombie",
    "gun": "walk_gun",
    "walk_gun": "walk_gun",
    "scared": "walk_scared",
    "walk_scared": "walk_scared",
}

ARRIVAL_DISTANCE_METERS = 0.18
PLAN_SAMPLE_COUNT = 12
TARGET_CHANGE_DEBOUNCE_SECONDS = 0.15


@dataclass(frozen=True, slots=True)
class JointSpec:
    name: str
    qpos_address: int
    unity_axis: tuple[float, float, float]
    minimum: float
    maximum: float


def _model_args() -> SimpleNamespace:
    planner = "default"
    return SimpleNamespace(
        controller="wasd",
        lookat_movement_direction=0,
        has_viewer=0,
        pre_filter_qpos=1,
        source_root_realignment=1,
        target_root_realignment=1,
        force_canonicalization=1,
        skip_ending_target_cond=0,
        random_speed_scale=0,
        speed_scale=[0.8, 1.2],
        generate_dt=2.0,
        max_steps=10_000,
        random_seed=1234,
        num_runs=1,
        use_qpos=1,
        planner=planner,
        allowed_mode=None,
        clips="G1",
        reprocess_clips=0,
        return_model_configs=True,
        return_dataloader=True,
        recording_dir=None,
        EXP=planner,
    )


class MotionBricksRuntime:
    def __init__(self, source_root: Path) -> None:
        source_root = source_root.resolve()
        if not (source_root / "motionbricks" / "motion_backbone").is_dir():
            raise FileNotFoundError(f"MotionBricks source not found: {source_root}")
        checkpoint = source_root / "out" / "motionbricks_pose" / "version_1" / "checkpoints" / "model-step=2000000.ckpt"
        if not checkpoint.is_file() or checkpoint.stat().st_size < 1_000_000:
            raise FileNotFoundError(
                "MotionBricks checkpoints are missing or still Git LFS pointers. "
                "Fetch motionbricks/out/** in the NVIDIA submodule first."
            )

        # NVIDIA's released configs contain paths relative to the MotionBricks
        # source root (for example out/.../skeleton/joints.p).
        os.chdir(source_root)
        sys.path.insert(0, str(source_root))
        import mujoco
        import torch
        from motionbricks.motion_backbone.demo.clips import clip_holder_G1
        from motionbricks.motion_backbone.demo.utils import navigation_demo

        if not torch.cuda.is_available():
            raise RuntimeError("MotionBricks requires a CUDA-enabled PyTorch installation")

        self.torch = torch
        self.mujoco = mujoco
        self.clip_names = tuple(clip_holder_G1.CLIPS.keys())
        self.args = _model_args()
        self.demo = navigation_demo(self.args)
        self.demo.full_agent.reset()
        self.joints = self._joint_specs()
        self.pose_targets = PoseTargetAdapter(
            self.demo.full_agent,
            self.torch,
            tuple(
                HingeLimit(spec.name, spec.qpos_address, spec.minimum, spec.maximum)
                for spec in self.joints
            ),
            lambda: self.demo.mj_data.qpos,
        )
        self.sequence = 0
        self._last_target: tuple[float, float, float, float] | None = None
        self._last_mode: int | None = None
        self._last_target_arrived = True
        self._last_pose_target_active = False
        self._pending_target: tuple[float, float, float, float] | None = None
        self._pending_target_since = 0.0

    @property
    def fps(self) -> float:
        return 1.0 / float(self.demo.mj_model.opt.timestep)

    def _joint_specs(self) -> tuple[JointSpec, ...]:
        specs: list[JointSpec] = []
        for joint_id in range(self.demo.mj_model.njnt):
            if self.demo.mj_model.jnt_type[joint_id] != self.mujoco.mjtJoint.mjJNT_HINGE:
                continue
            name = self.mujoco.mj_id2name(
                self.demo.mj_model, self.mujoco.mjtObj.mjOBJ_JOINT, joint_id
            )
            specs.append(
                JointSpec(
                    name=name,
                    qpos_address=int(self.demo.mj_model.jnt_qposadr[joint_id]),
                    unity_axis=mujoco_axis_to_unity(self.demo.mj_model.jnt_axis[joint_id]),
                    minimum=float(self.demo.mj_model.jnt_range[joint_id][0]),
                    maximum=float(self.demo.mj_model.jnt_range[joint_id][1]),
                )
            )
        if len(specs) != 29:
            raise RuntimeError(f"Expected 29 G1 hinge joints, found {len(specs)}")
        return tuple(specs)

    def _control_signals(self, control: ControlMessage):
        yaw = math.radians(control.look_yaw)
        sin_yaw, cos_yaw = math.sin(yaw), math.cos(yaw)
        move_x = -sin_yaw * control.move_x + cos_yaw * control.move_y
        move_y = -(cos_yaw * control.move_x + sin_yaw * control.move_y)
        magnitude = math.hypot(move_x, move_y)
        if magnitude > 1e-5:
            movement = (move_x / magnitude, move_y / magnitude, 0.0)
        else:
            movement = (0.0, 0.0, 0.0)
        facing = (cos_yaw, -sin_yaw, 0.0)

        target_position = None
        target_arrived = False
        if control.has_target:
            target_position = unity_position_to_mujoco(control.target_position)
            delta_x = target_position[0] - float(self.demo.mj_data.qpos[0])
            delta_y = target_position[1] - float(self.demo.mj_data.qpos[1])
            distance = math.hypot(delta_x, delta_y)
            if distance > 1e-5:
                movement = (delta_x / distance, delta_y / distance, 0.0)
            else:
                movement = (0.0, 0.0, 0.0)
            target_heading = unity_yaw_to_mujoco_heading(control.target_yaw)
            facing = (math.cos(target_heading), math.sin(target_heading), 0.0)
            target_arrived = distance <= ARRIVAL_DISTANCE_METERS
            magnitude = 0.0 if target_arrived else 1.0
        elif control.has_pose_target or getattr(self, "_last_pose_target_active", False):
            # A pose-only edit and its one-shot release generation must not
            # inherit clip locomotion momentum. Keep the root where it is and
            # use the commanded view heading.
            target_position = tuple(float(value) for value in self.demo.mj_data.qpos[0:3])
            target_heading = unity_yaw_to_mujoco_heading(control.look_yaw)
            movement = (0.0, 0.0, 0.0)
            facing = (math.cos(target_heading), math.sin(target_heading), 0.0)
            target_arrived = True
            magnitude = 0.0

        requested = STYLE_ALIASES.get(control.style.lower(), control.style.lower())
        if target_arrived or (
            not control.has_target
            and (control.has_pose_target or getattr(self, "_last_pose_target_active", False))
        ):
            # The goal condition is complete regardless of the selected
            # locomotion style, so keep a stable terminal idle pose.
            movement = (0.0, 0.0, 0.0)
            requested = "idle"
        elif magnitude <= 1e-5:
            # Every released G1 mode is locomotion-conditioned. Without a
            # direct or fixed-target movement request, stay in stable idle.
            requested = "idle"
        if requested not in self.clip_names:
            requested = "walk" if magnitude > 1e-5 else "idle"
        mode = self.clip_names.index(requested)
        self._current_target_arrived = target_arrived
        self._current_mode = mode

        torch = self.torch
        signals = {
            "movement_direction": torch.tensor([movement], dtype=torch.float32),
            "facing_direction": torch.tensor([facing], dtype=torch.float32),
            "mode": torch.tensor([[mode]], dtype=torch.int64),
            "allowed_pred_num_tokens": self.demo.controller.get_default_allowed_pred_num_tokens(mode),
            "context_mujoco_qpos": self.demo.full_agent.get_context_mujoco_qpos(),
        }
        if target_position is not None:
            # MotionBricks expects global MuJoCo coordinates for all four
            # target-token frames; full_agent canonicalizes them internally.
            signals["specific_target_positions"] = torch.tensor(
                [[target_position] * 4], dtype=torch.float32
            )
            signals["specific_target_headings"] = torch.full(
                (1, 4), target_heading, dtype=torch.float32
            )
            signals["has_specific_target"] = torch.ones((1, 1), dtype=torch.int32)
        return signals

    def _target_key(self, control: ControlMessage) -> tuple[object, ...] | None:
        """Stable target identity in official G1 hinge order, not wire-map order."""
        if not (control.has_target or control.has_pose_target):
            return None
        heading = control.target_yaw if control.has_target else control.look_yaw
        pose_angles = tuple(control.target_joint_angles.get(spec.name) for spec in self.joints)
        return (
            control.has_target,
            control.has_pose_target,
            *(control.target_position if control.has_target else (None, None, None)),
            heading,
            *pose_angles,
        )

    def _joint_rotations(self, qpos: Sequence[float]) -> dict[str, tuple[float, float, float, float]]:
        return {
            spec.name: axis_angle_to_quaternion(spec.unity_axis, qpos[spec.qpos_address])
            for spec in self.joints
        }

    def _generated_plan(self) -> tuple[tuple[tuple[float, ...], ...], Sequence[float]]:
        """Return a compact Unity-space preview and the generated terminal qpos."""
        frames = self.demo.full_agent.frames["mujoco_qpos"][0]
        if hasattr(frames, "detach"):
            frames = frames.detach()
        if hasattr(frames, "cpu"):
            frames = frames.cpu()
        if hasattr(frames, "numpy"):
            frames = frames.numpy()
        count = len(frames)
        if count == 0:
            raise RuntimeError("MotionBricks generated an empty qpos buffer")
        start_index = max(0, min(int(self.demo.full_agent._current_frame_idx), count - 1))
        future_frames = frames[start_index:]
        sample_count = min(len(future_frames), PLAN_SAMPLE_COUNT)
        indices = [round(index * (len(future_frames) - 1) / max(sample_count - 1, 1))
                   for index in range(sample_count)]
        plan = tuple(mujoco_position_to_unity(future_frames[index][0:3]) for index in indices)
        return plan, future_frames[-1]

    def _should_force_generation(
        self,
        control: ControlMessage,
        target_key: tuple[object, ...] | None,
        now: float,
    ) -> bool:
        """Force a settled target once, without replanning for every held key event."""
        if control.has_pose_target != getattr(self, "_last_pose_target_active", False):
            # A release needs an idle-to-idle generation too: otherwise the
            # last constrained target can remain in the current plan.
            self._pending_target = None
            return True
        if not (control.has_target or control.has_pose_target):
            self._pending_target = None
            return False

        idle_mode = self.clip_names.index("idle")
        eligible = (
            self._last_mode is None
            or self._last_target_arrived
            or self._last_mode == idle_mode
        )
        if not eligible:
            self._pending_target = None
            return False
        if self._last_target is None:
            self._pending_target = None
            return True

        if target_key != self._last_target:
            self._pending_target = target_key
            self._pending_target_since = now
            return False
        if (
            self._pending_target == target_key
            and now - self._pending_target_since >= TARGET_CHANGE_DEBOUNCE_SECONDS
        ):
            self._pending_target = None
            return True
        return False

    def next_pose(self, control: ControlMessage) -> PoseMessage:
        qpos = self.demo.full_agent.get_next_frame()
        self.demo.mj_data.qpos[:] = qpos
        signals = self._control_signals(control)
        target_key = self._target_key(control)
        # full_agent intentionally skips idle-to-idle replans. New goals start
        # immediately, while held-key target edits are regenerated once after
        # they settle instead of triggering expensive inference at 30 Hz.
        force_generation = self._should_force_generation(
            control, target_key, time.monotonic()
        )
        # The adapter is active only while this inference call can consume it;
        # clearing it restores the official clip target path for later calls.
        if control.has_pose_target:
            self.pose_targets.set_active(control.target_joint_angles)
        try:
            with self.torch.no_grad():
                self.demo.full_agent.generate_new_frames(
                    signals,
                    self.demo.controller.get_controller_dt() * self.args.generate_dt,
                    force_generation=force_generation,
                )
        finally:
            self.pose_targets.clear_active()
        self._last_target = target_key
        self._last_target_arrived = self._current_target_arrived
        self._last_pose_target_active = control.has_pose_target
        self._last_mode = self._current_mode

        plan_root_positions, goal_qpos = self._generated_plan()

        self.sequence += 1
        return PoseMessage(
            seq=self.sequence,
            timestamp=time.monotonic(),
            root_position=mujoco_position_to_unity(qpos[0:3]),
            root_rotation=mujoco_root_quaternion_to_unity(qpos[3:7]),
            joints=self._joint_rotations(qpos),
            plan_root_positions=plan_root_positions,
            goal_root_position=mujoco_position_to_unity(goal_qpos[0:3]),
            goal_root_rotation=mujoco_root_quaternion_to_unity(goal_qpos[3:7]),
            goal_joints=self._joint_rotations(goal_qpos),
        )


def run(
    runtime: MotionBricksRuntime,
    control_host: str,
    control_port: int,
    unity_host: str,
    unity_port: int,
) -> None:
    receiver = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    receiver.bind((control_host, control_port))
    receiver.setblocking(False)
    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (unity_host, unity_port)
    control = ControlMessage(seq=0, move_x=0.0, move_y=0.0, look_yaw=0.0)
    period = 1.0 / runtime.fps

    print(f"MotionBricks CUDA runtime ready at {runtime.fps:g} FPS")
    print(f"Listening on udp://{control_host}:{control_port}; sending to udp://{unity_host}:{unity_port}")
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

            sender.sendto(encode_pose(runtime.next_pose(control)), target)
            remaining = period - (time.perf_counter() - frame_start)
            if remaining > 0:
                time.sleep(remaining)
    except KeyboardInterrupt:
        print("Bridge stopped")
    finally:
        receiver.close()
        sender.close()


def main() -> None:
    project_root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--source-root",
        type=Path,
        default=project_root / "External" / "GR00T-WholeBodyControl" / "motionbricks",
    )
    parser.add_argument("--control-host", default="127.0.0.1")
    parser.add_argument("--control-port", type=int, default=5005)
    parser.add_argument("--unity-host", default="127.0.0.1")
    parser.add_argument("--unity-port", type=int, default=5006)
    args = parser.parse_args()
    run(
        MotionBricksRuntime(args.source_root),
        args.control_host,
        args.control_port,
        args.unity_host,
        args.unity_port,
    )


if __name__ == "__main__":
    main()
