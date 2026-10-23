# 1.1.1 NSIS 构建与静态核验

2026-10-23，Windows x64，.NET SDK 10.0.401、Node 24.16.0、标准 Unicode NSIS 3.12。基于管理员权限阶段 `9848ed2` 与工作流阶段 `f7ad07b`。此记录只覆盖构建及不触发 UAC 的静态检查；实际提升桌面、安装/重装/卸载由后续独立阶段记录。

```powershell
# 首次更新锁文件后，普通 solution 锁定恢复已实际通过：
dotnet restore TaskManager.sln --locked-mode
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-desktop.ps1 -SkipRestore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-installer.ps1 -SkipBuild -MakeNsisPath C:\nsis\makensis.exe
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-installer.ps1
```

最终命令全部退出 0。TypeScript/React 生产构建、真实 win-x64 self-contained publish 完成；NSIS 以 `/WX /INPUTCHARSET UTF8` 无警告编译。最终目录包含 3,061 个文件、571,364,552 字节；Host 文件版本 `1.1.1.0`，Electron 实际 `dist/version` 为 `45.0.0`，runtimeconfig 使用 `includedFrameworks` 包含 Microsoft.NETCore.App 与 Microsoft.AspNetCore.App `10.0.12`，没有外部 `framework/frameworks`。Csproj 固定两个运行时框架版本和 win-x64 RID，NuGet 锁文件保留 RID 依赖。

| 产物（`output/releases/`） | 字节 | SHA256 |
|---|---:|---|
| BlueDomainTaskManager-v1.1.1-win-x64-setup.exe | 160823149 | `93dee5f63ef5aec359cc50181535b29ebab5cff91bc8870388cbaba42f3897e0` |
| BlueDomainTaskManager-v1.1.1-win-x64.zip | 231224806 | `da61de6ddf7fd1c2817649ca1aa9b15f1536415c647b91ee363602c0fb197471` |

- `package-release.ps1` 独立打开 ZIP，逐文件核对数量、长度与 SHA256；全部 3,061 项通过。隐藏 `.electron`、Electron/LICENSES.chromium.html 和两份 .NET/ASP.NET LICENSE/THIRD-PARTY-NOTICES 均在目录与包内。
- `verify-installer.ps1` 用现有 7-Zip 独立静态提取 NSIS，全部 3,061 个 payload 文件长度与 SHA256 与同一发布目录完全一致。最终路径修复包证据：`output/installer-verification-20261023-151919/results.json`、`7z-extract.txt`。
- Windows SDK `10.0.26100.0/x64/mt.exe` 实际提取发布 Host、Setup 的资源 `#1`，均为 `requireAdministrator / uiAccess=false`。manifest 保存在上述证据目录。此 7-Zip 提取不提供重建后的 Uninstall.exe，卸载器实际 manifest 须在隔离安装后补验，不能以源文件声明代替。
- Setup Authenticode 状态实际为 `NotSigned`；没有使用或假定签名凭据。
- 额外运行不要求提升的输出目录诊断程序，调用生产 NSIS 的完整 CryptoAPI SHA256 函数：Host 正确哈希与独立 Get-FileHash 完全相等；错误预期哈希退出 2，并在下一项文件动作之前中止。诊断未安装应用、未执行维护 CLI、未触碰 IFEO。
- 四个打包脚本 PowerShell 语法、package.json 解析及 `git diff --check` 通过。显式 `-Version 1.1.2` 在修改任何产物之前退出 1，不能把旧 payload 换标签。

安装器只使用标准 NSIS/MUI2/System/Windows CryptoAPI；没有新增 electron-builder 或非标准安装协议。默认每机 `$PROGRAMFILES64\BlueDomainTaskManager`，自定义路径要求专用 `BlueDomainTaskManager` 末级，所有现有父级与 payload 路径拒绝 reparse point。HKLM64 卸载键固定为 `Software\Microsoft\Windows\CurrentVersion\Uninstall\BlueDomainTaskManager`；`.bluedomain-install.ini` 保存固定产品标识和精确目录。开始菜单/桌面快捷方式直接指向 Host，静默安装不启动应用，不注册 IFEO。

安装和卸载先检查本目录 Host/Electron 图像占用，不结束任何进程。卸载在删除任何 payload 前核验维护入口及本地 .NET 依赖哈希，执行固定 Host `--uninstall-task-manager-integration` 并要求退出 0；失败保留文件。生成的静态 Delete 清单只删除本包文件，RMDir 只尝试空目录，不使用递归删除，也不删除 LocalAppData 或额外文件。运行中阻止覆盖、同路径重装、异路径拒绝、已安装卸载器 admin manifest 与实际清理行为仍须由独立安装生命周期验证，不能由本静态记录宣称通过。

实际首次安装发现标准NSIS路径展开会把父目录尚不存在的目标清空；`885f333`改用Windows `GetFullPathNameW`，并从原始 `GetCommandLineW` 判断被NSIS消费的 `/D`。轻量新建/同路径/异路径/默认复用诊断通过，正式Setup重新以 `/WX` 编译及独立静态提取通过。上表记录修复后的最终Setup；真实生命周期见后续管理员验收记录。
