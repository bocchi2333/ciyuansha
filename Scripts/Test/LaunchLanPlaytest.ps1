param(
    [ValidateSet("host", "client")]
    [string]$Role = "host",
    [string]$Address = "127.0.0.1",
    [int]$Port = 24567,
    [string]$PlayerName = "",
    [string]$General = "",
    [switch]$AutoReady,
    [switch]$AutoStart,
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$GodotExe = "",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

function Test-DotNetSdk {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        return $false
    }

    $sdkLines = & dotnet --list-sdks 2>$null
    if ($LASTEXITCODE -ne 0 -or $null -eq $sdkLines) {
        return $false
    }

    foreach ($line in $sdkLines) {
        if ($line -match '^(\d+)\.') {
            $major = [int]$Matches[1]
            if ($major -ge 8) {
                return $true
            }
        }
    }

    return $false
}

function Show-MissingDotNetSdkMessage {
    Write-Host ""
    Write-Host "Missing .NET SDK 8.0 or later." -ForegroundColor Yellow
    Write-Host "Godot Mono needs the SDK to load this C# playtest project." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Fix:"
    Write-Host "1. Install .NET 8 SDK or later, not just the Runtime."
    Write-Host "2. Download: https://dotnet.microsoft.com/download/dotnet/8.0"
    Write-Host "3. Close this window and run HostPlaytest.bat or JoinPlaytest.bat again."
    Write-Host ""
    Write-Host "Note: install the SDK package. The Runtime-only package is not enough."
    Write-Host ""
}

if (!(Test-DotNetSdk)) {
    Show-MissingDotNetSdkMessage
    throw "Missing .NET SDK 8.0 or later."
}

if ([string]::IsNullOrWhiteSpace($GodotExe)) {
    $candidateRoot = Join-Path $ProjectPath "Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64"
    $windowedExe = Join-Path $candidateRoot "Godot_v4.6.2-stable_mono_win64.exe"
    $consoleExe = Join-Path $candidateRoot "Godot_v4.6.2-stable_mono_win64_console.exe"
    $GodotExe = if (Test-Path $windowedExe) { $windowedExe } else { $consoleExe }
}

if (!(Test-Path $GodotExe)) {
    throw "Godot executable not found: $GodotExe"
}

if ([string]::IsNullOrWhiteSpace($PlayerName)) {
    $PlayerName = if ($Role -eq "host") { "Host" } else { "Client" }
}

if ([string]::IsNullOrWhiteSpace($General)) {
    $General = if ($Role -eq "host") { "chenchen" } else { "hanfeiyang" }
}

Push-Location $ProjectPath
try {
    if (!$NoBuild) {
        dotnet build ".\CiyuanSha.sln" | Out-Host
    }

    $gameArgs = @(
        "--path", $ProjectPath,
        "--",
        "--lan-role=$Role",
        "--player-name=$PlayerName",
        "--general=$General",
        "--port=$Port"
    )

    if ($Role -eq "client") {
        $gameArgs += "--address=$Address"
    }

    if ($AutoReady) {
        $gameArgs += "--auto-ready=true"
    }

    if ($AutoStart) {
        $gameArgs += "--auto-start=true"
    }

    Write-Host "Launching CiyuanSha LAN playtest..."
    Write-Host "Role: $Role"
    if ($Role -eq "client") {
        Write-Host "Address: $Address"
    }
    Write-Host "Port: $Port"
    Write-Host "Player: $PlayerName"
    Write-Host "General: $General"

    Start-Process -FilePath $GodotExe -ArgumentList $gameArgs
}
finally {
    Pop-Location
}
