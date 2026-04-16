; FastEdit NSIS Installer Script
; Usage: makensis /DVERSION=1.0.0 /DARCH=x64 /DPUBLISH_DIR=path /DOUTPUT_DIR=path installer.nsi

!include "MUI2.nsh"
!include "FileFunc.nsh"

; ---- Configuration ----
!define APP_NAME "FastEdit"
!define APP_EXEC "FastEdit.exe"
!define PUBLISHER "FastEdit"

!ifndef VERSION
    !define VERSION "1.0.0"
!endif

!ifndef ARCH
    !define ARCH "x64"
!endif

!ifndef PUBLISH_DIR
    !define PUBLISH_DIR "output\publish-${ARCH}"
!endif

!ifndef OUTPUT_DIR
    !define OUTPUT_DIR "output"
!endif

Name "${APP_NAME} ${VERSION} (${ARCH})"
OutFile "${OUTPUT_DIR}\FastEdit-${VERSION}-${ARCH}-setup.exe"
Unicode True
RequestExecutionLevel admin

!if "${ARCH}" == "arm64"
    InstallDir "$PROGRAMFILES64\${APP_NAME}"
!else
    InstallDir "$PROGRAMFILES64\${APP_NAME}"
!endif

; ---- Interface ----
!define MUI_ICON "${PUBLISH_DIR}\fastedit.ico"
!define MUI_UNICON "${PUBLISH_DIR}\fastedit.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXEC}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${APP_NAME}"

; ---- Pages ----
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ---- Install Section ----
Section "Install"
    SetOutPath "$INSTDIR"

    ; Copy all published files
    File /r "${PUBLISH_DIR}\*.*"

    ; Create Start Menu shortcuts
    CreateDirectory "$SMPROGRAMS\${APP_NAME}"
    CreateShortCut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXEC}" "" "$INSTDIR\fastedit.ico"
    CreateShortCut "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk" "$INSTDIR\Uninstall.exe"

    ; Create Desktop shortcut
    CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXEC}" "" "$INSTDIR\fastedit.ico"

    ; Write uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    ; Add/Remove Programs registry
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayName" "${APP_NAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayVersion" "${VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "Publisher" "${PUBLISHER}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayIcon" "$INSTDIR\fastedit.ico"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "InstallLocation" "$INSTDIR"

    ; Calculate installed size
    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    IntFmt $0 "0x%08X" $0
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "EstimatedSize" "$0"

    ; Register file associations (optional — open with FastEdit)
    WriteRegStr HKCR "*\shell\FastEdit" "" "Open with FastEdit"
    WriteRegStr HKCR "*\shell\FastEdit" "Icon" "$INSTDIR\fastedit.ico"
    WriteRegStr HKCR "*\shell\FastEdit\command" "" '"$INSTDIR\${APP_EXEC}" "%1"'
SectionEnd

; ---- Uninstall Section ----
Section "Uninstall"
    ; Remove shortcuts
    Delete "$DESKTOP\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk"
    RMDir "$SMPROGRAMS\${APP_NAME}"

    ; Remove files and install directory
    RMDir /r "$INSTDIR"

    ; Remove registry keys
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
    DeleteRegKey HKCR "*\shell\FastEdit"
SectionEnd
