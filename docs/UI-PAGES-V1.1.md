# 1.1真实资源与管理页面阶段交接

日期：2026-10-23。本阶段承接进程工作流，完整Electron、实际125%桌面缩放、原生文件窗口和全应用性能仍由最终阶段验收；不把浏览器无溢出或Host构建等同于B矩阵全部通过。

## 已实现的页面闭环

- `PageWorkspace.tsx`替换旧`LegacyPages`，拆出`pages/Services.tsx`、`pages/Settings.tsx`和共享表格工具。沿用React、Tailwind、同源HTTP和既有CsWin32后端；无业务mock或新增通信协议。npm根与Web包/锁元数据同步1.1.0，依赖版本未升级。
- 性能页CPU/内存/网络/磁盘/GPU类别与资源选中项保存到page preferences。网卡默认优先真实`isHardware && IfOperStatusUp`，全部GetIfTable2接口仍可选，展示alias、description、LUID、状态、硬件/软件/回环/隧道标签；字节/秒速率与位/秒链路速度区分，收发双曲线及实/虚线图例同时存在。不把可能经过虚拟/过滤层的系统网络合计当物理网线。
- 磁盘显示真实PhysicalDisk非_Total实例、活动`100-idle`、读取与写入双曲线；整机活动明确为最忙物理磁盘，合计读写口径有说明。GPU只显示真实LUID/物理索引、最忙引擎、引擎数，不猜型号或Windows编号。GPU/磁盘历史按`telemetryCapturedAtUtc`去重，网络按基本`capturedAtUtc`；最多120点，null与超采样周期缺口拆线，不补0、不伪插历史。
- CPU展示逻辑核、进程/线程/句柄和系统运行时间；物理内存展示已用/可用/总量、系统提交/限制、缓存及真实来源语义。正常CPU/内存页只展示相关字段的缺失原因。设备消失暂时选择可用设备且提示，不自动把失踪资源偏好覆盖；各设备列表与图表内容独立滚动。
- 服务单击选择、双击/Enter/可见按钮进入同一个详情；展示真实配置命令、启动方式、账户、依赖、状态与读取时间。每项启动/停止/重启是否可用及原因可读，依权限和状态禁用。确认保存固定name/state/PID，客户端等待20秒、后端截止15秒；不重试不确定写，不停止依赖或提权。失败/超时保留最后结果和错误，禁用旧状态动作，显式重新读取后才能继续；成功依据actual completed和读回状态。
- 服务→进程先fresh GET服务详情，再在全局最新已知快照（非冻结数据）中找PID及创建早于服务观察的完整identity，最后GET进程详情重验。未采到/已退出则说明并要求刷新，不增加第二个snapshot轮询；进程→服务保留实际名称、查询和选择并揭示目标。
- 启动页提供来源、完整命令、真实可定位路径与不可定位原因；选中详情可复制完整命令，打开来源后端重读并核验。官方启动应用设置、当前用户Startup、公共Startup和服务管理器入口均可发现；仅打开固定Windows入口，不执行Run命令、不猜StartupApproved或自行改策略。
- 历史仍明确本次会话累计，运行状态按全局最新identity集合核对；已退出项禁定位，不按相同PID误跳。用户会话双击/按钮跳到实际sessionId筛选，当前用户仍用bootstrap实际账户；会话筛选改为真实会话选择器，避免其他会话被标为本应用会话。各页查询/排序/选择/滚动保留，显式重置视图可以在查询和排序已经默认时仍回顶。
- 设置区分真实采样暂停与列表冻结；冻结时性能曲线/采样继续，顶部横幅明确“列表显示已冻结”。采样速度、密度、进程树、侧栏、恢复界面偏好、快捷键帮助可发现。偏好恢复有确认，不改变采集历史、采样周期或窗口状态；坏偏好/读失败遵循既有hook，不默认覆盖服务器。置顶GET实际读回，窗口缩放动作后重查window-state；App快捷键变更也触发设置页重读。headless原生窗口动作明确禁用。

## 公共行为与Token

- 主搜索提示按当前页真实字段呈现，进程页保留名称/发布者/路径/PID，服务为name/display/PID，用户为用户名/会话名/ID，启动为名称/来源/命令，历史为名称/PID。数字进程/服务PID与会话ID是精确匹配。
- `DataTable`忽略子button/link/input/select等冒泡的Enter与双击，树按钮可执行本身行为，不误开行详情；新增显式`scrollResetKey`，避免滚动偏好回写形成受控回路。筛选期间树祖先保持展开，图标/aria-expanded/禁用说明一致；过滤隐藏旧选择时首行仍可键盘到达。
- `useTelemetry`变隐藏即取消尚未执行的前台timer并按至少5秒重排；已在flight请求完成后按隐藏周期继续。详情串行/取消过期，服务详情隐藏5秒、前台2秒；服务确认中停止轮询以固定对象。
- MASTER新增有意义的字体/尺寸/曲线/布局Token；旧结构间距、字重、边界、焦点、标题标记、输入/按钮高度、窗口控件、对话框和toast尺寸统一映射。颜色/字号/字重/阴影仅在root Token定义，所有业务组件不含独立色值；表头58px、紧凑/标准32/40px仍由同一Token供CSS与virtualizer读取。图表数学坐标、百分比与采样计算属于几何常数。

## 后端关联修复

`ProcessQueryService.Details`过去只按服务PID关联；现若进程创建时间晚于慢来源观察、创建不可确认或来源时间缺失，返回空`relatedServices`及`unavailable.relatedServices:stale`原因。`ProcessDetail`展示该原因；API.md已同步。此举是额外阻止旧PID与新创建身份错误关联，实际service→process仍使用fresh SCM状态和进程同句柄验证。

## 验证与后续

```powershell
npm run build
dotnet build src/TaskManager.Host -c Release -p:ElectronSkipExecCommands=true --no-restore
node tests/process-ui-v11.mjs
node tests/pages-ui-v11.mjs
```

必须先完成Web构建再构建Host，Vite会替换静态资源哈希，并行执行会使ASP.NET静态压缩清单引用已被删除的文件。最终上述顺序构建均0错误/0警告；进程工作流30断言、页面工作流49断言通过，均真实生产Host/采集/UI，无route mock。页面脚本只读服务配置/权限和定位，断言服务control请求0；当前非管理员，未以操作用户服务补成功覆盖。

页面测试新增树子按钮Enter/双击边界、原生资源默认选择与保留、真实服务配置/权限/关联身份、用户会话与历史跳转、启动命令剪贴板、headless禁用、真实冻结/暂停和JSON.NET偏好重载。960×640、1104×688、1380×860六页面无document级溢出；1104×688仅为缩小视口测试，不宣称真实125%Electron验收。JS异常0、CSP错误0。聚合检查列表在`docs/evidence/ui-pages-v11.json`，截图`output/playwright/v11-resources.png`、`v11-service-details.png`、`v11-settings-960.png`已查看。

最后阶段须重建完整portable Release，执行原A系列与B系列、真实Electron原生选择器/文件窗口/置顶/缩放与关闭清理、≥30秒整棵Electron/Host/renderer性能。服务成功启停/重启与部分生效仍受当前账户SCM创建权限error5限制，不能把禁用按钮/只读配置检查写成成功控制测试。
