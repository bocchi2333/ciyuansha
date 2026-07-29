param(
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [int]$Port = 24567,
    [string]$RulePrefix = "CiyuanSha LAN"
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (!(Test-IsAdministrator)) {
    throw "Please run this script in an elevated PowerShell window (Run as Administrator)."
}

$candidateRoot = Join-Path $ProjectPath "Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64"
$windowedExe = Join-Path $candidateRoot "Godot_v4.6.2-stable_mono_win64.exe"
$consoleExe = Join-Path $candidateRoot "Godot_v4.6.2-stable_mono_win64_console.exe"
$programs = @($windowedExe, $consoleExe) | Where-Object { Test-Path $_ }

if ($programs.Count -eq 0) {
    Write-Warning "Godot executable was not found under $candidateRoot. Port rule will still be created."
}

$portRuleName = "$RulePrefix UDP $Port"
if (-not (Get-NetFirewallRule -DisplayName $portRuleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule `
        -DisplayName $portRuleName `
        -Direction Inbound `
        -Action Allow `
        -Protocol UDP `
        -LocalPort $Port `
        -Profile Private `
        | Out-Null
    Write-Host "Created firewall rule: $portRuleName"
}
else {
    Write-Host "Firewall rule already exists: $portRuleName"
}

foreach ($program in $programs) {
    $programRuleName = "$RulePrefix Program $([IO.Path]::GetFileNameWithoutExtension($program))"
    if (Get-NetFirewallRule -DisplayName $programRuleName -ErrorAction SilentlyContinue) {
        Write-Host "Firewall rule already exists: $programRuleName"
        continue
    }

    New-NetFirewallRule `
        -DisplayName $programRuleName `
        -Direction Inbound `
        -Action Allow `
        -Program $program `
        -Profile Private `
        | Out-Null
    Write-Host "Created firewall rule: $programRuleName"
}

Write-Host "LAN firewall preparation complete. Port: $Port"
