# 单进程结束反馈交付记录

日期：2026-11-02。规格提交 `f9b3133`，实现提交 `899bf68`。阶段一由独立只读子代理检查，阶段二由另一子代理实现；主代理复核最终差异及交付检查。

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
| 浏览器及 Electron 成功/真实 409/取消/键盘链 | 未执行；管理员 worker 启动受执行策略阻止 |
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

## 产物与执行限制

新 index.html、JS/CSS 已同步至普通 Release 的 wwwroot 以及 `output/Takao-win-x64/wwwroot`。源、普通 Release、桌面目录的 JS SHA256 一致：`C1C70409BFB214C2684916F787797E8A8DEF3608A97B176C739ECF35258AB5C9`。没有安装或替换用户已安装应用，也没有新建安装包。

自动审批拒绝了组合命令（准备测试产物并以标准 Windows RunAs 启动既有固定 `windows-layout-worker.ps1`，测试集为 `static-v11,native-api,ui-smoke,process-ui-v11`），仅返回“blocked by policy”，没有更具体原因。被拒绝的命令未执行；之后仅以普通权限同步明确列出的生成文件并执行静态检查，没有重试提权或修改权限校验。当前普通令牌不满足生产 Host 的真实管理员前置条件，因此 A09/A10/A11/A16/A18 及 B05 的真实链路仍缺本轮证据，不能将编译通过视为 E2E 通过。

新增测试运行后会保存 `output/playwright/v11-process-terminate-{browser|desktop}-{failure|success}.png` 和 `output/playwright/final-desktop-terminate-success.png`；本轮没有生成这些截图。没有创建测试 Host、业务目标或 ETW 会话，检查专用测试端口 5191/5192/5193/9224/9227 均无监听。
