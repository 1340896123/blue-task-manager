# 启动应用右键菜单交付记录（2026-10-31）

已为启动应用行接入右键及 Shift+F10/ContextMenu 菜单：启动项详情、打开来源位置、复制启动命令、Windows 启动应用设置。启动页提取到 `src/Takao.Web/src/pages/Startup.tsx`，保留原有列、排序、列宽、选择、详情和更多菜单。所有位置动作沿用真实后端接口，未新增启动状态写入或命令执行。

源码审查确认：菜单保存目标 ID/来源/命令；打开来源显式传入目标并检查当前展示与最新快照；项目消失、命令变化、搜索/排序/缩放、切页和外部滚动会使菜单关闭。离线、处理请求和不可解析来源使用禁用语义与原因。共享 ContextMenu 增加外部滚动/窗口尺寸变化关闭及有条件焦点恢复，外部点击或窗口失焦不抢回焦点；键盘操作继续复用现有组件。

本阶段基线提交 `f7c43b5`。实际执行：

```powershell
npm --prefix src/Takao.Web ci --prefer-offline --no-audit --no-fund
npm run typecheck
npm run build
git diff --check
```

依赖按现有锁文件恢复，新增43个本地依赖包，锁文件无修改。首次类型检查因工作区未安装 `tsc` 失败，恢复后类型检查和生产构建均 exit 0；Vite 8.3.5 编译1967模块，生成 `index-DiZsPTOb.js`（423.51 kB）及 `index-CXxhIduX.css`（57.16 kB），输出位于 Host 的 `wwwroot`。`git diff --check` 无空白错误。最终 C# Host 编译由主代理在阶段提交后执行，不能将本记录当作已完成该步骤。

用户要求仅 C# 测试，本阶段没有运行 JavaScript 测试、浏览器、Electron、Windows 设置或 UI 自动化，没有截图。类型/生产编译和上述源码审查不构成菜单运行时验收；状态修复的66个 C# 断言及真实来源/清理证据单列在 [C#验收记录](STARTUP-STATUS-CSHARP-RESULTS.md)。
