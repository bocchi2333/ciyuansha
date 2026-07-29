param(
    [string[]]$Scenarios = @("duel2", "boss3", "identity4", "bots", "reconnect", "spectator", "stale", "replay"),
    [int]$BasePort = 24720,
    [int]$TimeoutSeconds = 90,
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$GodotExe = "",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($GodotExe)) {
    $GodotExe = Join-Path $ProjectPath "Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe"
}
if (!(Test-Path -LiteralPath $GodotExe)) {
    throw "Godot Mono console executable not found: $GodotExe"
}

if (!$NoBuild) {
    & dotnet build (Join-Path $ProjectPath "CiyuanSha.sln") --configuration Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Debug build failed." }
}

$definitions = @{
    duel2 = @(@{ role = "host"; index = 0 }, @{ role = "client"; index = 1 })
    boss3 = @(@{ role = "host"; index = 0 }, @{ role = "client"; index = 1 }, @{ role = "client"; index = 2 })
    identity4 = @(@{ role = "host"; index = 0 }, @{ role = "client"; index = 1 }, @{ role = "client"; index = 2 }, @{ role = "client"; index = 3 })
    bots = @(@{ role = "host"; index = 0 })
    reconnect = @(@{ role = "host"; index = 0 }, @{ role = "client"; index = 1 })
    spectator = @(@{ role = "host"; index = 0 }, @{ role = "client"; index = 1 }, @{ role = "spectator"; index = 9 })
    stale = @(@{ role = "host"; index = 0 }, @{ role = "client"; index = 1 })
    replay = @(@{ role = "host"; index = 0 }, @{ role = "spectator"; index = 9 })
}

$runId = [Guid]::NewGuid().ToString("N").Substring(0, 10)
$runRoot = Join-Path $ProjectPath "Build\LanV2Acceptance\$runId"
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$results = @()

for ($scenarioIndex = 0; $scenarioIndex -lt $Scenarios.Count; $scenarioIndex++) {
    $scenario = $Scenarios[$scenarioIndex]
    if (!$definitions.ContainsKey($scenario)) { throw "Unknown LAN V2 scenario: $scenario" }
    $participants = $definitions[$scenario]
    $port = $BasePort + $scenarioIndex
    $syncRoot = Join-Path $runRoot "$scenario-$port"
    New-Item -ItemType Directory -Path $syncRoot -Force | Out-Null
    $processes = @()

    Write-Host "LAN V2 scenario: $scenario ($($participants.Count) processes, UDP $port)"
    foreach ($participant in $participants) {
        $role = $participant.role
        $index = [int]$participant.index
        $key = if ($role -eq "host") { "host" } elseif ($role -eq "spectator") { "spectator" } else { "client$index" }
        $stdout = Join-Path $syncRoot "$key.stdout.log"
        $stderr = Join-Path $syncRoot "$key.stderr.log"
        $arguments = @(
            "--headless",
            "--path", $ProjectPath,
            "--scene", "res://Scenes/Test/LanV2Acceptance.tscn",
            "--",
            "--v2-role=$role",
            "--v2-scenario=$scenario",
            "--client-index=$index",
            "--expected-processes=$($participants.Count)",
            "--port=$port",
            "--address=127.0.0.1",
            "--sync-root=$syncRoot",
            "--runner-timeout=$TimeoutSeconds"
        )
        $process = Start-Process -FilePath $GodotExe -ArgumentList $arguments -WorkingDirectory $ProjectPath -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        $processes += [pscustomobject]@{ Key = $key; Process = $process; Stdout = $stdout; Stderr = $stderr }
        if ($role -eq "host") { Start-Sleep -Milliseconds 450 }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $doneMarker = Join-Path $syncRoot "done.marker"
    do {
        Start-Sleep -Milliseconds 250
        $hasFailure = @(Get-ChildItem -LiteralPath $syncRoot -Filter "*.fail" -ErrorAction SilentlyContinue).Count -gt 0
        $completed = Test-Path -LiteralPath $doneMarker
    } while (!$completed -and !$hasFailure -and [DateTime]::UtcNow -lt $deadline)

    if (!$completed) {
        foreach ($item in $processes | Where-Object { !$_.Process.HasExited }) {
            Stop-Process -Id $item.Process.Id -Force -ErrorAction SilentlyContinue
        }
    }

    $passCount = @(Get-ChildItem -LiteralPath $syncRoot -Filter "*.pass" -ErrorAction SilentlyContinue).Count
    $failCount = @(Get-ChildItem -LiteralPath $syncRoot -Filter "*.fail" -ErrorAction SilentlyContinue).Count
    $engineErrors = @($processes | Where-Object {
        (Test-Path -LiteralPath $_.Stderr) -and (Select-String -LiteralPath $_.Stderr -Pattern "^ERROR:" -Quiet)
    }).Count
    $scenarioPassed = $completed -and $passCount -eq $participants.Count -and $failCount -eq 0 -and $engineErrors -eq 0

    $results += [pscustomobject]@{
        Scenario = $scenario
        Passed = $scenarioPassed
        Directory = $syncRoot
    }
    Write-Host ($(if ($scenarioPassed) { "  PASS" } else { "  FAIL - inspect $syncRoot" }))
}

$reportPath = Join-Path $runRoot "summary.json"
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding UTF8
$failed = @($results | Where-Object { !$_.Passed })
Write-Host "LAN V2 report: $reportPath"
if ($failed.Count -gt 0) {
    Write-Error "$($failed.Count) LAN V2 scenario(s) failed: $($failed.Scenario -join ', ')"
    exit 1
}

Write-Host "LAN_V2_SUITE_SUCCESS ($($results.Count)/$($results.Count))"
