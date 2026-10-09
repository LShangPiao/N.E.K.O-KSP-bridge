# Build N.E.K.O-KSP-bridge
#
# KSP mods are compiled against the game's own assemblies.
# The KSP install path contains spaces, which breaks csc's /reference:
# arguments, so we copy the needed assemblies into a local refs/ dir first.
#
# Compiler: the only one available on this machine is the .NET Framework 4.0
# csc.exe, which means C# 5 syntax only (no string interpolation, no ?.,
# no expression-bodied members). That is a hard constraint - keep the sources
# C# 5 compatible.

$ErrorActionPreference = "Stop"

$Ksp       = "F:\SteamLibrary\steamapps\common\Kerbal Space Program"
$Managed   = Join-Path $Ksp "KSP_x64_Data\Managed"
$Csc       = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$GameData  = Join-Path $Ksp "GameData"
$ModDirName = "N.E.K.O-KSP-bridge"

$Root      = $PSScriptRoot
if (-not $Root) { $Root = Split-Path -Parent $MyInvocation.MyCommand.Path }
$SrcDir    = Join-Path $Root "src"
$RefDir    = Join-Path $Root "refs"
$OutDir    = Join-Path $Root "dist"
$OutDll    = Join-Path $OutDir "NekoKspBridge.dll"

Write-Output "=== environment ==="
if (-not (Test-Path $Csc))     { throw "csc.exe not found: $Csc" }
if (-not (Test-Path $Managed)) { throw "KSP Managed dir not found: $Managed" }
Write-Output "  csc     : $Csc"
Write-Output "  managed : $Managed"

New-Item -ItemType Directory -Force -Path $RefDir | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$refNames = @(
  "Assembly-CSharp.dll",
  "Assembly-CSharp-firstpass.dll",
  "UnityEngine.dll",
  "UnityEngine.CoreModule.dll",
  "UnityEngine.IMGUIModule.dll",
  "UnityEngine.PhysicsModule.dll",
  "UnityEngine.UI.dll",
  "UnityEngine.UIModule.dll",
  "0Harmony.dll"
)

$refs = @()
foreach ($name in $refNames) {
  $src = Join-Path $Managed $name
  if (-not (Test-Path $src)) {
    # 0Harmony lives in GameData, not Managed
    $alt = Join-Path $Ksp "GameData\000_Harmony\$name"
    if (Test-Path $alt) { $src = $alt }
  }
  if (-not (Test-Path $src)) {
    Write-Output "  skip (not found): $name"
    continue
  }
  $dst = Join-Path $RefDir $name
  Copy-Item $src $dst -Force
  $refs += $dst
}

$srcFiles = Get-ChildItem -Path $SrcDir -Filter "*.cs" | Select-Object -ExpandProperty FullName
Write-Output ""
Write-Output "=== sources ==="
$srcFiles | ForEach-Object { "  " + (Split-Path -Leaf $_) }
Write-Output ""
Write-Output "=== references ==="
$refs | ForEach-Object { "  " + (Split-Path -Leaf $_) }

$argList = @(
  "/target:library",
  "/out:$OutDll",
  "/nologo",
  "/optimize+",
  "/reference:System.dll",
  "/reference:System.Core.dll",
  "/reference:System.Xml.dll"
)
foreach ($r in $refs) { $argList += "/reference:$r" }
$argList += $srcFiles

Write-Output ""
Write-Output "=== compile ==="
& $Csc $argList
$code = $LASTEXITCODE
Write-Output "csc exit: $code"

if ($code -ne 0) {
  throw "compile failed"
}

$dll = Get-Item $OutDll
Write-Output ""
Write-Output "=== output ==="
Write-Output "  $OutDll  ($([math]::Round($dll.Length/1KB,1)) KB)"

# Deploy into GameData/<mod>/
$target = Join-Path $GameData $ModDirName
New-Item -ItemType Directory -Force -Path $target | Out-Null
$targetDll = Join-Path $target "NekoKspBridge.dll"

$deployed = $false
try {
  Copy-Item $OutDll $targetDll -Force -ErrorAction Stop
  $deployed = $true
} catch {
  Write-Output ""
  Write-Output "deploy failed (KSP may be running): $($_.Exception.Message)"
}

if ($deployed) {
  Write-Output ""
  Write-Output "=== deployed ==="
  Write-Output "  $targetDll"
}
