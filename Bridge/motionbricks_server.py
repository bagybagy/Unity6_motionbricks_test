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
)
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


@dataclass(frozen=True, slots=True)
class JointSpec:
    name: str
    qpos_address: int
    unity_axis: tuple[float, float, float]


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
        self.sequence = 0

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

        requested = STYLE_ALIASES.get(control.style.lower(), control.style.lower())
        if requested == "walk" and magnitude <= 1e-5:
            requested = "idle"
        if requested not in self.clip_names:
            requested = "walk" if magnitude > 1e-5 else "idle"
        mode = self.clip_names.index(requested)

        torch = self.torch
        return {
            "movement_direction": torch.tensor([movement], dtype=torch.float32),
            "facing_direction": torch.tensor([facing], dtype=torch.float32),
            "mode": torch.tensor([[mode]], dtype=torch.int64),
            "allowed_pred_num_tokens": self.demo.controller.get_default_allowed_pred_num_tokens(mode),
            "context_mujoco_qpos": self.demo.full_agent.get_context_mujoco_qpos(),
        }

    def next_pose(self, control: ControlMessage) -> PoseMessage:
        qpos = self.demo.full_agent.get_next_frame()
        self.demo.mj_data.qpos[:] = qpos
        signals = self._control_signals(control)
        with self.torch.no_grad():
            self.demo.full_agent.generate_new_frames(
                signals,
                self.demo.controller.get_controller_dt() * self.args.generate_dt,
            )

        self.sequence += 1
        joints = {
            spec.name: axis_angle_to_quaternion(spec.unity_axis, qpos[spec.qpos_address])
            for spec in self.joints
        }
        return PoseMessage(
            seq=self.sequence,
            timestamp=time.monotonic(),
            root_position=mujoco_position_to_unity(qpos[0:3]),
            root_rotation=mujoco_root_quaternion_to_unity(qpos[3:7]),
            joints=joints,
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
