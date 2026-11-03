# Qt迁移：后端阶段实际检查

日期：2026-11-04（Asia/Shanghai）。对应迁移Q01/Q02的后端部分，不表示Qt桌面已验收。

执行命令与结果：

- `dotnet restore Takao.sln --force-evaluate`：退出0，Electron.NET依赖从Host与依赖测试锁文件移除。
- `dotnet build Takao.sln -c Release --no-restore`：退出0，0警告、0错误；本机SDK 10.0.403。global.json同步固定该已安装稳定版本。
- `dotnet run --project tests/Takao.StartupTests/Takao.StartupTests.csproj -c Release`：退出0，66项检查通过。生产读取与专用GUID真实Run/Startup来源检查；精确清理后已有启动项指纹不变。未提升令牌下HKLM fixture写入与权限拒绝fixture未执行。
- `dotnet run --project tests/Takao.DiskTests/Takao.DiskTests.csproj -c Release`：退出0，56项检查通过。5块实际磁盘的CsWin32 DeviceIoControl身份与独立Windows存储/PDH查询对照；原生存储句柄309→309。
- `dotnet run --project tests/Takao.NetworkTests/Takao.NetworkTests.csproj -c Release --no-build -- output/qt-network-results.json`：退出1。真实ETW来源返回accessDenied、native1300：当前令牌没有ETW所需特权。此项不能记为通过，留给管理员验收worker重跑；未将未知流量改为0。

本阶段未执行Qt窗口/截图、1080P交互、安装生命周期、更新安装与IFEO写入；这些由后续Qt验收承担。后端保留真实采样与操作链路，仅替换桌面启动/窗口生命周期；Qt正常关闭触发Host停止，Host异常退出的Job Object清理须在真实桌面阶段核验。
