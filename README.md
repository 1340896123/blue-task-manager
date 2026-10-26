# 蓝域任务管理器

全部八页现采用 Windows 11 标准任务管理器布局：左侧导航、单一命令栏、性能资源磁贴与纵向设置。筛选、进程树、显示列、CSV 等扩展操作统一放在“更多”；见[布局规格](docs/WINDOWS-LAYOUT.md)与[本轮真实验收](docs/WINDOWS-LAYOUT-RESULTS.md)。

接管原“任务管理器”的740提升错误已补充修复链路：系统入口先启动普通权限Launcher，再由Windows请求管理员授权启动Host。旧接管在设置显示“旧接管需要修复”，可确认“修复接管”或取消；安装升级保留现状，不自动迁移。完整说明与本轮真实验收要求见[IFEO提升修复](docs/IFEO-ELEVATION-FIX.md)，旧验收结果不能代替本轮成功链证据。

[手动构建并发布 Windows 安装包](https://github.com/1340896123/blue-task-manager/actions/workflows/manual-windows-installer.yml)：进入Actions，点击 **Run workflow** 并选择`main`。安装E2E通过后会创建Git tag和附带NSIS安装包、SHA256及真实验收报告的[Release](https://github.com/1340896123/blue-task-manager/releases)。`release_tag`留空默认预览标签`v<Host版本>-build.<运行序号>`，填写`v1.1.1`可发布匹配Host版本的正式版；已有标签拒绝覆盖。Release附件长期提供，Actions artifacts保留14天，详见[使用说明](docs/MANUAL-INSTALLER-BUILD.md)。

面向 Windows 的真实系统任务管理器，技术栈为 React、Tailwind CSS、Electron.NET、ASP.NET Core 与 CsWin32。界面采用参考图的标题搜索区、固定侧栏、命令栏和高密度数据表，以 Fluent 蓝色 Token 统一视觉。

1.1 提供进程树、实时详情与 TCP/UDP 端点、优先级与效率模式、文件位置和属性、全量筛选 CSV 导出、服务操作、逐网卡 / 物理磁盘 / GPU 曲线，以及跨页定位和偏好保留。界面由统一设计 Token 实现 Windows 蓝色风格。

1.1.1 已强制管理员运行，并提供内置 .NET/ASP.NET 与 Electron 的 NSIS 安装包。实际安装生命周期22项、便携与安装后管理员桌面各9项通过；原有19套检查18套通过，系统入口可见性保留部分结果。本机Host整机CPU约0.32%，采样p95约78ms。签名、管理员服务成功链与全局接管等边界见[1.1.1实际验收](docs/evidence/admin-installer-results.md)；原480项为[1.1.0历史基线](docs/ACCEPTANCE-RESULTS.md)。

[下载 Windows x64 预览版](https://github.com/1340896123/blue-task-manager/releases/tag/v1.1.0-preview.1) · [公开源码](https://github.com/1340896123/blue-task-manager) · [发布说明](docs/RELEASE-V1.1.0-PREVIEW.md)

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

下载ZIP后完整解压，双击`TaskManager-win-x64/start.cmd`，或直接运行同目录的`TaskManager.Host.exe`；源码构建对应`output/TaskManager-win-x64`。目录包含生产UI、Electron45.0.0和Electron.NET桥接依赖，可以移动整个目录运行；不要只复制exe。启用了任务栏接管时，移动前须先在原目录取消。

目标为 Windows 10（19041）/11 x64。1.1.1 安装包和配套 ZIP 已内置 **Microsoft.NETCore.App / Microsoft.AspNetCore.App 10.0.12** 与 Electron 45.0.0，运行无需预装 .NET、Node/npm 或联网。首次源码构建需要下载 NuGet/npm/Electron 依赖。

运行 `BlueDomainTaskManager-v1.1.1-win-x64-setup.exe`，由 Windows UAC 请求管理员权限，默认每机安装到 `Program Files\BlueDomainTaskManager`，创建直接启动管理员 Host 的开始菜单和可选桌面快捷方式。安装完成不自动启动，也不启用任务栏入口接管。运行中的本安装应用会阻止覆盖；重装/升级使用原目录，更换目录须先卸载。通过 Windows“已安装的应用”卸载；卸载先验证维护文件并由生产 CsWin32 链路清理本目录精确自有任务栏接管，不能确认成功即中止。额外文件及各账户 LocalAppData 偏好保留。安装器尚未数字签名。

要恢复 Windows 标准任务管理器，可双击安装目录或便携目录中的 `restore-windows-task-manager.cmd`，同意管理员授权后查看结果并按任意键关闭。脚本通过现有原生维护入口撤销本目录的接管，再只读核验 64 位 IFEO 的 `Debugger` 与 `UseFilter`；其他工具的接管仍存在、授权取消或维护失败时会明确报错，不显示恢复成功。请保留同目录完整程序文件；命令行可加 `--no-pause` 取消结束等待。

默认端口 `127.0.0.1:5188`；如被其他实例占用，可执行 `TaskManager.Host.exe --port=5189`。关闭窗口会退出本实例的主机、采集器、Electron 子进程与监听端口。本程序必须以管理员权限运行：EXE通过Windows UAC请求提升，启动时还会验证实际提升令牌；直接运行DLL、无窗口模式和卸载维护入口也不能绕过检查。管理员仍可能无法访问SYSTEM/PPL等受保护数据，界面保留实际原因。新任务默认继承管理员权限。以其他管理员账户提供凭据时，偏好保存在该账户的LocalAppData，不迁移其他账户配置。

设置中的“任务栏右键中的任务管理器”可接管Windows原任务管理器入口，同时影响Ctrl+Shift+Esc等方式，**不是新增任意任务栏菜单项**。此项默认关闭，修改范围为当前计算机，程序启动已要求管理员权限，入口修改无需再次请求UAC；不会覆盖其他工具。启用前请把完整目录放在固定、且其他需要使用的账户可读取的位置；移动/删除前先在原目录取消。取消仅删除精确自有Debugger，保留其他配置。已有同目录本产品真实窗口时，系统入口会复用并恢复该窗口。详情见[任务栏入口说明](docs/TASKBAR-INTEGRATION.md)，`npm run test:taskbar-integration`复现只读/权限拒绝/设置/entry桌面链路，测试不会开启全局接管。

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

`test:e2e`按顺序运行11组原生/静态/浏览器回归、正式Release桌面、Electron进程工作流、原生窗口/偏好重启、固定Windows入口、受限进程、图标、任务栏入口，最后运行满120个真实历史点后的整应用性能。性能预热按实际1秒周期观察，最多等300秒并记录诊断；不造点加速，窗口须真实可见。

最终业务基线`251897e`已独立复验：11组334项、正式桌面42项、Electron进程34项、原生桌面22项、任务栏入口23项、图标16项、受限进程9项，共480项。性能实测覆盖满120点、约34秒、1083～1089进程；三项短期目标通过，不推广为其他机器或长时保证。系统入口保留4项受理/3项实际窗口确认的部分结果，本轮未重跑。旧06:39报告和此前失败尝试不作为最新性能依据。

分项命令包括`test:regression`、`test:desktop`、`test:process-desktop`、`test:native-desktop`、`test:system-locations`、`test:restricted-ui`、`test:icon`、`test:taskbar-integration`和`test:performance`。测试使用5191～5204和9224～9232回环端口，应顺序执行；每项隔离TASKMANAGER_DATA_DIR，不更改日常配置。桌面由便携目录启动，cwd故意设为其他目录；独立CsWin32 HWND/ClientRect探针验证实际尺寸和窗口状态。Chromium可通过PLAYWRIGHT_CHROMIUM_EXECUTABLE指定。`TASKMANAGER_RELEASE_DIRECTORY`可给`test:desktop`指定另一个已解压的完整程序目录。

`TASKMANAGER_CDP_PORT` 仅供显式桌面自动化启用回环 CDP，普通启动默认关闭。验收不 mock、不截取全机进程清单、不记录任何令牌，产物在 `output/playwright/`。测试只清理本次创建并保存身份的进程与临时目录。

键盘：Ctrl+F搜索、Ctrl+N运行、F5刷新、Enter/双击详情、Shift+F10/右键操作、Delete结束确认、Space冻结列表、F1帮助、Esc关闭；桌面Ctrl+= / Ctrl+- / Ctrl+0调整25%步进缩放/恢复100%。冻结列表时采集和性能曲线继续运行；设置中的自动采样开关会真正暂停采集。

性能页 CPU 图表支持双击切换“总体使用率 / 逻辑处理器”，也可使用“CPU 图表模式”选择器，或聚焦图表后按 Enter / 空格。模式会保存；逐逻辑处理器按 Windows 处理器组及组内索引显示真实 PDH 使用率，最多48张一页，历史保留真实时间与缺口。

性能资源列表中的“HWiNFO 传感器”只读接入 HWiNFO SM2 共享内存，显示实际分组、原名、单位、当前值与 HWiNFO 会话最小/最大/平均值，支持搜索、排序、调宽。需已有 HWiNFO 正在运行，并在 Sensors 设置中开启 Shared Memory Support，且当前版本/许可提供该功能。未提供或过期会显示明确原因。应用不附带 HWiNFO；规格与条件验收见 [HWiNFO / CPU 图表](docs/HWINFO-CPU-GRAPHS.md)。管理员终端可分别运行 `npm run test:hwinfo-cpu`、`npm run test:hwinfo-cpu-ui`、`npm run test:hwinfo-cpu-desktop`（专用端口5306/9306）；没有真实 HWiNFO 时不会把解析器检查称为成功采集。

GPU使用Windows PDH真实引擎计数并以DXGI实际型号名称标识；软件适配器明确标记，不猜Windows编号；磁盘I/O与进程I/O分别展示。逐进程网络已通过Windows ETW接入TCP/UDP IPv4/IPv6真实收发，列表显示接收+发送，详情、复制及CSV保留双向速率和独立来源时间；预热、容量不足、事件丢失或身份不可确认时显示未知与原因。硬件网卡性能继续使用GetIfTable2，不与进程ETW合计混用，端点列表也不冒充吞吐。规格和本机验证见[网络吞吐](docs/NETWORK-THROUGHPUT.md)与[验收结果](docs/NETWORK-THROUGHPUT-RESULTS.md)。启动策略编辑与驱动级分析仍未接入；服务动作受Windows当前账户权限约束，不自动停止依赖服务。

查找受限进程时先清空搜索和筛选，按实际 exe 名或 PID 定位；CPU、路径或发布者读取失败不等于进程不存在。系统任务管理器显示的文件说明可能与 exe 名不同，当前搜索不承诺匹配尚未读取的文件说明。

## 打包

安装包使用标准 Unicode NSIS 3/MUI2，先安装 NSIS 并将 `makensis.exe` 加入 PATH，再运行：

```powershell
npm run build:installer
# 复用已发布且验证版本/运行时的完整目录：
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-installer.ps1 -SkipBuild -MakeNsisPath C:\nsis\makensis.exe
# 独立静态提取和逐文件核验，需要7-Zip与Windows SDK mt.exe：
npm run verify:installer
```

公共入口先执行 `publish-desktop.ps1`，真实 `dotnet publish -r win-x64 --self-contained true` 后按 Host-first 方式组装，再从同一完整目录生成 `output/releases/BlueDomainTaskManager-v1.1.1-win-x64-setup.exe`、同版本 ZIP 和 `SHA256SUMS`。版本必须与 csproj、实际 Host 和 Electron 桥接一致；`-Version` 不能只修改文件标签。重复打包仅替换同版本文件，保留其他版本 ZIP。安装/删除清单和逐文件 SHA256 位于 `output/nsis/`；脚本包含隐藏 `.electron` 与许可，拒绝链接/本地偏好/环境文件，ZIP 逐文件校验。`npm run build:desktop` 继续生成依赖 .NET 10 的开发/测试目录；`npm run publish:desktop` 生成最终自包含目录。
