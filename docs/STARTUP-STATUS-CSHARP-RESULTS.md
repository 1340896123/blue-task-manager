# 启动状态 C# 验收（2026-10-31）

本轮遵从用户“只在 C# 进行测试，不打开 UI 测试”的要求，没有打开浏览器、Electron、Windows 启动设置或执行 UI 自动化。测试直接调用生产 `SlowDataReader.ReadStartup` → `StartupRegistry` / `StartupApprovalReader` → CsWin32 → Windows；JSON 用 JSON.NET。没有替换生产采集器，没有执行任何启动命令。

初次代码诊断：真实21项，8项启用、13项禁用、0未知，warnings为空；因此未复现截图的整列未知，不能声称本轮修复了一个已复现的全列故障。确证缺陷为另一 hive 的同名批准记录会污染原始来源：用真实现有禁用记录、另一 hive 缺失同名值的只读探针，旧 `StartupApprovalReader` 在 `HKCU/Run` 断言失败（进程退出 -532462766）；修复为按原始 hive 独立读取后通过。其他程序可能正常增删本机启动项，最终重跑的即时统计以 JSON 证据为准。

环境：Windows x64；项目锁定SDK10.0.401但本机仅有兼容10.0.403，以下显式调用已安装SDK，不修改仓库 global.json。本轮基线提交 `d2d78c8`；该文档与测试随“启动状态来源隔离”阶段独立提交。

可复现命令（PowerShell，仓库根目录）：

```powershell
dotnet 'C:\Program Files\dotnet\sdk\10.0.403\dotnet.dll' build tests/Takao.StartupTests -c Release --no-restore
dotnet 'C:\Program Files\dotnet\sdk\10.0.403\dotnet.dll' run --project tests/Takao.StartupTests -c Release --no-build -- docs/evidence/startup-context-menu/csharp-startup-results.json
```

结果：66个断言通过。包含4个真实跨 hive 独立查询探针；GUID命名的HKCU Run/RunOnce值和Startup文件夹文件；真实2/3、未知1/6/7/uint.MaxValue，非二进制类型、0/4/11/13/256字节，缺失默认、完整文件名匹配、后续慢速读取真实状态变化，以及JSON.NET的Boolean/null和原因字段保真。测试的未知状态输入仅证明防御边界，不代表Windows对其语义的核验。最终证据：[csharp-startup-results.json](evidence/startup-context-menu/csharp-startup-results.json)。

清理：只对本次成功创建的GUID值和文件执行删除，删除前核对类型/完整字节；finally继续尝试清理其他独立项并聚合错误。前后所有已有启动行的完整内容与状态指纹一致，没有修改已有用户项。没有创建/结束进程，也没有新增注册表键。未执行：HKLM写入（当前令牌未提升）、真实权限拒绝fixture、Windows设置状态切换及全部UI/桌面测试；截图路径无。
