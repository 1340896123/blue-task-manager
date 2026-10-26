# Windows 标准任务管理器恢复命令验收

日期：2026-10-26（Asia/Shanghai）。实现提交：`87358be`；发布完整性修补：`5aa2ab8`。

## 文件与发布链

以下三个位置均已具备 `restore-windows-task-manager.cmd`，独立 SHA256 核对一致：

- `E:\taskManager\scripts\restore-windows-task-manager.cmd`
- `C:\Program Files\BlueDomainTaskManager\restore-windows-task-manager.cmd`
- `E:\taskManager\output\TaskManager-win-x64\restore-windows-task-manager.cmd`

SHA256：`BE22557B16C45522FF7E5AD3B97D85F3376386AF0979581A4E54BC8AFE39B139`。

`build.ps1` 将脚本复制到发布目录；`publish-desktop.ps1` 保留该文件。安装器与 ZIP 从同一目录枚举文件，必需文件清单已包含恢复 CMD，缺失时拒绝交付。

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath `
  'E:\taskManager\scripts\restore-windows-task-manager.cmd', `
  'C:\Program Files\BlueDomainTaskManager\restore-windows-task-manager.cmd', `
  'E:\taskManager\output\TaskManager-win-x64\restore-windows-task-manager.cmd'
rg -n 'restore-windows-task-manager.cmd' scripts/build.ps1 scripts/build-installer.ps1 scripts/package-release.ps1 scripts/test-installer-ci.ps1
```

## 已执行检查

| 检查 | 结果 |
|---|---|
| 两个提交的 `git diff --check` | 通过，退出码 0 |
| CMD 内联 PowerShell、`build.ps1` 与 `build-installer.ps1` 语法解析 | 通过 |
| 从 `C:\Windows` 调用源 CMD，参数 `--no-pause` | 正确命中缺 Host 分支，退出码 2，无 UAC 或维护调用 |
| 发布复制与安装器/ZIP 必需清单 | 已包含 CMD |
| 安装目录、便携目录与源码脚本哈希 | 三者一致 |

```powershell
git diff --check 87358be^ 87358be
git diff --check 5aa2ab8^ 5aa2ab8
$line = Get-Content scripts/restore-windows-task-manager.cmd | Where-Object { $_ -match ' -Command "' }
$command = [regex]::Match($line, ' -Command "(.*)"$').Groups[1].Value
$tokens = $null; $errors = $null
[void][System.Management.Automation.Language.Parser]::ParseInput($command, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($path in @('scripts/build.ps1', 'scripts/build-installer.ps1')) {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PWD $path), [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw ($errors | Out-String) }
}
```

缺 Host 分支的运行工作目录为 `C:\Windows`，执行命令如下；源 `scripts` 目录没有 `TaskManager.Host.exe`：

```powershell
cmd.exe /d /c E:\taskManager\scripts\restore-windows-task-manager.cmd --no-pause
```

## 当前系统状态与未执行项

独立只读查询 HKLM 64 位 `SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe`，当前 `Debugger` 仍为：

```text
"C:\Program Files\BlueDomainTaskManager\TaskManager.Launcher.exe" --task-manager-entry
```

`UseFilter` 缺失。新增 CMD 未被执行为实际恢复操作，用户当前正在使用的系统接管保持此状态。真实 UAC 取消/维护失败/恢复成功链本轮未执行；安装器本轮未重建，清单检查不作为新安装包实测证据。截图不适用，本轮证据为命令行结果、文件哈希与静态清单。
