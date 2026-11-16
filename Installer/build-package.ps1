<#
  Windows 11 새 우클릭 메뉴용 스파스 패키지 빌드. build-installer.ps1 이 publish 뒤에 호출한다.
    1) 셸 확장을 NativeAOT 로 publish            → <PublishDir>\shell\Claude-Account-Switcher.ShellExtension.dll
    2) AppxManifest.xml 에 버전을 채워 .msix 로 묶음 → <PublishDir>\shell\Claude-Account-Switcher.msix
    3) 자체 서명 인증서로 서명(signtool)

  서명 인증서(Installer\Package\signing.cer 와 지문이 같고 개인 키가 있는 것)가 CurrentUser\My 에 없으면
  경고만 남기고 아무것도 만들지 않는다 → 앱은 "새 메뉴에 표시" 옵션을 숨기고 클래식 메뉴만 쓴다(포크·PR 빌드).
  필요 도구: Visual Studio C++ 빌드 도구(NativeAOT 링크) + Windows SDK(makeappx/signtool).
#>
#Requires -Version 5
param(
  [Parameter(Mandatory)][string]$PublishDir,
  [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference = 'Stop'

$root     = Split-Path -Parent $PSScriptRoot
$pkgSrc   = Join-Path $PSScriptRoot 'Package'
$shellDir = Join-Path $PublishDir 'shell'
$dllName  = 'Claude-Account-Switcher.ShellExtension.dll'
$msix     = Join-Path $shellDir 'Claude-Account-Switcher.msix'

# ---- 서명 인증서 ----
$thumb = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 (Join-Path $pkgSrc 'signing.cer')).Thumbprint
$cert  = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -eq $thumb -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) {
  Write-Warning "패키지 서명 인증서($thumb)의 개인 키가 없습니다 → 스파스 패키지를 건너뜁니다(새 메뉴 옵션 없이 빌드)."
  return
}

# ---- 도구 ----
function Find-SdkTool([string]$name) {
  $hit = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64\$name" -ErrorAction SilentlyContinue |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1
  if (-not $hit) { throw "Windows SDK 의 $name 을 찾지 못했습니다." }
  $hit.FullName
}
$makeappx = Find-SdkTool 'makeappx.exe'
$signtool = Find-SdkTool 'signtool.exe'

# NativeAOT 링크 단계가 vswhere.exe 를 PATH 에서 찾는다(없으면 링커 경로 탐색이 깨진다).
$vsInstaller = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if ((Test-Path $vsInstaller) -and ($env:PATH -notlike "*$vsInstaller*")) { $env:PATH += ";$vsInstaller" }

# ---- 버전(4자리) + CLSID 일치 검사 ----
$parts = @($Version.TrimStart('v', 'V').Split('.')) + @('0', '0', '0', '0')
$ver4  = ($parts[0..3]) -join '.'

$manifest = Get-Content (Join-Path $pkgSrc 'AppxManifest.xml') -Raw -Encoding UTF8
$contract = Get-Content (Join-Path $root 'Services\ExplorerMenuContract.cs') -Raw -Encoding UTF8
if ($contract -notmatch 'CommandClsid\s*=\s*"([0-9A-Fa-f-]{36})"') { throw 'ExplorerMenuContract.cs 에서 CommandClsid 를 찾지 못했습니다.' }
$clsid = $Matches[1]
$inManifest = [regex]::Matches($manifest, '(?:Clsid|Id)="([0-9A-Fa-f]{8}-[0-9A-Fa-f-]{27})"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
if (@($inManifest).Count -ne 1 -or $inManifest -ne $clsid) { throw "AppxManifest.xml 의 CLSID($($inManifest -join ', '))가 ExplorerMenuContract.CommandClsid($clsid)와 다릅니다." }
if ($manifest -notmatch [regex]::Escape("Publisher=`"$($cert.Subject)`"")) { throw "AppxManifest.xml 의 Publisher 가 인증서 Subject($($cert.Subject))와 다릅니다." }

# ---- 1) 셸 확장 publish ----
Write-Host "   shell extension (NativeAOT) v$ver4"
$work = Join-Path ([IO.Path]::GetTempPath()) ("cas-package-" + [Guid]::NewGuid().ToString('N'))
$aot  = Join-Path $work 'aot'
$stage = Join-Path $work 'stage'
New-Item -ItemType Directory -Force $aot, (Join-Path $stage 'shell'), $shellDir | Out-Null
try {
  & dotnet publish (Join-Path $root 'ShellExtension\ShellExtension.csproj') -c Release -r win-x64 "-p:Version=$($ver4 -replace '\.0$', '')" -o $aot --nologo -v q
  if ($LASTEXITCODE -ne 0) { throw '셸 확장 publish 실패' }
  Copy-Item (Join-Path $aot $dllName) $shellDir -Force

  # ---- 2) 패키지 ----
  # Logo 는 패키지 안에서, 나머지 그림은 외부 위치(설치 폴더)에서 읽으므로 양쪽에 둔다.
  Copy-Item (Join-Path $pkgSrc 'shell\*.png') (Join-Path $stage 'shell') -Force
  Copy-Item (Join-Path $pkgSrc 'shell\Logo*.png') $shellDir -Force
  [IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest.Replace('Version="0.0.0.0"', "Version=`"$ver4`""), (New-Object System.Text.UTF8Encoding $false))

  if (Test-Path $msix) { Remove-Item $msix -Force }
  & $makeappx pack /o /nv /d $stage /p $msix | Out-Null
  if ($LASTEXITCODE -ne 0 -or -not (Test-Path $msix)) { throw 'makeappx pack 실패' }

  # ---- 3) 서명 ----
  & $signtool sign /fd SHA256 /sha1 $thumb /s My $msix | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'signtool sign 실패' }
}
finally {
  Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "   package = $msix  (signed by $($cert.Subject), $thumb)"
