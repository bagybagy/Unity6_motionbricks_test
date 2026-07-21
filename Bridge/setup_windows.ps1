$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$venvPath = Join-Path $PSScriptRoot '.venv'
$pythonPath = Join-Path $venvPath 'Scripts\python.exe'
$motionBricksPath = Join-Path $projectRoot 'External\GR00T-WholeBodyControl\motionbricks'

if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
    throw 'uv was not found. Install it from https://docs.astral.sh/uv/ first.'
}

if (-not (Test-Path -LiteralPath (Join-Path $motionBricksPath 'setup.py'))) {
    throw 'The NVIDIA submodule is missing. Run: git submodule update --init --recursive'
}

uv venv --python 3.10 $venvPath
uv pip install --python $pythonPath torch --index-url https://download.pytorch.org/whl/cu130
uv pip install --python $pythonPath -e $motionBricksPath keyboard

& $pythonPath -c "import torch; print(f'PyTorch {torch.__version__}; CUDA available: {torch.cuda.is_available()}'); assert torch.cuda.is_available(), 'CUDA-enabled NVIDIA GPU was not detected'"

Write-Host "MotionBricks Python environment is ready: $pythonPath"
