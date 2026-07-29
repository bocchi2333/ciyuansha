param(
    [Parameter(Mandatory = $true)]
    [string]$Scenario,
    [int]$ClientCount = 1,
    [int]$Port = 24700,
    [int]$TimeoutSeconds = 35,
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$GodotExe = ""
)

$ErrorActionPreference = "Stop"

if ($ClientCount -lt 1 -or $ClientCount -gt 3) {
    throw "ClientCount must be between 1 and 3."
}

if ([string]::IsNullOrWhiteSpace($GodotExe)) {
    $GodotExe = (Resolve-Path (Join-Path $ProjectPath "Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe")).Path
}

$logRoot = Join-Path $ProjectPath "Build\SmokeLogs"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

function Wait-SmokeProcess {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$Timeout
    )

    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    try {
        $Process | Wait-Process -Timeout $Timeout -ErrorAction Stop
    }
    catch {
        if (!$Process.HasExited) {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        }
        throw "Smoke process $($Process.Id) timed out after $Timeout second(s)."
    }
}

function Start-SmokeProcess {
    param(
        [string]$Role,
        [int]$ClientIndex,
        [string]$OutputPath,
        [string]$ErrorPath
    )

    $arguments = @(
        "--headless",
        "--path", $ProjectPath,
        "res://Scenes/Test/LanSmokeTest.tscn",
        "--",
        "--role=$Role",
        "--scenario=$Scenario",
        "--port=$Port",
        "--timeout=$TimeoutSeconds"
    )
    if ($Role -eq "client") {
        $arguments += "--address=127.0.0.1"
        $arguments += "--client-index=$ClientIndex"
    }

    return Start-Process -FilePath $GodotExe -ArgumentList $arguments -RedirectStandardOutput $OutputPath -RedirectStandardError $ErrorPath -PassThru -WindowStyle Hidden
}

$hostOut = Join-Path $logRoot "host_$Scenario.log"
$hostErr = Join-Path $logRoot "host_$Scenario.err.log"
Remove-Item -LiteralPath $hostOut,$hostErr -ErrorAction SilentlyContinue

$clientEntries = @()
$hostProcess = $null
try {
    $hostProcess = Start-SmokeProcess -Role "host" -ClientIndex 0 -OutputPath $hostOut -ErrorPath $hostErr
    Start-Sleep -Milliseconds 900

    for ($index = 1; $index -le $ClientCount; $index++) {
        $clientOut = Join-Path $logRoot "client${index}_$Scenario.log"
        $clientErr = Join-Path $logRoot "client${index}_$Scenario.err.log"
        Remove-Item -LiteralPath $clientOut,$clientErr -ErrorAction SilentlyContinue
        $process = Start-SmokeProcess -Role "client" -ClientIndex $index -OutputPath $clientOut -ErrorPath $clientErr
        $clientEntries += [PSCustomObject]@{
            Index = $index
            Process = $process
            Output = $clientOut
            Error = $clientErr
        }
        Start-Sleep -Milliseconds 350
    }

    Wait-SmokeProcess -Process $hostProcess -Timeout ($TimeoutSeconds + 15)
    foreach ($entry in $clientEntries) {
        Wait-SmokeProcess -Process $entry.Process -Timeout ($TimeoutSeconds + 15)
    }

    $hostProcess.Refresh()
    $hostSuccess = (Get-Content -LiteralPath $hostOut -Raw).Contains("SMOKE_TEST_SUCCESS")
    $clientResults = foreach ($entry in $clientEntries) {
        $entry.Process.Refresh()
        [PSCustomObject]@{
            Client = $entry.Index
            ExitCode = $entry.Process.ExitCode
            Success = (Get-Content -LiteralPath $entry.Output -Raw).Contains("SMOKE_TEST_SUCCESS")
            Log = $entry.Output
        }
    }

    [PSCustomObject]@{
        Scenario = $Scenario
        Players = $ClientCount + 1
        HostExit = $hostProcess.ExitCode
        HostSuccess = $hostSuccess
        ClientsSuccess = -not ($clientResults.Success -contains $false)
    } | Format-List | Out-Host
    $clientResults | Format-Table Client,ExitCode,Success,Log -AutoSize | Out-Host

    if (!$hostSuccess -or $clientResults.Success -contains $false) {
        throw "LAN smoke scenario '$Scenario' failed. Logs: $logRoot"
    }
}
finally {
    if ($null -ne $hostProcess -and !$hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force -ErrorAction SilentlyContinue
    }
    foreach ($entry in $clientEntries) {
        if (!$entry.Process.HasExited) {
            Stop-Process -Id $entry.Process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}
