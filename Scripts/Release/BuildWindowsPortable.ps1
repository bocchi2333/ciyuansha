[CmdletBinding()]
param(
    [string]$Version = "2.0.0",
    [string]$OutputRoot = "",
    [string]$GodotExecutable = "",
    [switch]$SkipTests,
    [switch]$NoZip,
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $repositoryParent = [IO.Directory]::GetParent($repositoryRoot)
    $OutputRoot = Join-Path $repositoryParent.FullName "CiyuanSha_ReleaseBuild"
}

$OutputRoot = [IO.Path]::GetFullPath($OutputRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Resolve-GodotExecutable {
    if (![string]::IsNullOrWhiteSpace($GodotExecutable)) {
        return [IO.Path]::GetFullPath($GodotExecutable)
    }

    $bundled = Join-Path $repositoryRoot "Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe"
    if (Test-Path -LiteralPath $bundled) {
        return $bundled
    }

    $command = Get-Command "godot" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw "Godot 4.6.2 Mono was not found. Pass -GodotExecutable with its console executable."
}

function Assert-SafeReleasePath {
    param([string]$Path, [string]$ExpectedParent)

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $actualParent = [IO.Directory]::GetParent($resolvedPath).FullName.TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (![string]::Equals($actualParent, $ExpectedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release path must be an immediate child of the output root: $resolvedPath"
    }

    if (![IO.Path]::GetFileName($resolvedPath).StartsWith("CiyuanSha-Windows-x64-", [StringComparison]::Ordinal)) {
        throw "Release directory has an unexpected name: $resolvedPath"
    }
}

Push-Location $repositoryRoot
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit.Length -ne 40) {
        throw "Unable to resolve the Git commit."
    }

    $dirty = @(& git status --porcelain --untracked-files=normal)
    if (!$AllowDirty -and $dirty.Count -gt 0) {
        throw "The working tree is dirty. Commit the release inputs or pass -AllowDirty for a non-reproducible local build."
    }

    $shortCommit = $commit.Substring(0, 7)
    $releaseName = "CiyuanSha-Windows-x64-v$Version-$shortCommit"
    $releaseDirectory = Join-Path $OutputRoot $releaseName
    Assert-SafeReleasePath -Path $releaseDirectory -ExpectedParent $OutputRoot

    if (Test-Path -LiteralPath $releaseDirectory) {
        Remove-Item -LiteralPath $releaseDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null

    $godot = Resolve-GodotExecutable
    if (!(Test-Path -LiteralPath $godot)) {
        throw "Godot executable does not exist: $godot"
    }

    $template = Join-Path $env:APPDATA "Godot\export_templates\4.6.2.stable.mono\windows_release_x86_64.exe"
    if (!(Test-Path -LiteralPath $template)) {
        throw "Godot 4.6.2 Mono export templates are not installed: $template"
    }

    Invoke-NativeCommand -FilePath "dotnet" -Arguments @("build", "CiyuanSha.sln", "-c", "Release", "--nologo")
    if (!$SkipTests) {
        Invoke-NativeCommand -FilePath "dotnet" -Arguments @(
            "test",
            "CiyuanSha.GameCore.Tests\CiyuanSha.GameCore.Tests.csproj",
            "-c",
            "Release",
            "--no-build",
            "--logger",
            "console;verbosity=minimal"
        )
    }

    $gameExecutable = Join-Path $releaseDirectory "CiyuanSha.exe"
    Invoke-NativeCommand -FilePath $godot -Arguments @(
        "--headless",
        "--path",
        $repositoryRoot,
        "--export-release",
        "Windows x64 Portable",
        $gameExecutable
    )

    if (!(Test-Path -LiteralPath $gameExecutable)) {
        throw "Godot reported success but the exported executable was not created."
    }

    $requiredRuntimeFiles = @("hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll")
    foreach ($runtimeFile in $requiredRuntimeFiles) {
        $found = Get-ChildItem -LiteralPath $releaseDirectory -Recurse -File -Filter $runtimeFile -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -eq $found) {
            throw "The export is not self-contained; required runtime file is missing: $runtimeFile"
        }
    }

    foreach ($legalFile in @("LICENSE", "NOTICE", "THIRD_PARTY_NOTICES")) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $legalFile) -Destination (Join-Path $releaseDirectory $legalFile) -Force
    }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot "Data") -Destination (Join-Path $releaseDirectory "Data") -Recurse -Force

    $artMappingPath = Join-Path $repositoryRoot "Data\general_art_mapping.json"
    $artMapping = Get-Content -LiteralPath $artMappingPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $artSourceDirectory = Join-Path $repositoryRoot "武将卡牌"
    $artDestinationDirectory = Join-Path $releaseDirectory "武将卡牌"
    New-Item -ItemType Directory -Force -Path $artDestinationDirectory | Out-Null
    $mappedArtFiles = @($artMapping.Mappings.PSObject.Properties.Value) |
        Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique
    if ($mappedArtFiles.Count -ne 12) {
        throw "Expected 12 mapped general art files, found $($mappedArtFiles.Count)."
    }
    foreach ($artFile in $mappedArtFiles) {
        $artSource = Join-Path $artSourceDirectory $artFile
        if (!(Test-Path -LiteralPath $artSource)) {
            throw "Mapped general art file does not exist: $artSource"
        }
        Copy-Item -LiteralPath $artSource -Destination (Join-Path $artDestinationDirectory $artFile) -Force
    }

    $legalDirectory = Join-Path $releaseDirectory "Legal"
    New-Item -ItemType Directory -Force -Path $legalDirectory | Out-Null
    foreach ($document in @("noname-source-ledger.md", "asset-license-ledger.md", "gamecore-v2-rules.md")) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot "Docs\$document") -Destination (Join-Path $legalDirectory $document) -Force
    }

    $sourceDirectory = Join-Path $releaseDirectory "Source"
    New-Item -ItemType Directory -Force -Path $sourceDirectory | Out-Null
    $sourceArchive = Join-Path $sourceDirectory "CiyuanSha-Source-$shortCommit.zip"
    Invoke-NativeCommand -FilePath "git" -Arguments @("archive", "--format=zip", "--output=$sourceArchive", $commit)

    $readme = @"
次元杀 Windows x64 自包含便携版

版本：$Version
源码提交：$commit
协议：V2（不兼容旧客户端）

运行方法：
1. 解压整个压缩包，不要只从压缩包内直接打开 EXE。
2. 双击 CiyuanSha.exe。
3. 首次作为房主联机时，允许 Windows 防火墙的专用网络访问。

系统要求：
- Windows 10/11 x64
- 支持 DirectX 11 或 Vulkan 的显卡
- 不需要安装 Godot、.NET Runtime 或 .NET SDK

说明：
- 当前版本提供局域网主机/加入功能，不包含公网匹配服务。
- 本构建未进行商业代码签名，Windows SmartScreen 可能显示未知发布者。
- 录像和本机设置保存在 Godot 的用户数据目录。

许可证与源码：
- 本项目采用 GPL-3.0-only。
- LICENSE、NOTICE、THIRD_PARTY_NOTICES 位于本目录。
- 与本二进制对应的完整源码位于 Source\CiyuanSha-Source-$shortCommit.zip。
- 无名杀参考与移植记录位于 Legal\noname-source-ledger.md。
"@
    Set-Content -LiteralPath (Join-Path $releaseDirectory "README_PORTABLE.txt") -Value $readme -Encoding UTF8

    $manifest = [ordered]@{
        product = "CiyuanSha"
        displayName = "次元杀"
        version = $Version
        sourceCommit = $commit
        platform = "windows-x86_64"
        engine = "Godot 4.6.2 stable mono"
        framework = ".NET 8 self-contained"
        protocolVersion = 2
        builtAtUtc = [DateTime]::UtcNow.ToString("o")
        signed = $false
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $releaseDirectory "release-manifest.json") -Encoding UTF8

    $verificationDirectory = Join-Path $releaseDirectory "Verification"
    New-Item -ItemType Directory -Force -Path $verificationDirectory | Out-Null
    $stdoutLog = Join-Path $verificationDirectory "portable-smoke.stdout.log"
    $stderrLog = Join-Path $verificationDirectory "portable-smoke.stderr.log"
    $godotLog = Join-Path $verificationDirectory "portable-smoke.godot.log"
    $previousDotnetRoot = $env:DOTNET_ROOT
    $previousMultilevelLookup = $env:DOTNET_MULTILEVEL_LOOKUP
    try {
        $env:DOTNET_ROOT = Join-Path $releaseDirectory "__global_dotnet_disabled__"
        $env:DOTNET_MULTILEVEL_LOOKUP = "0"
        $smoke = Start-Process `
            -FilePath $gameExecutable `
            -ArgumentList @("--headless", "--quit-after", "10", "--log-file", $godotLog) `
            -WindowStyle Hidden `
            -Wait `
            -PassThru `
            -RedirectStandardOutput $stdoutLog `
            -RedirectStandardError $stderrLog
        if ($smoke.ExitCode -ne 0) {
            throw "Portable smoke test failed with exit code $($smoke.ExitCode). See $stderrLog"
        }
        $smokeText = @(
            Get-Content -LiteralPath $stdoutLog -Raw -ErrorAction SilentlyContinue
            Get-Content -LiteralPath $stderrLog -Raw -ErrorAction SilentlyContinue
            Get-Content -LiteralPath $godotLog -Raw -ErrorAction SilentlyContinue
        ) -join [Environment]::NewLine
        if ($smokeText -match "GameCore content initialization failed|Unhandled exception|SCRIPT ERROR|ERROR:") {
            throw "Portable smoke test logged a runtime error. See $godotLog"
        }
    }
    finally {
        $env:DOTNET_ROOT = $previousDotnetRoot
        $env:DOTNET_MULTILEVEL_LOOKUP = $previousMultilevelLookup
    }

    $checksumPath = Join-Path $releaseDirectory "checksums.sha256"
    $checksumLines = Get-ChildItem -LiteralPath $releaseDirectory -Recurse -File |
        Where-Object { $_.FullName -ne $checksumPath } |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($releaseDirectory.Length + 1).Replace("\", "/")
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash *$relativePath"
        }
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllLines($checksumPath, [string[]]$checksumLines, $utf8WithoutBom)

    $zipPath = Join-Path $OutputRoot "$releaseName.zip"
    $zipChecksumPath = "$zipPath.sha256"
    if (!$NoZip) {
        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }
        Compress-Archive -LiteralPath $releaseDirectory -DestinationPath $zipPath -CompressionLevel Optimal
        $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -LiteralPath $zipChecksumPath -Value "$zipHash *$([IO.Path]::GetFileName($zipPath))" -Encoding ASCII
    }

    Write-Host "Portable folder: $releaseDirectory"
    if (!$NoZip) {
        Write-Host "Portable archive: $zipPath"
        Write-Host "Archive checksum: $zipChecksumPath"
    }
    Write-Host "Source commit: $commit"
}
finally {
    Pop-Location
}
