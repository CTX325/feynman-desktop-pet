param(
    [switch]$GenerateAssets
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $projectRoot

$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $cscPath)) {
    throw "C# compiler not found: $cscPath"
}

if ($GenerateAssets -or -not (Test-Path (Join-Path $projectRoot "assets\feynman\manifest.json"))) {
    python (Join-Path $projectRoot "tools\generate_sprites.py")
    if ($LASTEXITCODE -ne 0) {
        throw "Sprite generation failed."
    }
}

function Find-GacAssembly([string]$name) {
    $roots = @(
        "C:\Windows\Microsoft.NET\assembly\GAC_64\$name",
        "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\$name",
        "C:\Windows\Microsoft.NET\assembly\GAC_32\$name"
    )
    $file = $null
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) {
            continue
        }
        $file = Get-ChildItem -Path $root -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $file) {
            break
        }
    }
    if ($null -eq $file) {
        throw ".NET Framework assembly not found: $name"
    }
    return $file.FullName
}

$references = @(
    (Find-GacAssembly "PresentationFramework"),
    (Find-GacAssembly "PresentationCore"),
    (Find-GacAssembly "WindowsBase"),
    (Find-GacAssembly "System.Xaml")
)

$binDirectory = Join-Path $projectRoot "bin"
New-Item -ItemType Directory -Path $binDirectory -Force | Out-Null
$outputPath = Join-Path $binDirectory "FeynmanPet.exe"
$sourcePath = Join-Path $projectRoot "src\FeynmanPet.cs"
$iconPath = Join-Path $projectRoot "assets\feynman\feynman.ico"
$compilerArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/codepage:65001",
    "/debug-",
    "/win32icon:$iconPath",
    "/out:$outputPath"
)
foreach ($reference in $references) {
    $compilerArgs += "/r:$reference"
}
$compilerArgs += $sourcePath

& $cscPath $compilerArgs
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Write-Host "Built: $outputPath"
Write-Host "Assets: $(Join-Path $projectRoot 'assets\feynman')"
