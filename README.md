# Unity6_motionbricks

Unity 6 game client for NVIDIA MotionBricks. The public MotionBricks Python model runs as a separate CUDA process and streams generated poses to Unity over UDP.

## Current milestone

The first milestone validates the Unity/Python transport without downloading the 2.2 GB model weights. Unity sends WASD controls to UDP port `5005`; the Python mock bridge sends poses back to UDP port `5006`.

```powershell
cd Bridge
python -m unittest discover -s tests -v
python mock_server.py
```

Open the project with Unity `6000.3.13f1`, enter Play mode, and use WASD. The in-game connection status should report incoming pose packets.

## External source

The official NVIDIA repository is tracked as a Git submodule at `External/GR00T-WholeBodyControl`. Clone this project without downloading Git LFS weights first:

```powershell
$env:GIT_LFS_SKIP_SMUDGE = '1'
git clone --recurse-submodules https://github.com/bagybagy/Unity6_motionbricks_test.git
```

The next milestone will fetch the official G1 checkpoints and replace the mock bridge with MotionBricks inference.
