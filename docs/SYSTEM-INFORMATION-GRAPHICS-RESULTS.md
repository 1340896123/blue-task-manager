# 系统信息显卡物理身份修复

日期：2026-11-07（Asia/Shanghai）。本阶段接在Qt提交`3235a30`后，仅修复系统信息显卡逻辑别名重复及来源文字；不改变性能页的GPU计数器/LUID语义。

实际新页和API显示四条不同LUID的同名NVIDIA适配器，而独立Windows CIM仅枚举一个PCI NVIDIA实例，另外三个是ROOT虚拟显示设备。不能按型号字符串去重，否则会误合并真实的两块同型号显卡。

采用Windows SDK10.0.26100.0公开`shared/d3dkmthk.h`中的只读查询，经CsWin32生成`D3DKMTOpenAdapterFromLuid`、`D3DKMTQueryAdapterInfo`和`D3DKMTCloseAdapter`。先查询`KMTQAITYPE_PHYSICALADAPTERCOUNT`，仅单节点时读`KMTQAITYPE_PHYSICALADAPTERPNPKEY`硬件键；规范化为真实PnP设备实例并按不区分大小写的实例去重，保留全部原始LUID。每次打开的KMT句柄均在finally关闭，没有安装驱动或修改系统。

查询失败、键格式无效或多节点聚合适配器保留独立逻辑LUID、未知原因与明确来源文字，不按空键或型号合并。软件标记保持；ROOT/SWD实例归为虚拟，其余确认实例归为物理。Qt只添加来源文字映射：物理显卡/软件适配器/虚拟适配器/逻辑适配器（物理身份未确认）。新DTO字段见[SYSTEM-INFORMATION.md](SYSTEM-INFORMATION.md)。

## 实际验证

- 独立只读probe确认四条NVIDIA LUID的硬件PnP键完全相同，指向同一PCI实例；BasicRender是独立ROOT设备。原始证据：`output/system-information/gpu-probe/results.json`。
- 定向生产HTTP验证7项通过：当前唯一物理GPU数量/精确PnP实例与独立CIM一致、专用显存非零、四个逻辑别名只呈现一次、软件分类明确、本次隔离Host已退出。证据：`output/system-information/graphics/results.json`。
- .NET Release成功，0警告/错误；Qt Release成功。Qt构建日志：`output/system-information/graphics/qt-build.log`。
- 更新了独立`output/system-information/release`中的Host.exe/Host.dll/Qt.exe；保留既有自包含deps/runtime文件，未覆盖或关闭用户正在使用的其他实例。后续最终WGC截图由主代理统一记录。

复现命令：

```powershell
dotnet build src/Takao.Host/Takao.Host.csproj -c Release --no-restore
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-system-information-graphics.ps1
& scripts/import-msvc.ps1
cmake --build output/qt-build --target Takao.Qt --parallel 4
```

本机没有两块同型号物理GPU或多节点聚合适配器，不能声明这些硬件现场已实测；实现按PnP实例而不是型号比较，不同实例保留的设计经过代码检查，多节点仅回退独立逻辑口径。没有重复运行其他页面、进程操作、安装或全量API矩阵；本轮只验证显卡身份改动。
