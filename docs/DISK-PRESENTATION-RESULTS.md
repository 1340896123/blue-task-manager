# 磁盘读写展示验收结果

2026-10-27（Asia/Shanghai），Windows x64，业务代码 `81dc0f4`，当前规格提交 `8b4c262`、`9c2c553`。用户明确保留读取/写入曲线，本次 D08 移除磁盘活动时间的数值、大小曲线、百分比、图例和说明文案，磁盘主图只保留读取/写入吞吐。

Web 生产构建（含 TypeScript）、当前 Host Release、`git diff --check` 通过；静态检查3项、真实浏览器100项、正式 Electron110项、受影响页面回归49项通过，共262项。两个专项 worker 均独立确认 `elevated:true`，专项报告均为 `PASS`、`cleanup:true`。前端构建与 Release `wwwroot/index.html` 的 SHA256 一致，专项报告保存了实际执行文件及JS/CSS散列。

## D08 与回归结果

| 项目 | 实际结果 |
|---|---|
| 活动时间删除 | 本机5块真实磁盘逐一键盘选择；磁贴、主图、事实、图例、可访问名和可展开说明没有活动时间、活动率、百分比或空闲时间口径。主区恰有一张读取/写入图。 |
| 读写曲线与数值 | 主图与磁贴均保留读取实线、写入虚线；同一渲染中的两组路径逐项相同，验证历史与字节速率缩放一致。图中显示字节/秒速率轴，磁贴读/写两项速率与详情事实一致。 |
| 设备身份 | 五盘型号、类型、总线、物理编号继续与独立 `Get-PhysicalDisk`、`Get-Disk`、`Win32_DiskDrive`、`Get-Counter` 对照；型号、HDD/SSD、SATA/NVMe搜索准确，未知字段仍按原可空合同验证。 |
| 布局和可访问性 | 浏览器与正式 Electron 均检查1380×860、960×640；正式 Electron 通过窗口API读回实际 `zoomFactor=1.25`，内容视口1104×688。reduced-motion、完整型号换行/选择、无文档横向溢出均通过。磁盘磁贴最小96px，逐项文字矩形均位于条目内，无裁切或重叠；普通资源条目继续72px。 |
| 后台与错误合同 | 本次业务提交仅包含Web性能组件和CSS。真实快照仍包含原生 `activePercent`，系统最忙活动率和读取聚合按原合同验证；静态身份不进入历史。代码审查确认活动率单字段原因不进入磁盘可见状态，读取/写入和身份原因保留。 |
| 暂停与清理 | 真实API暂停保留最后快照，恢复后样本继续；浏览磁盘没有IFEO/用户服务控制请求或UI运行时错误。两个专项仅关闭测试启动的Host/Electron/浏览器及其精确身份子进程，专用HTTP/CDP监听均释放。 |
| 相关页面回归 | `pages-ui-v11` 的49项通过，包含单张磁盘吞吐图和无活动文案；内存、网卡、GPU、服务只读详情、跨页定位、偏好、暂停/冻结及960/1104/1380布局相关检查通过，无CSP错误。 |

两名阶段代理分别目视复核了当前1380、960和125%截图，读/写主图、磁贴双速率、设备标题和事实可读，没有活动时间残留。UI/UX Pro Max适用桌面检查采用现有Token、显式线型/图例、数值、键盘选择与reduced-motion；未扩展新的视觉主题。

## 可复现命令

在仓库根目录执行，两个磁盘专项顺序运行。当前机器没有项目锁定的SDK 10.0.401，因此构建直接调用已安装10.0.403的MSBuild，`global.json` 保持原样。

```powershell
npm run build
dotnet 'C:\Program Files\dotnet\sdk\10.0.403\MSBuild.dll' src/TaskManager.Host/TaskManager.Host.csproj /t:Build /p:Configuration=Release /p:ElectronSkipExecCommands=true /p:BuildProjectReferences=true /v:minimal /nologo
node tests/static-v11.mjs

Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\disk-identity-worker.ps1" -Mode browser'
Get-Content output/playwright/disk-presentation/worker-browser.json
# complete=true、exitCode=0后继续
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\disk-identity-worker.ps1" -Mode desktop'
Get-Content output/playwright/disk-presentation/worker-desktop.json

Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\windows-layout-worker.ps1" -Suites pages-ui-v11'
Get-Content output/playwright/windows-layout/worker-browser-status.json
git diff --check
```

当前磁盘专项测试读取 `src/TaskManager.Host/bin/Release/net10.0-windows10.0.19041.0` 的生产UI及官方Electron.NET runtime。`disk-identity-worker.ps1` 现在将结果写入 `output/playwright/disk-presentation/`；旧D01～D07报告和 `docs/evidence/disk-identity/` 均作为历史证据保留。

## 保存证据与边界

- [浏览器100项](evidence/disk-presentation/browser-results.json)、[正式Electron110项](evidence/disk-presentation/desktop-results.json)、[页面回归49项](evidence/disk-presentation/pages-ui-results.json)。
- [浏览器真实提升worker](evidence/disk-presentation/worker-browser.json)、[桌面真实提升worker](evidence/disk-presentation/worker-desktop.json)、[页面回归退出状态](evidence/disk-presentation/pages-worker.json)。
- [960窗口](evidence/disk-presentation/browser-960-heading.png)、[1380桌面](evidence/disk-presentation/desktop-1380-heading.png)、[125%原生窗口](evidence/disk-presentation/desktop-125-heading-native.png)。其余截图和日志保存于 `output/playwright/disk-presentation/`。

本机未诱发“只有idle计数器失败”的真实分支，该过滤路径仅做代码审查；未伪造业务快照。真实拒绝访问、不支持、RAID/虚拟盘、热插拔及编号复用仍未触发，本次不为补覆盖而拔除用户磁盘。本轮验证当前Release目录，没有执行用户安装目录替换、安装器更新或全量无关测试。
