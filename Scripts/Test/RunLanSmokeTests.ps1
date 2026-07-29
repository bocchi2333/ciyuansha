param(
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$GodotExe = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($GodotExe)) {
    $GodotExe = (Resolve-Path (Join-Path $ProjectPath "Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe")).Path
}

function Invoke-SmokeScenario {
    param(
        [string]$Scenario,
        [int]$Port
    )

    $hostOut = Join-Path $ProjectPath "host_$Scenario.log"
    $hostErr = Join-Path $ProjectPath "host_$Scenario.err.log"
    $clientOut = Join-Path $ProjectPath "client_$Scenario.log"
    $clientErr = Join-Path $ProjectPath "client_$Scenario.err.log"

    Remove-Item $hostOut,$hostErr,$clientOut,$clientErr -ErrorAction SilentlyContinue

    $hostArgs = @(
        "--headless",
        "--path", $ProjectPath,
        "res://Scenes/Test/LanSmokeTest.tscn",
        "--",
        "--role=host",
        "--scenario=$Scenario",
        "--port=$Port",
        "--timeout=25"
    )

    $clientArgs = @(
        "--headless",
        "--path", $ProjectPath,
        "res://Scenes/Test/LanSmokeTest.tscn",
        "--",
        "--role=client",
        "--scenario=$Scenario",
        "--address=127.0.0.1",
        "--port=$Port",
        "--timeout=25"
    )

    $hostProc = Start-Process -FilePath $GodotExe -ArgumentList $hostArgs -RedirectStandardOutput $hostOut -RedirectStandardError $hostErr -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 1
    $clientProc = Start-Process -FilePath $GodotExe -ArgumentList $clientArgs -RedirectStandardOutput $clientOut -RedirectStandardError $clientErr -PassThru -WindowStyle Hidden

    Wait-SmokeProcess -Process $hostProc -TimeoutSeconds 40
    Wait-SmokeProcess -Process $clientProc -TimeoutSeconds 40
    $hostProc.Refresh()
    $clientProc.Refresh()

    $hostLog = Get-Content -Path $hostOut -Raw
    $clientLog = Get-Content -Path $clientOut -Raw

    $hostOk = $hostLog.Contains("SMOKE_TEST_SUCCESS")
    $clientOk = $clientLog.Contains("SMOKE_TEST_SUCCESS")

    [PSCustomObject]@{
        Scenario = $Scenario
        Port = $Port
        HostExit = $hostProc.ExitCode
        ClientExit = $clientProc.ExitCode
        HostSuccess = $hostOk
        ClientSuccess = $clientOk
        Passed = $hostOk -and $clientOk
        HostLog = $hostOut
        ClientLog = $clientOut
        HostErr = $hostErr
        ClientErr = $clientErr
    }
}

function Wait-SmokeProcess {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds
    )

    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    try {
        $Process | Wait-Process -Timeout $TimeoutSeconds -ErrorAction Stop
    }
    catch {
        if (!$Process.HasExited) {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
            throw "Smoke process $($Process.Id) timed out after $TimeoutSeconds second(s)."
        }
    }
}

Push-Location $ProjectPath
try {
    dotnet build ".\CiyuanSha.csproj" | Out-Host
    & $GodotExe --headless --path $ProjectPath --build-solutions --quit | Out-Host

    $results = @(
        Invoke-SmokeScenario -Scenario "general_select" -Port 24610
        Invoke-SmokeScenario -Scenario "dodge" -Port 24611
        Invoke-SmokeScenario -Scenario "peach_save" -Port 24612
        Invoke-SmokeScenario -Scenario "peach_rescue" -Port 24613
        Invoke-SmokeScenario -Scenario "death_no_save" -Port 24614
        Invoke-SmokeScenario -Scenario "slash_limit" -Port 24615
        Invoke-SmokeScenario -Scenario "weapon_range" -Port 24616
        Invoke-SmokeScenario -Scenario "full_round_basic" -Port 24617
        Invoke-SmokeScenario -Scenario "identity_victory" -Port 24618
        Invoke-SmokeScenario -Scenario "return_lobby_after_match" -Port 24619
    )

    $results | Format-Table Scenario,Port,HostExit,ClientExit,HostSuccess,ClientSuccess,Passed -AutoSize | Out-Host

    if ($results.Passed -contains $false) {
        throw "One or more LAN smoke scenarios failed."
    }
}
finally {
    Pop-Location
}
