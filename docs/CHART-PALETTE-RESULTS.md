# 白色简约图表验收结果

2026-10-27（Asia/Shanghai），Windows x64。业务样式提交 `5bddec0`，配色规范提交 `65178cc`。当前生产 React、Release Host 与官方 Electron.NET runtime 已通过真实 Windows 采样链验证。

Web生产构建（含TypeScript）、Host Release和差异检查通过。静态检查3项、真实浏览器120项、正式Electron141项通过，共264项；两个专项报告均为 `PASS`、`cleanup:true`，固定RunAs worker独立确认 `elevated:true`。Release的 `wwwroot/index.html` SHA256与本次前端构建相同，实际Host/JS/CSS散列保存在结果JSON。

## C系列实际结果

| 项目 | 实际结果 |
|---|---|
| C01 全部图表 | 本机5块磁盘、2张硬件网卡、2个GPU适配器、28个逻辑处理器均来自生产快照。CPU总体、逐CPU、内存、每磁盘、每网卡、每GPU主图及11张资源小曲线逐项检查计算样式，均为纯白图面、中性灰边界和统一序列Token。 |
| C02 双序列与对比度 | 磁盘读/写和网卡收/发保留蓝色实线、深灰虚线及明确图例；实际主/次数据路径和取样点均已观测。计算样式确认曲线、点和图例颜色一致。对白底对比度：主线5.674:1、次线6.124:1、标签最低6.385:1；关键线条≥3:1、文字≥4.5:1。 |
| C03 CPU模式 | 总图和28个实际group/index图采用同一白底、蓝线和浅灰网格；模式选择器、当前百分比、真实编号和最多48图的有界渲染保持。1380、960及正式Electron125%均检查了逐CPU网格。 |
| C04 数据和状态 | 首快照实际返回PDH `sampleId=0` 与GPU/磁盘/逐CPU预热原因；真实断开的硬件网卡保持未知速率和说明。暂停后样本及历史保持原值，恢复继续采样；历史仍有界为120点。磁盘D08逐盘检查通过，主区恰有一张读/写吞吐图，无活动时间/活动率文案。表格热度Token、资源选中颜色和键盘焦点保持。 |
| C05 布局 | 浏览器及正式Electron检查1380×860、960×640；Electron窗口API读回 `zoomFactor=1.25`，实际内容视口1104×688。reduced-motion、可见键盘焦点、无文档横向溢出均通过。目视复核CPU、逐CPU、磁盘、Up网卡和125%原生窗口，未见旧浅蓝图面、紫色次线、蓝色小图边框或文字重叠。 |
| C06 真实桌面与清理 | 生产UI→同源HTTP→JSON.NET→CsWin32→Windows链路通过；测试仅关闭专用Host/Electron/浏览器与其精确身份子进程，HTTP/CDP端口均释放。浏览图表无IFEO写入、用户服务控制或UI运行时错误。 |

本次产品改动仅为共享CSS配色，未改曲线点、缩放、历史、采样或API。所有当前设备类别均有实际数据源；未人为拔除设备、停用驱动或诱发长期PDH故障，相关故障恢复分支没有新增成功证据。HWiNFO继续为传感器表，没有为配色验收新增图表。本轮聚焦配色及受影响交互，没有重复执行无关的安装/服务/进程动作套件。

## 可复现命令

在仓库根目录执行。当前机器没有项目锁定的SDK 10.0.401，Host使用已安装10.0.403的MSBuild构建，`global.json`保持原样。两个专项使用相同专用端口，依序运行。

```powershell
npm run build
dotnet 'C:\Program Files\dotnet\sdk\10.0.403\MSBuild.dll' src/TaskManager.Host/TaskManager.Host.csproj /t:Build /p:Configuration=Release /p:ElectronSkipExecCommands=true /p:BuildProjectReferences=true /v:minimal /nologo
node tests/static-v11.mjs

Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\chart-palette-worker.ps1" -Mode browser'
Get-Content output/playwright/chart-palette/worker-browser.json
# 等待complete=true、exitCode=0后继续
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\chart-palette-worker.ps1" -Mode desktop'
Get-Content output/playwright/chart-palette/worker-desktop.json
git diff --check
```

## 保存证据

- [浏览器120项](evidence/chart-palette/browser-results.json)、[Electron141项](evidence/chart-palette/desktop-results.json)：实际构建散列、各资源计算样式、对比度、窗口尺寸、真实来源预热、暂停和清理。
- [125%原生CPU窗口](evidence/chart-palette/desktop-125-cpu-native.png)、[逐逻辑CPU网格](evidence/chart-palette/desktop-1380-logical-cpu.png)、[内存](evidence/chart-palette/desktop-1380-memory.png)。
- [125%磁盘双序列](evidence/chart-palette/desktop-125-disk.png)、[真实Up网卡双序列](evidence/chart-palette/desktop-1380-network.png)、[GPU](evidence/chart-palette/desktop-1380-gpu.png)、[960逐CPU](evidence/chart-palette/browser-960-logical-cpu.png)。

完整当前截图与worker日志保存在 `output/playwright/chart-palette/`；旧报告和旧截图保持其历史状态。
