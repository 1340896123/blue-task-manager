# 系统信息原生采集验收

日期：2026-11-07（Asia/Shanghai）。规格提交 `30f1ee8`，本阶段仅修改.NET硬件概览、HTTP与页面偏好白名单；Qt界面由下一阶段完成。范围为 [SI01～SI03及SI06后端部分](SYSTEM-INFORMATION.md)，未运行无关进程操作、服务、安装或全量桌面矩阵。

## 实现与真源

`GET /api/system-information`通过现有MVC/JSON.NET返回独立缓存；普通读取60秒TTL，`?refresh=true`只读重新采集，重叠请求共用单后台任务。HTTP等待最多8秒，未完成明确loading；原生任务15秒取消预算在分组和逐盘边界检查，不影响实时snapshot。失败按组/字段保留null与原因，刷新失败不会冒用旧值的采集时间。

SMBIOS type2/4/17经CsWin32读取，严格校验表/结构/字符串边界、UTF8、未知哨兵、扩展容量/速率；只显示已插CPU和已装/容量未知DIMM。DXGI提供显卡型号和专用显存、软件标志。磁盘复用DiskIdentityCache，硬件概览在同一已验证物理设备句柄读取型号/类型/总线和实际容量，GENERIC_READ失败可保留原本最小权限能读出的身份；性能页原有身份读取权限不变。显示模式及EDID名称/物理尺寸经公开Windows接口和只读注册表取得，OS来自真实版本注册表和GetNativeSystemInfo。未采集序列号、密钥或引用截图数值。

## 实际结果与证据

| 项目 | 结果 | 证据（相对仓库） |
|---|---|---|
| .NET10 Release构建 | 通过，0警告/错误 | `output/system-information/stage2/build.log` |
| 固件边界与真实SMBIOS | 通过10项：未知容量/速度、扩展字段、未插槽、截断结构、占位字符串，以及生产CsWin32实读 | `output/system-information/stage2/firmware-tests.json` |
| 生产Host真实HTTP | 通过21项，独立提升worker/隔离偏好与端口 | `output/system-information/stage2/api-results.json` |
| 缓存与并发刷新 | 普通读取相同采集时刻；8个重叠刷新返回同一新时刻；实时sampleId继续推进 | 同上 |
| 独立系统对照 | CPU型号/20核28线程、主板、2条内存的插槽/料号/32GiB容量/6400MT/s配置速度、5块物理磁盘容量、2560×1440@144Hz实际显示模式均与只读CIM对照匹配 | 同上及`output/system-information/independent-windows.json` |
| 新旧偏好 | 新systemInformation查询/滚动偏好保存成功；旧hwinfo资源偏好仍可读取供Qt迁移 | 同上 |
| 精确清理 | 本次测试创建的headless Host已退出；用户现有Takao实例不操作 | 同上`ownedHostExited:true` |

本机根状态为ready，显卡包含NVIDIA硬件适配器和DXGI软件适配器，各自保持真实标记。显示器EDID型号Q27G3ZN、物理尺寸600×340mm可读；Windows为实际Windows11 Enterprise、25H2、26200.9457、x64。容量统一字节字符串；CIM磁盘Size属于几何口径，对实际IOCTL长度采用小于0.1%差异核对，不把营销容量或分区总和当物理盘容量。

首次API检查末尾的偏好请求返回400，原因是测试JSON省略PagePreference必需的sortKey/sortDirection；补齐测试请求后通过，没有放宽生产验证。边界fixture只验证解析拒绝/未知语义，与真实Windows读取证据分开。

## 可复现命令

```powershell
dotnet build src/Takao.Host/Takao.Host.csproj -c Release --no-restore
dotnet run --project tests/Takao.SystemInformationTests/Takao.SystemInformationTests.csproj -c Release -- output/system-information/stage2/firmware-tests.json
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-system-information.ps1
```

API脚本在需要时以现有Windows RunAs方式启动隐藏的提升worker，不绕过Host管理员校验。缺少独立CIM证据时脚本会只读生成；生产采集不使用PowerShell/CIM。未在本机人为触发硬件拔插、真实权限拒绝、15秒超时或固件缺失，不宣称这些现场分支已运行；明确状态/清理路径经代码检查，截断/未知固件由独立边界测试验证。Qt界面、1080P实拍与旧HWiNFO页移除尚属下一阶段，不以本阶段API通过代替。
