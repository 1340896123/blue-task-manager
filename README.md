# 蓝域任务管理器

面向 Windows 的真实系统任务管理器，技术栈为 React、Tailwind CSS、Electron.NET、ASP.NET Core 与 CsWin32。界面采用参考图的标题搜索区、固定侧栏、命令栏和高密度数据表，以 Fluent 蓝色 Token 统一视觉。

1.1补齐日常排查与管理闭环：进程树、实时详情、优先级与效率模式、文件位置和属性、CSV导出、真实服务操作、逐网卡/物理磁盘/GPU曲线，以及跨页定位和偏好保留。完整桌面验收与发布状态以 [验收结果](docs/ACCEPTANCE-RESULTS.md) 为准。

- [当前1.1产品需求](docs/PRODUCT-V1.1.md)
- [原始PRD与1.0历史基线](docs/PRD.md)
- [架构与数据合同](docs/ARCHITECTURE.md)
- [工程规范](docs/CONVENTIONS.md)
- [设计系统与 Token](design-system/task-manager/MASTER.md)
- [真实链路验收矩阵](docs/ACCEPTANCE.md)
- [开源研究与决策](docs/RESEARCH.md)
- [实际后端 API](docs/API.md)
- [后端阶段验收](docs/evidence/native-backend.md)
- [React 界面阶段验收](docs/evidence/frontend-ui.md)

只显示真实 Windows 数据。无法获取的指标以“不可用”及原因呈现；进程 I/O 不冒充磁盘物理吞吐，应用历史明确限定为本次采集会话。最终交付需同时通过后端、浏览器 UI 和 Electron 桌面验证。

## 运行成品

双击 `output/TaskManager-win-x64/start.cmd`，或直接运行同目录的 `TaskManager.Host.exe`。该便携目录已经包含生产 UI、Electron 45.0.0、Electron.NET 桥接依赖，可以移动整个目录到其他位置运行；不要只复制 exe。

目标为 Windows 10/11 x64。框架依赖运行时为 **Microsoft.NETCore.App 10.0** 与 **Microsoft.AspNetCore.App 10.0**（兼容后续 10.0 补丁），已安装 .NET 10 SDK 的开发机满足要求；不要求 Microsoft.WindowsDesktop.App。成品运行不需要 Node/npm，不需要联网。首次源码构建需要下载 NuGet/npm/Electron 依赖。

默认端口 `127.0.0.1:5188`；如被其他实例占用，可执行 `TaskManager.Host.exe --port=5189`。关闭窗口会退出本实例的主机、采集器、Electron 子进程与监听端口。普通应用使用无需管理员权限，无法读取的系统字段会保留明确原因。

## 从源码构建

要求 .NET 10 SDK、Node 22.12+（本次 Node 24.16.0）、npm、Windows x64：

```powershell
npm run build:desktop
```

脚本锁定恢复 npm/NuGet 依赖，检查 TypeScript、构建 React/Tailwind 与 Release 主机、下载 Electron，最后重新生成 `output/TaskManager-win-x64`。仅在依赖已恢复时可使用 `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -SkipRestore`。

应用自有 HTTP JSON 使用 JSON.NET；Electron.NET 内部 Socket.IO 是官方框架实现。框架默认 Electron 版本已明确升级到 45.0.0；构建脚本通过标准 npm 依赖清单把未使用的 splash 图像解析库升级到已修复的 image-size 2.0.4，依赖树有单独锁文件，不修改框架源码。

## 真实验收

```powershell
npx playwright install chromium
npm run test:e2e
```

包含 `test:native`、`test:ui`、`test:recovery`、`test:desktop`。桌面测试实际从便携成品启动 .NET → Electron.NET → BrowserWindow，走 UI → HTTP → CsWin32 → Windows 创建/结束专用任务；独立 CsWin32 HWND 探针验证真实窗口状态。测试默认使用 5191～5194 与 9224 端口，启动前应空闲。Chromium 可通过 `PLAYWRIGHT_CHROMIUM_EXECUTABLE` 指定已安装路径。

1.1分阶段真链路检查：`node tests/native-core-v11.mjs`、`node tests/native-management-v11.mjs`、`node tests/native-telemetry-v11.mjs`、`npm run test:process-ui`、`npm run test:pages-ui`。页面检查使用5194，进程工作流使用5193，按顺序运行，避免与旧桌面测试端口冲突。每个测试使用独立偏好目录，不更改日常配置。

`TASKMANAGER_CDP_PORT` 仅供显式桌面自动化启用回环 CDP，普通启动默认关闭。验收不 mock、不截取全机进程清单、不记录任何令牌，产物在 `output/playwright/`。测试只清理本次创建并保存身份的进程与临时目录。

键盘：Ctrl+F搜索、Ctrl+N运行、F5刷新、Enter/双击详情、Shift+F10/右键操作、Delete结束确认、Space冻结列表、F1帮助、Esc关闭；桌面Ctrl+= / Ctrl+- / Ctrl+0调整25%步进缩放/恢复100%。冻结列表时采集和性能曲线继续运行；设置中的自动采样开关会真正暂停采集。

GPU使用Windows PDH真实引擎计数并以DXGI实际型号名称标识；软件适配器明确标记，不猜Windows编号；磁盘I/O与进程I/O分别展示。逐进程网络吞吐、启动策略编辑与驱动级分析仍未接入，端点列表不冒充吞吐。服务动作受Windows当前账户权限约束，不自动提权或停止依赖服务；当前标准用户环境成功启停测试需保留权限限制。
