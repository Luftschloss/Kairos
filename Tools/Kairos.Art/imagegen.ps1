# Run from any directory; all image paths are relative to the project root.
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$imagePython = Join-Path $projectRoot '.venv-imagegen/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $imagePython)) {
    throw 'Image environment missing. Follow Tools/Kairos.Art/README.md to install .venv-imagegen, or run imagegen.py with your own Python environment.'
}
& $imagePython (Join-Path $PSScriptRoot 'imagegen.py') @args
exit $LASTEXITCODE
