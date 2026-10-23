# 蓝域任务管理器

面向 Windows 的真实系统任务管理器，技术栈为 React、Tailwind CSS、Electron.NET、ASP.NET Core 与 CsWin32。界面采用参考图的标题搜索区、固定侧栏、命令栏和高密度数据表，以 Fluent 蓝色 Token 统一视觉。

1.1 提供进程树、实时详情与 TCP/UDP 端点、优先级与效率模式、文件位置和属性、全量筛选 CSV 导出、服务操作、逐网卡 / 物理磁盘 / GPU 曲线，以及跨页定位和偏好保留。界面由统一设计 Token 实现 Windows 蓝色风格。

核心真实链路及正式 Electron 工作流已验收；**最终性能测量未完成，尚不能宣称全部验收通过或性能预算达成**。服务成功启停受本次账户权限限制，当前用户 Startup 文件夹前台可见性也未确认。完整状态见 [1.1 验收记录](docs/ACCEPTANCE-RESULTS.md)。

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

只显示真实 Windows 数据。无法读取的字段保留“不可用”及原因，受限进程仍保留可枚举的基础行；进程 I/O 不冒充磁盘物理吞吐，历史明确限定为本次采集会话。文件 CompanyName 属于版本元数据，不代表数字签名验证。

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

`test:e2e` 按顺序运行 11 组原生 / 静态 / 浏览器回归、正式 Release 桌面、Electron 进程工作流、原生窗口 / 偏好重启、固定 Windows 入口，最后运行满 120 个真实历史点后的整应用性能。可用 `test:regression`、`test:desktop`、`test:process-desktop`、`test:native-desktop`、`test:system-locations`、`test:performance` 分别复现。性能预热通常需要数分钟，不造点加速，窗口须真实可见。

现有完整业务基线的 11 组共 334 项、正式桌面 42 项、Electron 进程 34 项、原生桌面 22 项通过。系统入口 4 项受理、3 项实际窗口确认；受限进程说明小修另有正式 Electron 9 项通过。最终性能三次尝试均未形成有效报告，旧 06:39 文件仅作历史定位。

分项命令包括 `test:native`、`test:metadata`、`test:core`、`test:management`、`test:telemetry`、`test:gpu-identity`、`test:process-ui`、`test:pages-ui`、`test:ui`、`test:recovery`、`test:static`。测试使用5191～5202和9224～9228回环端口，应顺序执行；每项隔离TASKMANAGER_DATA_DIR，不更改日常配置。桌面由便携目录启动，cwd故意设为其他目录；独立CsWin32 HWND/ClientRect探针验证实际尺寸和窗口状态。Chromium可通过PLAYWRIGHT_CHROMIUM_EXECUTABLE指定。

`TASKMANAGER_CDP_PORT` 仅供显式桌面自动化启用回环 CDP，普通启动默认关闭。验收不 mock、不截取全机进程清单、不记录任何令牌，产物在 `output/playwright/`。测试只清理本次创建并保存身份的进程与临时目录。

键盘：Ctrl+F搜索、Ctrl+N运行、F5刷新、Enter/双击详情、Shift+F10/右键操作、Delete结束确认、Space冻结列表、F1帮助、Esc关闭；桌面Ctrl+= / Ctrl+- / Ctrl+0调整25%步进缩放/恢复100%。冻结列表时采集和性能曲线继续运行；设置中的自动采样开关会真正暂停采集。

GPU使用Windows PDH真实引擎计数并以DXGI实际型号名称标识；软件适配器明确标记，不猜Windows编号；磁盘I/O与进程I/O分别展示。逐进程网络吞吐、启动策略编辑与驱动级分析仍未接入，端点列表不冒充吞吐。服务动作受Windows当前账户权限约束，不自动提权或停止依赖服务；当前标准用户环境成功启停测试需保留权限限制。

查找受限进程时先清空搜索和筛选，按实际 exe 名或 PID 定位；CPU、路径或发布者读取失败不等于进程不存在。系统任务管理器显示的文件说明可能与 exe 名不同，当前搜索不承诺匹配尚未读取的文件说明。
