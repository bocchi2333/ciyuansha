param(
    [int]$MatchesPerCombination = 1000,
    [int]$ParallelismPerShard = 2,
    [int]$MaximumDecisions = 50000,
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$ExistingRunRoot = "",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
if ($MatchesPerCombination -le 0) { throw "MatchesPerCombination must be positive." }
if ($ParallelismPerShard -le 0) { throw "ParallelismPerShard must be positive." }

if (!$NoBuild) {
    & dotnet build (Join-Path $ProjectPath "CiyuanSha.GameCore.Simulations\CiyuanSha.GameCore.Simulations.csproj") --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Release simulation build failed." }
}

$runner = Join-Path $ProjectPath "CiyuanSha.GameCore.Simulations\bin\Release\net8.0\CiyuanSha.GameCore.Simulations.dll"
if (!(Test-Path -LiteralPath $runner)) { throw "Simulation runner not found: $runner" }

$runId = [DateTimeOffset]::Now.ToString("yyyyMMdd-HHmmss")
$runRoot = if ([string]::IsNullOrWhiteSpace($ExistingRunRoot)) {
    Join-Path $ProjectPath "Build\SimulationAcceptance\formal-$runId"
} else {
    (Resolve-Path -LiteralPath $ExistingRunRoot).Path
}
if ([string]::IsNullOrWhiteSpace($ExistingRunRoot)) {
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
}
$modes = @("duel", "identity", "team_2v2", "boss_pve")
$difficulties = @("Easy", "Standard", "Hard")
$jobs = @()

foreach ($mode in $modes) {
    foreach ($difficulty in $difficulties) {
        $key = "$mode-$difficulty"
        $report = Join-Path $runRoot "$key.json"
        $stdout = Join-Path $runRoot "$key.stdout.log"
        $stderr = Join-Path $runRoot "$key.stderr.log"
        $arguments = @(
            $runner,
            "--matches", $MatchesPerCombination,
            "--mode", $mode,
            "--difficulty", $difficulty,
            "--parallelism", $ParallelismPerShard,
            "--max-decisions", $MaximumDecisions,
            "--output", $report
        )
        $process = if ([string]::IsNullOrWhiteSpace($ExistingRunRoot)) {
            Start-Process -FilePath "dotnet" -ArgumentList $arguments -WorkingDirectory $ProjectPath -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        } else {
            $null
        }
        $jobs += [pscustomobject]@{
            Key = $key
            Mode = $mode
            Difficulty = $difficulty
            Report = $report
            Stdout = $stdout
            Stderr = $stderr
            Process = $process
        }
    }
}

if ([string]::IsNullOrWhiteSpace($ExistingRunRoot)) {
    $completedKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $lastCompleted = -1
    do {
        Start-Sleep -Seconds 10
        foreach ($job in $jobs) {
            $job.Process.Refresh()
            if ($job.Process.HasExited) { [void]$completedKeys.Add($job.Key) }
        }
        $completedProcesses = $completedKeys.Count
        if ($completedProcesses -ne $lastCompleted) {
            Write-Host "Simulation shards completed: $completedProcesses/$($jobs.Count)"
            $lastCompleted = $completedProcesses
        }
    } while ($completedProcesses -lt $jobs.Count)
}

$reports = @($jobs | ForEach-Object {
    if (!(Test-Path -LiteralPath $_.Report)) { throw "Missing simulation report: $($_.Report); inspect $($_.Stderr)" }
    Get-Content -LiteralPath $_.Report -Raw -Encoding UTF8 | ConvertFrom-Json
})
$invalid = @($reports | Where-Object {
    $_.Total -ne $MatchesPerCombination -or
    $_.Completed -ne $MatchesPerCombination -or
    $_.Faulted -ne 0 -or
    $_.RejectedChoices -ne 0 -or
    @($_.Failures).Count -ne 0
})
if ($invalid.Count -gt 0) { throw "$($invalid.Count) simulation shard(s) failed acceptance." }

$packSignatures = @($reports | ForEach-Object {
    ($_.ContentPacks | ForEach-Object { "$($_.PackId):$($_.Version):$($_.ContentHash)" }) -join "|"
} | Select-Object -Unique)
if ($packSignatures.Count -ne 1) { throw "Simulation shards used different content pack sets." }

$appearanceTotals = @{}
foreach ($report in $reports) {
    foreach ($property in $report.GeneralAppearances.PSObject.Properties) {
        if (!$appearanceTotals.ContainsKey($property.Name)) { $appearanceTotals[$property.Name] = 0 }
        $appearanceTotals[$property.Name] += [int]$property.Value
    }
}
if ($appearanceTotals.Count -ne 12 -or ($appearanceTotals.Values | Measure-Object -Minimum).Minimum -lt 20) {
    throw "The 12-general appearance gate failed."
}

$digestInput = ($reports | Sort-Object ModeFilter, DifficultyFilter | ForEach-Object {
    "$($_.ModeFilter)/$($_.DifficultyFilter):$($_.DeterminismDigest)"
}) -join "`n"
$digestBytes = [Text.Encoding]::UTF8.GetBytes($digestInput)
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $suiteDigest = ([BitConverter]::ToString($sha256.ComputeHash($digestBytes))).Replace("-", "").ToLowerInvariant()
} finally {
    $sha256.Dispose()
}
$summary = [ordered]@{
    GeneratedUtc = [DateTimeOffset]::UtcNow
    EngineApiVersion = $reports[0].EngineApiVersion
    ProtocolVersion = $reports[0].ProtocolVersion
    ContentPacks = $reports[0].ContentPacks
    MatchesPerModeDifficulty = $MatchesPerCombination
    Total = ($reports | Measure-Object Total -Sum).Sum
    Completed = ($reports | Measure-Object Completed -Sum).Sum
    Faulted = ($reports | Measure-Object Faulted -Sum).Sum
    RejectedChoices = ($reports | Measure-Object RejectedChoices -Sum).Sum
    MaximumDecisions = ($reports | Measure-Object MaximumDecisions -Maximum).Maximum
    SuiteDigest = $suiteDigest
    GeneralAppearances = [ordered]@{}
    Matrix = @($reports | Sort-Object ModeFilter, DifficultyFilter | ForEach-Object {
        [ordered]@{
            Mode = $_.ModeFilter
            Difficulty = $_.DifficultyFilter
            Completed = $_.Completed
            Faulted = $_.Faulted
            RejectedChoices = $_.RejectedChoices
            MaximumDecisions = $_.MaximumDecisions
            DeterminismDigest = $_.DeterminismDigest
        }
    })
}
foreach ($entry in $appearanceTotals.GetEnumerator() | Sort-Object Key) {
    $summary.GeneralAppearances[$entry.Key] = $entry.Value
}
$summaryPath = Join-Path $runRoot "summary.json"
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Host "Simulation acceptance report: $summaryPath"
Write-Host "SIMULATION_SUITE_SUCCESS ($($summary.Completed)/$($summary.Total)); digest=$suiteDigest"
