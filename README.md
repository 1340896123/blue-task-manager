# 蓝域任务管理器

面向 Windows 的真实系统任务管理器，技术栈为 React、Tailwind CSS、Electron.NET、ASP.NET Core 与 CsWin32。界面采用参考图的标题搜索区、固定侧栏、命令栏和高密度数据表，以 Fluent 蓝色 Token 统一视觉。

已完成需求规范、真实 Win32 后端、React 界面、Electron.NET 桌面四个顺序阶段。实际验收以 [验收结果](docs/ACCEPTANCE-RESULTS.md) 为准。

- [产品需求](docs/PRD.md)
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

`TASKMANAGER_CDP_PORT` 仅供显式桌面自动化启用回环 CDP，普通启动默认关闭。验收不 mock、不截取全机进程清单、不记录任何令牌，产物在 `output/playwright/`。测试只清理本次创建并保存身份的进程与临时目录。

键盘：Ctrl+F 搜索、F5 刷新、Tab 导航、Esc 关闭对话框；桌面 Ctrl+= / Ctrl+- / Ctrl+0 调整 25% 步进缩放/恢复 100%。逐进程 GPU/网络、服务启停、启动项编辑、驱动级分析不在本版范围，界面明确说明。
