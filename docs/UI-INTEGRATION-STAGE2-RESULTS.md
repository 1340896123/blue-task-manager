# 界面接入阶段2：Qt接线结果

日期：2026-11-04。基于阶段1 `78984858c3c88347a334be44f555b60352f948b7`，范围及成熟来源见 [UI-INTEGRATION.md](UI-INTEGRATION.md)。保留原工作区未提交改动，不改后端严格偏好校验。

## 已完成

- 按实际字段映射I/O、网络合成列、GPU引擎、用户别名及启动项位置原因；请求特定字段时不混入其它字段错误，tooltip和可访问说明共用真实原因。
- 进程详情接入已有创建时间、父PID/会话、累计CPU/I/O、优先级、速率及网络/GPU/服务独立来源时间；合并顶层不可用原因，刷新失败保留最后有效观测。
- 历史条目提供真实会话观测详情；已退出条目仍可查看，存活身份的定位再次校验实际创建身份。
- 用户会话跳转清空旧查询、账户和状态筛选，并保存实际会话过滤；所有新页面查询为空，旧全局搜索只迁移到初始进程页，返回时恢复各页自己的查询。
- 性能显示内存各可见字段、逻辑CPU的实际失败、缺少设备的来源状态；磁盘说明过滤已移除的活动时间字段。HWiNFO缺失提供传感器模式/共享内存/许可检查步骤，迟到响应不覆盖其它资源页面。
- 设置和来源菜单显示真实进程网络provider模式、状态及窗口时间。

## 实际检查

1. `scripts/import-msvc.ps1` 后执行 `cmake --build output/qt-build --parallel 4`：Qt Release构建退出0。
2. `cmake -S src/Takao.Qt -B output/qt-tests-build -G Ninja '-DCMAKE_PREFIX_PATH=E:/taskManager/output/Qt' -DCMAKE_BUILD_TYPE=Release -DTAKAO_BUILD_INTEGRATION_TESTS=ON`，随后 `cmake --build output/qt-tests-build --target Takao.Qt.Wiring --parallel 4`：退出0。
3. `scripts/test-qt-wiring.ps1 -ReleaseDirectory E:\taskManager\output\ui-integration\stage2-lite-release -OutputDirectory E:\taskManager\output\ui-integration\stage2-wiring-final`：退出0；`results.json` 为 **PASS，21项**。验证Qt实际控件→生产HTTP→JSON.NET/CsWin32：七个新页面不继承进程查询、返回恢复进程查询、八页偏好通过严格校验、详情字段、会话清查询和真实过滤读回、专用真实进程结束后的历史详情与禁用存活定位。
4. 本轮独立轻量fixture使用当前Host的framework-dependent构建、现有Qt运行库和新原生Wiring目标，位于`output/ui-integration/stage2-wiring-final/<GUID>/application`；测试窗口1920×1080/100%。不覆盖主发布目录，不把组件方法调用计为物理键鼠证据。
5. 最终测试正常关闭Qt及Host，`host.log`结尾为实际偏好POST 200与Application is shutting down。中间诊断停滞Host86400已按完整GUID路径和真实创建FILETIME134382277345860537精确清理，`output/ui-integration/stage2-stalled-cleanup.json`；未按进程名称批量结束。
6. `git diff --check`：退出0。

早期新检查的初始化时序及临时QJsonValueRef问题已修正，原诊断结果保留，不累计为通过。本轮21项只证明所列接线；详情的进一步实值对照、全八页1080P截图、物理键鼠、最终自包含发布及生命周期由阶段3完成。没有HWiNFO成功源，不控制用户服务/启动项/IFEO，不把GPU无实例或权限不足转0。

## 后续复现

使用最终构建的发布目录时直接运行 `scripts/test-qt-wiring.ps1`；须先构建`Takao.Qt.Wiring`及既有`Takao.TestWorkload`。新检查使用公开Qt组件方法和真实后端，独立临时偏好仅用于验证恢复行为，不插入模拟业务行或数值。每轮删除专用结果文件，保存所创建Host的身份和清理状态；超时只处理原进程对象。
