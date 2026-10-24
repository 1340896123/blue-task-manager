# 首次启动替换提示：真实链路验收

日期：2026-10-25（Asia/Shanghai）。需求提交 `d1278e1`，业务提交 `8d465e2`，焦点修复 `4c7806b`。最终真实验收在 `2026-10-25 00:42:06 +08:00` 完成，**67 项断言通过**；管理员 worker 退出 0，耗时 27.57 秒。没有渲染器异常，没有发送任何系统接管 POST；独立递归读取的 HKLM64 IFEO 配置前后指纹一致。

本机现有 Debugger 指向 `C:\Program Files\BlueDomainTaskManager\TaskManager.Launcher.exe --task-manager-entry`。测试运行于另一专用目录，真实状态因此是“已有其他工具或目录接管此入口”，替换按钮禁用。本次没有覆盖、取消或临时重置已有接管。

## 命令与测试产物

主代理执行 `npm run typecheck`、`npm run build`、`npm run test:static` 与 Host Release 构建；均成功，Host 0 警告/0 错误。焦点修复后再次完成 Web 构建及静态检查。测试使用复制既有完整自包含运行时、覆盖已构建 Host 和生产 Web 产物的隔离目录 `output/first-launch/TaskManager-win-x64`，没有安装到 Program Files，也没有修改共享发布目录。

```powershell
node --check tests/first-launch-integration.mjs
$firstLaunchWorker = Join-Path (Get-Location) 'tests/first-launch-worker.ps1'
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @(
    '-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $firstLaunchWorker + '"')
)
# 已处于提升的终端也可直接运行：
powershell -NoProfile -ExecutionPolicy Bypass -File tests/first-launch-worker.ps1
```

worker 只运行固定的 `first-launch-integration.mjs`，先用独立 CsWin32 探针确认实际提升令牌。测试使用专用 5321/9321 端口和临时偏好目录；真实 Host/Electron 的 PID 与创建 FILETIME 记录于结果。窗口通过生产 `window/close` 关闭；headless 清理只处理该测试创建的 Host 及其精确命名 ETW 会话。全部测试身份退出、端口释放，临时文件已清理。

精确产物清单位于 `output/playwright/first-launch/artifacts.json`。隔离 Host 包含同时开发的原生网络工作基线，因此源码提交号不能独立标识整个二进制；以下 SHA256 标识实际验收产物。

| 产物 | SHA256 |
|---|---|
| `TaskManager.Host.dll` | `abb7d384385cfae78ac644b80d9434428a14844681e3c8fc3f00b4618c5276fe` |
| `wwwroot/index.html` | `e1091b0a075a327df325a8b8b2bca67af674d5908b3002fe9fc27ec2cf83d3a8` |
| `wwwroot/assets/index-CHoL4of6.js` | `9ff16ab5a58c5e059f83c54b0883fe17611adf4aaa2db5c9d2535e7a424b23c9` |
| `wwwroot/assets/index-CtEvxi1a.css` | `8e75ad6849b0367046f5d26dec5e465b343149cd74dc2cef067a8009616c93ce` |

## F 系列实际结果

| 项目 | 实际覆盖 |
|---|---|
| F01 | 旧 version-1 偏好缺字段默认 false；损坏文件有 `preferencesRecovered`；JSON.NET 真文件落盘与读回；偏好 GET 传输失败不从默认值抢弹，显式重读成功后再提示。 |
| F02 | 两次 headless/普通浏览器不弹；真实 Electron 首次显示；原生状态与独立 HKLM64 查询一致；未选择时无系统写入。 |
| F03 | 暂不、Escape、关闭按钮均保存 true；真实桌面重启不再弹；可进入设置管理。 |
| F04 | 密度、采样周期、窗口尺寸/缩放与重启保留完成标记；恢复默认界面偏好不重置该标记；前往设置清除共享搜索。 |
| F05 | 真实其他目录冲突禁用替换并显示后端原因；中断集成 GET 后保留未知/禁用状态，显式重读恢复；专用 `preferences.json` 路径改为目录产生真实 I/O 错误，明确“选择未保存，重启可能再次提示”，持续阻断至退出后重启再次提示。 |
| F06 | **未执行替换成功和恢复链路**：已有安装目录接管，测试不覆盖它；不能将冲突检查视作成功注册验证。 |
| F07 | 初始焦点在暂不，连续 Tab 保持对话框内；1380×860、960×640、实际 Electron 125%（逻辑 768×512）及 reduced-motion 通过；标题和底部动作可见，正文内部滚动，无页面横向溢出。真实系统写入忙态未触发。 |
| F08 | 弹窗打开期间真实 sampleId 继续前进；后续七页导航与设置可用；零渲染器异常；五次测试 Host 及其 Electron 身份全部退出，HTTP/CDP 释放。 |

F05 的同目录已启用、旧接管、真实无写权限、缺二进制、原生注册写入/读回失败没有对应环境，未执行，未用伪 API 或注册表 fixture 代替。读取故障只阻断真实 HTTP 传输，没有提供合成响应。未重跑完整安装/卸载、全套原生硬件采集、性能和用户服务控制验收。

## 截图与诊断记录

结构化结果 `output/playwright/first-launch/results.json`；worker 退出码和日志为同目录 `worker-status.json`、`integration.log`。

- `first-launch-dialog.png`：真实 Electron 对话框独立截图，暂不按钮焦点可见。
- `first-launch-1380.png`、`first-launch-960.png`：正常/最小窗口。
- `first-launch-125.png`、`first-launch-125-native.png`：实际 Electron 125% 及独立原生窗口截图。
- `first-launch-save-failure.png`：专用偏好目录的真实保存失败提示。

首轮发现 React `autoFocus` 在隐藏 dialog 挂载时未生效，原生打开后焦点落在关闭按钮；`4c7806b` 改为 `showModal()` 后显式聚焦暂不并禁止焦点滚动，最终复验通过。首轮证据保留为 `initial-focus-failure.*`。第二轮测试先恢复了可写偏好路径再退出，生产 `pagehide` 正确补存 true，导致“重启应再提示”的测试前提不成立；已改为持续阻断到退出，未修改业务，最终通过。此诊断保留于 `initial-fixture-order-failure.json`。
