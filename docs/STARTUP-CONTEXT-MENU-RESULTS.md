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

依赖按现有锁文件恢复，新增43个本地依赖包，锁文件无修改。首次类型检查因工作区未安装 `tsc` 失败，恢复后类型检查和生产构建均 exit 0；Vite 8.3.5 编译1967模块，生成 `index-DiZsPTOb.js`（423.51 kB）及 `index-CXxhIduX.css`（57.16 kB），输出位于 Host 的 `wwwroot`。`git diff --check` 无空白错误。

用户要求仅 C# 测试，本阶段没有运行 JavaScript 测试、浏览器、Electron、Windows 设置或 UI 自动化，没有截图。类型/生产编译和上述源码审查不构成菜单运行时验收；状态修复的66个 C# 断言及真实来源/清理证据单列在 [C#验收记录](STARTUP-STATUS-CSHARP-RESULTS.md)。

## 最终主机编译与交付

状态修复提交 `f7c43b5`、菜单提交 `c590cfa` 完成后，主代理审查了两阶段变更，并执行：

```powershell
dotnet 'C:\Program Files\dotnet\sdk\10.0.403\dotnet.dll' build src/Takao.Host -c Release --no-restore -p:ElectronSkipExecCommands=true
git diff d2d78c8..HEAD --check
```

主机 Release 编译 exit 0，0警告、0错误。显式使用已安装 SDK，不修改 global.json。新菜单资源已复制到 `src/Takao.Host/bin/Release/net10.0-windows10.0.19041.0/wwwroot`；源资源与 Release 的 index.html 和菜单 JS 散列分别完全一致。没有启动主机或 UI，没有执行安装、替换已安装应用或运行启动项命令。

| Release 文件 | SHA256 |
|---|---|
| `Takao.Host.dll` | `E25C3F5361FA41971BC1768929562B6690E164EFCD3002D117E538FB460BBE84` |
| `wwwroot/index.html` | `8FDCC4C466D7EA8B736C553621014194F8C21EBC72A97E3268C29304CD9B51A3` |
| `wwwroot/assets/index-DiZsPTOb.js` | `265EC1BA21B5B8EAAF79E750232BAEA11249A6ED1BB1FB767C931BC31DE0AA88` |

当前生产源直接读取已明确区分启用/禁用；截图的整列未知未在当前源码复现。本轮确证并修复跨 hive 同名批准记录引起的错误未知，不声称截图对应的旧安装构建已经更新。用户要求跳过的 UI 运行时行为保持未验证。
