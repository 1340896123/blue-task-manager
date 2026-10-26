# 已有任务管理器入口接管验收

日期：2026-10-26。规格见 [TASK-MANAGER-TAKEOVER.md](TASK-MANAGER-TAKEOVER.md)。本轮完成真实只读桌面/API 验收 **36 项通过**，另有原生静态约束 **3 项通过**。成功接管和标准恢复的全局写入链未执行，不能据此宣称 T03/T04 已通过。

## 产物与环境

- 需求 `771e563`、原生实现 `af420cf`、界面实现 `a635d5b`；125% 确认框修复 `78ef100`。
- 最终测试 checkout 为 `78ef100bf467e69336ab6b0467b695c0f3753577`。独立便携目录 `E:\taskManager\output\takeover\TaskManager-win-x64`，未升级 Program Files 安装；Host ProductVersion 为 `1.1.1+a635d5bc6b44ba60759243c2bce3aebd00ae3026`，包含新接管后端，生产 UI 更新到 `78ef100`。实际 DLL/EXE/JS/CSS SHA256 见 [results.json](evidence/task-manager-takeover/results.json)，不以 checkout 代替产物身份。
- 便携目录包含 `restore-windows-task-manager.cmd`，已独立核验与仓库同名脚本 SHA256 一致；本轮没有执行恢复脚本清理用户入口。
- 当前固定 SDK 10.0.401 不可用。从系统临时目录使用已安装 `11.0.100-rc.2.26504.105` SDK，Host/Launcher 的 net10.0 自包含 publish 均成功，运行时仍固定 `10.0.12`。没有修改 global.json。
- 专用端口为 HTTP `15333`、CDP `19333`。旧候选 `5333` 落在 Windows 保留范围 `5285～5384`，真实返回 SocketException 10013；已改端口。端口不监听不代表可绑定。
- 标准 UAC worker 实际提升令牌已核验；正式 Electron.NET、同源 HTTP、JSON.NET、CsWin32 与真实 Windows 注册表/进程/窗口链运行，没有 mock 业务 API。保留现有用户 IFEO 入口。

## 实际结果

| 项目 | 结果与证据 |
|---|---|
| T01 构建及保护请求 | Web 类型/生产构建、Host/Launcher 自包含 publish 成功；静态 3 项通过。真实 API 拒绝非法 takeOver 组合、缺失/过期 revision、隐式覆盖、非自有取消、缺令牌和外站 Origin；完整注册表不变。 |
| T02 当前其他目录状态 | 独立 HKLM64 RegistryKey 与生产 CsWin32 GET 的 Debugger 一致；当前已安装目录被判为现有入口，canTakeOver=true、canEnable=false，设置显示实际接管按钮和命令。 |
| T03 任意类型成功替换/标准恢复 | **未执行**：现有全局入口属于用户安装目录，未以测试 fixture 覆盖。没有产品成功写入/取消证据。 |
| T04 UseFilter/实际系统入口 | **未执行**：未修改全局过滤配置或启动经过本轮新命令的系统 taskmgr；不能将根值读回设计当作过滤实际生效证据。 |
| T05 权限/并发/部分错误 | 实际可写权限为 true；令牌/Origin/revision/非法请求拒绝通过。原生错误5、真实并发注册表变更、两步写入部分失败未诱发，明确未执行。 |
| T06 桌面/键盘/尺寸 | 首次提示有“前往设置接管现有入口”，仅保存偏好且不发送接管 POST；确认展示真实来源/目标、过滤、所有用户、Ctrl+Shift+Esc 与只恢复标准入口。暂不初始焦点、Tab 约束、Escape、键盘打开通过。1380×860、960×640、实际 Electron125%均无页面横溢，确认按钮可达。 |
| T07 生命周期/维护 | 本轮专用桌面正常关闭，全部专用 Host/Electron 创建身份退出，15333/19333 无监听残留。全局 Launcher 成功启动、标准入口恢复、卸载维护成功/失败链未执行；旧测试断言已同步新语义。 |
| T08 记录/清理 | [36项机器结果](evidence/task-manager-takeover/results.json)、[实际提升worker结果](evidence/task-manager-takeover/worker-status.json)与截图归档。integrationPosts=0、globalRegistryUnchanged=true，测试未终止用户进程。 |

125% 首轮验收发现正文把确认按钮挤出对话框；`78ef100` 改为正文内部滚动、保留底部动作，复验通过。Playwright 默认 page.screenshot 在实际 Electron 缩放后按逻辑尺寸裁切，因此最终证据采用原始 CDP compositor 截图及专用 HWND 原生截图；两者均已视觉查看，按钮完整可见。

## 复现命令

Web：`npm --prefix src/TaskManager.Web run build`。静态：`node tests/static-v11.mjs`。语法：`node --check tests/task-manager-takeover.mjs`；PowerShell 脚本经 Parser.ParseFile 检查。

在系统临时目录执行以下命令可避开仓库缺失的固定 SDK（本轮实际使用 SDK11 RC；有 SDK10.0.401 时可用标准发布脚本）：

```powershell
dotnet restore E:\taskManager\src\TaskManager.Host\TaskManager.Host.csproj -r win-x64 --locked-mode -v quiet
dotnet publish E:\taskManager\src\TaskManager.Host\TaskManager.Host.csproj -c Release -r win-x64 --self-contained true --no-restore -o E:\taskManager\output\takeover\publish\ -p:UseAppHost=true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishAot=false -p:ElectronSkipExecCommands=true -v quiet
dotnet publish E:\taskManager\src\TaskManager.Launcher\TaskManager.Launcher.csproj -c Release -r win-x64 --self-contained true --no-restore -o E:\taskManager\output\takeover\publish-launcher\ -p:UseAppHost=true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishAot=false -v quiet
```

独立目录由既有完整 `output/TaskManager-win-x64` 复制得到，保留官方 `.electron` 和许可证；覆盖 Host publish 的 `bin` 内容（排除 `.electron`）、Launcher 四个应用文件和最终 `wwwroot`。没有删除旧发布目录或覆盖 Program Files 安装目录。

从仓库根目录启动标准 UAC worker（有实际提升令牌时可直接运行同一 worker）。该套件只读系统入口：

```powershell
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\task-manager-takeover-worker.ps1" -ReleaseDirectory "E:\taskManager\output\takeover\TaskManager-win-x64"'
```

结果输出到 `output/playwright/task-manager-takeover/results.json` 和 `worker-status.json`。直接 `node tests/task-manager-takeover.mjs` 也需真实提升令牌。既有 `taskbar-integration`、`first-launch-integration`、`ifeo-elevation` 的新语义断言仅完成语法/清理状态辅助检查，本轮没有把它们计为全套通过。

## 截图

- [首次提示](evidence/task-manager-takeover/first-launch-existing-entry.png)
- [设置当前入口](evidence/task-manager-takeover/settings-existing-entry.png)
- [接管确认](evidence/task-manager-takeover/takeover-confirmation.png)
- [960×640确认](evidence/task-manager-takeover/takeover-confirmation-960.png)
- [125%完整CDP截图](evidence/task-manager-takeover/takeover-confirmation-125.png)
- [125%专用HWND原生截图](evidence/task-manager-takeover/takeover-confirmation-125-native.png)
