# GitHub Release 下载与更新

日期：2026-10-26。用户要求在左侧导航底部、设置右侧显示有更新时的下载图标，悬浮查看版本；点击后台下载，完成后选择“立即更新”或“下次启动自动更新”。本增量使用本仓库已发布 GitHub Releases、HTTPS、SHA256 与标准 NSIS 安装流程。

## 已核验来源与取舍

2026-10-26 实际在线读取：

| 来源 | 采用与边界 |
|---|---|
| [electron-builder 26.0.12 GitHubProvider.ts](https://github.com/electron-userland/electron-builder/blob/44c5265a9260ca6f319f111b5a2ca7e50b0a7cd6/packages/electron-updater/src/providers/GitHubProvider.ts) | 该 tag 实际指向固定提交 44c5265；源码区分 prerelease/latest 并读取频道文件。借鉴固定源、版本筛选和明确错误；当前 Release 缺 latest.yml/app-update.yml，自有 NSIS 也非其模板，不能直接启用间接依赖的 quitAndInstall。 |
| [同提交 NsisUpdater.ts](https://github.com/electron-userland/electron-builder/blob/44c5265a9260ca6f319f111b5a2ca7e50b0a7cd6/packages/electron-updater/src/NsisUpdater.ts) 126～163行 | 明确更新参数、可选 /S、运行选项和固定安装目录。采用已校验安装器、优雅退出、安装后运行的交接流程，保持本项目目录/标记/锁保护。 |
| [GitHub Releases REST](https://docs.github.com/en/rest/releases/releases?apiVersion=2022-11-28)、[assets](https://docs.github.com/en/rest/releases/assets?apiVersion=2022-11-28) | releases 列表支持已发布预览；latest 排除 prerelease/draft。附件提供 size/state/browser_download_url/sha256 digest。采用公开 REST 和官方附件 HTTPS。 |
| [GitHub Compare](https://docs.github.com/en/rest/commits/commits?apiVersion=2022-11-28#compare-two-commits) | status/ahead_by/behind_by 可确认同基础版本旧提交落后；不按文件日期或字符串顺序猜测版本。 |
| [NSIS官方MUI示例](https://github.com/nsis-dev/nsis/blob/v312/Examples/Modern%20UI/Basic.nsi)与现有安装脚本 | 保留每机管理员安装、专用目录和产品标记；仅明确更新模式允许有界等待旧映像释放。 |

现场最新发布为 [v1.1.1-build.8](https://github.com/1340896123/blue-task-manager/releases/tag/v1.1.1-build.8)，prerelease=true；setup 为169,320,155字节，API digest 为 `sha256:1eb7d01abfc163f3c68232500d992d944349c4b17b2ab5844564ffdaf671408e`，同Release的SHA256SUMS唯一安装包条目与其一致。这里只记录研究时实际状态，生产不能硬编码版本/大小/hash。

## 检查与版本身份

- 固定 `1340896123/blue-task-manager` 的公开 `/repos/.../releases`；固定 User-Agent、Accept 和 API 版本，不要求或保存用户 GitHub token。正式桌面启动后异步检查一次，长运行每6小时检查；GET状态只读缓存，不随采样或前端轮询访问GitHub。单任务串行，手动检查60秒冷却，自动失败至少15分钟退避，尊重Retry-After/限额，支持ETag/304。
- 元数据读取有界（最多100条、2MiB、15秒），只接受 published_at 存在、draft=false、合法v前缀SemVer。包含已发布prerelease，因为当前实际交付均为build.N；不使用遗漏预览的latest端点。选择最高可验证版本及对应唯一安装包。
- 未来CI在构建前将已验证release_tag与完整SourceCommit嵌入标准.NET AssemblyMetadata（ReleaseTag/SourceCommit）；程序集、安装产品标记和Release指向同一身份。普通本地构建无ReleaseTag时保留InformationalVersion的+commit，不能把用户偏好/renderer传值当发布身份。
- 有可信ReleaseTag用SemVer比较；core必须与程序集基础版本一致。历史`1.1.1+commit`先比较core，同core则解析候选tag的完整commit并请求`compare/<current>...<candidate>`，仅ahead_by>0且behind_by=0认更新。identical/behind无更新；diverged、404、未知身份或网络失败为不可确认，隐藏下载入口，不能降级本地较新构建。
- 实际安装commit `c2f1bbc4027ab9518a0c3fde59872da616ea0765` 到build.8 source `63f1b42544edc82c3b7edb963953b65875340508` 在线compare为ahead、6/0，说明同1.1.1也可能有更新；本地HEAD在release之后则不应提示更新。

## 下载、完整性与暂存

- 仅接受同Release的唯一uploaded安装附件：新名称 `Takao-v<core>-win-x64-setup.exe`，兼容历史名称 `BlueDomainTaskManager-v<core>-win-x64-setup.exe`；两种名称同时存在同样按重复候选拒绝，core等于tag core，size在有效范围且不超过1GiB。拒绝ZIP、其他架构、重复/空附件、任意命令或renderer路径。
- 安装包必须有合法GitHub sha256 digest；同Release的SHA256SUMS精确文件名条目也必须存在并一致。元数据/校验文本有小型读取上限。实际文件字节数、size与计算SHA256全部匹配后才ready；散列一致不冒充当前未签名安装包的Authenticode发布者验证。
- 初始下载URL必须为该固定仓库/tag的官方附件；仅HTTPS，重定向次数有界，只放行GitHub官方附件主机（github.com、release-assets.githubusercontent.com及确需的官方对象主机）。不保存带签名query的重定向URL，不转发凭据，不允许任意URL。
- 用户点击后才流式下载到固定part；实际字节驱动进度，total未知为null，最多每秒发布进度；完整校验后同目录原子改名。后台任务不阻塞页面或采样，只保留一个候选包/part。取消、断流、超时、磁盘不足、校验失败不ready、不自动安装，并清理part；重启半包重新下载，不伪装断点续传。关闭应用取消下载，网络重试与总时限有界。
- 暂存固定 `%ProgramData%\BlueDomainTaskManager\Updates`，提升Host创建受保护DACL，仅SYSTEM/Administrators可写，经CsWin32标准ACL/安全描述符调用；逐级拒绝reparse。已有目录也须核验权限/所有者/真实路径，不可信则不可用。安装目录可能自选，不能假设其都继承Program Files保护。
- pending也放受保护目录，用JSON.NET、TypeNameHandling.None、严格字段/长度/数量/读取上限和原子写。保存release/asset身份、固定安装目录、size/hash、选择、attemptId/attempts和错误；执行前再验目录、非链接文件、size/hash、版本和安装身份。LocalAppData、用户可写偏好、renderer URL/path/hash不能成为管理员执行信任根。
- 成功后的旧包可等锁释放再清理；只删除固定暂存目录内精确自有文件，不递归清理未知目录或用户下载。

## 安装资格与退出交接

自动安装仅用于HKLM64卸载记录、`.bluedomain-install.ini`产品ID/Directory、当前Host路径三者一致的正式每机安装；规范化路径且无链接。便携目录可检查版本并明确“安装版支持自动更新”，不能改写另一安装目录。权限/身份/目录不明时明确不可用。

1. 下载完成ready时自动显示一次选择窗口，关闭/Escape仅收起，图标可重开；完成下载不代表允许安装。
2. 立即更新：重验revision/文件/资格，生成attemptId并落盘。先完成API响应，再启动固定安装器明确更新模式；启动受理成功才优雅关闭本次Electron/Host，失败保留窗口并显示原因。不得按名强杀或终止其他用户实例。
3. 下次启动：原子保存scheduled、attempts=0，继续使用。下次正式桌面在权限检查、维护/entry转发判定后，HTTP/Electron/采样前消费；维护/headless、转发已有实例及同目录仍有实例时不安装。
4. NSIS明确更新模式：验证现有注册目录/产品标记，仅该模式最多60秒等Host/Launcher/Electron等映像解锁，再自动完成；普通手工安装仍按原规则拒绝运行中覆盖。只等待，不杀进程。
5. 安装完整写入及产品版本/标记/注册成功后，才重启固定安装目录Host；失败/取消/超时不启动半成品。固定HKLM产品键记录LastUpdateAttemptId/Result/Error（受限attemptId、固定状态/错误码），后续Host关联pending展示原因。新Host确认可信目标版本与安装结果后清理pending。
6. scheduled自动尝试最多一次，启动前即写attempts=1；启动失败、安装失败或下次仍旧版本时转error/ready并提供用户重试，不能启动循环。重试需再次显式选择与重验。升级保留偏好与既有IFEO入口，不为更新重置或接管系统入口。

## API与界面状态

固定`GET /api/updates`读缓存；`POST /api/updates/check|download|cancel|install`复用Origin/写令牌。download/install仅接受expectedRevision和install的mode=now|nextStart，后端决定仓库/URL/路径/参数；陈旧409，后台任务202，JSON.NET错误保留实际原因。

状态至少含status（checking/upToDate/available/downloading/ready/scheduled/installing/error/unavailable）、可信currentVersion/currentReleaseTag、candidateVersion/releaseUrl、downloadedBytes/totalBytes/progress、checkedAtUtc、canDownload/canInstall/安装支持原因、revision/error。未知字段为null，错误不当upToDate。前端检查/下载时约1秒读缓存、空闲30秒，不进telemetry历史。

| 状态 | 导航底部行为 |
|---|---|
| 无更新/尚未确认 | 无下载图标；设置更新行可读检查时间/失败原因并手动重查 |
| available | 设置右侧44px Download图标，title/聚焦提示“下载更新 <tag>”，中文aria-label；点击后台下载 |
| downloading | 同位置显示真实进度/忙态，版本与百分比可读；点击查看进度/取消，不自动弹阻塞窗 |
| ready | 完成自动弹一次，图标可重开；轮询/切页不重复抢焦点 |
| scheduled | 提示“<tag> 已下载，下次启动自动更新”，可立即更新或取消下次安排，不重复下载 |
| error且已确认候选 | 图标可重试并给原因；检查失败且候选未确认时不制造下载入口 |

展开侧栏settings占剩余空间，图标在其右侧红框；紧凑侧栏可上下排列，设置仍可达。复用Lucide及现有accent/hover/focus/disabled/reduced-motion Token，不新增色彩体系。完成Modal展示版本、已校验、立即更新会关闭重启，提供“立即更新”“下次启动自动更新”，初始焦点后者；Escape不安排更新。960×640/125%正文内部滚动、按钮可达。现有进程/任务/首次提示等Modal打开时等待，不能抢危险操作焦点。

## U系列验收

| ID | 场景 | 必须证据 |
|---|---|---|
| U01 | Web/Release/自包含/NSIS/CI | JSON.NET/CsWin32与固定源、ReleaseTag/SourceCommit身份一致；普通安装/既有接管/首次提示回归 |
| U02 | 真实GitHub releases/compare | 发布预览可识别，draft/非法tag/坏附件排除；旧同core提交确实ahead、本地较新不降级；offline/限额/unknown明确 |
| U03 | 官方附件后台下载 | 独立核对size/digest/SHA256SUMS/文件hash；真实进度、取消/断流/坏hash不ready，采样继续、part清理 |
| U04 | 正式Electron | 红框/紧凑位置、版本tooltip、进度、完成一次、Modal不抢焦点、两种选择、键盘/960/125%截图 |
| U05 | 权限与输入 | 非管理员不能替换installer/pending；reparse/不可信ACL/改文件/未知路径或URL/陈旧revision/缺token/foreign Origin拒绝 |
| U06 | 立即更新专用安装 | API后关精确实例、等锁、完整升级后重启新Host、版本/标记读回、偏好/IFEO保留；其它实例/超时不强杀或启半成品 |
| U07 | 下次启动与失败 | scheduled落盘、下次UI前安装、成功不循环；启动失败/取消/损坏/版本未变可重试且自动仅一次 |
| U08 | 交付与清理 | 命令/SHA/hash/截图/开销、专用目录/PID+创建身份与端口清理；不改用户运行中安装版 |

算法/输入验证可用明确标注的受控数据，不能代替真实GitHub/下载/UI/安装链。专用本地安装器只证明交接机制，不等同官方更高Release更新成功。不能伪造生产版本或业务API让UI出现更新；若无确实包含新代码的更高已发布Release或无法安全安装，如实记录未执行，交付可审查产物与命令。
