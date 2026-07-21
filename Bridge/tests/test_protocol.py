import json
import math
import unittest

from motionbridge.protocol import PoseMessage, decode_control, encode_pose
from motionbridge.pose_conversion import (
    axis_angle_to_quaternion,
    mujoco_axis_to_unity,
    mujoco_position_to_unity,
    mujoco_root_quaternion_to_unity,
)


class ProtocolTests(unittest.TestCase):
    def test_mujoco_to_unity_coordinate_conversion(self) -> None:
        self.assertEqual(mujoco_position_to_unity((1.0, 2.0, 3.0)), (-2.0, 3.0, 1.0))
        self.assertEqual(mujoco_axis_to_unity((0.0, 1.0, 0.0)), (-1.0, 0.0, 0.0))
        self.assertEqual(
            mujoco_root_quaternion_to_unity((1.0, 0.0, 0.0, 0.0)),
            (0.0, -0.0, -0.0, 1.0),
        )

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

    def test_encode_pose_uses_unity_quaternion_order(self) -> None:
        payload = encode_pose(
            PoseMessage(
                seq=3,
                timestamp=1.25,
                root_position=(1.0, 2.0, 3.0),
                root_rotation=(0.0, 0.0, 0.0, 1.0),
                joints={"left_knee_joint": (0.1, 0.2, 0.3, 0.9)},
            )
        )
        decoded = json.loads(payload)
        self.assertEqual(decoded["type"], "pose")
        self.assertEqual(decoded["root_rotation"], [0.0, 0.0, 0.0, 1.0])
        self.assertEqual(decoded["joints"]["left_knee_joint"], [0.1, 0.2, 0.3, 0.9])

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
