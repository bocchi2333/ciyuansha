param(
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$OutputRoot = "",
    [string]$PackageName = "CiyuanSha_Playtest",
    [switch]$NoZip,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $projectParent = [IO.Directory]::GetParent([IO.Path]::GetFullPath($ProjectPath))
    $OutputRoot = Join-Path $projectParent.FullName "CiyuanSha_PlaytestBuild"
}

$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$ProjectPath = [IO.Path]::GetFullPath($ProjectPath)
$packageDir = Join-Path $OutputRoot $PackageName
$zipPath = Join-Path $OutputRoot "$PackageName.zip"
$generalCardDirectoryName = -join ([char[]](0x6B66, 0x5C06, 0x5361, 0x724C))

function Copy-Directory {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (!(Test-Path $Source)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    robocopy $Source $Destination /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "robocopy failed: $Source -> $Destination (exit $LASTEXITCODE)"
    }
}

Push-Location $ProjectPath
try {
    if (!$SkipBuild) {
        dotnet build ".\CiyuanSha.sln" | Out-Host
    }

    if (Test-Path $packageDir) {
        $resolvedPackageDir = [IO.Path]::GetFullPath($packageDir)
        if (!$resolvedPackageDir.StartsWith($OutputRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clear package directory outside output root: $resolvedPackageDir"
        }

        Remove-Item -LiteralPath $resolvedPackageDir -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

    foreach ($file in @(
        "project.godot",
        "CiyuanSha.sln",
        "CiyuanSha.csproj",
        "icon.svg",
        "icon.svg.import",
        "HostPlaytest.bat",
        "JoinPlaytest.bat",
        "ShowLanInfo.bat",
        "PrepareFirewall.bat"
    )) {
        $source = Join-Path $ProjectPath $file
        if (Test-Path $source) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $packageDir $file) -Force
        }
    }

    foreach ($dir in @(
        "Assets",
        "Data",
        "Docs",
        "Scenes",
        "Scripts",
        ".godot",
        "Godot_v4.6.2-stable_mono_win64"
    )) {
        Copy-Directory -Source (Join-Path $ProjectPath $dir) -Destination (Join-Path $packageDir $dir)
    }

    Copy-Directory `
        -Source (Join-Path $ProjectPath $generalCardDirectoryName) `
        -Destination (Join-Path $packageDir $generalCardDirectoryName)

    $editorCache = Join-Path $packageDir ".godot\editor"
    if (Test-Path $editorCache) {
        Remove-Item -LiteralPath $editorCache -Recurse -Force
    }

    Get-ChildItem -Path $packageDir -Recurse -Include "*.log","*.err.log" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue

    $readme = @"
《次元杀》局域网试玩包

开始前：
0. 每台试玩电脑都需要安装 .NET 8 SDK 或更高版本：
   https://dotnet.microsoft.com/download/dotnet/8.0
   必须选择 SDK，只有 Runtime（运行时）仍会导致 Godot 无法加载 C# 项目。
1. 房主双击 ShowLanInfo.bat，把显示的局域网 IP 发给朋友。
2. 房主双击 HostPlaytest.bat，输入自己的名字开房。
3. 朋友双击 JoinPlaytest.bat，输入房主 IP 和自己的名字加入。
4. 若无法加入，房主右键以管理员身份运行 PrepareFirewall.bat 后重试。
5. 若朋友中途断线，直接在游戏弹窗中点击“重新连接”；请保持原来的玩家名。

详细说明：
- Docs\playtest-guide.md
- Docs\reconnect-notes.md

默认 UDP 端口：24567
本包版本：$PackageName
"@
    Set-Content -Path (Join-Path $packageDir "README_PLAYTEST.txt") -Value $readme -Encoding UTF8

    $versionInfo = @"
PackageName=$PackageName
GeneratedAt=$(Get-Date -Format "yyyy-MM-dd HH:mm:ss K")
Godot=4.6.2 stable mono
DotNetTarget=net8.0
DefaultUdpPort=24567
"@
    Set-Content -Path (Join-Path $packageDir "PLAYTEST_VERSION.txt") -Value $versionInfo -Encoding UTF8

    if (!$NoZip) {
        if (Test-Path $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }

        Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -Force
        Write-Host "Created package zip: $zipPath"
    }

    Write-Host "Created package folder: $packageDir"
}
finally {
    Pop-Location
}
