# 启动应用真实启用状态（2026-10-28）

## 2026-10-31 来源隔离修复与本轮验证范围

当前纯 C# 生产读取未复现截图的全列未知：初次读取21项，8项启用、13项禁用，无未知和来源警告；截图可能来自旧构建，不能把此推测作为已证实根因。另已确证现实现会将另一 hive 的同名禁用/异常记录错误施加到原始来源，造成不必要的“无法确定”。本轮按原始 hive + Run/Run32/StartupFolder + 原始值名独立读取，取消跨 hive 比较。后文原“同名状态冲突即未知”的合同由本节覆盖。

已实际在线读取 BCUninstaller 固定提交 [30da609384c98ba6e35c6530129541ea4ff3970a 的 NewStartupDisable.cs](https://github.com/BCUninstaller/Bulk-Crap-Uninstaller/blob/30da609384c98ba6e35c6530129541ea4ff3970a/source/UninstallTools/Startup/Normal/NewStartupDisable.cs) 和同目录 StartupEntryFactory.cs，核验其按来源匹配 hive/Run32/StartupFolder 的方式。其“非02都禁用”的宽泛解析、读取时创建键和状态写入均未采用；本项目继续最小只读权限，未知状态1/6/7和损坏记录保留 null。本轮没有取得可扩展这些状态语义的足够证据。

用户明确要求本轮只在 C# 测试、不打开 UI：S06 和 S07 的浏览器/Electron/截图部分不执行，不能引用历史截图冒充本轮验证。新增 `tests/Takao.StartupTests`，实际读取生产 CsWin32 采集链；只读的真实跨 hive 场景在旧代码上失败、修复后通过；GUID 专用 Run/RunOnce/Startup 文件及批准值在 finally 中校验身份后精确清理。具体命令、结果与未执行边界见 [C#验收记录](STARTUP-STATUS-CSHARP-RESULTS.md)。

## 问题与范围

截图中整列“无法确定”的直接原因是两个后端来源均固定返回 `enabled:null`，前端状态单元格也固定写“无法确定”；已有接口与真实 Windows 状态未接通。此次仅接入既有 Run / RunOnce / Startup 文件夹条目的**只读状态**，不新增启动项写入、执行命令、驱动或私有协议。更改启动状态继续打开现有官方 `ms-settings:startupapps`。

此增量覆盖 PRD/API 历史“所有启动状态都未知”的约束；B13“不猜测/改写 StartupApproved”的约束继续适用，只增加有来源的只读解释。禁止把注册项存在当作 Windows 最终执行保证。

## 研究与证据边界

- 成熟项目参考入口：[TaskExplorer](https://github.com/DavidXanatos/TaskExplorer)、[Bulk Crap Uninstaller](https://github.com/Klocman/Bulk-Crap-Uninstaller)。本轮尝试 GitHub API / raw 的 PowerShell、curl Schannel 与 Node fetch 均因 TLS 或连接超时失败；DNS 返回 `198.18.x.x`，指定正常源地址仍超时，显式本地代理也连接重置。没有成功读取本轮固定提交源码，因此不能声称已在线核验具体枚举或优先级实现，不复制其源码。
- 公开 Windows 来源：[Run and RunOnce Registry Keys](https://learn.microsoft.com/windows/win32/setupapi/run-and-runonce-registry-keys)、[Registry Keys Affected by WOW64](https://learn.microsoft.com/windows/win32/winprog64/shared-registry-keys)、[RegQueryValueExW](https://learn.microsoft.com/windows/win32/api/winreg/nf-winreg-regqueryvalueexw)。本轮在线抓取同样失败；这些是公开接口参考链接，不是 StartupApproved 二进制格式的微软正式保证。
- StartupApproved 是 Windows Shell 的实际状态记录，二进制格式不是本文自行定义的协议。初始实现仅解释已知、可独立对照的显式状态；未知/损坏/权限失败保留 null，不按奇偶或“非3即启用”推断。
- 本机只读预检：HKCU Run 有状态 DWORD 1/2/3，HKCU StartupFolder 有3，HKLM Run / Run32 有2/3；各观测值长度12。状态1必须保留未知；没有6/7样本，不把 parser 单测当作 Windows 对其语义的证明。研究限制、实机对照、实际支持的枚举及未执行分支写入验收结果。

## 来源匹配与状态合同

所有读取均通过 CsWin32 生成的 RegOpenKeyEx / RegQueryValueEx 等 API；C# DTO 与响应继续使用 JSON.NET。使用 `KEY_QUERY_VALUE`，不申请写权限。状态读取放入已有慢速来源后台任务，不增加每帧阻塞。

| 原始条目 | 读取的 Approval 来源 |
|---|---|
| 当前用户 HKCU Run（32/64共享视图） | HKCU `Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run`，64位 Shell 视图，原始 value name |
| 本机 HKLM Run / Registry64 | HKLM 同根 `Explorer\StartupApproved\Run`，64位 Shell 视图，原始 value name |
| 本机 HKLM Run / Registry32 | HKLM 同根 `Explorer\StartupApproved\Run32`，**仍为64位 Shell 视图**，原始 value name |
| 当前用户 Startup 文件夹文件 | HKCU 同根 `Explorer\StartupApproved\StartupFolder`，原始文件名含扩展名 |
| 公共 Startup 文件夹文件 | HKLM 同根 `Explorer\StartupApproved\StartupFolder`，原始文件名含扩展名 |
| HKCU/HKLM RunOnce | 不套用同名 Run 的 Approval；明确一次性来源不适用或无法确定 |

HKCU Run 的共享视图可去重；HKLM32/64 是独立来源，即使名称和命令相同也不能合并为一项。行 ID 应保留独立真实来源，前端选中、图标与打开位置继续按该 ID 工作。不得只按显示名称匹配；Startup 文件夹必须用实际文件名而非去掉 `.lnk` 的显示名称。

本轮将旧占位类型 `enabled:string|null` 收敛为 `enabled:boolean|null`，前后端同步更新，保留字段名与 `enabledReason:string` 原因字段：

| 实际读取结果 | enabled / 展示 | 原因要求 |
|---|---|---|
| REG_BINARY、恰12字节、首DWORD为2 | true / 已启用 | 指出实际 StartupApproved 来源和状态；不承诺程序一定成功运行 |
| REG_BINARY、恰12字节、首DWORD为3 | false / 已禁用 | 指出 Windows 实际禁用记录；不能因 Run 值仍存在而显示启用 |
| 其他DWORD（含本机1）、类型或长度异常 | null / 无法确定 | 指出未知状态/格式，保留原生错误或状态码；不按奇偶猜测 |
| Run/Startup的Approval键或值不存在 | true / 已启用 | reason明确“未设置Windows禁用记录，按默认启用，不代表执行成功”；不能称为显式批准；RunOnce不得套此逻辑 |
| HKCU/HKLM存在同名项 | 按原始来源独立判断 | hive属于条目身份，另一范围同名记录不参与该项状态判断；Run32/StartupFolder同样按来源隔离 |
| 访问拒绝、读取异常、超限 | null / 无法确定 | 区分权限、来源失败、容量，不伪装为缺失或已启用；不使其他条目失败 |

若实施前获得了6/7或同名优先级的成熟源码与真实 Windows 核验，可补充此表及研究链接；没有证据则保留未知。最多读取固定小缓冲（已知记录12字节），异常更大值明确格式/容量问题；处理读取期间大小变化，释放注册表句柄。

## 界面

保持既有 Windows 蓝色布局和 MASTER Token；不新增视觉体系。状态单元格消费 `enabled`，以中文文字显示已启用/已禁用/无法确定；可获取 `enabledReason`，不只用颜色。支持状态列真实排序，未知不被当作 false/0。详情中的状态同样消费该字段；默认启用、一次性来源和真实失败可读，保留官方启动设置入口。

## S系列验收

| ID | 验收与证据 |
|---|---|
| S01 | 前端 typecheck / 生产 build、C# Release build、静态无 System.Text.Json / 手写DllImport / 业务mock；记录命令与结果 |
| S02 | 对本机真实已有项只读对照 Registry64 / Registry32 与相应 Approval 的 DWORD/类型/长度；API和UI与独立读取一致，不提交完整用户启动清单 |
| S03 | 使用 GUID 命名的**本次专用** HKCU Run/Approval 值，覆盖2、3、未知1、损坏类型/长度、缺失；不运行该命令，finally只删除本次已成功创建且身份匹配的值 |
| S04 | 使用 GUID 命名的本次专用 Startup 文件（或真实Shell快捷方式）与对应含扩展名 Approval 值，覆盖真实启用/禁用；不执行文件，不操作现有用户项，finally精确清理 |
| S05 | 专用 RunOnce 条目不受同名 Run Approval误判；HKCU共享视图无重复，HKLM32/64身份/映射通过只读对照；管理员专用HKLM写入若未执行必须明示 |
| S06 | 真实生产浏览器与正式 Electron 打开启动页，中文状态、原因、状态排序、详情、官方设置入口与搜索不回归；1380×860、960×640、实际125%截图保存应用区域 |
| S07 | 回归 docs/ACCEPTANCE.md 必验适用项，真实进程/API/页面和正式Electron启动关闭；仅清理本次专用进程/PID/文件/注册表值，端口无残留；权限拒绝/6与7/策略冲突等未触发分支如实记录 |

结果保存 `docs/STARTUP-STATUS-RESULTS.md`，包括日期、Git SHA、可重现命令、断言数、截图路径、研究/权限限制和清理证据。生产构建不能代替真实 E2E；既有源读取失败不能被测试fixture伪造成功。
