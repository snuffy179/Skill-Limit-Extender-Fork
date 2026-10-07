[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

# スクリプトのある build ディレクトリ
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
# リポジトリ直下（build の 1つ上）
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path

# csproj/ソースの参照はリポジトリ直下
$ProjectFile = Join-Path $RepoRoot "skill_Limit_Extender.csproj"
$PluginName = "SkillLimitExtender"
$AssemblyName = "skill_Limit_Extender.dll"

# Read the canonical version from VersionInfo.cs.
$version = "0.0.0"
$versionFile = Join-Path $RepoRoot "VersionInfo.cs"
if (Test-Path $versionFile) {
    $content = Get-Content $versionFile -Raw
    $m = [regex]::Match($content, 'public const string Version\s*=\s*"([^"]+)"')
    if ($m.Success) { $version = $m.Groups[1].Value }
}

Write-Host "Building ($Configuration)..."
dotnet build $ProjectFile -c $Configuration

# csproj の OutputPath を読み取り（未設定なら bin/Release）
$outputPath = ""
$csprojContent = Get-Content $ProjectFile -Raw
$opMatch = [regex]::Match($csprojContent, '<OutputPath>([^<]+)</OutputPath>')
if ($opMatch.Success) { $outputPath = $opMatch.Groups[1].Value.Trim() }
if ([string]::IsNullOrWhiteSpace($outputPath)) {
    $outputPath = Join-Path $RepoRoot "bin\$Configuration"
}

$dllPath = Join-Path $outputPath $AssemblyName
if (!(Test-Path $dllPath)) {
    throw "DLL not found: $dllPath"
}

# 出力ディレクトリ作成（dist はリポジトリ直下）
$distRoot = Join-Path $RepoRoot "dist"
$pkgRoot = Join-Path $distRoot $PluginName
$pluginsDir = Join-Path $pkgRoot "BepInEx\plugins\$PluginName"

New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null

# パッケージファイルのコピー（manifest/README/icon はリポジトリ直下から）
Copy-Item (Join-Path $RepoRoot "manifest.json") -Destination $pkgRoot -Force
if (Test-Path (Join-Path $RepoRoot "README.md")) {
    Copy-Item (Join-Path $RepoRoot "README.md") -Destination $pkgRoot -Force
}
if (Test-Path (Join-Path $RepoRoot "icon.png")) {
    Copy-Item (Join-Path $RepoRoot "icon.png") -Destination $pkgRoot -Force
}
Copy-Item $dllPath -Destination $pluginsDir -Force

# zip作成
$zipName = "${PluginName}_v$version.zip"
$zipPath = Join-Path $distRoot $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($pkgRoot, $zipPath)

Write-Host "Package created: $zipPath"
Write-Host "Upload this zip to Thunderstore or Nexus."