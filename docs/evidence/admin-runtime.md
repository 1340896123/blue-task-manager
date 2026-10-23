# 管理员启动阶段验证

日期：2026-10-23。版本：1.1.1；Host文件版本：1.1.1.0。本阶段仅修改运行权限、固定卸载维护入口、对应文案及版本，不代表NSIS或提升成功链已验收。

- `dotnet restore src/TaskManager.Host/TaskManager.Host.csproj -p:ElectronSkipExecCommands=true`：通过。
- `dotnet build src/TaskManager.Host/TaskManager.Host.csproj -c Release --no-restore -p:ElectronSkipExecCommands=true`：通过，0警告、0错误。
- `npm run typecheck`、`git diff --check`：通过。
- SDK `mt.exe` 独立提取实际EXE的manifest：`requireAdministrator`、`uiAccess=false`，文件版本1.1.1.0。
- 独立`whoami /groups /fo csv /nh`确认当前进程为Medium `S-1-16-8192`，Administrators仅deny-only。
- Medium会话使用`dotnet TaskManager.Host.dll`，分别无参数、`--headless --port=5211`、`--port=5211 --task-manager-entry C:/Windows/System32/taskmgr.exe`、`--uninstall-task-manager-integration`：四者均返回740及明确`administratorRequired`错误；隔离偏好目录未创建、5211未监听、Host目录下无Electron子进程。
- Medium会话`Process.Start`的`UseShellExecute=false`启动实际Host EXE：Windows原生错误740；未尝试自动提权或回退。

原始结果与提取manifest位于独立工作树的`output/admin-runtime/results.json`和`output/admin-runtime/host.manifest.xml`。它们只包含本阶段结果，无写令牌、账户名称或机器进程清单。

核心复现命令（从仓库根目录执行，Medium会话）：

```powershell
$hostDirectory = Join-Path (Get-Location) 'src/TaskManager.Host/bin/Release/net10.0-windows10.0.19041.0'
& 'C:/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64/mt.exe' ('-inputresource:' + (Join-Path $hostDirectory 'TaskManager.Host.exe') + ';#1') '-out:output/admin-runtime/host.manifest.xml'
$env:TASKMANAGER_DATA_DIR = Join-Path (Get-Location) ('output/admin-runtime/' + [guid]::NewGuid())
dotnet (Join-Path $hostDirectory 'TaskManager.Host.dll') --headless --port=5211
$LASTEXITCODE # 740，其他三组参数同上。
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $hostDirectory 'TaskManager.Host.exe'))
$start.UseShellExecute = $false
try { [Diagnostics.Process]::Start($start) } catch [ComponentModel.Win32Exception] { $_.Exception.NativeErrorCode } # 740
Test-Path -LiteralPath $env:TASKMANAGER_DATA_DIR # False
[Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains 5211 # False
Remove-Item Env:TASKMANAGER_DATA_DIR
```

固定维护参数为`--uninstall-task-manager-integration`，只接受这一个精确参数；任何其他参数、路径或命令均不接受。维护清理在WebApplication/偏好创建之前执行，复用生产IFEO服务的REG_SZ类型和完整命令所有权、revision重验、同写句柄重读与删除后读回。非自有值成功无改动；读取或清理失败返回非零，安装器必须中止删除文件。

未执行：真实提升后的桌面、维护清理成功/失败、全局IFEO注册/取消、管理员服务启停和NSIS生命周期。当前Medium会话的拒绝结果不能替代这些成功证据。后续R系列验收须调整旧管理测试的低权限SCM硬断言，按实际权限分支且只操作测试自建服务。
