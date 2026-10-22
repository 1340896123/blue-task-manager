# 最终真实链路验收

日期：2026-10-23（Asia/Shanghai）。代码基线：`4f8cb85bddf8faa9a289946215941ae954f0370d`，其后提交仅归档本报告与证据。环境：Windows 11 企业版 x64，10.0.26200 / build26200；.NET SDK10.0.401；Node24.16.0；Electron45.0.0；ElectronNET.Core/Core.AspNet0.6.0；Playwright1.64.1。

## 可复现命令与实际结果

在源码根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
dotnet restore TaskManager.sln --locked-mode
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -SkipRestore
npm run test:native
npm run test:ui
npm run test:recovery
npm run test:desktop
```

构建恢复使用npm ci与NuGet `--locked-mode`，TypeScript、Vite生产构建、C# Release均成功。运行时依赖锁位于scripts/electron-runtime/package-lock.json。最终四组测试分别通过80、36、4、42项断言，共162项；四条可单独执行，亦由`npm run test:e2e`依次组织。不是以单独浏览器通过替代桌面验收。

原生回归真实主机PID108440，首帧sampleId1。浏览器验收于03:15完成；断连恢复于03:16:48完成。最后桌面验收于03:28:13完成，真实主机PID106924。测试仅对本次创建的专用TestWorkload操作，使用保存的PID+FILETIME；不保存全机进程转储、bootstrap令牌或Electron框架stdout。桌面运行工作目录故意设为系统临时目录，以验证产物不依赖源码cwd。

主代理独立复验：03:31:32再次从成品运行`npm run test:desktop`，42项全部通过，测试hostPid106060及其Electron进程/端口已清理。最终归档的桌面JSON为该次独立复验结果。首轮完整构建已实际执行根/UI npm ci与NuGet锁恢复；增加独立窗口探针后再次对三个项目执行锁恢复，最后对三项目Release完整构建，未将未执行的全平台安装器或清空全局NuGet缓存恢复算作通过。

## A01～A19

| ID | 结果 | 实际证据与范围 |
|---|---|---|
| A01 | 通过 | npm根/UI锁文件恢复，三个.NET项目锁文件恢复；TypeScript、Vite、Release构建exit0，0警告0错误；Electron明确45.0.0，运行时下载成功。便携输出含wwwroot与完整.electron |
| A02 | 通过 | 原生80断言：Windows health，当前宿主PID存在，sampleId/capturedAt前进，真实非空进程列表 |
| A03 | 通过 | 专用负载PID、名称、内存与独立Windows Get-Process比对；原生SCM/WTS/IPHelper均来自实际系统 |
| A04 | 通过 | 系统/新进程首帧CPU为null预热；后续CPU在合理区间；真实CPU/文件I/O负载产生大于0增量，无负速率 |
| A05 | 通过 | 生产UI浏览器及实际Electron截图均具有标题搜索、侧栏、命令栏、汇总表头、蓝色热度表格、连接与采样时间状态。人工视查1380截图与参考布局一致 |
| A06 | 通过 | 浏览器真实PID搜索/清空与CPU aria-sort；桌面额外暂停真采样并检查真实工作集按数值升序及内存aria-sort，未知值保持未知 |
| A07 | 通过 | 浏览器与实际Electron逐一导航性能、会话历史、启动项、用户、详情、服务、设置；标题正确。性能3条图表只用已采集历史，无示例曲线 |
| A08 | 通过 | 所有逐进程GPU/网络为null且有可访问不可用原因；I/O列和底栏明确“进程读写，非物理磁盘吞吐” |
| A09 | 通过 | 浏览器合法/无效可执行文件提交到真CreateProcess；桌面再创建专用负载并通过独立Windows进程树确认PID存在，未使用shell或假PID |
| A10 | 通过 | 浏览器/桌面均先取消后确认结束；取消仍存活，确认后独立存活查询失败、真实采集列表消失；另一独立测试负载保持存活 |
| A11 | 通过 | 错FILETIME409；PID0/4/自身宿主403；目标仍存活、后端继续服务 |
| A12 | 通过 | 缺失/错误令牌、外站Origin、错误Host端口拒绝。Kestrel只Listen127.0.0.1；不开放CORS；桌面额外实际点击外站链接和window.open，均被main进程同步拒绝 |
| A13 | 通过 | 真观测CPU/I/O历史累计后清除，起点前进、立即空列表，命名为本次会话，未冒充Windows完整历史 |
| A14 | 通过 | 原生与UI真暂停后sampleId稳定；周期2000ms生效，恢复后新样本前进；采样与轮询均串行 |
| A15 | 通过 | 真停止独立测试host，DOM保留同一身份与最后指标/采样时间并显示断连；同端口重启真实host，令牌实际轮换；UI重新bootstrap后POST暂停成功，未route mock |
| A16 | 通过 | Ctrl+F聚焦搜索，原生dialog Tab/Shift+Tab循环，Esc返回触发按钮；CPU/内存aria-sort。人工检查可见焦点，表格滚动区有scroll-padding，刷新不抢输入焦点。没有宣称完成全套屏幕阅读器或WCAG认证 |
| A17 | 通过 | 独立CsWin32 SetWindowPos将真实窗口调整960×640、1380×860、1600×1000；无页面横向溢出，窄表格自身可横向滚动；Electron WebContents实际125%缩放（DPR比1.25），close/主操作在视口内（允许innerWidth整数舍入1px）；reduced-motion实际取消CSS过渡。截图均已视查 |
| A18 | 通过 | 从output便携目录真实启动.NET→Electron.NET→BrowserWindow，desktop=true、精确loopback来源、renderer无require/process。真实UI搜索/导航/创建/取消/结束通过；独立CsWin32按测试PID+创建时间查HWND/GetWindowPlacement确认最大化、还原、最小化。关闭后保存的本次宿主/Electron身份全部退出，HTTP5193和CDP9224关闭；独立负载仍存活后由测试清理 |
| A19 | 通过 | 生产12个C#源文件静态检查：无System.Text.Json、手写DllImport/LibraryImport、Results.Json；30项NativeMethods声明，有实际CsWin32调用。无业务random/mock/fake数据。React19.3.1/Tailwind4.3.3/Electron.NET实际构建与运行。官方框架内部Socket.IO序列化是依赖内部实现，应用自有JSON全走JSON.NET |

主代理独立A19检查：排除obj/bin/node_modules/wwwroot后的生产C#/TS/TSX，System.Text.Json、DllImport、LibraryImport、Math.random、route.fulfill、NotImplementedException匹配0；原生采集与动作源码实际PInvoke调用行36。成品start.cmd和wwwroot/index.html均存在。

## 成品与证据

便携目录：`output/TaskManager-win-x64/`；入口：`start.cmd`或`TaskManager.Host.exe`。完整目录约436MiB；运行要求Microsoft.NETCore.App10.0和Microsoft.AspNetCore.App10.0，不要求Node/npm或Microsoft.WindowsDesktop.App。递归检查无ReparsePoint/junction指向源码目录。生产默认不启用CDP。

完整机器生成结果位于output/playwright，审核副本在本目录evidence：

- [真实桌面1380](evidence/final-desktop-1380.png)、[真实桌面960](evidence/final-desktop-960.png)、[125%完整可视内容](evidence/final-desktop-125-percent.png)、[真实结束确认](evidence/final-desktop-terminate-confirm.png)。
- [真实断连](evidence/final-recovery-disconnected.png)、[重连恢复](evidence/final-recovery-connected.png)。
- [桌面42项结果](evidence/final-desktop-results.json)、[恢复4项结果](evidence/final-recovery-results.json)、[浏览器36项结果](evidence/final-ui-results.json)。

缩放截图使用CDP Page.captureScreenshot的完整surface，避免Playwright按缩放CSS尺寸裁切物理surface。常规尺寸截图为实际Electron renderer，未替换数据、未修改图像。原生HWND总尺寸包含Windows不可见边框，因此内容像素可略小于请求窗口总尺寸。

最终依赖audit：根/UI构建输出均0漏洞；产物.electron全依赖npm audit为0。官方默认image-size1.x已通过npm清单固定到2.0.4并锁依赖，未修改框架源码；本版不启用splash解析。

## 清理与限制

最后检查5188/5191/5192/5193/5194/9224无本应用监听。本次测试主机、Electron与专用负载全部退出，测试临时目录删除；没有按进程名称批量结束任何其他程序。失败探测也由本次保存的PID+创建时间清理，最终PASS截图和报告覆盖已修复问题。

逐进程GPU/网络、物理磁盘活动时间、服务启停、启动项编辑、内核驱动分析不在PRD本版范围，界面有明确说明。启动项最终启用状态无法由Run项存在与否确定，保持“无法确定”；受保护进程字段按Windows真实权限返回不可用。非本机Windows版本、安装器、管理员提升、全套屏幕阅读器测试未执行，不计作已通过项目。
