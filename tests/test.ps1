$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $projectRoot

& (Join-Path $projectRoot "build.ps1")
python (Join-Path $projectRoot "tests\test_assets.py")
if ($LASTEXITCODE -ne 0) {
    throw "PNG asset test failed."
}
python (Join-Path $projectRoot "tests\test_repository.py")
if ($LASTEXITCODE -ne 0) {
    throw "Repository security test failed."
}

$executable = Join-Path $projectRoot "bin\FeynmanPet.exe"
$selfTest = Start-Process -FilePath $executable -ArgumentList "--self-test" -WorkingDirectory $projectRoot -PassThru
if (-not $selfTest.WaitForExit(20000)) {
    $selfTest.Kill()
    throw "Self-test timed out."
}
if ($selfTest.ExitCode -ne 0) {
    throw "Application self-test failed with exit code $($selfTest.ExitCode)."
}
$report = Get-Content -Raw (Join-Path $projectRoot "logs\self-test.txt")
if ($report -match "FAIL") {
    throw "Self-test report contains FAIL:`n$report"
}

$voiceTest = Start-Process -FilePath $executable -ArgumentList "--voice-diagnostics" -WorkingDirectory $projectRoot -PassThru
if (-not $voiceTest.WaitForExit(30000)) {
    $voiceTest.Kill()
    throw "Voice diagnostics timed out."
}
$voiceReport = Get-Content -Raw (Join-Path $projectRoot "logs\voice-diagnostics.txt")
if ($voiceReport -notmatch "TtsVoices=[1-9]") {
    throw "No Windows TTS voices were detected."
}
if ($voiceReport -notmatch "ChineseTtsVoice=INSTALLED") {
    throw "No Chinese TTS voice is installed."
}
if ($voiceReport -notmatch "EnglishTtsVoice=INSTALLED") {
    throw "No English TTS voice is installed."
}
$ttsTextTest = Start-Process -FilePath $executable -ArgumentList "--tts-text-test" -WorkingDirectory $projectRoot -PassThru
if (-not $ttsTextTest.WaitForExit(20000)) {
    $ttsTextTest.Kill()
    throw "TTS text preprocessing test timed out."
}
if ($ttsTextTest.ExitCode -ne 0) {
    throw "TTS text preprocessing test failed."
}
$ttsTextReport = Get-Content -Raw (Join-Path $projectRoot "logs\tts-text-test.txt")
if ($ttsTextReport -notmatch "TTS_TEXT_TESTS=PASS") {
    throw "TTS text preprocessing report did not pass:`n$ttsTextReport"
}

$runtimeLog = Join-Path $projectRoot "logs\runtime.log"
if (Test-Path $runtimeLog) {
    Clear-Content -LiteralPath $runtimeLog
}
$animationTest = Start-Process -FilePath $executable -ArgumentList "--animation-test","--no-save" -WorkingDirectory $projectRoot -PassThru
if (-not $animationTest.WaitForExit(30000)) {
    $animationTest.Kill()
    throw "Animation test timed out."
}
if ($animationTest.ExitCode -ne 0) {
    throw "Animation test failed with exit code $($animationTest.ExitCode)."
}
$log = Get-Content -Raw $runtimeLog
foreach ($state in @("idle", "blink", "walk", "jump", "sleep", "think", "explain", "pet", "click")) {
    if ($log -notmatch "State=$state") {
        throw "Animation test did not enter state: $state"
    }
}
if ($log -notmatch "Animation test completed") {
    throw "Animation test did not report completion."
}
Clear-Content -LiteralPath $runtimeLog

Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class FeynmanWindowTest {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    public static IntPtr Find(uint target) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam) {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid != target || !IsWindowVisible(hWnd)) return true;
            int length = GetWindowTextLength(hWnd);
            StringBuilder text = new StringBuilder(length + 1);
            GetWindowText(hWnd, text, text.Capacity);
            if (text.ToString() == "Feynman Desktop Pet") {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static IntPtr FindTitle(uint target, string expectedTitle) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam) {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid != target || !IsWindowVisible(hWnd)) return true;
            int length = GetWindowTextLength(hWnd);
            StringBuilder text = new StringBuilder(length + 1);
            GetWindowText(hWnd, text, text.Capacity);
            if (text.ToString() == expectedTitle) {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static int CountVisible(uint target) {
        int count = 0;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam) {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid == target && IsWindowVisible(hWnd)) count++;
            return true;
        }, IntPtr.Zero);
        return count;
    }
}
'@
[FeynmanWindowTest]::SetProcessDPIAware() | Out-Null

function Get-PetWindowRect([int]$processId) {
    $handle = [IntPtr]::Zero
    for ($attempt = 0; $attempt -lt 24; $attempt++) {
        $handle = [FeynmanWindowTest]::Find([uint32]$processId)
        if ($handle -ne [IntPtr]::Zero) {
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if ($handle -eq [IntPtr]::Zero) {
        throw "Desktop pet window was not created within six seconds for process $processId."
    }
    $rect = New-Object FeynmanWindowTest+RECT
    [FeynmanWindowTest]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $style = [FeynmanWindowTest]::GetWindowLong($handle, -20)
    if (($style -band 0x00080000) -eq 0) {
        throw "Window is not layered/transparent."
    }
    if (($style -band 0x08000000) -eq 0) {
        throw "Window can steal activation; WS_EX_NOACTIVATE is missing."
    }
    return [pscustomobject]@{
        Handle = $handle
        Left = $rect.Left
        Top = $rect.Top
        Width = $rect.Right - $rect.Left
        Height = $rect.Bottom - $rect.Top
    }
}

function Wait-WindowTitle([int]$processId, [string]$title) {
    for ($attempt = 0; $attempt -lt 24; $attempt++) {
        $handle = [FeynmanWindowTest]::FindTitle([uint32]$processId, $title)
        if ($handle -ne [IntPtr]::Zero) {
            return $handle
        }
        Start-Sleep -Milliseconds 250
    }
    return [IntPtr]::Zero
}

$expectedSizes = @{
    "48" = @(48, 72)
    "64" = @(64, 96)
    "96" = @(96, 144)
}
foreach ($size in @("48", "64", "96")) {
    $process = Start-Process -FilePath $executable -ArgumentList "--state=idle",("--size=" + $size),"--no-save","--auto-exit=8" -WorkingDirectory $projectRoot -PassThru
    Start-Sleep -Milliseconds 1300
    $rect = Get-PetWindowRect $process.Id
    $expected = $expectedSizes[$size]
    if ($rect.Width -ne $expected[0] -or $rect.Height -ne $expected[1]) {
        Stop-Process -Id $process.Id -Force
        throw "Size $size rendered as $($rect.Width)x$($rect.Height), expected $($expected[0])x$($expected[1])."
    }
    if ($size -eq "64") {
        $transparentX = $rect.Left + 1
        $transparentY = $rect.Top + 1
        $opaqueX = $rect.Left + 32
        $opaqueY = $rect.Top + 24
        $transparentParam = [IntPtr](($transparentY -shl 16) -bor ($transparentX -band 0xFFFF))
        $opaqueParam = [IntPtr](($opaqueY -shl 16) -bor ($opaqueX -band 0xFFFF))
        $transparentHit = [FeynmanWindowTest]::SendMessage($rect.Handle, 0x84, [IntPtr]::Zero, $transparentParam).ToInt64()
        $opaqueHit = [FeynmanWindowTest]::SendMessage($rect.Handle, 0x84, [IntPtr]::Zero, $opaqueParam).ToInt64()
        if ($transparentHit -ne -1) {
            throw "Transparent pixel did not return HTTRANSPARENT; got $transparentHit."
        }
        if ($opaqueHit -ne 1) {
            throw "Character pixel did not return HTCLIENT; got $opaqueHit."
        }
        $clickParam = [IntPtr]((24 -shl 16) -bor 32)
        [FeynmanWindowTest]::SendMessage($rect.Handle, 0x201, [IntPtr]1, $clickParam) | Out-Null
        Start-Sleep -Milliseconds 80
        [FeynmanWindowTest]::SendMessage($rect.Handle, 0x202, [IntPtr]::Zero, $clickParam) | Out-Null
        Start-Sleep -Milliseconds 450
        $clickLog = Get-Content -Raw $runtimeLog
        if ($clickLog -notmatch "State=pet \(user\)") {
            throw "Head click did not trigger the pet response."
        }
        $beforeMenu = [FeynmanWindowTest]::CountVisible([uint32]$process.Id)
        [FeynmanWindowTest]::SendMessage($rect.Handle, 0x205, [IntPtr]2, $clickParam) | Out-Null
        Start-Sleep -Milliseconds 450
        $afterMenu = [FeynmanWindowTest]::CountVisible([uint32]$process.Id)
        if ($afterMenu -le $beforeMenu) {
            throw "Right click did not open the context menu."
        }
    }
    Stop-Process -Id $process.Id -Force
    Start-Sleep -Milliseconds 250
}

$assistantProcess = Start-Process -FilePath $executable -ArgumentList "--open-assistant","--no-save","--auto-exit=6" -WorkingDirectory $projectRoot -PassThru
$assistantTitle = -join ([char[]](0x8D39,0x66FC,0x20,0x41,0x49,0x20,0x5BF9,0x8BDD))
$assistantHandle = Wait-WindowTitle $assistantProcess.Id $assistantTitle
$micHandle = Wait-WindowTitle $assistantProcess.Id "Feynman microphone"
if ($assistantHandle -eq [IntPtr]::Zero) {
    Stop-Process -Id $assistantProcess.Id -Force
    throw "AI assistant window did not open."
}
if ($micHandle -eq [IntPtr]::Zero) {
    Stop-Process -Id $assistantProcess.Id -Force
    throw "Microphone shortcut window did not open."
}
if (-not $assistantProcess.WaitForExit(15000)) {
    $assistantProcess.Kill()
    throw "AI assistant process did not close cleanly."
}
if ($assistantProcess.ExitCode -ne 0) {
    throw "AI assistant process exited with code $($assistantProcess.ExitCode)."
}
Start-Sleep -Milliseconds 250

$settingsPath = Join-Path $projectRoot "data\pet-settings.ini"
$backup = $null
if (Test-Path $settingsPath) {
    $backup = [System.IO.File]::ReadAllBytes($settingsPath)
}
try {
    if (Test-Path $settingsPath) {
        Remove-Item -LiteralPath $settingsPath -Force
    }
    $first = Start-Process -FilePath $executable -ArgumentList "--state=idle","--size=96","--auto-exit=3" -WorkingDirectory $projectRoot -PassThru
    Start-Sleep -Milliseconds 1200
    $firstRect = Get-PetWindowRect $first.Id
    if (-not $first.WaitForExit(12000)) {
        $first.Kill()
        throw "Position-memory first run timed out."
    }
    if (-not (Test-Path $settingsPath)) {
        throw "Settings file was not written."
    }
    $settingsText = Get-Content -Raw $settingsPath
    if ($settingsText -notmatch "profile=96x144") {
        throw "Selected size was not persisted."
    }
    $second = Start-Process -FilePath $executable -ArgumentList "--state=idle","--no-save","--auto-exit=3" -WorkingDirectory $projectRoot -PassThru
    Start-Sleep -Milliseconds 1200
    $secondRect = Get-PetWindowRect $second.Id
    if ($firstRect.Left -ne $secondRect.Left -or $firstRect.Top -ne $secondRect.Top) {
        throw "Window position was not restored: $($firstRect.Left),$($firstRect.Top) vs $($secondRect.Left),$($secondRect.Top)."
    }
    if (-not $second.WaitForExit(12000)) {
        $second.Kill()
    }
}
finally {
    Get-Process FeynmanPet -ErrorAction SilentlyContinue | Stop-Process -Force
    if ($null -ne $backup) {
        [System.IO.File]::WriteAllBytes($settingsPath, $backup)
    }
    elseif (Test-Path $settingsPath) {
        Remove-Item -LiteralPath $settingsPath -Force
    }
}

Write-Host "All tests passed."
Get-Content (Join-Path $projectRoot "logs\self-test.txt")
