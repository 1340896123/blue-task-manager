# 本机连接错误与窗口关闭修复

日期：2026-10-25。适用范围：截图中的“无法连接本机服务，请检查主机状态后重试。”及桌面断连后的窗口控制。

## 已确认问题与证据边界

- `src/TaskManager.Web/src/App.tsx` 的 `windowAction('close')` 先等待偏好保存，再经 `POST /api/window/close` 关闭。`api.ts` 默认请求超时为9秒；服务不可达时，此请求只能报错，无法关闭窗口。
- `src/TaskManager.Host/Services/DesktopWindowService.cs` 设置 `Frame = false`，标题栏按钮因此是主要可见关闭入口。关闭依赖采集主机 HTTP 的设计形成确定缺陷；窗口操作不能依赖采集服务健康状态。
- `api.ts` 的 `errorMessage` 把所有 JavaScript `TypeError` 都转换为本机断连提示，可能掩盖客户端程序错误。应只在实际 `fetch` 传输失败的边界分类网络故障。
- 当前截图不能证明服务退出、启动失败或拒绝请求的具体原因。排查时未发现运行中的本应用主机/Electron，不能据此反推截图时的现场原因。
- 通知已有本地关闭按钮和9秒自动清除，普通状态无需请求服务。原生模态框打开时框外控件不可操作，但截图未显示完整窗口，尚不能将此分支认定为本次原因。

## 最小修复合同

1. 桌面最小化、最大化/还原、关闭通过 Electron 官方 `contextBridge`、`ipcRenderer.invoke` 与 `ipcMain.handle` 调用对应 `BrowserWindow` 方法；只暴露这三个固定命令，不转发任意通道或业务操作。
2. 主进程验证发送者属于本应用窗口、主 frame 与受信任本机页面 origin；维持 sandbox、contextIsolation、禁用 nodeIntegration。仅控制发送者所属窗口。
3. 关闭前偏好尽力保存并设置短时上限；保存失败或本机 HTTP 中断不得阻止关闭。禁止不确定业务写操作自动重试。
4. 桌面原生入口在 bootstrap 尚未成功或已断连时仍可使用；普通浏览器继续明确禁用桌面窗口控制。
5. 错误消息保留后端明确原因；仅已分类的网络传输异常显示连接提示，客户端 `TypeError` 保留真实消息，不伪装网络故障。
6. 沿用现有样式与设计 Token，不引入业务假数据或新系统协议。普通通知关闭保持纯本地行为。

## 成熟实现参考

- [Electron 官方 IPC 教程](https://github.com/electron/electron/blob/main/docs/tutorial/ipc.md)：受限 `contextBridge` API、`ipcRenderer.invoke` 与 `ipcMain.handle`。
- [Electron 官方安全指南](https://github.com/electron/electron/blob/main/docs/tutorial/security.md)：验证 IPC sender、限制导航与 Node 权限。
- ElectronNET 现有 `WebPreferences.Preload` 与 `custom_main.js` 扩展点；不修改框架内部通信实现。

## 真实链路验收

- C# Release 构建、前端 TypeScript/生产构建及现有真实API回归。
- 正式 Electron 窗口连接真实采集主机，确认真实快照；最小化、最大化/还原与关闭有效，关闭后本测试创建的主机、Electron与端口退出。
- 只停止测试专用主机或阻断测试实例自己的 HTTP，保留真实 Electron；显示断连并保留最后数据，关闭按钮在短时上限内仍能退出，不再以连接错误代替关闭。
- 初始 API 不可达时窗口控制仍可用；非主 frame、错误 origin、未知命令拒绝，不给普通浏览器或任意页面窗口控制权限。
- 普通通知可用鼠标和键盘关闭；客户端类型错误与真实传输失败分类不同。保存可复现命令、结果和应用范围截图路径；未执行分支如实记录。
