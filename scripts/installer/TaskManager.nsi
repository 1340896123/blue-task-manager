; Standard NSIS/MUI2 offline installer. Generated includes enumerate every
; installed/deleted file; user data and unlisted files are never removed.
Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WinVer.nsh"
!include "x64.nsh"

!define PRODUCT_NAME "蓝域任务管理器"
!define PRODUCT_ID "BlueDomainTaskManager.Admin.x64"
!define PRODUCT_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\BlueDomainTaskManager"
Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "${INSTALLER_PATH}"
InstallDir "$PROGRAMFILES64\BlueDomainTaskManager"
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
VIAddVersionKey /LANG=2052 "LegalCopyright" "BlueDomainTaskManager contributors"
!define MUI_ICON "${PROJECT_ROOT}\assets\branding\task-manager.ico"
!define MUI_UNICON "${PROJECT_ROOT}\assets\branding\task-manager.ico"
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

!macro Fail Message
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
        !insertmacro Fail "请选择本地磁盘上的专用 BlueDomainTaskManager 文件夹。"
    ${EndIf}
    ${GetFileName} "$INSTDIR" $0
    ${If} $0 != "BlueDomainTaskManager"
        !insertmacro Fail "安装目录必须以 BlueDomainTaskManager 为最后一级文件夹。"
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

!macro ValidateMarker
    ReadINIStr $0 "$INSTDIR\.bluedomain-install.ini" "Product" "Id"
    ReadINIStr $1 "$INSTDIR\.bluedomain-install.ini" "Product" "Directory"
    ${If} $0 != "${PRODUCT_ID}"
    ${OrIf} $1 != $INSTDIR
    ${OrIf} $RegisteredDirectory != $INSTDIR
        !insertmacro Fail "安装目录或产品标记不匹配。未修改任何应用文件。"
    ${EndIf}
!macroend

; An executable image mapped by a running Host/Electron cannot be opened for
; exclusive writing. Fail conservatively for any lock, without killing a PID.
!macro CheckImageLock RelativePath
    ${If} ${FileExists} "$INSTDIR\${RelativePath}"
    System::Call 'kernel32::CreateFileW(w "$INSTDIR\${RelativePath}", i 0x40000000, i 0, p 0, i 3, i 0, p 0) p .r0'
    ${If} $0 == -1
        !insertmacro Fail "应用正在运行或文件被占用。请先关闭本安装目录中的蓝域任务管理器，再重试。"
    ${EndIf}
    System::Call 'kernel32::CloseHandle(p r0)'
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
    !insertmacro PlatformCheck
    ; NSIS consumes /D before exposing $CMDLINE. Inspect the original Windows
    ; command line only to detect that switch; retain NSIS's own parsed path.
    System::Call 'kernel32::GetCommandLineW() w .r0'
    ClearErrors
    ${GetOptions} "$0" "/D=" $1
    ${IfNot} ${Errors}
        Return
    ${EndIf}
    ${If} $RegisteredDirectory != ""
        StrCpy $INSTDIR $RegisteredDirectory
    ${EndIf}
FunctionEnd

Function .onVerifyInstDir
    Call ValidateDirectory
FunctionEnd

Section "蓝域任务管理器（必需）" MainSection
    SectionIn RO
    Call ValidateDirectory
    ${DirState} "$INSTDIR" $0
    ${If} $0 == 1
        !insertmacro ValidateMarker
    ${ElseIf} $RegisteredDirectory != ""
        !insertmacro Fail "现有安装记录与目录不一致。请先修复或卸载旧版。"
    ${EndIf}
    !include "${GENERATED_DIR}\check-paths.nsh"
    !insertmacro CheckImageLock "TaskManager.Host.exe"
    !insertmacro CheckImageLock "TaskManager.Launcher.exe"
    !insertmacro CheckImageLock ".electron\node_modules\electron\dist\electron.exe"
    SetOverwrite on
    ClearErrors
    !include "${GENERATED_DIR}\install-files.nsh"
    ${If} ${Errors}
        !insertmacro Fail "写入应用文件失败。请检查磁盘空间或目录权限，然后重新安装。"
    ${EndIf}
    WriteINIStr "$INSTDIR\.bluedomain-install.ini" "Product" "Id" "${PRODUCT_ID}"
    WriteINIStr "$INSTDIR\.bluedomain-install.ini" "Product" "Directory" "$INSTDIR"
    WriteINIStr "$INSTDIR\.bluedomain-install.ini" "Product" "Version" "${PRODUCT_VERSION}"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    WriteRegStr HKLM "${PRODUCT_KEY}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr HKLM "${PRODUCT_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr HKLM "${PRODUCT_KEY}" "Publisher" "BlueDomainTaskManager contributors"
    WriteRegStr HKLM "${PRODUCT_KEY}" "URLInfoAbout" "https://github.com/1340896123/blue-task-manager"
    WriteRegStr HKLM "${PRODUCT_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "${PRODUCT_KEY}" "DisplayIcon" '"$INSTDIR\TaskManager.Host.exe",0'
    WriteRegStr HKLM "${PRODUCT_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKLM "${PRODUCT_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
    WriteRegDWORD HKLM "${PRODUCT_KEY}" "EstimatedSize" ${PAYLOAD_SIZE_KB}
    WriteRegDWORD HKLM "${PRODUCT_KEY}" "NoModify" 1
    WriteRegDWORD HKLM "${PRODUCT_KEY}" "NoRepair" 1
    CreateShortcut "$SMPROGRAMS\蓝域任务管理器.lnk" "$INSTDIR\TaskManager.Host.exe" "" "$INSTDIR\TaskManager.Host.exe" 0
SectionEnd

Section "桌面快捷方式" DesktopSection
    CreateShortcut "$DESKTOP\蓝域任务管理器.lnk" "$INSTDIR\TaskManager.Host.exe" "" "$INSTDIR\TaskManager.Host.exe" 0
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
    !insertmacro CheckImageLock "TaskManager.Host.exe"
    !insertmacro CheckImageLock "TaskManager.Launcher.exe"
    !insertmacro CheckImageLock ".electron\node_modules\electron\dist\electron.exe"
    !include "${GENERATED_DIR}\verify-maintenance.nsh"
    ClearErrors
    ExecWait '"$INSTDIR\TaskManager.Host.exe" --uninstall-task-manager-integration' $0
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
    Delete "$SMPROGRAMS\蓝域任务管理器.lnk"
    Delete "$DESKTOP\蓝域任务管理器.lnk"
    DeleteRegKey HKLM "${PRODUCT_KEY}"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"
SectionEnd
