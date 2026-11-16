<#
  스파스 패키지 서명용 자체 서명 인증서를 만든다(최초 1회, 또는 키를 잃어 교체할 때만).

  - 개인 키는 이 PC 의 인증서 저장소(CurrentUser\My)에만 남는다. 저장소에는 공개 인증서만 들어간다:
      Installer\Package\signing.cer   ← 앱에 임베드되어, 사용자가 "새 메뉴에 표시"를 켤 때 신뢰 등록(UAC 1회)에 쓰인다.
  - CI(release 워크플로)가 서명하려면 개인 키가 GitHub 시크릿에 있어야 한다 → publish-signing-secrets.ps1 참조.
  - 인증서를 교체하면 기존 사용자는 새 인증서를 다시 신뢰 등록해야 한다(앱이 알아서 UAC 를 한 번 더 띄운다).
    그러니 꼭 필요할 때만 다시 만들 것. Subject 는 AppxManifest.xml 의 Publisher 와 글자 그대로 같아야 한다.
#>
#Requires -Version 5
param([switch]$Force)
$ErrorActionPreference = 'Stop'

$subject  = 'CN=akon47'
$cerPath  = Join-Path $PSScriptRoot 'Package\signing.cer'

if ((Test-Path $cerPath) -and -not $Force) {
  throw "이미 인증서가 있습니다: $cerPath  (교체하려면 -Force — 기존 사용자가 다시 신뢰 등록해야 합니다)"
}

$cert = New-SelfSignedCertificate -Type Custom -Subject $subject `
  -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
  -FriendlyName 'Claude Account Switcher package signing' `
  -CertStoreLocation Cert:\CurrentUser\My `
  -NotAfter (Get-Date).AddYears(20) `
  -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')   # 코드 서명 전용, CA 아님

Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
Write-Host "인증서 생성: $($cert.Subject)  지문 $($cert.Thumbprint)  만료 $($cert.NotAfter.ToString('yyyy-MM-dd'))" -ForegroundColor Green
Write-Host "공개 인증서: $cerPath"
Write-Host '다음: powershell Installer\publish-signing-secrets.ps1  (개인 키를 GitHub 시크릿으로 올림)'
