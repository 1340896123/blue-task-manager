# 工程规范

## 1. 开发与提交

1. 先读 PRD、架构和设计 MASTER；发现实现约束时同步文档，不悄悄改产品语义。
2. 顺序阶段由不同子代理承担；上一阶段验收并 Git 提交后交接。主代理负责跨阶段最终验收与修复。
3. 提交使用可读前缀，例如 `docs: define product and native architecture`、`feat: implement real Win32 telemetry`、`feat: build Windows blue task manager UI`、`test: validate Electron desktop end to end`。一阶段可以有必要修复提交。
4. 不提交依赖目录、构建缓存、API令牌、用户配置、完整机器进程转储。截图/验收证据应裁切到应用，避免采集无关桌面信息。
5. 包版本固定并提交 lockfile；不使用浮动latest。先验证当前环境和官方成熟实现，再采用依赖。

## 2. C# 与原生边界

- Nullable启用；公开DTO使用明确单位/可空值；不要使用 `object` 字典装所有指标导致前端合同不可检查。
- Windows原生绑定由CsWin32生成并统一封装到Native/Windows采集层；业务控制器不声明手写DllImport或LibraryImport。
- `NativeMethods.txt` 仅列实际使用API；通过 `using`/finally 释放 SafeHandle、本地缓冲和WTS/IPHelper分配内存。异常不导致整个采样循环停止。
- 不以 Get-Process/PowerShell/tasklist/随机数/示例数组作为生产数据源；.NET可做时间、缓存、文件与加密工作，系统监控字段走约定的CsWin32链路。
- 默认最小访问权限。可读字段独立处理；访问拒绝、目标退出和尚未预热属于预期状态，不以通用异常覆盖其他行。
- JSON只使用JSON.NET；MVC注册AddNewtonsoftJson；检查所有API返回路径。避免隐式Results.Json或默认Minimal API序列化。
- 运算先检测计数器回退和身份变化；负速率不截断伪装成零。采样间隔用单调时间差；UTC只做展示标记。
- 静态元数据（路径、发布者、用户）缓存按身份管理；不每秒反复验证所有签名。未知发布者显示“不可用”，不猜测Microsoft等名称。

## 3. React 与 Tailwind

- TypeScript strict；DTO由集中类型文件维护；数据访问集中到client模块；组件不写散落fetch重试循环。
- 所有业务数据来自HTTP真链路；不提供DEMO模式、fallbackProcesses、随机图表、硬编码CPU/内存数值。开发空态与骨架只能表达“尚未读取”，不能伪装监控内容。
- 样式颜色/尺寸/动效以 `design-system/task-manager/MASTER.md` 的语义Token为源，映射Tailwind，不在页面任意增加Hex色值。系统蓝色不是每个面都铺饱和蓝色。
- 交互优先原生button/input/select/table/dialog语义。图标按钮有中文可访问名称；装饰SVG aria-hidden；输入有可见或sr-only label。
- 数值显示 `font-variant-numeric: tabular-nums`；未知用“—”并有原因，不对null用`|| 0`、`?? 0`填充。
- 轮询取消旧请求/卸载请求；串行或锁定进行中的请求，前台断线保留最后快照并标过期。排序只影响视图，不更改原始采样。
- 选择使用进程身份，不使用数组下标/PID单独作React key；新采样保留当前选中身份，身份退出后取消危险操作。
- 搜索受控输入；排序按钮有aria-sort；无需每个秒级单元都aria-live。状态条简短宣告连接/失败变化。
- 标准桌面紧凑行高度32px，普通操作高度36px；导航和主要交互44px。桌面高密度是有意取舍，不套用手机大卡片/巨大间距。

## 4. 错误与状态

- 错误信息指出失败动作、可理解的Windows原因、重试入口。按钮忙时禁用防止重复提交，完成后明确反馈。
- 结束进程确认展示实时选中身份；用户取消不发送写请求；请求冲突后更新列表。
- 无权限字段与整机连接失败分别显示，不把无权限当成所有数据不支持。
- 没有实现的能力显示说明，不能提供可点击但无实际行为的“更多”“效率模式”“服务启停”等装饰按钮。

## 5. 验证纪律

- 使用生产采集器测试；只添加能验证资源计数差分、身份保护、权限边界的必要测试，避免逐行镜像实现的测试。
- 前端生产构建/类型检查、C# Release构建、真实API检查、浏览器UI E2E、Electron桌面启动关闭均必须完成。
- E2E进程由测试专门创建，记录PID+创建时间；清理仅处理这些目标；禁止按`node`、`dotnet`、`electron`、`powershell`名称批量杀进程。
- 原生错误记录code而不写敏感令牌；验收报告区分“通过”“失败”“明确不在本版范围”，不把未执行写成通过。
