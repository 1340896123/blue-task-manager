# 证据范围与阶段标签

当前结论见 [1.1 验收记录](../ACCEPTANCE-RESULTS.md)。文件名中的 final 不表示适用于以后所有提交；以本索引、JSON 时间和报告的业务基线为准。

## 1.1 功能归档

最新业务基线`251897e`，2026-10-23当地09:22～09:33独立发布复验。下表功能结果已更新；系统入口仍保留07:45的部分确认，未重跑。首次`21ebffd`记录及阶段证据可从Git历史追溯。

| 文件 | 范围 |
|---|---|
| regression-v11.json | 11 组静态 / 原生 / Chromium，334 项 |
| final-desktop-results.json | 正式 Electron 42 项，替换旧 1.0 副本 |
| process-ui-v11-results.json | Chromium 进程工作流 34 项 |
| process-desktop-v11-results.json | Electron 进程工作流 34 项 |
| pages-ui-v11-results.json | 页面工作流 50 项 |
| native-desktop-v11-results.json | 真实选择器 / 文件 / 节流 / 重启等 22 项，仅公开断言 |
| system-locations-v11-results.json | 4 入口受理、3 实际窗口确认、6 受理 / 清理断言，非总 PASS |
| gpu-identity-v11.json | 真实 DXGI / PDH / Windows 对照 12 项，软件适配器明确标记 |
| final-ui-results.json | 浏览器 36 项，替换旧副本 |
| final-recovery-results.json | 真 Host 停启 / 令牌轮换，4 项 |
| release-functional-results.json | 最新六组Electron/原生窗口/任务栏/图标/受限进程146项；加上11组334项，共480项 |
| taskbar-integration-results.json | 23项；真实5拒绝与配置不变、entry窗口复用；不含成功注册/取消 |
| product-icon-results.json | 16项；EXE提取和真实窗口WM_GETICON |
| desktop-performance-release.json | 120点、约34秒、30个UI样本，本机三项短期目标PASS |

`restricted-process-ui-v11.json`/`guard-process-visible.png`为最新受限进程只读复验9项；首次说明修复为`4ca01bd`。`taskbar-integration-*.png`与`product-desktop.png`为最新设置及图标的应用自身截图。

`final-desktop-*`、`final-recovery-*`、`v11-process-*`、`v11-resources.png`、`v11-service-details.png`、`v11-settings-960.png`、`v11-gpu-identity*.png`、`v11-desktop-960-125-*.png` 是应用自身真实截图；图中数值只代表采样时刻，不用于性能预算。

Windows picker / Explorer / Properties 原图含用户目录或无关导航，只留本地忽略目录 output，不公开归档。JSON 仅含断言、汇总和相对截图名，不发布令牌、完整快照、全机进程清单或服务配置转储。

## 历史阶段材料

- `native-backend.md`、`frontend-ui.md`：原始 1.0 阶段实现 / 检查交接，旧能力边界不替代 1.1。
- `ui-pages-v11.json`：较早的页面阶段副本；本次以 `pages-ui-v11-results.json` 为准。
- `performance-v1-baseline.json`：1.0 性能基线。
- `performance-v11-core2a.json`、`performance-v11-core2a-workstation.json`、`performance-v11-telemetry.json`：中间阶段观察；条件不同，不证明最终整应用预算达成。

最新性能依据是`desktop-performance-release.json`，UTC2026-10-23T01:33:03.696Z。Host整机CPU0.207%、整应用0.520%、采样p95 56.52ms、搜索至下一rAF p95 25.6ms。原06:39的`output/performance/desktop-v11.json`及三次前提不足尝试仅供历史定位，不用于本轮结论或严格A/B。短期测量不证明长时无泄漏或其他机器均达标。
