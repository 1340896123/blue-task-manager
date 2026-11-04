# 界面占位与真实链路接入

日期：2026-11-04。当前 Qt 6 Widgets + .NET 10 / CsWin32 / JSON.NET；只使用既有回环 HTTP/JSON。用户要求先统一盘点、再按三个子代理顺序完成，阶段完成检查后各自提交。现有工作区修改保留。

## 统一盘点与分工

| 页面 / 范围 | 现场与缺口 | 接入及负责阶段 |
|---|---|---|
| 进程 / 详细信息 | CPU、内存、I/O、身份、路径、账户和操作已有真实链；现场727条进程网络全部null，原因是系统ETW logger资源不足（1450） | 阶段1：经典系统logger无法创建时，采用标准Microsoft-Windows-Kernel-Network manifest provider的普通实时ETW session，保留身份、完整窗口、丢事件和本实例清理 |
| 进程网格 / 详情 | 合成I/O列没有对应原因字段；详情未展示已采集累计CPU/I/O、会话、优先级、GPU引擎和独立来源时间 | 阶段2：接入已有字段、补充原因与真实采样时间；不把未知填0 |
| 性能 | CPU/内存、5块真实磁盘、2个GPU均实采可用；内存部分字段和逐CPU失败未完整传到可见状态；无设备时缺少直接原因 | 阶段2：补齐来源错误反馈和无设备说明，保留曲线真实缺口 |
| HWiNFO | 现场共享内存不存在（Win32 2），不是空界面或未实现采集器 | 阶段2：保留外部来源状态和恢复说明；不安装/修改第三方软件，不以其它温度或假值替代 |
| 应用历史记录 | 累计CPU/I/O实际可用；已退出条目“详情”只尝试定位存活进程 | 阶段2：真实观测详情与存活定位分开；已退出历史仍可查看 |
| 启动应用 | 真实来源与状态已有链；2条RunOnce无法应用StartupApproved规则，必须保留未知；来源路径缺失时需解释 | 阶段2：展示实际原因和来源位置能力，继续官方启动设置入口 |
| 用户 | 字段映射后原因仍使用原键；查看会话进程不清除旧搜索/筛选，导致入口返回空列表 | 阶段2：修正映射与定位筛选状态，保留实际WTS和账户聚合 |
| 服务 | 详情/SCM启停重启/进程关联已接入，权限和状态限制不是占位 | 阶段2复核现有入口；阶段3仅用专用测试资源验证，不控制用户服务 |
| 设置 | 采样、偏好、置顶、入口和更新都有真实API | 阶段2统一状态反馈；阶段3运行真实API和Qt验收 |

现场GPU无引擎的670条进程不是“0%”：Windows没有报告该身份的引擎，继续noObservedEngine；受保护进程的accessDenied、首帧warmingUp、进程退出exited以及文件缺少CompanyName都保留明确状态。

## 成熟来源与标准接口

实施前实际读取 Microsoft krabsetw 固定提交 [d626e49c41a2a324ad8b433b186703aa551373b3 的 user_trace_001.cpp](https://github.com/microsoft/krabsetw/blob/d626e49c41a2a324ad8b433b186703aa551373b3/examples/NativeExamples/user_trace_001.cpp) 和 provider.hpp：普通实时session按provider GUID/keywords启用、后台消费EVENT_RECORD、独立schema识别。借鉴公开ETW模式，继续CsWin32，不增加库或复制源码。

实际读取 [repnz ETW manifest 固定提交 d5f68e8acda5da154ab44e405b610dd8c2ba1164](https://github.com/repnz/etw-providers-docs/blob/d5f68e8acda5da154ab44e405b610dd8c2ba1164/Manifests-Win10-18990/Microsoft-Windows-Kernel-Network.xml)，并以本机 `wevtutil gp Microsoft-Windows-Kernel-Network /ge:true /f:xml` 独立核对。GUID为7dd42a49-5329-4832-8dfd-43d979153a88，keywords IPv4=0x10/IPv6=0x20；v0的event ID 10/11、26/27、42/43、58/59分别为TCP4、TCP6、UDP4、UDP6收发，payload首部为UInt32 PID/size。manifest event ID、task和opcode联合校验，不把manifest版本0与classic版本2混用，不使用event header PID归属流量。

经典系统logger仅在创建返回1450时尝试普通实时session；启用provider失败时仍返回真实错误。永不接管/停止其他采集器，不更改Windows ETW全局设置。沿用唯一所有者、bounded队列、时间与丢事件检查、同消费者marker完整窗口和创建身份保护。

阶段3补充：普通manifest回退会话必须同时使用公开的`EVENT_TRACE_INDEPENDENT_SESSION_MODE`。最终验收现场在普通模式下出现持续eventsLost，独立Windows logman短采也复现；采用Microsoft PerfView的标准独立会话配置后，实际四协议事件恢复且零丢失。成熟来源为[TraceEventSession.cs，9f071a5cb2e70cdfee13abc510344ab1e8840c90](https://github.com/microsoft/perfview/blob/9f071a5cb2e70cdfee13abc510344ab1e8840c90/src/TraceEvent/TraceEventSession.cs)的GetProperties；常量由CsWin32生成，不扩展协议或改系统全局日志设置。新增两个本轮自有采集器并发接收真实流量、停止其中一个不影响另一个的隔离验收。

Qt阶段实施前实际读取 [Qt 6.8.3 QAbstractItemModel，c07c2d5a527a644d36e7853d55132ae38921682f](https://github.com/qt/qtbase/blob/c07c2d5a527a644d36e7853d55132ae38921682f/src/corelib/itemmodels/qabstractitemmodel.cpp)，核对DisplayRole与ToolTipRole等独立数据角色。采用精确字段原因及可访问说明，不把整行无关错误当作某个有效单元格的失败。ui-ux-pro-max定向 `error feedback recovery --domain ux` 命中Error Recovery与Error Feedback：就近显示错误及恢复步骤；沿用现有MASTER与Qt公共控件，不新增视觉系统或数据协议。

Qt接线同时规定：查询按页保存和恢复，新页为空；旧版全局搜索仅向初始进程页迁移。会话定位清除旧查询/账户/状态筛选并保存新会话过滤；历史详情读取真实已观测记录，已退出身份仍可查看，只有存活身份提供再次核验的定位入口。进程详情显示累计CPU/I/O、优先级/会话/父PID、网络/GPU独立来源时间与关联服务来源错误，失败保留最后有效观测。性能独立呈现可见字段/逻辑CPU失败及缺少设备的实际状态；HWiNFO缺失提供传感器模式/共享内存及许可检查步骤。

GPU资源磁贴使用已匹配的真实description及软件适配器标志；缺少描述时用完整LUID/phys身份。`physicalIndex`不是Windows GPU编号，不再把不同适配器一律标为“GPU 0”。完整名称保留在原生列表文本与tooltip中。

## 验收

| ID | 必验 |
|---|---|
| UI01 | Release .NET/Qt构建；三个阶段各自提交，保留原工作区改动 |
| UI02 | 同一现场原经典logger1450；回退后真实进程网络恢复非null，source表明实际provider模式 |
| UI03 | 专用真实TCP/UDP IPv4/可用IPv6套接字收发有正速率，独立空闲进程和停止后完整窗口为真实0；未知/暂停/过期/身份变化保持保护 |
| UI04 | 全部8页字段、按钮、菜单、别名原因与跨页定位接通；历史退出行可查看已有观测 |
| UI05 | 1080P Qt控件与实际桌面检查；截图/命令/退出码/SHA记录；不以静态源码替代运行证据 |
| UI06 | 运行现行ACCEPTANCE要求的相关原生链路，清理仅本次创建的进程/session/临时资源；未执行环境分支单独记录 |

本轮结果将追加独立报告；历史结果不作为本轮通过证据。
