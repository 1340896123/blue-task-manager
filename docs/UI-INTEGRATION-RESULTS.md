# 界面占位接入：最终验收

日期：2026-11-04（Asia/Shanghai）。范围见 [UI-INTEGRATION.md](UI-INTEGRATION.md)。阶段1业务提交`78984858c3c88347a334be44f555b60352f948b7`，阶段2业务提交`15fffac414bb0338ccc0f0342578094471d6dab4`；阶段3在其基础上完成下述最小修复、真实验收和独立提交。

## 已接通与最终修复

八页沿用真实Windows来源。完成进程网络采集回退、I/O和用户别名原因、完整进程详情与独立来源时间、已退出历史详情、用户会话定位和按页查询/偏好保存；HWiNFO外部来源缺失显示真实原因与恢复步骤。GPU磁贴显示真实描述/软件标志，缺失描述时保留LUID/phys身份，不再误用phys索引作为Windows GPU编号。

最终验收发现普通ETW回退在其他会话存在的现场持续丢事件：C#、framework-dependent/自包含、PS5/PS7及独立Windows logman短采均复现。采用[Microsoft PerfView固定提交](https://github.com/microsoft/perfview/blob/9f071a5cb2e70cdfee13abc510344ab1e8840c90/src/TraceEvent/TraceEventSession.cs)的标准`EVENT_TRACE_INDEPENDENT_SESSION_MODE`后恢复，常量由CsWin32生成；不增加驱动、私有协议或修改ETW全局设置。临时诊断已移除。首次受控恢复实测808/809条进程有数值、20条正流量、零事件/缓冲丢失，全部8种TCP/UDP IPv4/IPv6收发布局实际出现。

“更多”菜单改为按按钮右边缘对齐，避免在1080P主窗内截断菜单文字；保留公开Qt原生菜单。测试工具修复了端点文件尚在写入即被读取、Windows短暂结果文件共享冲突、数值本地化比对和UTF-8读取问题。

## 实际结果与证据

所有路径相对`E:\taskManager`；本节仅列本轮实际执行结果，不累计重试/旧验收数量。

| 检查 | 实际结果 | 证据 |
|---|---|---|
| 自包含发布 | 退出0；Qt6.8.3、.NET10.0.12，构建SDK10.0.403/MSVC19.52.37025.0 | `output/ui-integration/stage3/publish-final.log`、`publish-final.exit` |
| 最终依赖/许可 | 必需文件完整，无Electron资源，Host/Qt SHA256留档 | `output/ui-integration/stage3/release-manifest.json` |
| 网络全量及并发隔离 | PASS71，退出0；四协议真实收发/空闲/停止、暂停/恢复、身份/过期、释放重建；两个自有会话均正收发且零丢失，停止peer后primary继续有效 | `output/ui-integration/stage3/concurrent-network-results.json`、`concurrent-network.exit` |
| 生产API回归 | PASS80，退出0；真实采样、权限与身份保护、历史、设置和专用进程结束 | `output/ui-integration/stage3/verified-native-api.log`、`acceptance-verified.json` |
| Qt接线实值 | PASS28，退出0；八页查询隔离/偏好、详情与独立API值对照、实际ETW来源、用户过滤、退出历史 | `output/ui-integration/stage3/verified-wiring/results.json`、`cleanup.json` |
| Qt组件 | PASS80，退出0，20张1080P WGC图像；八页/全部下拉/真实间隔、暂停、冻结、端点与专用结束 | `output/ui-integration/stage3/rechecked-components/native-results.json`、`captures.json` |
| Fluent组件 | PASS26，退出0，6张WGC图像 | `output/ui-integration/stage3/fluent/results.json`、`fluent.log` |
| 图标缓存策略 | PASS26，退出0 | `output/ui-integration/stage3/final-icon-cache.log` |
| 启动项 | PASS66，退出0，仅本轮GUID自有HKCU/Startup项，清理后原指纹不变 | `output/ui-integration/stage3/startup.log` |
| 物理磁盘 | PASS56，退出0；真实DeviceIoControl与独立Get-PhysicalDisk/Get-Counter对照、缓存及句柄 | `output/ui-integration/stage3/disk-results.json` |
| 生命周期 | PASS20，退出0；正式Host→Qt、管理员/发布运行库、端口、窗口关闭、Job异常退出和自有ETW清理 | `output/ui-integration/stage3/verified-lifecycle/results.json` |
| 对比度 | 28对语义Token全部通过正文/说明及焦点/曲线阈值 | `output/ui-integration/stage3/contrast.json` |
| 真实键鼠扩展 | PASS62，退出0，28张1920×1080 WGC图像；完整Extended及NativeFileDialogs，非聚焦诊断模式 | `output/ui-integration/stage3/desktop-verified/results.json`、`desktop-verified.exit` |

组件方法调用与真实键鼠分别记录。组件完整回归在GPU命名修复后通过；随后单行菜单锚点调整通过Qt重建，并由独立物理流程复核。仅1920×1080可见物理窗口、100%缩放；主屏2560×1440仅用于FYLHelper实际分辨率初始化，没有改变系统分辨率或执行其它显示矩阵。

物理流程覆盖八页Tab/Space、搜索及偏好读回、所有已有下拉、原生程序选择与Esc取消、专用进程创建、原生CSV保存且恰一行身份匹配、实际进程菜单、结束取消/确认/反馈、列宽320px保存、逻辑CPU模式、侧栏、两秒/一秒间隔、冻结且后台继续采样、标准密度、滚动及重置取消。测试结束目标为`39504:134382333742222272`，确认后真实退出；Host正常退出、Qt窗口不残留，清理/剪贴板恢复无错误。

本机早期未观察到Alt数字导航和Shift+F10弹出菜单，不计这些快捷键通过。最终导航使用可见原生按钮及已验证的Tab/Space；进程菜单使用经过身份过滤的真实右键。Delete两次打开确认与Alt+F4关闭在最终运行均实际观察成功。所有输入仍为FYLHelper，popup选择仅在WindowFromPoint确认本轮Qt Popup归属后发送真实点击；没有UIA/DOM/测试RPC替代动作。

因既有GoLink窗口曾遮挡测试目标，最终仅在本轮临时profile中预设Takao自身alwaysOnTop=true，由产品原有Windows链路恢复；不把该设置当作物理置顶动作通过，也未关闭、移动或改配置于其他程序。窗口前台被Windows拒绝时，仅对本轮拥有的带标题栏Qt窗口临时抬高、核对命中归属后以FYL点击激活，并恢复原窗口层级。最终受测Host、Qt和测试脚本SHA256均在桌面JSON中，核对与交付文件一致。

## 复现命令

使用PowerShell7，涉及真实Host/设备的脚本使用实际提升令牌。所有运行须按顺序完成，避免两个测试争用前台和FYLHelper输入锁。

```powershell
& scripts/publish-desktop.ps1
& scripts/import-msvc.ps1
cmake -S src/Takao.Qt -B output/qt-tests-build -G Ninja '-DCMAKE_PREFIX_PATH=E:/taskManager/output/Qt' -DCMAKE_BUILD_TYPE=Release -DTAKAO_BUILD_INTEGRATION_TESTS=ON
cmake --build output/qt-tests-build --target Takao.Qt.Integration Takao.Qt.Wiring Takao.Qt.Fluent.Tests Takao.Qt.IconCache.Tests --parallel 4
dotnet run --project tests/Takao.NetworkTests -c Release -- output/ui-integration/stage3/network-repeat.json
$env:TASKMANAGER_TEST_PORT = '5191' # Choose an unused loopback port for this test instance.
node tests/native-api.mjs
& scripts/test-qt-wiring.ps1 -OutputDirectory output/ui-integration/wiring
& scripts/test-qt-components.ps1 -SkipBuild -OutputDirectory output/ui-integration/components
& scripts/test-qt-ui-integration-desktop.ps1 -Extended -NativeFileDialogs -OutputDirectory output/ui-integration/desktop
& tests/qt-lifecycle.ps1 -OutputDirectory output/ui-integration/lifecycle
python tests/qt-contrast.py --output output/ui-integration/contrast.json
```

网络专项新增并发隔离使用真实专用套接字和两个本轮自有会话；peer停止后再次要求primary的更新窗口继续正收发且没有丢事件。未知、预热、丢失和不安全身份仍不能转零。

## 画面复核、范围与清理

`rechecked-components/01-page.png`～`08-page.png`覆盖八页；`resource-gpu.png`显示真实硬件/软件名称，`resource-hwinfo.png`显示外部来源原因和恢复说明，`13-process-connections.png`、`15-termination-feedback.png`保存真实端点和结束反馈。正文/说明、可见焦点、单一命令栏、可读曲线、表格自身滚动与设置限宽均按指定ui-ux-pro-max桌面适用规则复核。

本机没有可用HWiNFO共享内存，成功传感器链未执行；没有为测试安装/修改HWiNFO。未执行用户服务启停、用户启动配置更改、IFEO接管、更新安装/卸载、硬件热插拔及未真实诱发的权限分支。启动项脚本的历史固定notExecuted文案不用于推断令牌；实际提升令牌由生命周期检查独立验证。已有指向旧目录的IFEO与旧测试命名启动项属于进入本轮前现场，未擅自清理。

早期失败日志保留在`stage3/acceptance.json`、`acceptance-final.json`、`acceptance-verified.json`及对应目录。包括PS5编码/脚本解析、独立测试platform搜索路径、初次输入锁、初始化/文件共享竞态、Alt导航未观察成功、菜单文字超出原截图边界、同名正文/Popup文字定位和首次并发检查失败的中间结果，不将它们计为最终通过。最终并发报告额外保存双方窗口事实，断言未放宽。真实网络丢失另有`final-network-audit.json`、`native-network-summary.txt`与恢复后的`independent-network-audit.json`，不以界面截图替代采集证据。

清理仅处理本轮Popen/Process原句柄、本轮专用负载的PID+创建FILETIME、或完整自有ETW名称前缀；没有按名称批量结束用户进程。进入本轮前的七项未提交修改均保留，新物理脚本单独保存，不混入原307行用户测试改动。

最终可运行目录：`E:\taskManager\output\Takao-win-x64`，正式入口`Takao.Host.exe`。
