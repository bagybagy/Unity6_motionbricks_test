# Unity6_motionbricks

Unity 6 game client for NVIDIA MotionBricks. The public MotionBricks Python model runs as a separate CUDA process and streams generated poses to Unity over UDP.

## Transport smoke test

The mock bridge validates the Unity/Python transport without downloading the roughly 2.2 GB model weights. Unity sends WASD controls to UDP port `5005`; Python sends poses back to UDP port `5006`.

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

## Real MotionBricks inference

On Windows, install [uv](https://docs.astral.sh/uv/) and Git LFS, then prepare the Python 3.10 environment with the CUDA 13.0 PyTorch wheel:

```powershell
.\Bridge\setup_windows.ps1
```

Fetch NVIDIA's official G1 checkpoints and mesh assets from inside the submodule. These downloads remain in Git LFS storage and are not committed to this repository:

```powershell
cd External\GR00T-WholeBodyControl
git -c lfs.concurrenttransfers=1 lfs pull --include="motionbricks/out/**" --exclude=""
git -c lfs.concurrenttransfers=1 lfs pull --include="motionbricks/assets/skeletons/g1/meshes/**" --exclude=""
cd ..\..
```

Start the CUDA inference bridge:

```powershell
$env:TORCH_FORCE_NO_WEIGHTS_ONLY_LOAD = '1'
Bridge\.venv\Scripts\python.exe -u Bridge\motionbricks_server.py
```

The environment variable allows the current official NVIDIA Lightning checkpoints to load under recent PyTorch versions. Only use it with checkpoints from the tracked NVIDIA submodule. The bridge sends the G1 root transform and 29 named joint rotations to Unity over UDP.
