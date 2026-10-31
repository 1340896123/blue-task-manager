# 启动应用右键菜单（2026-10-31）

## 范围与真实链路

启动应用列表的行右键、Shift+F10 和 ContextMenu 键打开同一操作菜单，提供“启动项详情”“打开来源位置”“复制启动命令”和“Windows 启动应用设置”。原有页头和更多菜单继续可用。状态仍为真实只读数据；管理状态通过现有官方 Windows 设置入口，不新增批准记录写入或启动命令执行。

菜单固定打开时的启动项 ID、来源和原始命令，位置操作显式接收该对象，不依赖随后变化的选中行。真实快照中项目消失或命令变化时关闭；搜索、排序、切页、外部滚动、窗口尺寸和缩放变化也关闭。后端继续按 ID 重读来源，使用既有 `startup/open-location` 与 `desktop/open-system-location` 同源接口。

打开位置与系统设置在离线或操作处理中禁用，位置不可可靠解析时显示已有来源原因。详情和复制使用已显示的真实记录，复制只写剪贴板。动作失败沿用现有文字反馈，不显示假成功。

## 交互与设计

复用现有 ContextMenu、DataTable 和 Windows 蓝色设计 Token。鼠标或键盘打开时聚焦触发行，再聚焦第一个可用菜单项；支持方向键、Home/End、Enter、Escape。Escape 关闭后归还触发行；点击菜单外部、切到其他窗口或其他控件已获得焦点时不抢回焦点。

参考已实际核验的 [Microsoft Fluent UI useMenu.tsx，固定提交 d2375b2452d9b8882ed63d4f4e91d8f537379e27](https://github.com/microsoft/fluentui/blob/d2375b2452d9b8882ed63d4f4e91d8f537379e27/packages/react-components/react-menu/library/src/components/Menu/useMenu.tsx)：上下文坐标、外部点击/滚动关闭、首次可用项焦点，以及只在焦点仍位于菜单或 body 时归还发起控件。只借鉴行为，不引入依赖或复制实现。

## 本轮验收边界

用户明确要求“只在 C# 进行测试，不打开 UI 测试”，优先于历史 S06/S07 和常规交付中的浏览器、Electron、截图要求。本轮只运行 C# 真实状态专项；前端只执行 TypeScript 类型检查和生产编译，并审查固定目标、禁用条件和键盘事件接线。没有打开 UI，也不把编译或源码审查记作菜单运行时验证。C# 结果见 [状态验收记录](STARTUP-STATUS-CSHARP-RESULTS.md)，菜单编译和审查记录见 [菜单交付记录](STARTUP-CONTEXT-MENU-RESULTS.md)。
