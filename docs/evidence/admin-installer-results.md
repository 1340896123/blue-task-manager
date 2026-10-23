# 1.1.1 管理员与 NSIS 实际验收

2026-10-23，Windows 11 x64（build 26200），.NET SDK 10.0.401、Node 24.16.0、NSIS 3.12。权限实现 `9848ed2`、安装实现 `2b5535e`、首次安装路径修复 `885f333`；本记录随最终测试阶段提交。结构化结果见 [admin-installer-results.json](admin-installer-results.json)。

最终安装包 `output/releases/BlueDomainTaskManager-v1.1.1-win-x64-setup.exe`，160,823,149 字节，SHA256 `93dee5f63ef5aec359cc50181535b29ebab5cff91bc8870388cbaba42f3897e0`。安装器实际未签名。ZIP 为同一 3,061 文件的自包含目录，SHA256 `da61de6ddf7fd1c2817649ca1aa9b15f1536415c647b91ee363602c0fb197471`；运行无需预装 .NET/ASP.NET、Node/npm 或联网。

## 实际通过

- Host、Setup 与真实安装后生成的 Uninstall.exe 均由 SDK mt.exe 提取，`requireAdministrator / uiAccess=false`。Medium/管理员 deny-only 下，EXE CreateProcess 原生 740；DLL、headless、entry、维护入口均拒绝且没有端口、Electron、隔离偏好副作用，详见 [权限阶段](admin-runtime.md)。
- 通过 Windows 标准 RunAs 启动实际提升的验收 worker，未更改 UAC 策略。CsWin32 独立探针确认便携和安装后的 Host、官方 Electron browser 子进程及专用新任务的真实 TokenElevation。两次各 9 项检查通过；renderer 仍无 Node 全局且保持 sandbox。即使 DOTNET_ROOT/DOTNET_ROOT_X64 指向不存在目录，实际加载的 coreclr.dll 仍来自本程序目录。
- 真实安装生命周期 22 项通过：含空格父目录的新建安装、全部 3,061 文件 SHA256、HKLM64 版本/目录、公共开始菜单/桌面快捷方式、安装后桌面运行、同版本修复、额外文件保留、显式异路径拒绝。专用已启动 Host 同时阻止重装和卸载且未被安装器终止。
- 篡改安装目录 runtimeconfig 后，真实卸载器返回 2 并保留应用和卸载记录；恢复原字节后，普通 `/S` 卸载执行固定生产维护 CLI，退出 0，并最终删除全部包文件、卸载器自身、产品标记、快捷方式与卸载记录，只保留测试额外文件。检查后仅删除本次拥有的测试目录；本机没有留下本次安装记录或安装目录。
- 安装、修复与卸载前后，独立读取的真实 taskmgr IFEO 全键值/类型/子键指纹相同，没有开启全局接管。所有业务动作仅针对专用测试进程；没有启停用户服务、修改观察到的受保护进程或按名称批量清理进程。
- `test:e2e` 对应 19 套检查分项顺序执行；18 套通过，系统入口可见性一套保留部分结果。包括原生 API/元数据/核心/管理/PDH/GPU、浏览器进程/页面/恢复、正式 Electron 桌面 42 项、进程桌面 34 项、原生窗口 22 项、图标 16 项、任务栏入口 20 项与受限进程 7 项。提升后修正旧测试的 SCM 权限假设及 GPU 代表进程选择 PID 4 的问题，生产关键进程保护保持有效。
- 满 120 个真实历史点后采样约 30 秒，本机性能目标通过：Host 整机 CPU 0.3183%，整应用 0.9340%，采样 p95 77.83 ms，搜索到下一 rAF p95 36.6 ms。此为本次机器短期观测，不替代长时或其他机器结果。

截图：[便携管理员桌面](administrator-desktop.png)、[安装后的管理员桌面](installed-administrator-desktop.png)。原始逐套输出与生命周期记录在 `output/admin-installer/`，性能完整数据在 `output/performance/desktop-v11-release.json`，最终包静态提取在 `output/installer-verification-20261023-151919/`；不包含写令牌或完整机器进程清单。

## 可复现命令

普通终端构建与静态校验：

```powershell
npm run build:installer
npm run verify:installer
```

在具有真实管理员令牌的终端中执行（全部依赖先构建完成）：

```powershell
npm run test:administrator
npm run test:e2e
npm run test:installer
```

安装生命周期测试拒绝覆盖已有产品安装或同名公共快捷方式；它仅安装到本次新建的临时专用目录，保存精确测试身份，并在成功后清理。本机实际使用标准 `Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden` 启动隔离 worker。拒绝卸载的两项测试使用 NSIS 标准 `_?=` 参数同步读取实际错误码，最后的成功卸载使用普通 `/S`，独立等待真实克隆卸载器完成及文件/注册表消失。测试脚本使用 UTF-8 BOM，以兼容 Windows PowerShell 5 的中文路径解析。

## 明确保留

当前用户 Startup 文件夹由 Shell 受理，但本次未独立确认精确前台窗口；其余公共 Startup、Windows 启动设置、服务管理器窗口确认通过。管理员服务成功启停未创建专用服务测试；不以读取成功替代写成功。真实全局 IFEO 启用/所有权类型变体未执行；没有较旧 NSIS 包可验证跨版本升级，仅验证同版本修复。当前既有系统策略自动提升管理员账户，未实测交互 UAC 取消、错误凭据或令牌 API 故障。以上项目均未计为通过。
