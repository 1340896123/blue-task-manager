# 管理员运行与 NSIS 安装包增量

2026-10-26 GitHub更新增量见 [GITHUB-UPDATES.md](GITHUB-UPDATES.md)：自动安装仅限注册记录/安装标记/当前Host路径一致的正式目录，受保护ProgramData暂存不依赖安装目录默认ACL。NSIS仅在明确更新模式有界等旧映像释放、完整成功后重启；普通手工运行中拒绝与卸载所有权/保留文件规则继续生效。

更新模式的固定参数为 `/S /TASKMANAGERUPDATE=<32位小写hex>`，不允许 `/D` 改目录。合法尝试在固定产品注册记录确认后记录早期目录/标记失败，只有完整身份核验后才进入等待与替换；固定结果键和 60 秒超时已在独立产品机制测试验证。未确认产品身份或平台检查失败时不改产品记录。真实官方更高 Release 的整条更新仍以 [更新安装器验收](UPDATE-INSTALLER-RESULTS.md) 的执行范围为准。

日期：2026-10-23。用户要求“调整权限，要求为管理员权限运行。并且制作 nsis 的安装包”。本增量覆盖旧文档的“普通用户运行”“无需安装器”要求；原验收结果保留为历史事实，新行为按 R 系列重新验收。

> IFEO 740修复以 [IFEO-ELEVATION-FIX.md](IFEO-ELEVATION-FIX.md) 为当前专项合同，覆盖本页“唯一入口/IFEO直接Host/不另建launcher”旧描述。Host继续requireAdministrator与实际TokenElevation校验；仅系统IFEO先创建同目录asInvoker Launcher，由CsWin32 ShellExecuteEx runas固定Host与固定entry参数。Launcher取消1223不重试，不启动HTTP/Electron。新旧自有命令均可严格取消/卸载；旧Host值必须用户明确修复，不因升级自动迁移。

## 1. 交付与启动合同

- Windows 10/11 x64；沿用 React/Tailwind/Electron.NET、JSON.NET、CsWin32 与同源 HTTP，不增加服务、驱动、提权代理或私有协议。
- `Takao.Host.exe` 是业务主机入口，嵌入 `requestedExecutionLevel level="requireAdministrator" uiAccess="false"`。直接双击、开始菜单、桌面快捷方式和便携 `start.cmd` 直接启动Host；IFEO经同目录 `Takao.Launcher.exe`（asInvoker）使用标准runas启动同一个管理员Host。Windows 的 UAC 同意/凭据由系统处理，取消后不启动应用、不循环重试。
- 在任务栏 entry 解析/窗口复用、偏好读写、Kestrel 与 Electron 初始化之前，使用 CsWin32 `OpenProcessToken` / `GetTokenInformation(TokenElevation)` 验证实际进程令牌。不是提升令牌或查询失败即明确拒绝、返回非零；管理员组 deny-only 不能当作已提升。`dotnet Takao.Host.dll`、`dotnet run`、`--headless`、安装器维护参数均不能绕过检查。
- 保留 Host-first 模式：管理员 Host 启动官方 Electron.NET Electron browser 主进程，子进程继承相应安全上下文。Chromium renderer 的 sandbox 限制继续有效，不能为提权关闭 sandbox、contextIsolation 或打开 nodeIntegration；不能把“每个 renderer 都是高完整性”作为验收要求。
- 管理员权限不等于 SYSTEM/PPL/内核权限，不自动启用 SeDebugPrivilege；关键进程保护、PID+创建时间核验、最小句柄权限、Origin/写令牌检查全部保留。新任务由管理员 Host 的标准 CreateProcess 创建，默认继承提升权限，运行对话框说明同步反映真实行为。
- 设置及 README 明确“需要管理员权限”。用户用另一管理员账户提供凭据时，进程/配置归属于该凭据账户；偏好继续位于该账户 LocalAppData，不写入 Program Files，不迁移其他账户数据。

## 2. 发布与安装合同

使用系统已有标准 NSIS/MUI2 编译器打包完整 Host-first 目录，版本提升为 `1.1.1`，安装包名 `Takao-v1.1.1-win-x64-setup.exe`。研究借鉴 electron-builder 的每机安装、运行中保护、卸载与保留用户数据模式，不复制其整套脚本或假定只改 `win.target=nsis` 就能正确启动 Host。

- 生产发布使用 `dotnet publish -c Release -r win-x64 --self-contained true`，显式保持 apphost、不裁剪、不启用单文件/AOT。带齐 ASP.NET Core/.NET runtime、生产 wwwroot、隐藏 `.electron` 及 Electron 45.0.0、bridge、图标与许可；运行无需预装 .NET/Node/npm 或联网。锁定 RID runtime 包，不能只复制旧 `dotnet build` 输出冒充自包含。
- 同一已验证完整目录生成便携 ZIP 与 NSIS 安装器；便携运行也要求管理员。安装包为本地完整安装包，不是 nsis-web 下载器。构建不执行 Podman/WSL 恢复流程。
- NSIS Unicode + MUI2，安装器/卸载器 `RequestExecutionLevel admin`，Windows x64/最低版本检查，`SetRegView 64`；新安装默认每机安装至专用 `$PROGRAMFILES64\Takao`，已有安装沿用注册记录中的原目录。保留旧版本稳定产品标识与 HKLM 卸载记录以支持升级；显示产品名、版本、图标、发布者/项目地址和正确引用的卸载命令，支持 Windows“已安装的应用”卸载。
- 开始菜单/桌面快捷方式直接指向可信 `$INSTDIR\Takao.Host.exe`，不经任意 shell 命令或改兼容性注册表。安装完成启动为用户可选；静默安装不自动打开应用，也不自动启用任务栏接管。
- 重复安装/升级复用原专用目录，异路径安装要求先卸载旧版；运行中本应用阻止覆盖并提示关闭，不按 electron/dotnet/node 名称批量结束。生成精确文件清单用于安装与删除，核验专用安装路径与自有标识，不对用户可选目录执行递归删除。升级保留账户偏好；卸载默认保留 LocalAppData，不扫描删除其他账户数据。
- 安装清单排除仓库、测试负载、用户偏好、令牌、环境文件与链接；保留必要 Electron/.NET 许可。构建输出安装 EXE 与 ZIP 的大小/SHA256，交付前核验实际 EXE manifest 和包内容。没有签名凭据时如实标注未签名，不虚构可信发布者，不发布凭据。

## 3. IFEO 升级与卸载

任务栏接管默认关闭；本轮不把“安装”视为启用全机接管的请求。固定 HKLM64 `Image File Execution Options\taskmgr.exe` 的所有权为 **REG_SZ 类型且完整值精确等于** `"<当前安装目录>\Takao.Launcher.exe" --task-manager-entry` 或旧 `"<当前安装目录>\Takao.Host.exe" --task-manager-entry`。新注册使用Launcher；旧值明确修复才迁移。其他工具、其他目录、类型或参数差异均不拥有；不删除整键。

同路径升级保留原自有 Debugger，因为入口路径不变；拒绝运行中覆盖。若升级涉及删除原目录或换路径，必须先在原目录通过生产 CsWin32 所有权逻辑取消精确自有值，读回确认后才删除文件，不自动转移/启用新路径。其他便携目录的接管不属于安装器，保持原样并说明。

正式卸载在删除任何 Host/运行时文件前执行受管理员检查保护的本地维护入口，复用 `TaskManagerIntegrationService` 的严格类型/命令/预期版本重读与删除后读回；入口不启动 HTTP/Electron、不接收注册表路径或任意命令。没有自有值则成功不改动；存在自有值且不能确认清理成功时返回非零，卸载中止并保留应用，以免留下失效系统入口。文件缺失/损坏时也必须有明确失败或等价严格所有权清理，不能盲删他人值。NSIS 普通 `ReadRegStr` 的文本相等不能替代 REG_SZ 类型与生产所有权检查。

## 4. 成熟实现研究（实际获取于 2026-10-23）

| 来源 | 已核验行为与采用结论 |
|---|---|
| [Microsoft /MANIFESTUAC 文档源码](https://github.com/MicrosoftDocs/cpp-docs/blob/main/docs/build/reference/manifestuac-embeds-uac-information-in-manifest.md) | requireAdministrator 要求管理员安全上下文；asInvoker/highestAvailable 不能满足“必须管理员”；uiAccess 保持 false。C# 通过 ApplicationManifest 嵌入对应标准 manifest，最终用 Windows SDK mt.exe 独立提取确认。 |
| [Microsoft GetTokenInformation](https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/securitybaseapi/nf-securitybaseapi-gettokeninformation.md)、[CreateProcessW](https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/processthreadsapi/nf-processthreadsapi-createprocessw.md) | TOKEN_QUERY 可查询实际令牌；CreateProcess 使用调用进程的安全上下文。采用 CsWin32 TokenElevation 检查，不能只查账户组名。 |
| [Electron.NET 87cc6f9 Host-first controller](https://github.com/ElectronNET/Electron.NET/blob/87cc6f98b6a9c5968d7846c0e775ea713bd0d7b2/src/ElectronNET.AspNet/Runtime/Controllers/RuntimeControllerAspNetDotnetFirst.cs) | StartCore 把主机命令交给 ElectronProcessActive；保留当前官方 Host-first 生命周期，不另建 launcher/IPC。 |
| [electron-builder v26.0.12 installer.nsi](https://github.com/electron-userland/electron-builder/blob/v26.0.12/packages/app-builder-lib/templates/nsis/installer.nsi)、[uninstaller.nsh](https://github.com/electron-userland/electron-builder/blob/v26.0.12/packages/app-builder-lib/templates/nsis/uninstaller.nsh)、[winOptions.ts](https://github.com/electron-userland/electron-builder/blob/v26.0.12/packages/app-builder-lib/src/options/winOptions.ts) | 官方生态采用 MUI2、每机 RequestExecutionLevel admin、运行中保护和独立卸载钩子；`requestedExecutionLevel` 默认 asInvoker，只改变 Electron 入口不覆盖本项目 Host。借鉴行为，本机直接用标准 NSIS。 |
| [NSIS v3.11 Modern UI Basic.nsi](https://github.com/nsis-dev/nsis/blob/v311/Examples/Modern%20UI/Basic.nsi) | 标准 Unicode/MUI2 页面、WriteUninstaller 和安装/卸载分节。采用标准编译器及内置机制，不设计特殊安装协议。 |
| [.NET 发布官方文档源码](https://github.com/dotnet/docs/blob/main/docs/core/deploying/index.md) | self-contained 包含应用、依赖和平台运行时；apphost 可带原生 manifest，但 DLL 可由 dotnet 直接运行，因此运行时检查仍必需。采用显式 win-x64 自包含发布。 |

## 5. 可执行验收与限制

R 系列完整矩阵见 [ACCEPTANCE.md](ACCEPTANCE.md)。验收首先运行构建、manifest/内容静态检查与 Medium 令牌拒绝测试；提升成功链使用标准 `Start-Process -Verb RunAs` 启动独立验收 worker，不能把启动请求受理等同于 UAC 同意或测试成功。当前会话为 Medium/管理员 deny-only，缺少成功提升证据前不声称管理员桌面、安装、升级、卸载已通过。

```powershell
npm run build:desktop
# 使用实际安装的 Windows SDK mt.exe，替换路径中的SDK版本：
& 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\mt.exe' '-inputresource:output\Takao-win-x64\Takao.Host.exe;#1' '-out:output\host.manifest.xml'
dotnet output\Takao-win-x64\Takao.Host.dll --headless --port=5211
# 本命令在非提升会话应返回非零，5211不监听、无Electron/偏好文件副作用。
```

实施阶段提供统一 NSIS 构建与验收脚本，记录精确命令、退出码、版本、包 SHA256、截图与测试身份。中等权限进程用 CreateProcess/UseShellExecute=false 运行 EXE，预期真实 Windows 740；DLL 路径预期主机自身明确拒绝。UAC 取消或无法交互时，成功链标未执行，不能保留旧 E2E 的通过结论。

现有 `tests/native-management-v11.mjs` 对“当前会话没有 SCM 创建权限”的硬断言须改为按真实权限分支。提升验收仅使用本轮专用测试服务/进程及独立配置目录；无自建服务成功证据则明确标成功链未执行，绝不能对用户服务试启停。全局 IFEO 成功注册测试必须隔离且恢复原始完整配置；一般包测试只读生产 taskmgr 项，不自动打开接管。

本文件是增量规范；最终提升桌面、安装/修复/卸载与签名实际结果见 [1.1.1验收记录](evidence/admin-installer-results.md)，未执行边界也在其中逐项说明，不能把规范当作全部通过证据。
