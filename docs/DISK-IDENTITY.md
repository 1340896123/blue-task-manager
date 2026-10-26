# 物理磁盘身份信息增量

日期：2026-10-26。用户要求补齐磁盘型号、类型；范围为既有性能页与真实 `physicalDisks[]`，沿用 React/Tailwind、Electron.NET、JSON.NET 与 CsWin32，不新增传输协议或驱动。

同日展示调整：用户要求删除磁盘活动时间及相关图表，并明确保留读取/写入曲线。以下界面行为以本次调整为准；原生采集、API活动字段及系统聚合仍遵循既有合同。

## 1. 用户可见行为

选中任一真实磁盘后，在既有右侧资源标题处显示完整型号，并在统计区显示“读取”“写入”“型号”“类型”“总线类型”。类型为 Windows 原生查询支持的 HDD/SSD；总线独立显示 SATA、NVMe、USB、RAID 等实际枚举，不将 NVMe 当作介质类型。左侧磁贴保留“磁盘 + 原始实例名”，次行显示已知介质/总线信息，摘要显示真实读取/写入速率，小曲线改用真实读/写双线历史。搜索包含型号、介质、总线。长型号可换行，不挤压图表或制造页面横向溢出。

磁盘右侧仅显示一张“读取与写入”吞吐图，保留实线/虚线、读取/写入图例和字节/秒速率单位。磁盘UI不再显示活动时间数值、活动率百分比、活动曲线、对应图例或活动采集口径；型号和身份事实保持可访问。活动率单字段失败不应进入磁盘吞吐或身份错误提示，读取/写入及身份字段的失败仍明确展示。

未知字段显示“—”并提供可访问原因；型号、类型、总线各自成功或失败，不因其中一项失败隐藏其他信息。权限不足、不支持、设备移除、描述符无效、身份不匹配均有明确状态。不能由型号关键字、盘符、驱动器可移动标志、Win32_DiskDrive.MediaType 的 `Fixed hard disk media` 或 `InterfaceType=SCSI` 推测 SSD/HDD/NVMe。

## 2. 成熟来源与公开接口

本轮实际通过 GitHub API 核验 System Informer 固定提交 `d40e6e0441103828287b1b60807259040e541b5f`：

- [storage.c](https://github.com/winsiderss/systeminformer/blob/d40e6e0441103828287b1b60807259040e541b5f/plugins/HardwareDevices/storage.c) 的 `DiskDriveQueryDeviceInformation`：`StorageDeviceProperty` 两段读取 `STORAGE_DESCRIPTOR_HEADER.Size`，以实际返回长度验证描述符及字符串偏移，ProductIdOffset 为型号，VendorIdOffset 单独读取。
- 同文件 `DiskDriveQueryDeviceTypeAndNumber`：`IOCTL_STORAGE_GET_DEVICE_NUMBER` 返回真实 DeviceType/DeviceNumber。
- [disk.c](https://github.com/winsiderss/systeminformer/blob/d40e6e0441103828287b1b60807259040e541b5f/plugins/HardwareDevices/disk.c) 的 `DiskDrivesUpdate` / `DiskDriveUpdateDeviceInfo`：原生 StorageDeviceNumber 关联磁盘索引，移除设备时丢弃旧动态状态。

仅借鉴公开 API、部分成功、长度校验和身份关联设计，独立实现，不复制源码或引入其驱动。

微软公开合同：

- [IOCTL_STORAGE_QUERY_PROPERTY](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_storage_query_property)、[STORAGE_DEVICE_DESCRIPTOR](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddstor/ns-ntddstor-storage_device_descriptor)：ProductIdOffset/VendorIdOffset 和 STORAGE_BUS_TYPE。
- [DEVICE_SEEK_PENALTY_DESCRIPTOR](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-device_seek_penalty_descriptor)：StorageDeviceSeekPenaltyProperty 返回 IncursSeekPenalty。类型按此原生报告判定并在资源说明注明；虚拟、RAID、Storage Spaces 等复合设备不能宣称其背后物理介质已被识别。
- [IOCTL_STORAGE_GET_DEVICE_NUMBER](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddstor/ni-ntddstor-ioctl_storage_get_device_number)、[STORAGE_DEVICE_NUMBER](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddstor/ns-ntddstor-storage_device_number)：验证设备类型与编号；编号不是永久硬件身份。
- [Win32_DiskDrive.Index](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-diskdrive#index)：物理编号来自 STORAGE_DEVICE_NUMBER，0xffffffff 表示无法映射物理驱动器。

本机只读研究确认 PDH PhysicalDisk 实例 `0 D:`～`4 C:` 的首段编号，与独立 Get-PhysicalDisk/Win32_DiskDrive 的 0～4 对应；该观测仍需运行时原生设备编号读回，不作为全机永真假设。

## 3. 原生采集与缓存

1. 只接受有界、严格解析的 PDH 实例前导十进制编号（编号后为实例分隔空白或字符串结束）；保留原始 id/name，不更改历史关联。无法解析、溢出或编号超边界时返回 `identityMismatch`/`notSupported`，不按枚举次序关联。
2. 使用 CsWin32 生成的 CreateFile/DeviceIoControl 打开 `\\.\PhysicalDriveN`，最小访问权限并共享读写；用同一句柄的 IOCTL_STORAGE_GET_DEVICE_NUMBER 验证 DeviceType=FILE_DEVICE_DISK、DeviceNumber=N 且非 0xffffffff，之后才能发布该实例的身份字段。句柄及时释放。
3. StorageDeviceProperty 按 header/完整 descriptor 分段有界查询；长度、Size、偏移、终止符均以实际返回字节验证。型号取非空 ProductId，必要时保留不重复的 VendorId 前缀，仅修剪边界空白；不读出或发布序列号。
4. 总线采用真实 STORAGE_BUS_TYPE。未知/未来枚举返回明确原因，不能回退到 SATA/NVMe。介质按有效 seek-penalty：普通物理 ATA/SATA/NVMe/SCSI/SAS 等设备可报告 HDD（有惩罚）/SSD（无惩罚），并注明 Windows 寻道惩罚口径；RAID、Storage Spaces、Virtual/FileBackedVirtual 等复合或虚拟设备不从该值推定背后 SSD/HDD，保持未知并说明。查询不支持时类型为 null，不由总线补造 SSD。
5. 身份读取在后台执行，不占用 HTTP/settings 锁，不阻塞基础快照就绪。缓存有界为当前最多256个实例；成功最多30秒重新读取，失败短期退避。实例集合移除时立即淘汰；新实例立即查询。编号复用/热插拔同名实例最迟在刷新期限重新核验，身份时间保持实际查询时间，失败时不沿用上一块盘的字段。
6. 计数器与身份状态独立。PDH过期只使活动率/吞吐失效，保留尚在有效缓存期内的静态身份及其 unavailable。身份元数据不复制到120点性能历史，不在每秒采样重复打开所有磁盘；记录首次查询与稳定采样开销。

## 4. HTTP/TypeScript 合同

`PhysicalDiskDto` / `PhysicalDisk` 保持已有必需参数，新增可空字段：

| 字段 | 类型 | 语义 |
|---|---|---|
| deviceNumber | uint/null | 已通过原生读回验证的物理磁盘编号 |
| model | string/null | Windows 存储描述符报告的型号，不推测品牌 |
| mediaType | string/null | HDD 或 SSD；原生判定不充分时 null |
| busType | string/null | 真实总线枚举的可读名称，例如 SATA、NVMe、USB |
| identityCapturedAtUtc | ISO8601 UTC/null | 本组身份信息最近实际查询的时间 |

各缺失字段在原 `unavailable` 字典中使用对应键给出 `accessDenied`、`notSupported`、`notCollected`、`deviceRemoved`、`identityMismatch`、`invalidData` 或 `error` 原因/原生错误码。型号/总线失败不使活动率变0；PDH预热不阻止型号成功展示。`capabilities.diskIdentity` 声明公开来源和限制，设置页提供中文可读名称。JSON输出继续由JSON.NET完成，原始name/id与性能历史时间语义不变。

## 5. 界面规范

复用 MASTER 的 resource-title/subtitle/body/caption、事实列表、secondary、accent、space 与 overflow-wrap；保留双栏资源布局与单张蓝色读/写吞吐图，删除活动图后不留空白占位。磁盘大小曲线统一采用同一读/写历史，双线分别对应读取/写入且不采用0～100%百分比轴。型号是可选择文本，明确标签的事实项不用新按钮/徽标/大卡片；介质与总线使用两个字段，避免“NVMe=SSD”的混合标签。缺失原因可通过键盘与鼠标读取，不仅依赖颜色或悬浮。

用户指定 UI/UX Pro Max 的定向检索首先得到不适用的手机键盘建议，收窄后命中通用文字对比4.5:1；仅采纳适用的可读性建议，不持久化偏题输出，不套用手机字号或新设计系统。交付按 pro-rules 适用桌面项检查图标、语义Token、可见焦点、长文本、reduced-motion、960×640/1380×860和实际Electron125%缩放。

## 6. D 系列验收

| ID | 场景 | 必须结果 |
|---|---|---|
| D01 | Host Release、Web类型及生产构建 | 成功；CsWin32/JSON.NET、原字段和历史兼容；无新增驱动/私有协议 |
| D02 | 真实API逐磁盘与独立 Get-Disk/Get-PhysicalDisk、Win32_DiskDrive、Get-Counter 对照 | 每个实例原始id/name、原生deviceNumber与系统编号匹配；型号、总线、HDD/SSD与独立结果一致或有实际原因；无_Total；至少两个有效PDH样本 |
| D03 | 真实不支持/字段部分成功、身份错误与长度校验 | 缺失逐字段null+原因；型号/类型失败不覆盖其他成功字段；不把未知变0或猜测类型；无法真实触发分支明确记未执行，边界fixture不冒充真实设备成功 |
| D04 | 磁盘选择、搜索型号/类型/总线、键盘及长型号 | 正确设备标题/事实项可读、左侧身份保持，搜索能定位真实设备；无页面横溢，未知原因可访问 |
| D05 | 1380×860、960×640、正式Electron125%、reduced-motion | 型号换行、统计/图表可达、焦点可见，截图保存于 docs/evidence/disk-identity/ |
| D06 | 稳定采样、暂停恢复、热插拔/重编号可执行条件与关闭 | 基础采样不被元数据锁阻塞；缓存/句柄有界；过期PDH不丢失有效身份原因；移除/刷新不保留上一设备身份；不拔除用户磁盘为测试补覆盖率，未执行明确记录 |
| D07 | 正式Electron真实链路与相关回归 | 生产UI→HTTP→JSON.NET→CsWin32→Windows，回归B14/L04/L08/L10相关项，保存可复现命令、Git SHA、结果与截图；只清理测试创建的进程身份 |
| D08 | 删除活动时间UI并保留真实读/写曲线 | 逐真实磁盘的磁贴、右侧事实、图表、图例、可访问名及可展开说明均无活动时间/活动率展示；右侧恰有一张读取/写入吞吐图，磁贴为真实读/写速率和同源双线小曲线，无百分比轴或移除后空白占位；读取/写入/身份缺失仍有原因。实测浏览器与正式Electron的1380×860、960×640，以及正式Electron实际125%和reduced-motion，型号/类型/总线/搜索/键盘仍正常；保存当前API、命令、结果与截图，后台活动字段兼容性保持 |

原始D01～D07结果保留于 [DISK-IDENTITY-RESULTS.md](DISK-IDENTITY-RESULTS.md)，作为活动时间UI移除前的历史证据。D08及本次受影响回归另记当前验收结果，保存截图路径与Git SHA；不得把旧报告的双图截图当作当前界面证明。构建或fixture不代替真实API/桌面设备对照。
