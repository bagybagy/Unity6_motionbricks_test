"""Measure MotionBricks initialization, frame latency, and CUDA memory use."""

from __future__ import annotations

import argparse
from pathlib import Path
import statistics
import time

import torch

from motionbricks_server import MotionBricksRuntime
from motionbridge.protocol import ControlMessage


def percentile(values: list[float], ratio: float) -> float:
    ordered = sorted(values)
    return ordered[min(round((len(ordered) - 1) * ratio), len(ordered) - 1)]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--frames", type=int, default=90)
    parser.add_argument("--warmup-frames", type=int, default=10)
    parser.add_argument("--target-distance", type=float, default=3.0)
    args = parser.parse_args()
    if args.frames < 2:
        parser.error("--frames must be at least 2")

    project_root = Path(__file__).resolve().parents[1]
    source_root = project_root / "External" / "GR00T-WholeBodyControl" / "motionbricks"
    torch.cuda.reset_peak_memory_stats()
    started = time.perf_counter()
    runtime = MotionBricksRuntime(source_root)
    torch.cuda.synchronize()
    initialization_seconds = time.perf_counter() - started

    control = ControlMessage(
        seq=1,
        move_x=0.0,
        move_y=0.0,
        look_yaw=0.0,
        has_target=True,
        target_position=(0.0, 0.0, args.target_distance),
        target_yaw=0.0,
    )
    for _ in range(max(0, args.warmup_frames)):
        runtime.next_pose(control)
    torch.cuda.synchronize()

    timings: list[float] = []
    first_pose = None
    last_pose = None
    for _ in range(args.frames):
        torch.cuda.synchronize()
        frame_started = time.perf_counter()
        pose = runtime.next_pose(control)
        torch.cuda.synchronize()
        timings.append((time.perf_counter() - frame_started) * 1_000.0)
        if first_pose is None:
            first_pose = pose
        last_pose = pose

    horizontal_distance = (
        (last_pose.root_position[0] - first_pose.root_position[0]) ** 2
        + (last_pose.root_position[2] - first_pose.root_position[2]) ** 2
    ) ** 0.5
    print(f"GPU: {torch.cuda.get_device_name(0)}")
    print(f"Initialization: {initialization_seconds:.3f} s")
    print(f"CUDA allocated: {torch.cuda.memory_allocated() / 2**20:.1f} MiB")
    print(f"CUDA reserved: {torch.cuda.memory_reserved() / 2**20:.1f} MiB")
    print(f"CUDA peak allocated: {torch.cuda.max_memory_allocated() / 2**20:.1f} MiB")
    print(f"CUDA peak reserved: {torch.cuda.max_memory_reserved() / 2**20:.1f} MiB")
    print(
        f"Frame latency: mean {statistics.mean(timings):.3f} ms, "
        f"p95 {percentile(timings, 0.95):.3f} ms, max {max(timings):.3f} ms"
    )
    print(f"Frames over 33.3 ms: {sum(value > 33.3 for value in timings)}/{len(timings)}")
    print(
        f"Output: {len(last_pose.joints)} joints, "
        f"{len(last_pose.plan_root_positions)} plan points, "
        f"root moved {horizontal_distance:.3f} m"
    )


if __name__ == "__main__":
    main()
