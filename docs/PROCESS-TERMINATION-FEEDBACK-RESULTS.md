# 单进程结束反馈交付记录

日期：2026-11-02。规格提交 `f9b3133`，实现提交 `899bf68`。阶段一由独立只读子代理检查，阶段二由另一子代理实现；主代理复核最终差异及交付检查。

补充真实 E2E 已完成：2026-11-02 16:34～16:41（Asia/Shanghai），5 套共 242 项断言通过。浏览器与正式 Electron 均验证成功自动关闭并通知、真实失败保留弹窗。测试前置修复提交 `635f72e`，机器结果见 [汇总](evidence/process-termination-feedback/summary.json)。

## 已实现

`ProcessActionDialog` 仅在真实结束接口返回 `completed === true` 时触发成功回调；`ProcessWorkspace` 自动关闭弹窗并使用现有成功 toast 显示名称、PID 与后端确认消息。HTTP/Windows 错误保留原弹窗及错误，false/null 保留“尚未确认退出”结果，不显示成功、不自动重试。保留单次确认及固定创建身份，完整进程树仍展示逐项结果。

同步 `process-ui-v11.mjs`、`desktop-e2e.mjs`、`ui-smoke.mjs` 的成功断言；process-ui 新增错误 FILETIME 发送到真实后端的 409 断言、目标仍存活、错误弹窗保留与无成功通知，随后正常结束同一专用目标。仅修改请求，不替换响应；process-desktop 复用该流程。

## 实际执行

| 检查 | 结果 |
|---|---|
| `npm run typecheck` | 通过 |
| `npm run build` | 通过；生产 JS `index-Cv0LXw99.js`，CSS `index-CwaLydxz.css` |
| C# solution Release 构建 | 通过；0 警告、0 错误 |
| Windows x64 自包含 Host publish | 通过；输出 `output/termination-feedback-publishbin` |
| 三个修改的 E2E 脚本 `node --check` | 通过 |
| `node tests/static-v11.mjs` | 3 项通过；JSON.NET/CsWin32、真实业务数据及语义 Token 静态约束保持 |
| `git diff --check` | 通过 |
| 浏览器及 Electron 成功/真实 409/取消/键盘链 | 通过；标准 RunAs 固定 worker 验证实际管理员令牌，分套结果见下文 |
| false/null 尚未确认退出 | 源码检查保留弹窗；真实 Windows 超时未执行 |

构建命令：

```powershell
npm run typecheck
npm run build
dotnet 'C:\Program Files\dotnet\sdk\10.0.403\dotnet.dll' build Takao.sln -c Release --no-restore -p:ElectronSkipExecCommands=true
dotnet 'C:\Program Files\dotnet\sdk\10.0.403\dotnet.dll' publish src/Takao.Host/Takao.Host.csproj -c Release -r win-x64 --self-contained true --no-restore -o output/termination-feedback-publish -p:UseAppHost=true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishAot=false -p:ElectronSkipExecCommands=true
node --check tests/process-ui-v11.mjs
node --check tests/desktop-e2e.mjs
node --check tests/ui-smoke.mjs
node tests/static-v11.mjs
git diff f9b3133..899bf68 --check
```

本机未安装 global.json 指定的 SDK 10.0.401，使用已安装 10.0.403 的明确入口完成构建，不改 global.json。

上述 publish 命令的 `-o output/termination-feedback-publish` 未带尾随分隔符；Electron.NET 的发布目标实际追加 `bin`，日志确认最终 .NET 载荷位于 `output/termination-feedback-publishbin`，表格记录的是该实际路径。

## 产物与执行限制

新 index.html、JS/CSS 已同步至普通 Release 的 wwwroot 以及 `output/Takao-win-x64/wwwroot`。源、普通 Release、桌面目录的 JS SHA256 一致：`C1C70409BFB214C2684916F787797E8A8DEF3608A97B176C739ECF35258AB5C9`。没有安装或替换用户已安装应用，也没有新建安装包。

初次交付时，自动审批拒绝了准备测试目录和启动管理员 worker 的组合命令，仅返回“blocked by policy”。此为历史未执行记录。用户随后明确要求 E2E，本次单独调用既有固定 worker 的标准 Windows RunAs 成功，未修改 UAC、权限校验、系统入口或安全策略。

## 真实 E2E 结果

环境：Windows 11 build 26200，Node 24.16.0，锁定 Playwright 1.64.1；生产 UI 的源、普通 Release 与正式桌面目录 JS SHA256 相同。真实 UI → 同源 HTTP → JSON.NET → CsWin32 → Windows，无业务数据或接口响应模拟。

| 测试套件 | 断言 | 结果与证据 |
|---|---:|---|
| `native-api` | 79 | [通过](evidence/process-termination-feedback/native-api.json)：真实采样、CreateProcess/TerminateProcess、身份/保护/令牌/Origin 拒绝、暂停恢复 |
| `ui-smoke` | 37 | [通过](evidence/process-termination-feedback/ui-smoke.json)：真实八页、取消仍存活、只结束选中专用目标、成功通知、失败保留与焦点回归 |
| `process-ui-v11` | 40 | [通过](evidence/process-termination-feedback/browser-results.json)：真实 409 保留弹窗、无成功 toast，正常请求确认退出后自动收起并通知 |
| `desktop-e2e` | 45 | [通过](evidence/process-termination-feedback/desktop-main-results.json)：正式 Electron、真实窗口/125%/960px、成功通知、关闭释放进程与端口 |
| `process-desktop-v11` | 41 | [通过](evidence/process-termination-feedback/desktop-results.json)：正式 Electron 复验真实 409/成功/取消、树/详情/效率模式/优先级与清理 |

浏览器 worker [结果](evidence/process-termination-feedback/browser-worker.json) 与专项桌面 worker [结果](evidence/process-termination-feedback/desktop-worker.json) 均确认 `elevated:true`、`complete:true`，对应套件 exitCode 0；主桌面在[首次桌面 worker 记录](evidence/process-termination-feedback/desktop-initial-worker.json)中已通过。独立只读子代理复核浏览器报告、请求代码及截图，确认没有 mock 或虚假通过。

开始时两套 UI 测试因缺少根目录 Playwright 包未能启动；按现有锁文件执行 `npm ci --prefer-offline --no-audit --no-fund` 后恢复。桌面进程专项首次被“替换标准任务管理器？”提示阻挡，等待测试目标行时触发 `locator.waitFor: Timeout 30000ms exceeded`，定位于 `tests/process-ui-v11.mjs:52`；原始调试日志仅保留在忽略的 `output/playwright/windows-layout`，初次失败结果仍归档在上述 worker JSON。测试前置修复只改测试，在真实初次读取完成后点击“暂不替换”，确认弹窗移除及隔离偏好保存，再进行进程操作。此后专项重跑 41 项通过，未改产品行为、写 IFEO 或伪造偏好响应。

复现命令（每条 worker 完成后再执行下一条；结果以 JSON 中 exitCode 为准）：

```powershell
npm ci --prefer-offline --no-audit --no-fund
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\windows-layout-worker.ps1" -Suites native-api,ui-smoke,process-ui-v11'
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\windows-layout-worker.ps1" -Suites process-desktop-v11,desktop-e2e -Desktop'
```

成功判断同时检查真实 `completed === true`、弹窗消失、正确 PID 的成功 toast、独立 Windows 进程退出和列表移除。失败用测试目标的 FILETIME+1 通过 `route.continue` 发送到生产后端，检查真实 409/identityChanged、弹窗及错误保留、目标仍存活、无成功通知；恢复真实身份后结束同一专用目标。未拦截或替换业务响应。

应用范围截图已目视核对并归档：

- 浏览器：[失败保留](evidence/process-termination-feedback/v11-process-terminate-browser-failure.png)、[成功收起](evidence/process-termination-feedback/v11-process-terminate-browser-success.png)。
- 正式 Electron：[失败保留](evidence/process-termination-feedback/v11-process-terminate-desktop-failure.png)、[成功收起](evidence/process-termination-feedback/v11-process-terminate-desktop-success.png)、[正式窗口成功通知](evidence/process-termination-feedback/final-desktop-terminate-success.png)。

只操作测试创建的专用进程、临时目录和隔离偏好。测试内部验证目标身份、关闭本次 Host/Electron 并清理其精确 ETW 会话；结束后独立查询已知测试 PID 均退出，5191/5192/5193/9224/9227 均无监听。没有替换用户已安装应用或系统任务管理器入口。

限制：`completed=false/null` 的真实 Windows 退出超时未稳定触发，仅源码核对保留分支；成功自动关闭后的具体焦点目的地没有专项断言，已有 Modal Escape 焦点回归通过。本轮覆盖受影响的 A09/A10/A11/A16/A17/A18 和 B05 等路径，不宣称执行全部产品验收矩阵。
