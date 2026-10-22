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

`test:e2e`按顺序运行：11组原生/静态/浏览器回归、正式Release桌面回归、真实Electron进程工作流、原生选择器/文件窗口/偏好重启、固定Windows入口，最后满120个真实历史点后的整应用性能。性能部分需要约3分钟，不用造点加速。可用 `test:regression`、`test:desktop`、`test:process-desktop`、`test:native-desktop`、`test:system-locations`、`test:performance` 分别复现。

分项命令包括 `test:native`、`test:metadata`、`test:core`、`test:management`、`test:telemetry`、`test:gpu-identity`、`test:process-ui`、`test:pages-ui`、`test:ui`、`test:recovery`、`test:static`。测试使用5191～5202和9224～9228回环端口，应顺序执行；每项隔离TASKMANAGER_DATA_DIR，不更改日常配置。桌面由便携目录启动，cwd故意设为其他目录；独立CsWin32 HWND/ClientRect探针验证实际尺寸和窗口状态。Chromium可通过PLAYWRIGHT_CHROMIUM_EXECUTABLE指定。

`TASKMANAGER_CDP_PORT` 仅供显式桌面自动化启用回环 CDP，普通启动默认关闭。验收不 mock、不截取全机进程清单、不记录任何令牌，产物在 `output/playwright/`。测试只清理本次创建并保存身份的进程与临时目录。

键盘：Ctrl+F搜索、Ctrl+N运行、F5刷新、Enter/双击详情、Shift+F10/右键操作、Delete结束确认、Space冻结列表、F1帮助、Esc关闭；桌面Ctrl+= / Ctrl+- / Ctrl+0调整25%步进缩放/恢复100%。冻结列表时采集和性能曲线继续运行；设置中的自动采样开关会真正暂停采集。

GPU使用Windows PDH真实引擎计数并以DXGI实际型号名称标识；软件适配器明确标记，不猜Windows编号；磁盘I/O与进程I/O分别展示。逐进程网络吞吐、启动策略编辑与驱动级分析仍未接入，端点列表不冒充吞吐。服务动作受Windows当前账户权限约束，不自动提权或停止依赖服务；当前标准用户环境成功启停测试需保留权限限制。
