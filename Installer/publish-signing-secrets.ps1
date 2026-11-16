<#
  패키지 서명 인증서의 개인 키를 GitHub Actions 시크릿으로 올린다(release 워크플로가 .msix 서명에 사용).
    MSIX_CERT_PFX       PFX(base64)
    MSIX_CERT_PASSWORD  PFX 암호(이 스크립트가 무작위로 만들며 화면·파일 어디에도 남기지 않는다)

  이 PC 의 CurrentUser\My 에 new-signing-cert.ps1 로 만든 인증서(개인 키 포함)가 있어야 한다.
  gh CLI 로그인 필요. -BackupTo <경로.pfx> 를 주면 같은 PFX 를 그 파일로도 남기고 암호를 한 번 보여준다(키 백업용).
#>
#Requires -Version 5
param([string]$BackupTo)
$ErrorActionPreference = 'Stop'

$cerPath = Join-Path $PSScriptRoot 'Package\signing.cer'
if (-not (Test-Path $cerPath)) { throw "공개 인증서가 없습니다: $cerPath  (먼저 new-signing-cert.ps1)" }
$thumb = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $cerPath).Thumbprint
$cert  = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -eq $thumb -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) { throw "개인 키가 있는 인증서($thumb)가 이 PC 의 CurrentUser\My 에 없습니다." }

# 무작위 암호(영숫자 40자).
$bytes = New-Object byte[] 30
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$plain  = [Convert]::ToBase64String($bytes) -replace '[+/=]', 'x'
$secure = ConvertTo-SecureString $plain -AsPlainText -Force

# 값은 BOM 없는 임시 파일에 써서 gh 의 표준 입력으로 넘긴다. 문자열을 파이프로 보내면 Windows PowerShell 이 맨 앞에
# UTF-8 BOM 을 붙이는 환경이 있어(실제로 겪음) 시크릿이 깨진 채 올라간다 — Base64 해독 실패, 암호 불일치.
function Set-Secret([string]$name, [string]$value) {
  $tmp = Join-Path ([IO.Path]::GetTempPath()) ("secret-" + [Guid]::NewGuid().ToString('N') + '.txt')
  try {
    [IO.File]::WriteAllText($tmp, $value, (New-Object System.Text.ASCIIEncoding))
    cmd /c "gh secret set $name < `"$tmp`""
    if ($LASTEXITCODE -ne 0) { throw "gh secret set $name 실패" }
  }
  finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}

$pfx = Join-Path ([IO.Path]::GetTempPath()) ("signing-" + [Guid]::NewGuid().ToString('N') + '.pfx')
try {
  Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null
  Set-Secret 'MSIX_CERT_PFX' ([Convert]::ToBase64String([IO.File]::ReadAllBytes($pfx)))
  Set-Secret 'MSIX_CERT_PASSWORD' $plain
  Write-Host 'GitHub 시크릿 등록 완료: MSIX_CERT_PFX, MSIX_CERT_PASSWORD' -ForegroundColor Green

  if ($BackupTo) {
    Copy-Item $pfx $BackupTo -Force
    Write-Host "백업 PFX: $BackupTo"
    Write-Host "백업 암호(지금만 표시됩니다 — 안전한 곳에 보관): $plain" -ForegroundColor Yellow
  }
}
finally {
  Remove-Item $pfx -Force -ErrorAction SilentlyContinue
}
