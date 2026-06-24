<#
  Claude Account Switcher 인스톨러 빌드
  1) 자기완결(self-contained) 단일 exe 로 publish
  2) NSIS(makensis)로 Setup.nsi 컴파일 → dist\Claude-Account-Switcher-Setup_v<버전>.exe

  -Version 0.2.0  으로 버전을 주입할 수 있다(CI에서 태그 기반으로 전달). 생략 시 csproj 의 <Version> 사용.
#>
#Requires -Version 5
param([string]$Version)
$ErrorActionPreference = 'Stop'

$root       = Split-Path -Parent $PSScriptRoot          # 저장소 루트
$proj       = Join-Path $root 'Claude-Account-Switcher.csproj'
$publishDir = Join-Path $root 'publish\win-x64'
$distDir    = Join-Path $root 'dist'
$nsi        = Join-Path $PSScriptRoot 'Setup.nsi'

Write-Host '== [1/3] publish (self-contained single-file) ==' -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
$pubArgs = @(
  'publish', $proj,
  '-c', 'Release',
  '-r', 'win-x64',
  '--self-contained', 'true',
  '-p:PublishSingleFile=true',
  '-p:IncludeNativeLibrariesForSelfExtract=true',
  '-p:EnableCompressionInSingleFile=true',
  '-p:DebugType=none',
  '-p:DebugSymbols=false',
  '-o', $publishDir
)
if ($Version) {
  $v = $Version.TrimStart('v', 'V')
  $pubArgs += "-p:Version=$v"
  Write-Host "   injected version = $v"
}
& dotnet @pubArgs
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 실패' }

# exe 에서 파일 버전 읽기
$exe = Join-Path $publishDir 'Claude-Account-Switcher.exe'
if (-not (Test-Path $exe)) { throw "publish 결과 exe 없음: $exe" }
$ver = (Get-Item $exe).VersionInfo.FileVersion       # 4자리(예: 0.2.0.0) — VIProductVersion 용
if ([string]::IsNullOrWhiteSpace($ver)) { $ver = '0.1.0.0' }
# 파일 이름용 3자리 버전(예: 0.2.0). -Version 이 있으면 그 값을, 없으면 4자리에서 앞 3개를 쓴다.
$setupVer = if ($Version) { $Version.TrimStart('v', 'V') } else { ($ver -split '\.')[0..2] -join '.' }
Write-Host "   version = $ver  (setup name = v$setupVer)"

Write-Host '== [2/3] makensis 탐색 ==' -ForegroundColor Cyan
$makensis = (Get-Command makensis.exe -ErrorAction SilentlyContinue).Source
if (-not $makensis) {
  foreach ($p in @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe")) {
    if (Test-Path $p) { $makensis = $p; break }
  }
}
if (-not $makensis) { throw 'makensis.exe 를 찾지 못했습니다. NSIS 설치가 필요합니다.' }
Write-Host "   makensis = $makensis"

New-Item -ItemType Directory -Force $distDir | Out-Null
Get-ChildItem $distDir -Filter *.exe -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host '== [3/3] makensis 컴파일 ==' -ForegroundColor Cyan
& $makensis "/DPRODUCT_VERSION=$ver" "/DSETUP_VERSION=$setupVer" "/DPUBLISH_DIR=$publishDir" "/DOUT_DIR=$distDir" $nsi
if ($LASTEXITCODE -ne 0) { throw 'makensis 컴파일 실패' }

$out = Join-Path $distDir "Claude-Account-Switcher-Setup_v$setupVer.exe"
Write-Host ""
Write-Host "완료: $out" -ForegroundColor Green
if (Test-Path $out) {
  $mb = [math]::Round((Get-Item $out).Length / 1MB, 1)
  Write-Host "크기: $mb MB"
}
