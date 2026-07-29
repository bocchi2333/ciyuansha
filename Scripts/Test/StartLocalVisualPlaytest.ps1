param(
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$GodotExe = "",
    [int]$Port = 24567,
    [switch]$Manual
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($GodotExe)) {
    $candidateRoot = Join-Path $ProjectPath "Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64"
    $windowedExe = Join-Path $candidateRoot "Godot_v4.6.2-stable_mono_win64.exe"
    $consoleExe = Join-Path $candidateRoot "Godot_v4.6.2-stable_mono_win64_console.exe"
    $GodotExe = if (Test-Path $windowedExe) { $windowedExe } else { $consoleExe }
}

if (!(Test-Path $GodotExe)) {
    throw "Godot executable not found: $GodotExe"
}

Push-Location $ProjectPath
try {
    dotnet build ".\CiyuanSha.sln" | Out-Host

    $hostArgs = @("--path", $ProjectPath)
    $clientArgs = @("--path", $ProjectPath)
    if (!$Manual) {
        $hostArgs += @("--", "--lan-role=host", "--player-name=Host", "--general=chenchen", "--port=$Port", "--auto-ready=true", "--auto-start=true")
        $clientArgs += @("--", "--lan-role=client", "--address=127.0.0.1", "--port=$Port", "--player-name=Client", "--general=hanfeiyang", "--auto-ready=true")
    }

    Write-Host "Launching host window..."
    Start-Process -FilePath $GodotExe -ArgumentList $hostArgs

    Start-Sleep -Seconds 1

    Write-Host "Launching client window..."
    Start-Process -FilePath $GodotExe -ArgumentList $clientArgs

    Write-Host ""
    if ($Manual) {
        Write-Host "Manual steps:"
        Write-Host "1. In the first window, enter a player name and click Host."
        Write-Host "2. In the second window, keep address 127.0.0.1 and click Join."
        Write-Host "3. Pick generals in both windows, click Ready in both, then Start Match on host."
        Write-Host "4. After the match ends, host clicks Return Lobby."
    }
    else {
        Write-Host "Auto mode is enabled:"
        Write-Host "- Host and client should connect, ready up, and start automatically."
        Write-Host "- Port: $Port"
        Write-Host "- After the match ends, host clicks Return Lobby."
        Write-Host "- Use -Manual if you want to click through the lobby yourself."
    }
}
finally {
    Pop-Location
}
