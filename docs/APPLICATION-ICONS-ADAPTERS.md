# 全页应用图标与真实网络适配器

2026-10-24 用户增量：所有页面中的应用列表显示对应真实图标；性能页仅显示真实硬件网络适配器。覆盖进程/详细信息/用户子进程、历史（含退出身份）、启动应用、服务及关联详情；用户账号行使用用户语义图标。复用MASTER图标尺寸，不猜品牌、不改变行高。

## HTTP与原生合同

新增只读 `GET /api/application-icon?kind={process|history|startup|service}&id={encodeURIComponent(行标识)}`：process/history用现有identity，startup用现有id，service用SCM name。服务端仅解析当前快照中已观测的行，不接收客户端路径/AUMID/图标索引、不执行命令。200返回image/png；无法提取、路径丢失、无权限或来源已离开快照返回204；非法kind/id返回400；Shell暂不可用沿用503。前端限并发/有界缓存，204/loading/临时失败需受控重试。旧活进程icon接口保留原同句柄身份重验和409/404语义。

- 普通进程：SHGetFileInfo从真实exe取图标。打包应用：后台metadata worker核验创建身份后读GetApplicationUserModelId；有真实EXE嵌入资源时优先用ExtractIconEx小图标，与Windows文件属性来源一致。无资源/提取失败才用AppsFolder + IShellItemImageFactory和标准ICONBACKGROUND，最后保留文件Shell回退。不能仅凭SHGetFileInfo返回非空判断存在嵌入资源，它会为无资源EXE返回通用图标。
- 历史：内存按身份保留首次可用路径/AUMID，与既有最多20,000行同生命周期；JSON.NET JsonIgnore不导出，无前端DTO变化。退出后首次打开历史仍可取图标，不重用PID。
- 启动：可信Startup来源文件优先；.lnk由Shell解析目标/自定义图标。Run/RunOnce只读命令按环境变量、引号、CreateProcess文档中的未引号前缀顺序解析，最多32候选，裸exe用Windows SearchPath；不执行。
- 服务：仅对已观测的准确服务name以SERVICE_QUERY_CONFIG读取binary path，支持停止服务，不凭瞬时PID猜宿主；只读解析同上。
- 提取在独立有消息泵COM STA按需执行，采样器不提取。PNG最多512条、服务路径最多512条/15秒，失败或打包Shell暂时失败回退15秒后可重试；普通文件mtime变化失效。释放HICON/HBITMAP/COM对象，PNG不内嵌快照。

已核查CsWin32现有SHGetFileInfo便捷重载会正确传结构体字节长度，缺口主要为其他页静态AppWindow未接入真实接口。

2026-10-26修复：Codex打包应用的ICONONLY结果为白色透明底图形，非透明检查无法发现白底白图；采用[Microsoft GetImage的SIIGBF_ICONBACKGROUND](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitemimagefactory-getimage)让Shell按应用注册背景色绘制底板。I05增加白底合成像素检查及当前真实Codex进程行截图。测试端口可用TASKMANAGER_ICONS_PORT/TASKMANAGER_ICONS_CDP_PORT覆盖，以避开Windows排除端口范围，不改系统端口策略。

同日按用户Windows文件属性参考进一步校正来源：Codex的EXE本身已有透明底深灰图标，不应被AppsFolder蓝色底板覆盖。遵循TaskExplorer已有的SHGetFileInfo/ExtractIconEx原生提取思路，以[ExtractIconEx](https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-extracticonexw)单次提取确认真实资源，保持无资源MSIX的AppsFolder回退。I05同时对照独立EXE资源的opaque像素/alpha轮廓，并覆盖真实无资源MSIX；不按应用名称改色或画背景。

前端所有应用名称共用ApplicationIcon，固定沿用16px图标与21px占位；用户账号行仅用UserRound语义图标。图标靠近可见区域时订阅，离开区域/卸载后取消未完成读取，共用最多2个请求与严格512条LRU缓存。204和临时失败按15/30/60秒退避；成功后15秒复核一次以等待打包应用元数据，随后最多每60秒刷新可见图标，进程路径/启动命令变化立即换键。刷新失败保留上次成功图标，淘汰/替换/窗口卸载释放Blob URL，不依赖每帧采样触发请求。

## 网络口径

GetIfTable2入口仅保留HardwareInterface=true、FilterInterface=false、Type非SOFTWARE_LOOPBACK(24)/TUNNEL(131)，并排除OperStatus=IfOperStatusNotPresent的已移除设备。使用Windows/NDIS公开标志和存在状态，不按名称/MAC/厂商猜测。保留当前仍存在但断开的硬件及其真实状态和null速率/原因；可能包含Windows报告的隐藏硬件，不等同Get-NetAdapter默认可见设备集合。

networkAdapters、逐网卡性能历史和系统总量使用相同硬件集合；总量只汇总其中Up接口。无活动硬件为null并明确原因。活动硬件集合变化总量预热一帧，稳定单网卡继续差分；虚拟接口变化不影响硬件集合。LUID/UInt64字符串与未知不填零的合同不变。

## I系列真实验收

| ID | 场景与通过条件 |
|---|---|
| I01 | Host Release/Web类型与生产构建通过；JSON.NET/CsWin32/管理员边界不变 |
| I02 | 专用真实exe在进程/详情/详细信息/用户子进程图标可解码，与独立提取相符 |
| I03 | 专用进程退出后首次历史图标请求仍200；错误identity或清历史后204 |
| I04 | 真实启动快捷方式/Run命令、运行或停止服务返回真实PNG或有根据204；不执行命令/服务控制 |
| I05 | 有真实打包应用时检查文件资源优先和无资源AppsFolder回退；Codex/ChatGPT逐进程API图像与独立EXE资源的opaque像素/alpha轮廓一致，真实行解码/截图与Windows文件属性的透明底图标一致；无实例明确未覆盖，不伪造应用 |
| I06 | 独立WindowProbe network-counters全量对照：集合等于HardwareInterface&&!FilterInterface&&非24/131&&非NotPresent；当前存在的断开硬件保留，已移除硬件及虚拟/过滤/隧道/回环不出现 |
| I07 | 正式Electron全应用列表截图、滚动/切页与恢复：有界图标请求/缓存、无行高抖动；仅清理测试身份/本实例 |

API/桌面通过标准提升worker运行。结果/命令/截图写本轮验收报告，构建不替代真实链路。
