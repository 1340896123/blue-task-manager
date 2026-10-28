# 已启用免提示：前置与冲突回归验收

> 历史验收记录：本文保留当时的产品名、路径与构建结果。自 2026-10-28 起产品统一命名为 Takao；当前命令与目录以 [README](../README.md) 为准，历史记录不作为本次重命名的验收证据。

日期：2026-10-26。规格为 [FIRST-LAUNCH-INTEGRATION.md 的 F09](FIRST-LAUNCH-INTEGRATION.md)。本轮真实桌面套件 **40 项通过**；验证了初次读取期间不创建提示，以及其他目录冲突仍正常提示。**真实当前目录已启用的 F09 成功分支未执行**，不能把冲突场景的通过当作该分支通过。

生产提交为 `85be85a`、`8872757`，测试 checkout 为 `8872757e11aa84cc0a8f27de84c402b7afac0c41`。独立便携目录 `E:\taskManager\output\takeover\TaskManager-win-x64` 的生产 `wwwroot` 已更新，JS 为 `index-AknTUE7K.js`。本轮未改后端；复用先前自包含 Host（ProductVersion `1.1.1+a635d5bc6b44ba60759243c2bce3aebd00ae3026`）。实际产物 SHA256 见 [机器结果](evidence/first-launch-silent/results.json)。未升级 Program Files 安装。

## 实际验证

| 场景 | 结果 |
|---|---|
| 初次真实 GET 尚未完成 | 在重新加载生产 renderer 前安装 DOM 挂载观察器，把初次入口 GET 持有 700ms，随后 `route.continue()` 请求真实后端；没有替换响应内容。期间 dialog 数量为 0、首次提示挂载次数为 0，偏好标记仍为 false，IFEO POST 为 0。 |
| 放行真实其他目录状态 | 独立 HKLM64 与生产 CsWin32 GET 均确认入口属于安装目录，测试便携目录为 conflict。放行原请求后只挂载一次首次提示，并提供“前往设置接管现有入口”。 |
| 前往设置 | 只保存用户选择；真实 preferences API 与 JSON.NET `preferences.json` 的标记均为 true，没有 IFEO POST。设置继续显示实际其他目录命令及明确接管入口。 |
| 既有接管回归 | 原保护请求、确认文案、暂不初始焦点、Tab/Escape、1380×860、960×640、实际125%继续通过；无 renderer 错误。 |
| 清理 | 最终 integrationPosts=0、globalRegistryUnchanged=true；专用 Host/Electron 创建身份退出，15333/19333 无监听残留，只清理测试实例。 |
| F09 当前目录 enabled:true | **未执行**：当前 HKLM 入口指向 `C:\Program Files\BlueDomainTaskManager`，测试目录不同；没有改写用户全局入口来制造 enabled 状态。因此静默自动保存、重启继续不弹、该分支真实保存失败反馈均未取得成功链证据。 |

真实未知/读取失败/旧接管状态的本轮成功分支也未制造；本次仅对真实冲突状态确认不会被误认成已启用。此前接管成功/过滤写入等未执行范围继续见 [接管验收记录](TASK-MANAGER-TAKEOVER-RESULTS.md)。

## 复现

`npm --prefix src/TaskManager.Web run build` 生成生产资源；`node --check tests/task-manager-takeover.mjs` 通过。更新独立便携目录后启动标准 UAC worker；实际提升令牌由原生探针核验：

```powershell
Copy-Item -LiteralPath E:\taskManager\src\TaskManager.Host\wwwroot\index.html -Destination E:\taskManager\output\takeover\TaskManager-win-x64\wwwroot -Force
Get-ChildItem -LiteralPath E:\taskManager\src\TaskManager.Host\wwwroot\assets -Force | Copy-Item -Destination E:\taskManager\output\takeover\TaskManager-win-x64\wwwroot\assets -Force
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\task-manager-takeover-worker.ps1" -ReleaseDirectory "E:\taskManager\output\takeover\TaskManager-win-x64"'
```

运行结果输出到 `output/playwright/task-manager-takeover`；归档包括 [40项结果](evidence/first-launch-silent/results.json)及 [实际提升 worker 状态](evidence/first-launch-silent/worker-status.json)。截图使用完整 CDP compositor/专用 HWND 方式，已视觉查看读取期间、冲突提示及设置页面：

- [初次读取期间无提示](evidence/first-launch-silent/first-launch-reading-no-dialog.png)
- [真实冲突读取后提示](evidence/first-launch-silent/first-launch-existing-entry.png)
- [前往设置后的实际入口](evidence/first-launch-silent/settings-existing-entry.png)
- [125%确认框回归](evidence/first-launch-silent/takeover-confirmation-125-native.png)
