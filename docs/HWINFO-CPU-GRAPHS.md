# HWiNFO 与 CPU 图表模式增量

日期：2026-10-24。用户要求接入 HWiNFO，并在性能 CPU 图表双击切换“总体使用率 / 逻辑处理器”。沿用 Windows 标准资源列表、JSON.NET、CsWin32 与真实采样。

## 范围与交互

- CPU 默认单张0～100%总体使用率图；双击主图区域切换为每个逻辑处理器一张0～100%小图，再双击切回。CPU磁贴保持总使用率。提供“总体使用率 / 逻辑处理器”原生选择控件作为可发现、键盘可操作的等效入口；更新同一持久偏好，不重启采集或清空历史。
- 小图以Windows处理器组及组内索引标识，不称为物理核。资源内容区内响应式排列并可纵向滚动，无页面横向溢出。未知、预热、断流保持缺口，不能复制总使用率作为逐CPU曲线。
- 性能资源列表增加一个“HWiNFO 传感器”入口，展示全部实际发布的传感器分组、原始/用户名称、读取项、单位、当前/最小/最大/平均值和来源时刻，支持共享搜索。不为该入口造无意义小曲线，不把全部传感器复制到120点历史。min/max/avg属于HWiNFO监控会话，不称为本应用历史。
- HWiNFO是可选只读来源，不自动安装/启动/配置，不附带其程序、驱动、图标或许可证，不绕过版本/许可限制。没有映射仅表示“未运行、未启用共享内存或当前版本/许可未提供”，不能断言未安装。不可用不影响原有Windows采样。
- CPU温度/功耗等以实际HWiNFO分组/读取项展示，不猜测名称与Windows逻辑处理器的对应，不以HWiNFO Usage替代Windows CPU利用率。

## 公开研究与读取边界

2026-10-24通过GitHub API核验固定版本：

| 来源 | 采用方式 |
|---|---|
| [Hwinfo.SharedMemory.Net README](https://github.com/Seraksab/Hwinfo.SharedMemory.Net/blob/1d09864597f4342e2466880399ad0872892f870a/README.md)、[布局](https://github.com/Seraksab/Hwinfo.SharedMemory.Net/blob/1d09864597f4342e2466880399ad0872892f870a/Hwinfo.SharedMemory/SharedMemoryElements.cs)、[读取器](https://github.com/Seraksab/Hwinfo.SharedMemory.Net/blob/1d09864597f4342e2466880399ad0872892f870a/Hwinfo.SharedMemory/SharedMemoryReader.cs)、[映射解析](https://github.com/Seraksab/Hwinfo.SharedMemory.Net/blob/1d09864597f4342e2466880399ad0872892f870a/Hwinfo.SharedMemory/MappedSection.cs) | MIT、版权Manfred Graf；参考只读、缓存、边界检查、mutex和头一致性模式。实际复制实质代码须保留许可；本项目以CsWin32接入公开映射接口，不采用远程能力。 |
| [SystemInformer CPU](https://github.com/winsiderss/systeminformer/blob/897116aaeda8c9de1653c50aafd5155cf7f30609/SystemInformer/syssccpu.c)、[许可](https://github.com/winsiderss/systeminformer/blob/897116aaeda8c9de1653c50aafd5155cf7f30609/LICENSE.txt) | 当前主仓MIT；OneGraphPerCpu/PhSipSetOneGraphPerCpu体现总图/各处理器图与独立历史的持久模式。只借鉴模式，不复制实现；其双击用于定位历史高占用进程，本项目双击切换来自用户要求。未复制GPL代码。 |
| [Windows PDH采集](https://learn.microsoft.com/windows/win32/perfctrs/collecting-performance-data)、[PdhAddEnglishCounterW](https://learn.microsoft.com/windows/win32/api/pdh/nf-pdh-pdhaddenglishcounterw) | 在现有长期批量PDH query添加English `\Processor Information(*)\% Processor Time`；只接收真实 `group,index`，排除_Total和组总计；两个有效样本/CStatus后展示。不按CPU另建query。 |
| [HWiNFO官方共享内存入口](https://www.hwinfo.com/shared-memory-viewer/) | 本次HTTP访问被Cloudflare challenge阻挡，未取得官方SDK正文，不把镜像当官方授权或声称已核验当前许可时限。采用既有官方SM2接口，实际发布由用户已有HWiNFO提供。 |

只读打开 `Global\HWiNFO_SENS_SM2`，不创建、不写入；可用时使用 `Global\HWiNFO_SM2_MUTEX` 有界同步，复制前后核验头一致，持续变化有界重试后返回明确状态。v2头最少44字节，签名 `0x53695748`（HWiS），pollTime是Unix秒；传感器前缀264字节、读取项前缀316字节。偏移/数量/实际stride从头读取，不硬编码数组步长。以checked宽整数检查映射范围、乘加溢出、传感器索引和字符串边界；非有限double为null/invalidData。保留原始type及未知类型，传统NUL字符串不能ASCII丢单位符号，不支持布局明确unsupportedVersion。

上限为映射64MiB、1024组、8192读取项，超限capacity，不无提示截断。只缓存最近完整结果。缺失映射按周期有界重试，关闭/重启/恢复重新打开；退出释放view/map/mutex。访问拒绝、同步超时、坏头、版本失败分别报告。pollTime不前进超过 `max(30秒,3×采样周期)` 标stale并清空当前值；HTTP读取时间不能充当新传感器样本时间。

## HTTP 与 DTO

| 位置 | 字段 |
|---|---|
| `snapshot.logicalProcessors[]` | `{id:string,group:number,index:number,percent:number|null,unavailable:Availability}`；id为PDH `group,index`，按group/index排序，最大4096项；缺失原因键percent。 |
| `snapshot.telemetry` | 增加 `cpuCapturedAtUtc:string|null`，逐CPU独立PDH时间；错误键logicalProcessors。不能借用基本快照时间冒充新样本。 |
| `performanceHistory[]` | 增加 `cpuTelemetryCapturedAtUtc:string|null,logicalProcessors:[{id,percent}]`；最多120个基本历史点，各CPU按独立时间去重；新/消失实例不补0。 |
| `snapshot.hwInfo` | `{status:string,capturedAtUtc:string|null,pollTimeUtc:string|null,sensorCount:number,readingCount:number,unavailable:Availability}`；status=available/unavailable/stale，初读unavailable/warmingUp。capturedAtUtc为本应用读取完成时间，pollTimeUtc为HWiNFO发布时间。 |
| `GET /api/hwinfo/sensors` | 上述状态字段加 `sensors[]`；返回不可变最近缓存，不同步触发硬件采集。暂不可用仍200并明确状态/原因。同源GET/no-store/JSON.NET，不接受路径、映射名、远程主机或偏移参数。 |
| `sensors[]` | `{id:string,sensorId:number,sensorInstance:number,name:string,originalName:string,readings:[{id:string,readingId:number,type:number,label:string,originalLabel:string,unit:string,value:number|null,minimum:number|null,maximum:number|null,average:number|null,unavailable:Availability}]}`；组id=`sensorId:sensorInstance`、读取id再加readingId；用户名称空时回退原名。 |
| `PagePreference` | 可选 `cpuGraphMode:"total"|"logicalProcessors"`，旧配置默认total，既有JSON.NET路径白名单持久化；resourceType增加hwinfo。不保存采样数据/历史。 |
| `capabilities` | 增加logicalProcessors（Windows PDH Processor Information）与hwInfo（HWiNFO SM2只读）。available表示链已接入，当前数据以状态/原因决定。 |

HWiNFO完整列表仅在选中视图时按原周期轮询，离开/卸载取消，最多一个飞行请求；摘要随原快照提供。后台惰性串行采集，映射/mutex等待不能阻塞基础health/settings/采样锁。可用独立二进制协议fixture验证解析边界，不能注入生产或当作HWiNFO成功链路。实现中合理调整字段须同步本文、API、前端和真实测试。

## 验收

采用ACCEPTANCE的H01～H08。只操作本次专用主机/负载；已有用户任务管理器和HWiNFO不由测试关闭/配置。本机未发现HWiNFO时实测缺失分支，成功采集与停止/恢复如未真实运行必须标未执行，不以fixture替代。CPU真PDH、偏好、浏览器及正式Electron图表模式仍实际验收。
