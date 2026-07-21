import json
import math
import unittest
from types import SimpleNamespace

from mock_server import MockRuntime
from motionbricks_server import MotionBricksRuntime, TARGET_CHANGE_DEBOUNCE_SECONDS
from motionbridge.protocol import ControlMessage, PoseMessage, decode_control, encode_pose
from motionbridge.pose_conversion import (
    axis_angle_to_quaternion,
    mujoco_axis_to_unity,
    mujoco_position_to_unity,
    mujoco_root_quaternion_to_unity,
    unity_position_to_mujoco,
    unity_yaw_to_mujoco_heading,
)


class ProtocolTests(unittest.TestCase):
    def test_mujoco_to_unity_coordinate_conversion(self) -> None:
        self.assertEqual(mujoco_position_to_unity((1.0, 2.0, 3.0)), (-2.0, 3.0, 1.0))
        self.assertEqual(unity_position_to_mujoco((-2.0, 3.0, 1.0)), (1.0, 2.0, 3.0))
        self.assertEqual(mujoco_axis_to_unity((0.0, 1.0, 0.0)), (-1.0, 0.0, 0.0))
        self.assertEqual(
            mujoco_root_quaternion_to_unity((1.0, 0.0, 0.0, 0.0)),
            (0.0, -0.0, -0.0, 1.0),
        )
        self.assertAlmostEqual(unity_yaw_to_mujoco_heading(90.0), -math.pi / 2)

    def test_axis_angle_conversion(self) -> None:
        rotation = axis_angle_to_quaternion((0.0, 1.0, 0.0), math.pi)
        self.assertAlmostEqual(rotation[0], 0.0)
        self.assertAlmostEqual(rotation[1], 1.0)
        self.assertAlmostEqual(rotation[2], 0.0)
        self.assertAlmostEqual(rotation[3], 0.0)

    def test_decode_control_clamps_movement(self) -> None:
        message = decode_control(
            b'{"type":"control","seq":7,"move_x":2,"move_y":-3,"look_yaw":45,"style":"zombie"}'
        )
        self.assertEqual(message.seq, 7)
        self.assertEqual(message.move_x, 1.0)
        self.assertEqual(message.move_y, -1.0)
        self.assertEqual(message.style, "zombie")
        self.assertFalse(message.has_target)

    def test_decode_control_reads_optional_fixed_target(self) -> None:
        message = decode_control(
            b'{"type":"control","seq":8,"has_target":true,"target_position":[1,2,3],'
            b'"target_yaw":-90}'
        )
        self.assertTrue(message.has_target)
        self.assertEqual(message.target_position, (1.0, 2.0, 3.0))
        self.assertEqual(message.target_yaw, -90.0)

    def test_decode_control_accepts_null_target_after_clear(self) -> None:
        message = decode_control(
            b'{"type":"control","seq":9,"has_target":false,"target_position":null}'
        )
        self.assertFalse(message.has_target)
        self.assertEqual(message.target_position, (0.0, 0.0, 0.0))

    def test_encode_pose_uses_unity_quaternion_order(self) -> None:
        payload = encode_pose(
            PoseMessage(
                seq=3,
                timestamp=1.25,
                root_position=(1.0, 2.0, 3.0),
                root_rotation=(0.0, 0.0, 0.0, 1.0),
                joints={"left_knee_joint": (0.1, 0.2, 0.3, 0.9)},
                plan_root_positions=((1.0, 2.0, 3.0), (4.0, 5.0, 6.0)),
                goal_root_position=(4.0, 5.0, 6.0),
                goal_root_rotation=(0.0, 1.0, 0.0, 0.0),
                goal_joints={"left_knee_joint": (0.0, 0.0, 0.0, 1.0)},
            )
        )
        decoded = json.loads(payload)
        self.assertEqual(decoded["type"], "pose")
        self.assertEqual(decoded["root_rotation"], [0.0, 0.0, 0.0, 1.0])
        self.assertEqual(decoded["joints"]["left_knee_joint"], [0.1, 0.2, 0.3, 0.9])
        self.assertEqual(decoded["plan_root_positions"], [[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
        self.assertEqual(decoded["goal_root_position"], [4.0, 5.0, 6.0])
        self.assertEqual(decoded["goal_joints"]["left_knee_joint"], [0.0, 0.0, 0.0, 1.0])

    def test_target_fields_are_optional_for_existing_control_messages(self) -> None:
        message = ControlMessage(seq=1, move_x=0.0, move_y=0.0, look_yaw=0.0)
        self.assertFalse(message.has_target)
        self.assertEqual(message.target_position, (0.0, 0.0, 0.0))

    def test_mock_runtime_follows_fixed_target_and_returns_plan(self) -> None:
        runtime = MockRuntime()
        control = ControlMessage(
            seq=1, move_x=0.0, move_y=0.0, look_yaw=0.0,
            has_target=True, target_position=(2.0, -10.0, 0.0), target_yaw=90.0,
        )
        first = runtime.next_pose(control, 1.0, 1)
        second = runtime.next_pose(control, 1.1, 2)
        self.assertGreater(second.root_position[0], first.root_position[0])
        self.assertEqual(second.goal_root_position, (2.0, 1.0, 0.0))
        self.assertEqual(len(second.plan_root_positions), 8)
        self.assertEqual(second.plan_root_positions[-1], (2.0, 1.0, 0.0))
        self.assertTrue(all(position[1] == 1.0 for position in second.plan_root_positions))

    def test_motionbricks_target_arrival_forces_idle_for_every_style(self) -> None:
        class FakeTorch:
            float32 = object()
            int64 = object()
            int32 = object()

            @staticmethod
            def tensor(value, dtype):
                return value

            @staticmethod
            def full(shape, value, dtype):
                return [[value] * shape[1]]

            @staticmethod
            def ones(shape, dtype):
                return [[1] * shape[1]]

        runtime = SimpleNamespace(
            demo=SimpleNamespace(
                mj_data=SimpleNamespace(qpos=(0.0, 0.0, 0.0)),
                full_agent=SimpleNamespace(get_context_mujoco_qpos=lambda: "context"),
                controller=SimpleNamespace(get_default_allowed_pred_num_tokens=lambda mode: mode),
            ),
            clip_names=("idle", "walk_zombie"),
            torch=FakeTorch(),
        )
        control = ControlMessage(
            seq=1, move_x=1.0, move_y=1.0, look_yaw=0.0, style="zombie",
            has_target=True, target_position=(0.0, 0.0, 0.0), target_yaw=0.0,
        )
        signals = MotionBricksRuntime._control_signals(runtime, control)
        self.assertEqual(signals["movement_direction"], [(0.0, 0.0, 0.0)])
        self.assertEqual(signals["mode"], [[0]])

    def test_generated_plan_excludes_consumed_frames_and_keeps_goal(self) -> None:
        frames = [(float(index), 0.0, 1.0) + (0.0,) * 28 for index in range(6)]
        runtime = SimpleNamespace(
            demo=SimpleNamespace(
                full_agent=SimpleNamespace(
                    frames={"mujoco_qpos": [frames]}, _current_frame_idx=3,
                )
            )
        )
        plan, goal = MotionBricksRuntime._generated_plan(runtime)
        self.assertEqual(plan[0], (0.0, 1.0, 3.0))
        self.assertEqual(plan[-1], (0.0, 1.0, 5.0))
        self.assertEqual(goal[0], 5.0)

    def test_settled_target_change_is_debounced_before_forced_generation(self) -> None:
        old_target = (1.0, 0.0, 2.0, 0.0)
        changed_target = (1.0, 0.0, 2.0, 45.0)
        runtime = SimpleNamespace(
            clip_names=("idle", "walk"),
            _last_mode=0,
            _last_target_arrived=True,
            _last_target=old_target,
            _pending_target=None,
            _pending_target_since=0.0,
        )
        control = ControlMessage(
            seq=1, move_x=0.0, move_y=0.0, look_yaw=0.0,
            has_target=True, target_position=(1.0, 0.0, 2.0), target_yaw=45.0,
        )

        self.assertFalse(MotionBricksRuntime._should_force_generation(
            runtime, control, changed_target, 10.0
        ))
        runtime._last_target = changed_target
        self.assertFalse(MotionBricksRuntime._should_force_generation(
            runtime, control, changed_target, 10.0 + TARGET_CHANGE_DEBOUNCE_SECONDS / 2
        ))
        self.assertTrue(MotionBricksRuntime._should_force_generation(
            runtime, control, changed_target, 10.0 + TARGET_CHANGE_DEBOUNCE_SECONDS
        ))

    def test_rejects_non_finite_values(self) -> None:
        with self.assertRaises(ValueError):
            encode_pose(
                PoseMessage(
                    seq=1,
                    timestamp=0.0,
                    root_position=(math.nan, 0.0, 0.0),
                    root_rotation=(0.0, 0.0, 0.0, 1.0),
                    joints={},
                )
            )


if __name__ == "__main__":
    unittest.main()
