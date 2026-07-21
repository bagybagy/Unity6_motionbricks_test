# Unity6_motionbricks

Unity 6 game client for NVIDIA MotionBricks. The public MotionBricks Python model runs as a separate CUDA process and streams generated poses to Unity over UDP.

## Transport smoke test

The mock bridge validates the Unity/Python transport without downloading the roughly 2.2 GB model weights. Unity sends WASD controls to UDP port `5005`; Python sends poses back to UDP port `5006`.

```powershell
cd Bridge
python -m unittest discover -s tests -v
python mock_server.py
```

Open `Assets/MotionBricks/Scenes/MotionBricksDemo.unity` with Unity `6000.3.13f1` and enter Play mode. The scene creates a visible primitive G1 rig and automatically binds all 29 streamed joints. Click the ground to set a fixed navigation target; use WASD to adjust the target, Q/E to rotate its desired heading, and Escape to return to direct WASD control. Number keys 1-9 select target motion styles such as default, slow, zombie, injured, and stealth. The orange marker shows the requested position and heading, the cyan line shows MotionBricks' generated root plan, and the cyan G1 shows its generated terminal pose. You can recreate the scene at any time with **MotionBricks > Create or Reset Demo Scene**.

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

Unity and Python communicate over loopback UDP ports `5005` and `5006` by default; this integration does not call an online inference service. Pose packets are optimized for same-machine use and can exceed a normal Ethernet MTU, so remote-machine transport is not currently supported. To measure the model on another CUDA GPU:

```powershell
$env:TORCH_FORCE_NO_WEIGHTS_ONLY_LOAD = '1'
Bridge\.venv\Scripts\python.exe -u Bridge\benchmark_runtime.py --frames 180
```

On the development RTX 5070 Ti, the public Python runtime reserved about 818 MiB of CUDA memory. After warmup, a 180-frame target run averaged 2.37 ms per streamed frame; eight replanning frames exceeded 33.3 ms and the maximum was 57.5 ms. Lower-power GPUs should be measured with the command above because occasional replanning latency, rather than VRAM capacity, is the likely constraint.
