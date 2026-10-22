# 1.1进程日常工作流阶段交接

日期：2026-10-23。此阶段实现React基础设施与进程工作流，未宣称整个B矩阵或最终Electron验收完成。其余页面仍保留真实v1内容，下一顺序UI阶段继续扩展。

## 组件与真实链路

- `App.tsx`只负责窗口外壳、每页搜索、导航、连接/冻结提示、运行/帮助入口。`ProcessWorkspace`负责平铺/虚拟树、筛选/列、稳定身份选择与菜单；`ProcessDetail`负责概览、慢服务来源及TCP/UDP按需读取；`ProcessActionDialog`负责五优先级、效率恢复、单进程与完整树确认/逐项结果；`RunDialog`负责真实CreateProcess与Electron原生exe浏览。
- `processModel.ts`按同快照父PID与创建FILETIME校验父子、循环保护、同层排序、祖先上下文搜索。数字查询为精确PID；平铺/树都使用TanStack Virtual稳定identity。树提供treegrid/aria-level/aria-expanded。折叠集合仅在当前页内存有界于当前观察身份，未伪造祖先。
- 菜单打开保存目标对象；后台刷新、排序、搜索不能换成另一行。当前有效选择来自当前筛选/展开结果，隐藏旧选择不能继续用工具栏结束。Enter/双击详情、Shift+F10/右键菜单、方向键/Home/End、树左右键、Ctrl+F/N、F5、Space冻结、Delete确认、F1帮助有可见等效入口；输入/按钮/链接/菜单/对话框不被普通快捷键劫持。
- 详情每2秒串行读取当前页签，隐藏时5秒；切页签/关闭取消请求，旧结果忽略。404/409或最新快照目标消失后保留最后信息与来源时间并禁写。CPU/I/O、原生详情、GPU与慢服务时间分别标明；服务来源早于进程创建时拒绝关联旧PID。跨服务导航回调带实际服务name。
- 单结束按`completed`显示已确认/尚未确认；完整树先展示全部实际成员，使用原样精确身份集合提交，保留terminated/failed/pending逐项结果。409提供“重新预览完整成员”。写操作忙时禁重复，未知结果不自动重试。
- 效率动作使用真实GET/POST；恢复判断依据`canRestore`，覆盖部分状态而不只`enabled`。失败可只读刷新实际状态再恢复。启用说明Windows节流+空闲优先级以及Host会话原基线边界：关闭本应用不会自动恢复目标，新会话无法恢复旧基线。优先级说明Windows状态持续性。
- 文件位置/属性只传身份与白名单动作。复制PID/路径/信息使用真实剪贴板。CSV导出全部筛选结果，树包含祖先上下文与折叠子节点；UTF-8 BOM、RFC4180、UTC时刻、bytes/bytes-sec/%单位、null空值及字段原因、公式前缀转义均实现。
- `ProcessIcon`仅虚拟可见行请求Shell PNG；并发2、5秒超时、离屏取消、待队列256、LRU Blob URL缓存256并回收、通用fallback及img onError。Host CSP仅`img-src`增加`blob:`，其他来源限制未放宽。浏览器验收实际检查PNG解码`complete && naturalWidth>0`与CSP控制台错误，修复了初版截图空白图标问题。

## 性能和偏好基础设施

- `types.ts`同步实际核心/资源/偏好DTO，保留精度字符串与null，不把GPU引擎或网络连接变成吞吐。
- `api.ts`把ETag当不透明字符串；304不解析JSON、不替换snapshot引用、视为连接成功。只读可取消，图标204→null/PNG→Blob。SCM/树客户端20秒，exe选择器无固定超时；只有执行前invalidToken拒绝可刷新令牌重送，不重试超时/不确定写。
- `useTelemetry`每客户端仅一个飞行snapshot请求；隐藏轮询至少5秒、可见立即补帧。连接健康、采样过期、采集暂停、显示冻结独立。冻结保留不可变显示引用，后台采样/历史继续。健康时钟只发布变化的stale布尔值，不因每秒时钟在304时重刷整棵App。
- `usePreferences`初读完成后才允许自动保存；读取失败可以临时本地操作但禁止默认覆盖服务器配置，并提供重新读取/显式恢复默认。StrictMode旧加载结果由effect本地alive隔离。600ms合并保存、写请求串行、防乱序、保存失败可见且不自动循环重试；只POST完整白名单UI，省略window以保留Host最新窗口偏好。窗口关闭前await flush。滚动记录更新ref并合并写盘，避免每像素滚动引发整个App重渲染。
- 每页query/sort/selection/scroll/filter/columns/resource字段框架具备；processes/details已接入完整交互，其他表格接入查询/排序/滚动，下一阶段补对应页选择/资源控件。密度/sidebar/tree持久化。`preferences.page(page)`与`updatePage(page, patch, publish=true)`可供下一页组件使用；滚动第三参数false为不发布React状态、仍保存偏好。
- 表头高度从MASTER `--height-table-header:58px`读取，CSS内容高度也修正，真实DOM与虚拟估算差<1px；行高同样读compact/standard Token。新增详情/菜单/列/字体尺寸写入MASTER，组件无新硬编码颜色/字号/阴影；旧CSS字号均映射语义Token。

## 下一阶段接入点与未完成范围

- `LegacyPages.tsx`是仍可用的原性能/历史/启动/用户/服务/设置页。应由下一页代理替换/提取，不能把服务页当前只读文案当1.1最终范围。真实GPU/磁盘/网卡选择图、服务详情启停、启动来源位置/官方入口、用户/历史跨页定位、窗口置顶与高级设置均待该阶段。
- `App.locateProcess(identity)`会继续最新显示并定位精确身份；`showService(name)`设置服务页query/selectedIdentity。可把这些回调传入新的页面组件；当前进程详情→服务已接后者。服务→进程必须先fresh服务详情并核对进程创建时间，不只按慢列表PID。
- 全局搜索label仍为`搜索名称、发布者、路径或 PID`，table标签为`进程列表`/`进程详细信息`（树模式role=treegrid），行`data-row-key=identity`；More按钮`更多进程操作`，菜单项`设置优先级`/`效率模式`/`结束进程树…`；详情tabs `概览`/`关联服务`/`TCP / UDP`；Run参数label为`参数（可选）`。
- 旧`tests/ui-smoke.mjs`与desktop/recovery脚本需要最终阶段更新：工具栏“暂停采样”已按PRD改为“冻结显示”，真实采集暂停仍在设置“自动采样”switch；GPU不再恒null、详情不再是选中时静态快照。最终不能为了通过旧断言倒退真实行为。
- 本阶段headless原生浏览按钮明确禁用。Electron原生选择器成功/取消、Shell真实窗口、125%缩放、窗口生命周期、持久化Host重启、最终>=30秒完整Electron性能仍需最后阶段验收；标准用户SCM成功链仍受权限限制，保持原生交接的真实限制。

## 可复现阶段检查

```powershell
npm --prefix src/TaskManager.Web run typecheck
npm --prefix src/TaskManager.Web run build
dotnet build src/TaskManager.Host -c Release -p:ElectronSkipExecCommands=true --no-restore
node tests/process-ui-v11.mjs
```

脚本使用5193（可由`TASKMANAGER_PROCESS_UI_PORT`覆盖），`TASKMANAGER_DATA_DIR`隔离临时配置；只创建/结束自己的真实TestWorkload身份。最近一次30项真实浏览器断言通过，包括树/祖先、采样继续、来源/端点、独立优先级读回、效率启用恢复、单/树退出结果、退出详情保留禁写、Run恢复和actual身份定位、CSV/304/密度恢复、图标解码/CSP、960px无页面溢出与0运行时异常。

结果：`output/playwright/process-ui-v11-results.json`；人工检查截图`v11-process-tree.png`（1380×860）与`v11-process-960.png`（960×640）。最终UI build与Host Release均0错误/警告；这些是本阶段浏览器工作流检查，不替代最终Electron或性能结果。
