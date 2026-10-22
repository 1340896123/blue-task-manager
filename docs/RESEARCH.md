# 开源实现研究与技术决策

核验日期：2026-10-23。只借鉴架构、计数语义和公开Win32实践，不直接复制TaskExplorer的C++实现、驱动代码或远程协议。

## 1. TaskExplorer

仓库：[DavidXanatos/TaskExplorer](https://github.com/DavidXanatos/TaskExplorer)。本次研究 master commit：`9f3f95d22f07f759dd1e0114417529ba51decb01`，通过GitHub API读取commit和文件树、raw源码核验。

| 实际源文件/符号 | 观察 | 本项目采用方式 |
|---|---|---|
| [WinProcess.cpp](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/API/Windows/WinProcess.cpp) `InitStaticData`（约267、298～300行） | 原生进程信息的PID与CreateTime组成SProcessUID；随后单独读取静态元数据；查询权限分级尝试 | PID+原始FILETIME身份；静态与动态信息缓存分离；低权限字段部分成功 |
| 同文件 `InitStaticData`（约434～471行）、`UpdateDynamicData`（约815行） | 打开查询句柄与动态数据更新分开；可用时补真实创建时间；存在内核驱动依赖扩展 | CsWin32读取公开用户态接口；不引入驱动；动作在同一句柄重验身份 |
| [ProcessModel.cpp](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/GUI/Models/ProcessModel.cpp) `Map`（约208～250行） | CPU累计时间、I/O与Disk/Network分属不同计数块；隐藏列无需拉取昂贵扩展 | 严格区分进程I/O和物理磁盘/网络；元数据按需与缓存，不用无源零值填列 |
| 同文件（约1290～1335、1429～1437行） | 真实终止/新建状态、动态CPU数据、创建时间和运行时长分开；GPU仅在需要时采集 | 真采样时间轴；运行时间从真实创建时间计算；GPU没有采集源就null |
| [WinService.cpp](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/API/Windows/WinService.cpp) `UpdatePID` / `UpdateDynamicData`（约129～172行） | 服务状态/PID来源为ENUM_SERVICE_STATUS_PROCESSW，配置改变与动态状态分别处理 | 通过SCM/CsWin32枚举服务与PID，慢配置缓存，不构造静态服务表 |
| [SystemAPI.cpp](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/API/SystemAPI.cpp) | 中央系统API协调进程数据、缓存与异步读取 | 单例SnapshotService串行采样，UI读取不可变完整快照 |

研究时还确认存在 `TaskExplorer/API/Windows/Monitors/WinGpuMonitor.cpp`、`ProcessHacker/GpuCounters.c`、`WinSocket.cpp`；这些能力需要独立采集实现，不能由一般进程I/O推导。因本版范围和无驱动要求，不承诺搬入全套GPU/ETW/远程子系统。

## 2. Electron.NET

核验官方[当前README](https://github.com/ElectronNET/Electron.NET/blob/main/README.md)及其最小ASP.NET Core示例。现代版本已采用 `ElectronNET.Core` / `ElectronNET.Core.AspNet`，支持.NET 8/10和Node22+；通过 `builder.UseElectron(args, async () => ...)` 回调创建真实BrowserWindow。

采用框架提供的主机生命周期，不另写Node与.NET通信私有协议；生产UI由同源ASP.NET Core提供。候选版本0.6.0须在实现阶段恢复与真实启动验证。现代Core改善.NET支持与启动方式，但其默认Electron30.4.0同样偏旧，不能直接照用；实现阶段必须核验npm，显式固定当前受支持Electron版本。实际锁定版本与启动命令由桌面阶段记录。

## 3. CsWin32

核验[microsoft/CsWin32 README](https://github.com/microsoft/CsWin32/blob/main/README.md)与[Getting started](https://microsoft.github.io/CsWin32/docs/getting-started.html)。它在编译时基于Win32 metadata生成强类型P/Invoke、COM及友好SafeHandle重载；不需要手写DllImport签名。采用NativeMethods.txt最小API集，把句柄生命周期集中在原生采集层。

## 4. UI/UX Pro Max 核验记录

使用用户指定 `C:\Users\jieok\.skills-manager\skills\ui-ux-pro-max\SKILL.md`，以及references/quick-reference.md和pro-rules.md。堆栈由用户明确为React/Tailwind，不做默认推断。

1. `Windows system monitor dashboard --design-system --density 9`：返回Minimalism/Swiss、蓝色配色，但页面模式为Hero+Features+CTA，与桌面工具不匹配。
2. 按查询合同重试 `admin dashboard dense monitoring --design-system --density 9`：返回Real-Time/Operations与实时新鲜度/暂停建议，仍含Landing和Glassmorphism，不构成可直接采用的完整Windows设计系统。
3. 因两次系统查询均部分偏离，未把生成输出持久化为产品事实；本项目MASTER是依据用户Windows蓝色要求和参考布局人工综合的Token规范。复用已验证适用的高密度、实时数据新鲜度、暂停/更新周期、对比度/焦点建议；不采用营销Hero、玻璃模糊、绿色CTA、在线Fira字体。
4. React查询 `live table keyboard selection` 无命中；仅重试 `rerender memo`，命中“React.memo按实测使用/避免普遍memo”的Rendering/Performance建议。未将无命中输出作为依据。
5. `focus not obscured --domain ux` 命中WCAG2.2 AA最小可见与AAA增强规则；采用可见焦点和sticky滚动留白，不把AAA要求宣称为AA。

最终桌面设计取舍：Segoe UI Variable/微软雅黑本地字体；浅蓝灰/白色结构面；Windows蓝色强调；高密度表格；克制动效；按真实数值绘制蓝色热度；不使用截图中的固定应用logo/进程名或营销大卡片。
