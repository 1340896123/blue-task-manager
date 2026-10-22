# GPU友好身份阶段交接

本阶段补齐“看到GPU后能辨认是哪一项”的日常闭环。性能页选择器与标题使用Windows DXGI真实Description，原始LUID仍在次级文本及详情中显示；DXGI软件标志如实标注。本机真实结果为 **NVIDIA GeForce RTX 3080 Ti** 与 **Microsoft Basic Render Driver（软件适配器）**，后者不是第二块实体显卡。没有猜测Windows GPU编号或添加实时显存指标。

原生链为CsWin32生成的CreateDXGIFactory1→IDXGIFactory1.EnumAdapters1→IDXGIAdapter1.GetDesc1。PDH `luid_0xHIGH_0xLOW`与DXGI AdapterLuid的完整高低位按原始UInt32位匹配，包含HighPart为负的位模式；不按枚举顺序或厂商名称关联。公开设计参考TaskExplorer WinGpuMonitor及微软DXGI说明，记录于RESEARCH.md。

GpuAdapterDto新增`description:string|null,isSoftware:boolean|null,identityCapturedAtUtc:UTC|null`；各字段不可用原因在原有unavailable，metadata失败不使PDH利用率失效。新增`gpuIdentity` capability。读取位于既有惰性PDH worker，基本health/首快照/settings不等DXGI；最多256身份，成功缓存5分钟，新未知LUID最多每10秒重枚举。每次创建的新factory与adapter均在finally ReleaseComObject，不跨帧保留COM对象；发布后只替换DTO引用。设备热插拔路径保留标准的有限刷新，未在本机执行物理插拔试验。

UI复用MASTER现有body/caption/subtitle、text-secondary与资源列表尺寸Token，长名称按既有规则换行，无新硬编码颜色/字号。Description不可读时回退LUID并显示原因。GPU/磁盘曲线独立时间轴、最忙引擎与资源数值算法保持原合同；身份时间不是GPU采样时间。系统GPU合计仍为所有PDH适配器中最忙引擎，包含软件适配器，文案已明确“最忙适配器引擎（含软件适配器）”。physicalIndex只保留公开PDH phys字段，不据此认定实体硬件或Windows编号。

复现顺序：

```powershell
npm --prefix src/TaskManager.Web run build
dotnet build src/TaskManager.Host -c Release -p:ElectronSkipExecCommands=true --no-restore
dotnet build tests/TaskManager.WindowProbe -c Release --no-restore
node tests/gpu-identity-v11.mjs
node tests/process-ui-v11.mjs
node tests/pages-ui-v11.mjs
$env:TASKMANAGER_TELEMETRY_REPORT='output/performance/gpu-identity-review.json'
node tests/native-telemetry-v11.mjs
```

Web类型检查/生产构建与Host、独立探针Release均0警告/错误。身份专项12断言通过：独立DXGI探针按分开的原始高/低位逐项核对LUID/描述/软件标志，Windows CIM视频控制器名称对照非软件项，真实Chromium逐项选择名称/次级LUID；跨3个PDH样本身份时间不变，证明稳定来源复用缓存；960px无溢出且无运行错误。最新专项首快照约716ms。CIM和独立探针仅为验收，不给应用供数。原进程UI30、页面UI50断言通过，页面旧“只LUID”断言已改为“真实名称且保留LUID”。专项结果归档`docs/evidence/gpu-identity-v11.json`，截图`output/playwright/v11-gpu-identity.png`及`v11-gpu-identity-960.png`。

原生遥测24断言回归通过，采样算法未改变；30.14秒、992～998进程、27基本/27PDH样本，基本采样p95 65.78ms，PDH query p95 1.57ms，Host整机平均CPU0.361%、private65.3MB、句柄644→647。该轮基本就绪507ms；是无renderer与实际I/O负载下的短期读数，不等同完整Electron资源。测量记录`output/performance/gpu-identity-review.json`；保留此前满120点主机测量供最终阶段比较。

完整Electron E2E、最终Release与整体应用性能仍由下一阶段负责，不把浏览器检查等同桌面生命周期验收。既有服务成功链的标准用户权限限制未变化。
