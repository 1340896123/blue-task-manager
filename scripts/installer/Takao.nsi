; Standard NSIS/MUI2 offline installer. Generated includes enumerate every
; installed/deleted file; user data and unlisted files are never removed.
Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WinVer.nsh"
!include "x64.nsh"

!ifdef TASKMANAGER_INSTALLER_TEST
    ; Compile-time fixtures have their own product identity and no shortcuts.
    ; The production builder never supplies this define.
    !ifndef TEST_PRODUCT_ID
        !error "TEST_PRODUCT_ID is required for the isolated installer fixture."
    !endif
    !ifndef TEST_PRODUCT_KEY
        !error "TEST_PRODUCT_KEY is required for the isolated installer fixture."
    !endif
    !define PRODUCT_NAME "Takao Update Installer Fixture"
    !define PRODUCT_ID "${TEST_PRODUCT_ID}"
    !define PRODUCT_KEY "${TEST_PRODUCT_KEY}"
!else
    !define PRODUCT_NAME "Takao"
    !define PRODUCT_ID "BlueDomainTaskManager.Admin.x64"
    !define PRODUCT_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\BlueDomainTaskManager"
!endif
!ifndef RELEASE_TAG
    !define RELEASE_TAG ""
!endif
!ifndef SOURCE_COMMIT
    !define SOURCE_COMMIT ""
!endif
Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "${INSTALLER_PATH}"
InstallDir "$PROGRAMFILES64\Takao"
RequestExecutionLevel admin
ManifestSupportedOS all
SetCompressor /SOLID lzma
SetCompressorDictSize 32
SetDatablockOptimize on
VIProductVersion "${PRODUCT_VERSION}.0"
VIAddVersionKey /LANG=2052 "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey /LANG=2052 "ProductVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=2052 "FileVersion" "${PRODUCT_VERSION}.0"
VIAddVersionKey /LANG=2052 "FileDescription" "${PRODUCT_NAME} 安装程序"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Takao contributors"
VIAddVersionKey /LANG=2052 "Comments" "ReleaseTag=${RELEASE_TAG}; SourceCommit=${SOURCE_COMMIT}"
!define MUI_ICON "${PROJECT_ROOT}\assets\branding\takao.ico"
!define MUI_UNICON "${PROJECT_ROOT}\assets\branding\takao.ico"
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"

Var RegisteredDirectory
Var HashPath
Var HashExpected
Var HashProvider
Var HashObject
Var HashFile
Var HashBuffer
Var HashRead
Var HashResult
Var HashIndex
Var HashActual
Var UpdateRequested
Var UpdateMode
Var UpdateRecording
Var UpdateAttemptId
Var UpdateWaitStarted
Var UpdateFailureRecorded

!macro RecordUpdateFailure Error
    ${If} $UpdateRecording == 1
        WriteRegStr HKLM "${PRODUCT_KEY}" "LastUpdateAttemptId" "$UpdateAttemptId"
        WriteRegStr HKLM "${PRODUCT_KEY}" "LastUpdateResult" "failed"
        WriteRegStr HKLM "${PRODUCT_KEY}" "LastUpdateError" "${Error}"
        StrCpy $UpdateFailureRecorded 1
    ${EndIf}
!macroend

!macro Fail Message
    !insertmacro RecordUpdateFailure "${Message}"
    MessageBox MB_ICONSTOP|MB_OK "${Message}" /SD IDOK
    SetErrorLevel 2
    Abort
!macroend

!macro PlatformCheck
    SetRegView 64
    SetShellVarContext all
    ${IfNot} ${RunningX64}
        !insertmacro Fail "本程序需要 Windows x64。"
    ${EndIf}
    ${IfNot} ${AtLeastWin10}
        !insertmacro Fail "本程序需要 Windows 10（19041）或更新版本。"
    ${EndIf}
    ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "CurrentBuildNumber"
    ${If} $0 < 19041
        !insertmacro Fail "本程序需要 Windows 10（19041）或更新版本。"
    ${EndIf}
    ReadRegStr $RegisteredDirectory HKLM "${PRODUCT_KEY}" "InstallLocation"
!macroend

; Validate every existing path component, disallow links/junctions, and require
; a dedicated leaf. Do not allow a drive root or arbitrary existing directory.
!macro DefineValidateDirectory Prefix
Function ${Prefix}ValidateDirectory
    ; NSIS GetFullPathName also expands the filesystem's long path and can
    ; return empty when a new parent does not exist. Win32 normalizes lexical
    ; absolute paths without requiring an existing installation directory.
    System::Call 'kernel32::GetFullPathNameW(w "$INSTDIR", i ${NSIS_MAX_STRLEN}, w .r0, p 0) i .r1'
    ${If} $1 == 0
    ${OrIf} $1 >= ${NSIS_MAX_STRLEN}
        !insertmacro Fail "安装路径无效或过长。"
    ${EndIf}
    StrCpy $INSTDIR $0
    StrCpy $1 $INSTDIR 2 1
    ${If} $1 != ":\"
        !insertmacro Fail "请选择本地磁盘上的专用 Takao 文件夹。"
    ${EndIf}
    ${GetFileName} "$INSTDIR" $0
    ${If} $0 != "Takao"
        ${If} $0 != "BlueDomainTaskManager"
        ${OrIf} $RegisteredDirectory != $INSTDIR
            !insertmacro Fail "新安装目录必须以 Takao 为最后一级文件夹；已有安装只能沿用已注册的旧目录。"
        ${EndIf}
    ${EndIf}
    StrCpy $1 $INSTDIR
    ${Do}
        System::Call 'kernel32::GetFileAttributesW(w r1) i .r2'
        ${If} $2 != -1
            IntOp $2 $2 & 0x400
            ${If} $2 != 0
                !insertmacro Fail "安装路径不能包含符号链接或目录联接。"
            ${EndIf}
        ${EndIf}
        ${GetParent} "$1" $0
        ${If} $0 == $1
            ${ExitDo}
        ${EndIf}
        StrCpy $1 $0
    ${LoopUntil} $1 == ""
    ${If} $RegisteredDirectory != ""
    ${AndIf} $RegisteredDirectory != $INSTDIR
        !insertmacro Fail "已在其他目录安装。请先卸载旧版后再更换目录。"
    ${EndIf}
FunctionEnd
!macroend
!insertmacro DefineValidateDirectory ""
!insertmacro DefineValidateDirectory "un."

; Read standard ShellLink/IPersistFile COM interfaces before deleting a legacy
; shortcut. A user-repointed link or one with custom arguments is preserved.
!ifndef TASKMANAGER_INSTALLER_TEST
!macro DefineRemoveLegacyShortcut Prefix
Function ${Prefix}RemoveLegacyShortcut
    Exch $0
    Push $1
    Push $2
    Push $3
    Push $4
    Push $5
    StrCpy $1 0
    StrCpy $2 0
    System::Call 'ole32::OleInitialize(p 0) i .r3'
    ${If} $3 >= 0
        System::Call 'ole32::CoCreateInstance(g "{00021401-0000-0000-C000-000000000046}", p 0, i 1, g "{000214F9-0000-0000-C000-000000000046}", *p .r1) i .r3'
        ${If} $3 == 0
            System::Call '$1->0(g "{0000010B-0000-0000-C000-000000000046}", *p .r2) i .r3'
            ${If} $3 == 0
                System::Call '$2->5(w r0, i 0) i .r3'
                ${If} $3 == 0
                    System::Call '$1->3(w .r4, i ${NSIS_MAX_STRLEN}, p 0, i 0) i .r3'
                    ${If} $3 == 0
                        System::Call '$1->10(w .r5, i ${NSIS_MAX_STRLEN}) i .r3'
                        ${If} $3 == 0
                        ${AndIf} $5 == ""
                            ${If} $4 == "$INSTDIR\TaskManager.Host.exe"
                            ${OrIf} $4 == "$INSTDIR\TaskManager.Launcher.exe"
                                Delete "$0"
                            ${EndIf}
                        ${EndIf}
                    ${EndIf}
                ${EndIf}
                System::Call '$2->2() i'
            ${EndIf}
            System::Call '$1->2() i'
        ${EndIf}
        System::Call 'ole32::OleUninitialize()'
    ${EndIf}
    Pop $5
    Pop $4
    Pop $3
    Pop $2
    Pop $1
    Pop $0
FunctionEnd
!macroend
!insertmacro DefineRemoveLegacyShortcut ""
!insertmacro DefineRemoveLegacyShortcut "un."
!endif

!macro ValidateMarker
    ReadINIStr $0 "$INSTDIR\.bluedomain-install.ini" "Product" "Id"
    ReadINIStr $1 "$INSTDIR\.bluedomain-install.ini" "Product" "Directory"
    ${If} $0 != "${PRODUCT_ID}"
    ${OrIf} $1 != $INSTDIR
    ${OrIf} $RegisteredDirectory != $INSTDIR
        !insertmacro Fail "安装目录或产品标记不匹配。未修改任何应用文件。"
    ${EndIf}
!macroend

; Standard installation and uninstall still refuse an occupied image immediately.
; An authenticated update handoff has one shared 60-second budget for all images.
; GetTickCount subtraction remains valid across its 32-bit wrap for this interval.
!macro CheckImageLock RelativePath
    ${If} ${FileExists} "$INSTDIR\${RelativePath}"
        ${Do}
            System::Call 'kernel32::CreateFileW(w "$INSTDIR\${RelativePath}", i 0x40000000, i 0, p 0, i 3, i 0, p 0) p .r0'
            ${If} $0 != -1
                System::Call 'kernel32::CloseHandle(p r0)'
                ${ExitDo}
            ${EndIf}
            ${If} $UpdateMode != 1
                !insertmacro Fail "应用正在运行或文件被占用。请先关闭本安装目录中的Takao，再重试。"
            ${EndIf}
            System::Call 'kernel32::GetTickCount() i .r1'
            IntOp $1 $1 - $UpdateWaitStarted
            ${If} $1 >= 60000
                !insertmacro RecordUpdateFailure "imageLockTimeout"
                SetErrorLevel 2
                Abort
            ${EndIf}
            Sleep 250
        ${Loop}
    ${EndIf}
!macroend

!macro CheckPayloadPath RelativePath
    System::Call 'kernel32::GetFileAttributesW(w "$INSTDIR\${RelativePath}") i .r0'
    ${If} $0 != -1
        IntOp $0 $0 & 0x400
        ${If} $0 != 0
            !insertmacro Fail "应用目录包含符号链接或目录联接，未修改应用文件。"
        ${EndIf}
    ${EndIf}
!macroend

Function .onInit
    StrCpy $UpdateRequested 0
    StrCpy $UpdateMode 0
    StrCpy $UpdateRecording 0
    StrCpy $UpdateFailureRecorded 0
    ; Only this exact fixed switch is accepted. Values are correlation IDs,
    ; never paths, commands or URLs, and malformed requests cannot fall through
    ; into an ordinary installation.
    System::Call 'kernel32::GetCommandLineW() w .r0'
    ClearErrors
    ${GetOptionsS} "$0" "/TASKMANAGERUPDATE=" $UpdateAttemptId
    ${IfNot} ${Errors}
        StrLen $1 $UpdateAttemptId
        System::Call 'shlwapi::StrSpnW(w "$UpdateAttemptId", w "0123456789abcdef") i .r2'
        ${If} $1 != 32
        ${OrIf} $2 != 32
            !insertmacro Fail "更新尝试标识无效。请从应用中重新选择更新。"
        ${EndIf}
        StrCpy $UpdateRequested 1
    ${Else}
        ClearErrors
        ${GetOptions} "$0" "/TASKMANAGERUPDATE" $1
        ${IfNot} ${Errors}
            !insertmacro Fail "更新参数无效。请从应用中重新选择更新。"
        ${EndIf}
    ${EndIf}
    !insertmacro PlatformCheck
    ; NSIS consumes /D before exposing $CMDLINE. Inspect the original Windows
    ; command line only to detect that switch; retain NSIS's own parsed path.
    System::Call 'kernel32::GetCommandLineW() w .r0'
    ClearErrors
    ${GetOptions} "$0" "/D=" $1
    ${If} $UpdateRequested == 1
        ${IfNot} ${Errors}
            !insertmacro Fail "自动更新不能指定其他安装目录。"
        ${EndIf}
        ${If} $RegisteredDirectory == ""
            !insertmacro Fail "自动更新需要现有正式安装记录。"
        ${EndIf}
        ; A valid attempt can record damaged-directory/marker failures only
        ; after this fixed product registration identifies our uninstaller.
        ; Foreign/missing registrations and invalid parameters are not changed.
        ReadRegStr $1 HKLM "${PRODUCT_KEY}" "DisplayName"
        ReadRegStr $2 HKLM "${PRODUCT_KEY}" "UninstallString"
        ${If} $1 != "${PRODUCT_NAME}"
            ; The fixed product key/marker still identifies the previous brand.
            ${If} $1 != "蓝域任务管理器"
                !insertmacro Fail "自动更新无法确认现有产品安装记录。"
            ${EndIf}
        ${EndIf}
        ${If} $2 != '"$RegisteredDirectory\Uninstall.exe"'
            !insertmacro Fail "自动更新无法确认现有产品安装记录。"
        ${EndIf}
        StrCpy $UpdateRecording 1
        StrCpy $INSTDIR $RegisteredDirectory
        Call ValidateDirectory
        !insertmacro ValidateMarker
        StrCpy $UpdateMode 1
        ClearErrors
        WriteRegStr HKLM "${PRODUCT_KEY}" "LastUpdateAttemptId" "$UpdateAttemptId"
        WriteRegStr HKLM "${PRODUCT_KEY}" "LastUpdateResult" "started"
        WriteRegStr HKLM "${PRODUCT_KEY}" "LastUpdateError" ""
        ${If} ${Errors}
            !insertmacro Fail "无法记录更新尝试，未开始替换应用文件。"
        ${EndIf}
        SetSilent silent
        System::Call 'kernel32::GetTickCount() i .r0'
        StrCpy $UpdateWaitStarted $0
        Return
    ${EndIf}
    ${IfNot} ${Errors}
        Return
    ${EndIf}
    ${If} $RegisteredDirectory != ""
        StrCpy $INSTDIR $RegisteredDirectory
    ${EndIf}
FunctionEnd

Function .onInstFailed
    ${If} $UpdateFailureRecorded != 1
        !insertmacro RecordUpdateFailure "installerFailed"
    ${EndIf}
FunctionEnd

Function .onInstSuccess
    ${If} $UpdateMode == 1
        ClearErrors
        WriteRegStr HKLM "${PRODUCT_KEY}" "LastUpdateAttemptId" "$UpdateAttemptId"
        WriteRegStr HKLM "${PRODUCT_KEY}" "LastUpdateResult" "success"
        WriteRegStr HKLM "${PRODUCT_KEY}" "LastUpdateError" ""
        ${If} ${Errors}
            !insertmacro RecordUpdateFailure "resultWriteFailed"
            SetErrorLevel 2
            Return
        ${EndIf}
        ; Every payload, product marker and registry write has finished. Start
        ; only this installation; NSIS failures never launch a partial payload.
        ClearErrors
        Exec '"$INSTDIR\Takao.Host.exe"'
        ${If} ${Errors}
            !insertmacro RecordUpdateFailure "restartFailed"
            SetErrorLevel 2
        ${EndIf}
    ${EndIf}
FunctionEnd

Function .onVerifyInstDir
    Call ValidateDirectory
FunctionEnd

Section "Takao（必需）" MainSection
    SectionIn RO
    Call ValidateDirectory
    ${DirState} "$INSTDIR" $0
    ${If} $0 == 1
        !insertmacro ValidateMarker
    ${ElseIf} $RegisteredDirectory != ""
        !insertmacro Fail "现有安装记录与目录不一致。请先修复或卸载旧版。"
    ${EndIf}
    !include "${GENERATED_DIR}\check-paths.nsh"
    !insertmacro CheckImageLock "Takao.Host.exe"
    !insertmacro CheckImageLock "Takao.Launcher.exe"
    !insertmacro CheckImageLock "TaskManager.Host.exe"
    !insertmacro CheckImageLock "TaskManager.Launcher.exe"
    !insertmacro CheckImageLock ".electron\node_modules\electron\dist\electron.exe"
    SetOverwrite on
    ClearErrors
    !include "${GENERATED_DIR}\install-files.nsh"
    ${If} ${Errors}
        !insertmacro Fail "写入应用文件失败。请检查磁盘空间或目录权限，然后重新安装。"
    ${EndIf}
    ClearErrors
    WriteINIStr "$INSTDIR\.bluedomain-install.ini" "Product" "Id" "${PRODUCT_ID}"
    WriteINIStr "$INSTDIR\.bluedomain-install.ini" "Product" "Directory" "$INSTDIR"
    WriteINIStr "$INSTDIR\.bluedomain-install.ini" "Product" "Version" "${PRODUCT_VERSION}"
    WriteINIStr "$INSTDIR\.bluedomain-install.ini" "Product" "ReleaseTag" "${RELEASE_TAG}"
    WriteINIStr "$INSTDIR\.bluedomain-install.ini" "Product" "SourceCommit" "${SOURCE_COMMIT}"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    WriteRegStr HKLM "${PRODUCT_KEY}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr HKLM "${PRODUCT_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr HKLM "${PRODUCT_KEY}" "ReleaseTag" "${RELEASE_TAG}"
    WriteRegStr HKLM "${PRODUCT_KEY}" "SourceCommit" "${SOURCE_COMMIT}"
    WriteRegStr HKLM "${PRODUCT_KEY}" "Publisher" "Takao contributors"
    WriteRegStr HKLM "${PRODUCT_KEY}" "URLInfoAbout" "https://github.com/1340896123/blue-task-manager"
    WriteRegStr HKLM "${PRODUCT_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "${PRODUCT_KEY}" "DisplayIcon" '"$INSTDIR\Takao.Host.exe",0'
    WriteRegStr HKLM "${PRODUCT_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKLM "${PRODUCT_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
    WriteRegDWORD HKLM "${PRODUCT_KEY}" "EstimatedSize" ${PAYLOAD_SIZE_KB}
    WriteRegDWORD HKLM "${PRODUCT_KEY}" "NoModify" 1
    WriteRegDWORD HKLM "${PRODUCT_KEY}" "NoRepair" 1
    !ifndef TASKMANAGER_INSTALLER_TEST
        Push "$SMPROGRAMS\蓝域任务管理器.lnk"
        Call RemoveLegacyShortcut
        Push "$DESKTOP\蓝域任务管理器.lnk"
        Call RemoveLegacyShortcut
        CreateShortcut "$SMPROGRAMS\Takao.lnk" "$INSTDIR\Takao.Host.exe" "" "$INSTDIR\Takao.Host.exe" 0
    !endif
    ${If} ${Errors}
        !insertmacro Fail "写入安装标记、卸载记录或快捷方式失败。请修复安装后重试。"
    ${EndIf}
SectionEnd

Section "桌面快捷方式" DesktopSection
    !ifndef TASKMANAGER_INSTALLER_TEST
        ClearErrors
        CreateShortcut "$DESKTOP\Takao.lnk" "$INSTDIR\Takao.Host.exe" "" "$INSTDIR\Takao.Host.exe" 0
        ${If} ${Errors}
            !insertmacro Fail "写入桌面快捷方式失败。请修复安装后重试。"
        ${EndIf}
    !endif
SectionEnd

Function un.onInit
    !insertmacro PlatformCheck
    Call un.ValidateDirectory
    !insertmacro ValidateMarker
FunctionEnd

; CryptoAPI is a Windows system API. No downloaded hash plugin or shell is
; used. Corrupt/missing Host, managed dependencies, runtime and JSON metadata
; abort uninstall before invoking the fixed production maintenance command.
Function un.VerifyFile
    Pop $HashExpected
    Pop $HashPath
    StrCpy $HashProvider 0
    StrCpy $HashObject 0
    StrCpy $HashFile -1
    StrCpy $HashBuffer 0
    System::Call 'advapi32::CryptAcquireContextW(*p .s, p 0, p 0, i 24, i 0xF0000000) i .r0'
    Pop $HashProvider
    ${If} $0 == 0
        Goto hash_failed
    ${EndIf}
    System::Call 'advapi32::CryptCreateHash(p $HashProvider, i 0x800c, p 0, i 0, *p .s) i .r0'
    Pop $HashObject
    ${If} $0 == 0
        Goto hash_failed
    ${EndIf}
    System::Call 'kernel32::CreateFileW(w "$HashPath", i 0x80000000, i 1, p 0, i 3, i 0, p 0) p .s'
    Pop $HashFile
    ${If} $HashFile == -1
        Goto hash_failed
    ${EndIf}
    System::Alloc 65536
    Pop $HashBuffer
    ${If} $HashBuffer == 0
        Goto hash_failed
    ${EndIf}
hash_read:
    System::Call 'kernel32::ReadFile(p $HashFile, p $HashBuffer, i 65536, *i .s, p 0) i .r0'
    Pop $HashRead
    ${If} $0 == 0
        Goto hash_failed
    ${EndIf}
    ${If} $HashRead != 0
        System::Call 'advapi32::CryptHashData(p $HashObject, p $HashBuffer, i $HashRead, i 0) i .r0'
        ${If} $0 == 0
            Goto hash_failed
        ${EndIf}
        Goto hash_read
    ${EndIf}
    System::Call 'advapi32::CryptGetHashParam(p $HashObject, i 2, p $HashBuffer, *i 32, i 0) i .r0'
    ${If} $0 == 0
        Goto hash_failed
    ${EndIf}
    StrCpy $HashActual ""
    StrCpy $HashIndex 0
hash_format:
    IntOp $0 $HashBuffer + $HashIndex
    System::Call '*$0(&i1 .r1)'
    IntFmt $1 "%02X" $1
    StrCpy $HashActual "$HashActual$1"
    IntOp $HashIndex $HashIndex + 1
    IntCmp $HashIndex 32 0 hash_format
    ${If} $HashActual != $HashExpected
        Goto hash_failed
    ${EndIf}
    StrCpy $HashResult 1
    Goto hash_cleanup
hash_failed:
    StrCpy $HashResult 0
hash_cleanup:
    ${If} $HashBuffer != 0
        System::Free $HashBuffer
    ${EndIf}
    ${If} $HashFile != -1
        System::Call 'kernel32::CloseHandle(p $HashFile)'
    ${EndIf}
    ${If} $HashObject != 0
        System::Call 'advapi32::CryptDestroyHash(p $HashObject)'
    ${EndIf}
    ${If} $HashProvider != 0
        System::Call 'advapi32::CryptReleaseContext(p $HashProvider, i 0)'
    ${EndIf}
    ${If} $HashResult != 1
        !insertmacro Fail "应用维护文件缺失、损坏或不匹配：$HashPath。卸载已中止，应用文件和任务栏入口保持原样。请用相同版本安装器修复后重试。"
    ${EndIf}
FunctionEnd

Section "Uninstall"
    Call un.ValidateDirectory
    !insertmacro ValidateMarker
    !include "${GENERATED_DIR}\check-paths.nsh"
    !insertmacro CheckImageLock "Takao.Host.exe"
    !insertmacro CheckImageLock "Takao.Launcher.exe"
    !insertmacro CheckImageLock "TaskManager.Host.exe"
    !insertmacro CheckImageLock "TaskManager.Launcher.exe"
    !insertmacro CheckImageLock ".electron\node_modules\electron\dist\electron.exe"
    !include "${GENERATED_DIR}\verify-maintenance.nsh"
    ClearErrors
    ExecWait '"$INSTDIR\Takao.Host.exe" --uninstall-task-manager-integration' $0
    ${If} ${Errors}
    ${OrIf} $0 != 0
        !insertmacro Fail "无法确认本安装目录的任务栏入口已安全清理。卸载已中止，应用文件保持原样。"
    ${EndIf}
    ClearErrors
    !include "${GENERATED_DIR}\delete-files.nsh"
    ${If} ${Errors}
        !insertmacro Fail "部分应用文件无法删除。卸载记录已保留，请关闭占用文件的程序并重试。"
    ${EndIf}
    ; RMDir intentionally ignores non-empty directories to preserve extra files.
    !include "${GENERATED_DIR}\remove-directories.nsh"
    ClearErrors
    Delete "$INSTDIR\.bluedomain-install.ini"
    !ifndef TASKMANAGER_INSTALLER_TEST
        Push "$SMPROGRAMS\蓝域任务管理器.lnk"
        Call un.RemoveLegacyShortcut
        Push "$DESKTOP\蓝域任务管理器.lnk"
        Call un.RemoveLegacyShortcut
        Delete "$SMPROGRAMS\Takao.lnk"
        Delete "$DESKTOP\Takao.lnk"
    !endif
    DeleteRegKey HKLM "${PRODUCT_KEY}"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"
SectionEnd
