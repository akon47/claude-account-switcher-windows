; Claude Account Switcher NSIS 설치 스크립트 (현재 사용자 설치, 관리자 불필요)
; build-installer.ps1 이 -DPRODUCT_VERSION / -DPUBLISH_DIR / -DOUT_DIR 을 넘겨준다.

!ifndef PRODUCT_VERSION
  !define PRODUCT_VERSION "0.1.0.0"
!endif
!ifndef SETUP_VERSION
  !define SETUP_VERSION "${PRODUCT_VERSION}"   ; 파일명용(예: 0.2.0). build-installer.ps1 이 3자리로 전달.
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\publish\win-x64"
!endif

!define PRODUCT_NAME "Claude Account Switcher"
!define PRODUCT_PUBLISHER "Kim, Hwan"
!define PRODUCT_WEB_SITE "https://github.com/akon47/claude-account-switcher-windows"
!define APP_EXE "Claude-Account-Switcher.exe"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"

!include "MUI2.nsh"

RequestExecutionLevel user
Unicode true
SetCompressor /SOLID lzma

; ---- MUI ----
!define MUI_ABORTWARNING
!define MUI_ICON "..\Resources\app.ico"
!define MUI_UNICON "..\Resources\app.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "welcome.bmp"
!define MUI_WELCOMEFINISHPAGE_BITMAP_NOSTRETCH
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "welcome.bmp"

!define MUI_WELCOMEPAGE_TITLE "$(WelcomeTitle)"
!define MUI_WELCOMEPAGE_TEXT "$(WelcomeText)"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "$(RunAppText)"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; 언어: 영어를 먼저 넣어 기본(폴백)으로 두고, 한국어를 추가한다.
; NSIS 가 시스템 UI 언어를 자동 감지 → 한국어 윈도우에서만 한국어, 그 외엔 영어로 표시.
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Korean"

LangString WelcomeTitle ${LANG_ENGLISH} "Claude Account Switcher Setup"
LangString WelcomeTitle ${LANG_KOREAN}  "Claude Account Switcher 설치"
LangString WelcomeText  ${LANG_ENGLISH} "A system-tray app to switch and run multiple Claude Code accounts.$\r$\n$\r$\nClick Next to continue."
LangString WelcomeText  ${LANG_KOREAN}  "여러 Claude Code 계정을 전환·실행하는 시스템 트레이 앱입니다.$\r$\n$\r$\n계속하려면 '다음'을 누르세요."
LangString RunAppText   ${LANG_ENGLISH} "Run Claude Account Switcher"
LangString RunAppText   ${LANG_KOREAN}  "Claude Account Switcher 실행"

Name "${PRODUCT_NAME}"
!ifdef OUT_DIR
  OutFile "${OUT_DIR}\Claude-Account-Switcher-Setup_v${SETUP_VERSION}.exe"
!else
  OutFile "Claude-Account-Switcher-Setup_v${SETUP_VERSION}.exe"
!endif
InstallDir "$LOCALAPPDATA\Programs\${PRODUCT_NAME}"
InstallDirRegKey HKCU "${PRODUCT_UNINST_KEY}" "InstallLocation"
ShowInstDetails show
ShowUnInstDetails show
BrandingText "${PRODUCT_NAME} ${PRODUCT_VERSION}"

VIProductVersion "${PRODUCT_VERSION}"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "ProductVersion" "${PRODUCT_VERSION}"
VIAddVersionKey "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey "LegalCopyright" "(c) ${PRODUCT_PUBLISHER}"
VIAddVersionKey "FileDescription" "${PRODUCT_NAME} Setup"

Var NeedUninstall
Var OldUninst

Function CloseRunningApp
  nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM ${APP_EXE} /T'
  Pop $0
  Sleep 400
FunctionEnd

Function .onInit
  Call CloseRunningApp
  ReadRegStr $OldUninst HKCU "${PRODUCT_UNINST_KEY}" "UninstallString"
  StrCmp $OldUninst "" noOld
  StrCpy $NeedUninstall "1"
  noOld:
FunctionEnd

Section "Install"
  ; 기존 설치가 있으면 먼저 조용히 제거
  StrCmp $NeedUninstall "1" 0 skipUninst
    ExecWait '"$OldUninst" /S _?=$INSTDIR'
    Call CloseRunningApp
  skipUninst:

  SetOutPath "$INSTDIR"
  SetOverwrite on
  File /r "${PUBLISH_DIR}\*.*"

  CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
  CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortCut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\${APP_EXE}"

  WriteUninstaller "$INSTDIR\uninst.exe"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "UninstallString" "$INSTDIR\uninst.exe"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\uninst.exe" /S'
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
  WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoRepair" 1
SectionEnd

Function un.onInit
  nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM ${APP_EXE} /T'
  Pop $0
  Sleep 400
FunctionEnd

Section Uninstall
  ; 앱이 등록한 HKCU 항목 정리 (자동 실행 / 탐색기 우클릭 메뉴)
  ; 주의: 아래 키/값 문자열은 Services\AutoStart.cs(RunKey/ValueName)와 Services\ExplorerMenu.cs
  ;       (Verb/SubKeyName)에 정의된 것과 동일해야 한다. 코드에서 이름을 바꾸면 여기도 함께 고칠 것.
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Claude-Account-Switcher"
  DeleteRegKey HKCU "Software\Classes\Directory\shell\ClaudeAccountSwitcher"
  DeleteRegKey HKCU "Software\Classes\Directory\Background\shell\ClaudeAccountSwitcher"
  DeleteRegKey HKCU "Software\Classes\ClaudeAccountSwitcher.Sub"

  Delete "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk"
  RMDir "$SMPROGRAMS\${PRODUCT_NAME}"
  Delete "$DESKTOP\${PRODUCT_NAME}.lnk"

  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${PRODUCT_UNINST_KEY}"
  ; 참고: 사용자 데이터(%APPDATA%\ClaudeAccountSwitcher)와 계정 자격증명은 보존한다.
SectionEnd
