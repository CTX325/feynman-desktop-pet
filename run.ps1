$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$executable = Join-Path $projectRoot "bin\FeynmanPet.exe"

if (-not (Test-Path $executable)) {
    & (Join-Path $projectRoot "build.ps1")
}

Start-Process -FilePath $executable -WorkingDirectory $projectRoot
