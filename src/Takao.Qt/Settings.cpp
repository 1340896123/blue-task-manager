#include "MainWindow.h"
#include <QApplication>
#include <QDesktopServices>
#include <QDialog>
#include <QFormLayout>
#include <QGroupBox>
#include <QHBoxLayout>
#include <QMessageBox>
#include <QScrollArea>
#include <QScrollBar>
#include <QSignalBlocker>
#include <QVBoxLayout>

namespace {
QLabel* description(const QString& text, QWidget* parent) {
    auto label = new QLabel(text, parent);
    label->setWordWrap(true);
    label->setProperty("secondary", true);
    return label;
}
class IntegrationDialog final : public QDialog {
  public:
    using QDialog::QDialog;
    void reject() override {
        if (!property("busy").toBool())
            QDialog::reject();
    }
};
QString integrationLabel(const QJsonObject& state) {
    const QHash<QString, QString> labels{{"enabled", "本目录已接管"},
                                         {"disabled", "未接管 Windows 标准入口"},
                                         {"migrationRequired", "旧接管需要修复"},
                                         {"conflict", "已有其它工具或目录接管"},
                                         {"unavailable", "暂时无法确认"}};
    return labels.value(state["status"].toString(), "正在读取真实状态…");
}
} // namespace
void MainWindow::buildSettings() {
    settingsPage_ = new QWidget();
    auto outer = new QVBoxLayout(settingsPage_);
    outer->setContentsMargins(0, 0, 0, 0);
    auto scroll = new QScrollArea(settingsPage_);
    scroll->setWidgetResizable(true);
    auto content = new QWidget(scroll);
    auto layout = new QVBoxLayout(content);
    layout->setContentsMargins(0, 0, 16, 16);
    layout->setSpacing(20);
    auto sampling = new QGroupBox("采样与显示", content);
    auto sampleForm = new QFormLayout(sampling);
    sampleForm->setContentsMargins(20, 24, 20, 16);
    sampleForm->setSpacing(12);
    interval_ = new QComboBox(sampling);
    interval_->addItem("每 1 秒", 1000);
    interval_->addItem("每 2 秒", 2000);
    interval_->addItem("每 5 秒", 5000);
    interval_->setAccessibleName("采样间隔");
    sampleForm->addRow("更新间隔", interval_);
    freezeBox_ = new QCheckBox("冻结当前显示，后台仍继续采集", sampling);
    freezeBox_->setAccessibleName("冻结当前显示");
    sampleForm->addRow(freezeBox_);
    sampleForm->addRow(
        description("暂停采集使用页头按钮；冻结仅保留展示快照。二者均不会制造缺失时间的采样。", sampling));
    layout->addWidget(sampling);
    auto appearance = new QGroupBox("外观与窗口", content);
    auto appearanceForm = new QFormLayout(appearance);
    appearanceForm->setContentsMargins(20, 24, 20, 16);
    appearanceForm->setSpacing(12);
    density_ = new QComboBox(appearance);
    density_->addItem("紧凑 · 32 px 数据行", "compact");
    density_->addItem("标准 · 40 px 数据行", "standard");
    density_->setAccessibleName("数据密度");
    appearanceForm->addRow("列表密度", density_);
    zoomBox_ = new QComboBox(appearance);
    for (double factor : {.75, 1., 1.25, 1.5, 1.75, 2.})
        zoomBox_->addItem(QString::number(factor * 100, 'f', 0) + " %", factor);
    zoomBox_->setAccessibleName("文字大小");
    appearanceForm->addRow("文字大小", zoomBox_);
    topmost_ = new QCheckBox("使 Takao 始终置顶", appearance);
    topmost_->setAccessibleName("始终置顶");
    appearanceForm->addRow(topmost_);
    preferenceStatus_ = description("偏好通过 .NET JSON.NET 保存到当前账户配置目录。", appearance);
    appearanceForm->addRow(preferenceStatus_);
    layout->addWidget(appearance);
    auto integration = new QGroupBox("任务管理器入口", content);
    auto integrationLayout = new QVBoxLayout(integration);
    integrationLayout->setContentsMargins(20, 24, 20, 16);
    integrationLayout->setSpacing(12);
    integrationStatus_ = new QLabel("正在读取真实入口…", integration);
    integrationStatus_->setWordWrap(true);
    integrationLayout->addWidget(integrationStatus_);
    integrationLayout->addWidget(description("接管后，任务栏的“任务管理器”、Ctrl+Shift+Esc 等入口打开 "
                                             "Takao，影响这台计算机的所有用户。启用需要明确确认；取消仅恢复 "
                                             "Windows 标准任务管理器。移动或删除程序目录前，请先取消接管。",
                                             integration));
    auto integrationButtons = new QHBoxLayout();
    auto read = button("刷新入口实际状态", integration);
    integrationAction_ = button("启用接管…", integration);
    disableIntegration_ = button("取消接管…", integration);
    integrationButtons->addWidget(read);
    integrationButtons->addWidget(integrationAction_);
    integrationButtons->addWidget(disableIntegration_);
    integrationButtons->addStretch();
    integrationLayout->addLayout(integrationButtons);
    layout->addWidget(integration);
    auto updates = new QGroupBox("版本与更新", content);
    auto updatesLayout = new QVBoxLayout(updates);
    updatesLayout->setContentsMargins(20, 24, 20, 16);
    updatesLayout->setSpacing(12);
    updateStatus_ = new QLabel("正在读取当前版本…", updates);
    updateStatus_->setWordWrap(true);
    updatesLayout->addWidget(updateStatus_);
    auto updateButtons = new QHBoxLayout();
    checkUpdate_ = button("检查更新", updates);
    downloadUpdate_ = button("下载更新", updates);
    installNow_ = button("立即更新…", updates);
    installNext_ = button("下次启动更新", updates);
    cancelUpdate_ = button("取消下载", updates);
    for (auto b : {checkUpdate_, downloadUpdate_, installNow_, installNext_, cancelUpdate_})
        updateButtons->addWidget(b);
    updateButtons->addStretch();
    updatesLayout->addLayout(updateButtons);
    updatesLayout->addWidget(description("更新从项目 GitHub Releases 获取，后端验证发布身份和 "
                                         "SHA256。便携目录可检查及下载；自动安装以实际安装资格为准。",
                                         updates));
    layout->addWidget(updates);
    auto sources = new QGroupBox("来源与能力", content);
    auto sourceLayout = new QVBoxLayout(sources);
    sourceLayout->setContentsMargins(20, 24, 20, 16);
    sourceStatus_ = new QLabel("Windows → CsWin32 → .NET / JSON.NET → HTTP → Qt Widgets", sources);
    sourceStatus_->setWordWrap(true);
    sourceStatus_->setTextInteractionFlags(Qt::TextSelectableByMouse | Qt::TextSelectableByKeyboard);
    sourceLayout->addWidget(sourceStatus_);
    auto sourceButtons = new QHBoxLayout();
    auto status = button("查看采样状态", sources);
    auto startup = button("Windows 启动设置", sources);
    auto service = button("Windows 服务管理器", sources);
    auto reset = button("重置界面偏好…", sources);
    for (auto b : {status, startup, service, reset})
        sourceButtons->addWidget(b);
    sourceButtons->addStretch();
    sourceLayout->addLayout(sourceButtons);
    layout->addWidget(sources);
    layout->addStretch();
    scroll->setWidget(content);
    outer->addWidget(scroll);
    connect(interval_, &QComboBox::currentIndexChanged, this, [this] {
        if (!settingsSync_)
            applySampling(interval_->currentData().toInt(), latest_["paused"].toBool());
    });
    connect(freezeBox_, &QCheckBox::toggled, this, [this](bool checked) {
        if (settingsSync_)
            return;
        frozen_ = checked;
        if (!checked)
            shown_ = latest_;
        applySnapshot();
    });
    connect(density_, &QComboBox::currentIndexChanged, this, [this] {
        if (settingsSync_)
            return;
        preferences_["density"] = density_->currentData().toString();
        applyAppearance();
        saveSoon();
    });
    connect(zoomBox_, &QComboBox::currentIndexChanged, this, [this] {
        if (settingsSync_)
            return;
        const auto desiredZoom = zoomBox_->currentData().toDouble();
        auto w = preferences_["window"].toObject();
        w["width"] = std::clamp(width(), 960, 3840);
        w["height"] = std::clamp(height(), 640, 2160);
        w["zoomFactor"] = desiredZoom;
        if (actualTopmost_)
            w["alwaysOnTop"] = *actualTopmost_;
        preferences_["window"] = w;
        applyAppearance();
        saveSoon();
    });
    connect(topmost_, &QCheckBox::toggled, this, [this](bool desired) {
        if (!settingsSync_)
            syncTopmost(desired, true);
    });
    connect(read, &QPushButton::clicked, this, [this] { readIntegration(); });
    connect(integrationAction_, &QPushButton::clicked, this, [this] {
        applyIntegration(integration_["canRepair"].toBool()                ? "repair"
                         : integration_["status"].toString() == "conflict" ? "takeOver"
                                                                           : "enable");
    });
    connect(disableIntegration_, &QPushButton::clicked, this, [this] { applyIntegration("disable"); });
    connect(checkUpdate_, &QPushButton::clicked, this, [this] { updateCommand("check"); });
    connect(downloadUpdate_, &QPushButton::clicked, this, [this] { updateCommand("download"); });
    connect(cancelUpdate_, &QPushButton::clicked, this, [this] { updateCommand("cancel"); });
    connect(installNow_, &QPushButton::clicked, this, [this] {
        if (QMessageBox::question(
                this, "立即更新", "更新将关闭当前程序并运行已校验的安装器，完成后重新启动。",
                QMessageBox::Yes | QMessageBox::Cancel, QMessageBox::Cancel) == QMessageBox::Yes)
            updateCommand("install", "now");
    });
    connect(installNext_, &QPushButton::clicked, this, [this] { updateCommand("install", "nextStart"); });
    connect(startup, &QPushButton::clicked, this,
            [this] { post("desktop/open-system-location", {{"location", "startupSettings"}}); });
    connect(service, &QPushButton::clicked, this,
            [this] { post("desktop/open-system-location", {{"location", "services"}}); });
    connect(status, &QPushButton::clicked, this, [this] {
        QStringList lines{
            "采样时间  " + formatValue(latest_["capturedAtUtc"], "date"),
            "后端来源  Microsoft.Windows.CsWin32", reasonText(latest_["processNetwork"].toObject()),
            reasonText(latest_["telemetry"].toObject()), reasonText(latest_["hwInfo"].toObject())};
        const auto capabilities = bootstrap_["capabilities"].toObject();
        for (auto it = capabilities.begin(); it != capabilities.end(); ++it) {
            const auto c = it.value().toObject();
            lines << it.key() + " · " + c["source"].toString() + " · " +
                         (c["available"].toBool() ? "已接入" : "未接入") + " " + c["reason"].toString();
        }
        presentText("来源与能力", lines.join("\n"));
    });
    connect(reset, &QPushButton::clicked, this, [this] {
        if (QMessageBox::question(
                this, "重置界面偏好", "重置搜索、列宽和界面设置；保留首次入口选择标记，不更改 Windows 配置。",
                QMessageBox::Yes | QMessageBox::Cancel, QMessageBox::Cancel) != QMessageBox::Yes)
            return;
        const auto dismissed = preferences_["taskManagerPromptDismissed"];
        auto window = preferences_["window"].toObject();
        window["width"] = std::clamp(width(), 960, 3840);
        window["height"] = std::clamp(height(), 640, 2160);
        window["zoomFactor"] = 1;
        if (actualTopmost_)
            window["alwaysOnTop"] = *actualTopmost_;
        preferences_ = {{"version", 1},
                        {"density", "compact"},
                        {"sidebarCollapsed", false},
                        {"processView", "flat"},
                        {"taskManagerPromptDismissed", dismissed},
                        {"window", window},
                        {"pages", QJsonObject()}};
        applyAppearance();
        sidebar_->setFixedWidth(204);
        for (int i = 0; i < nav_.size(); ++i)
            nav_[i]->setText(titles_[i]);
        saveSoon();
        switchPage(page_);
        notify("界面偏好已重置，Windows 配置保持实际状态。");
    });
}
void MainWindow::updateSettings() {
    if (!interval_)
        return;
    settingsSync_ = true;
    interval_->setCurrentIndex(std::max(0, interval_->findData(intervalMs_)));
    density_->setCurrentIndex(std::max(0, density_->findData(preferences_["density"].toString())));
    zoomBox_->setCurrentIndex(std::max(0, zoomBox_->findData(zoom_)));
    freezeBox_->setChecked(frozen_);
    topmost_->setTristate(!actualTopmost_.has_value());
    topmost_->setCheckState(actualTopmost_ ? (*actualTopmost_ ? Qt::Checked : Qt::Unchecked)
                                           : Qt::PartiallyChecked);
    topmost_->setToolTip(actualTopmost_ ? QStringLiteral("Windows 已读回当前窗口状态。")
                                        : QStringLiteral("尚未从 Windows 确认窗口置顶状态。"));
    interval_->setEnabled(connected_);
    topmost_->setEnabled(connected_ && actualTopmost_.has_value() && !applyingTopmost_);
    settingsSync_ = false;
    sourceStatus_->setText(
        QString(
            "Qt %1 Widgets + .NET 10 · JSON.NET · CsWin32\n最近真实采样  %2\nWindows 原生数据：CPU / 内存 / "
            "进程 / SCM / Startup / WTS / PDH / IP Helper / ETW；不可读或未预热字段保持未知及原因。")
            .arg(qVersion(), formatValue(latest_["capturedAtUtc"], "date")));
}
void MainWindow::applyAppearance(bool restoreWindowState) {
    const auto window = preferences_["window"].toObject();
    zoom_ = std::clamp(window["zoomFactor"].toDouble(1), .75, 2.);
    auto font = qApp->font();
    font.setPixelSize(qRound(14 * zoom_));
    qApp->setFont(font);
    const auto rowHeight = qRound((preferences_["density"].toString() == "standard" ? 40 : 32) * zoom_);
    for (auto view : {table_, sensors_}) {
        const auto scroll = view->verticalScrollBar()->value();
        view->setProperty("rowHeight", rowHeight);
        view->doItemsLayout();
        view->verticalScrollBar()->setValue(scroll);
    }
    sidebar_->setFixedWidth(preferences_["sidebarCollapsed"].toBool() ? 56 : 204);
    for (int i = 0; i < nav_.size(); ++i)
        nav_[i]->setText(preferences_["sidebarCollapsed"].toBool() ? QString() : titles_[i]);
    updateSettings();
    if (restoreWindowState) {
        const auto desired = window["alwaysOnTop"];
        syncTopmost(desired.isBool() ? std::optional<bool>(desired.toBool()) : std::nullopt);
    }
}
void MainWindow::syncTopmost(std::optional<bool> desired, bool persist) {
    if (!connected_ || applyingTopmost_)
        return;
    applyingTopmost_ = true;
    updateSettings();
    const auto finish = [this, desired, persist](const QJsonObject& state, const QString& error) {
        applyingTopmost_ = false;
        if (state["available"].toBool() && state["alwaysOnTop"].isBool()) {
            actualTopmost_ = state["alwaysOnTop"].toBool();
            if (desired && *actualTopmost_ != *desired)
                notify("Windows 读回的窗口置顶状态与请求不一致，请重新核验后再操作。", true);
            if (persist && desired && *actualTopmost_ == *desired) {
                auto window = preferences_["window"].toObject();
                window["width"] = std::clamp(width(), 960, 3840);
                window["height"] = std::clamp(height(), 640, 2160);
                window["zoomFactor"] = zoom_;
                window["alwaysOnTop"] = *actualTopmost_;
                preferences_["window"] = window;
                saveSoon();
            }
        } else {
            // Missing HWND/read failures are unknown, never unchecked state
            // mirrored into the persisted user preference.
            actualTopmost_.reset();
        }
        if (!error.isEmpty())
            notify(error, true);
        updateSettings();
    };
    api_.get("desktop/window-state", this,
             [this, desired, finish](const QJsonObject& state, const QString& error, int) {
                 if (!error.isEmpty() || !state["available"].toBool() || !state["alwaysOnTop"].isBool() ||
                     !desired || state["alwaysOnTop"].toBool() == *desired) {
                     finish(state, error);
                     return;
                 }
                 // This established API is a toggle: read first, toggle at
                 // most once, and read actual Windows state again.
                 api_.post("window/always-on-top", {}, this,
                           [this, finish](const QJsonObject&, const QString& failure, int) {
                               api_.get("desktop/window-state", this,
                                        [finish, failure](const QJsonObject& actual, const QString& readError,
                                                          int) {
                                            finish(actual, failure.isEmpty() ? readError : failure);
                                        });
                           });
             });
}
void MainWindow::readIntegration(bool first) {
    if (!connected_)
        return;
    api_.get("desktop/task-manager-integration", this,
             [this, first](const QJsonObject& state, const QString& error, int) {
                 if (!error.isEmpty()) {
                     integrationStatus_->setText(error);
                     integrationAction_->setEnabled(false);
                     disableIntegration_->setEnabled(false);
                     if (first)
                         promptFirstLaunch();
                     return;
                 }
                 integration_ = state;
                 QStringList parts{integrationLabel(state), "入口  " + state["executablePath"].toString()};
                 if (!state["conflictReason"].isNull())
                     parts << state["conflictReason"].toString();
                 if (!state["readError"].isNull())
                     parts << state["readError"].toObject()["message"].toString();
                 if (!state["writeError"].isNull())
                     parts << state["writeError"].toObject()["message"].toString();
                 integrationStatus_->setText(parts.join("\n"));
                 integrationAction_->setText(state["canRepair"].toBool()                ? "修复旧接管…"
                                             : state["status"].toString() == "conflict" ? "接管现有入口…"
                                                                                        : "启用接管…");
                 integrationAction_->setEnabled(connected_ && state["canWrite"].toBool() &&
                                                (state["canEnable"].toBool() ||
                                                 state["canTakeOver"].toBool() ||
                                                 state["canRepair"].toBool()));
                 disableIntegration_->setEnabled(connected_ && state["canDisable"].toBool());
                 if (first) {
                     if (state["status"].toString() == "enabled" && state["enabled"].toBool()) {
                         if (!preferences_["taskManagerPromptDismissed"].toBool()) {
                             preferences_["taskManagerPromptDismissed"] = true;
                             saveSoon();
                         }
                     } else
                         promptFirstLaunch();
                 }
             });
}
void MainWindow::promptFirstLaunch() {
    if (firstPrompt_ || preferences_["taskManagerPromptDismissed"].toBool() || loadingPreferences_)
        return;
    firstPrompt_ = true;
    auto d = new IntegrationDialog(this);
    d->setAttribute(Qt::WA_DeleteOnClose);
    d->setWindowTitle("替换标准任务管理器？");
    d->setModal(true);
    d->resize(680, 360);
    auto layout = new QVBoxLayout(d);
    auto text = new QLabel("让任务栏“任务管理器”、Ctrl+Shift+Esc 等 Windows 入口打开 "
                           "Takao。\n\n影响此计算机所有用户，系统启动时仍请求管理员授权。可在设置取消并恢复 "
                           "Windows 标准任务管理器；移动目录前先取消接管。\n\n实际状态：" +
                               integrationLabel(integration_),
                           d);
    text->setWordWrap(true);
    layout->addWidget(text);
    auto buttons = new QHBoxLayout();
    buttons->addStretch();
    auto defer = button("暂不替换", d);
    auto settings = button("前往设置管理", d, true);
    buttons->addWidget(defer);
    buttons->addWidget(settings);
    auto replace = button("替换标准任务管理器", d, true);
    replace->setEnabled(integration_["status"].toString() == "disabled" &&
                        integration_["canEnable"].toBool() && integration_["canWrite"].toBool());
    buttons->addWidget(replace);
    auto failure = new QLabel(d);
    failure->setWordWrap(true);
    failure->setStyleSheet("color:#A4262C;");
    layout->addWidget(failure);
    layout->addLayout(buttons);
    connect(replace, &QPushButton::clicked, d, [this, d, defer, settings, replace, failure] {
        d->setProperty("busy", true);
        for (auto b : {defer, settings, replace})
            b->setEnabled(false);
        replace->setText("正在写入并核验…");
        api_.post("desktop/task-manager-integration",
                  {{"enabled", true}, {"expectedRevision", integration_["revision"]}}, d,
                  [this, d, defer, settings, replace, failure](const QJsonObject& result,
                                                               const QString& error, int) {
                      d->setProperty("busy", false);
                      defer->setEnabled(true);
                      settings->setEnabled(true);
                      replace->setText("替换标准任务管理器");
                      if (!error.isEmpty()) {
                          failure->setText(error + " 请重新核验入口后再决定下一步。");
                          readIntegration();
                          return;
                      }
                      const auto actual = result["state"].toObject();
                      if (actual["status"].toString() != "enabled" || !actual["enabled"].toBool()) {
                          failure->setText("未能读回确认替换成功。请到设置重新读取实际入口。");
                          return;
                      }
                      integration_ = actual;
                      d->accept();
                      notify(result["message"].toString());
                      readIntegration();
                  });
    });
    connect(defer, &QPushButton::clicked, d, &QDialog::reject);
    connect(settings, &QPushButton::clicked, d, [this, d] {
        switchPage(7);
        d->accept();
    });
    connect(d, &QDialog::finished, this, [this] {
        preferences_["taskManagerPromptDismissed"] = true;
        saveSoon();
    });
    d->open();
    defer->setFocus();
}
void MainWindow::applyIntegration(QString action, bool first) {
    Q_UNUSED(first);
    const auto target = integration_;
    const bool enable = action != "disable";
    const bool can = action == "repair"     ? target["canRepair"].toBool()
                     : action == "takeOver" ? target["canTakeOver"].toBool()
                     : enable               ? target["canEnable"].toBool()
                                            : target["canDisable"].toBool();
    if (!can || !target["canWrite"].toBool()) {
        notify("当前真实入口状态或写权限不允许此操作，请刷新核验。", true);
        return;
    }
    auto d = new IntegrationDialog(this);
    d->setAttribute(Qt::WA_DeleteOnClose);
    d->setWindowTitle(enable ? "确认接管 Windows 任务管理器入口" : "取消入口接管");
    d->setModal(true);
    d->resize(760, 380);
    auto layout = new QVBoxLayout(d);
    QString message = enable ? "系统任务管理器入口将启动本目录 Takao，影响全部账户。启动时 Windows "
                               "请求管理员授权。\n移动或删除目录前必须先取消接管。"
                             : "取消后恢复 Windows 标准任务管理器，不恢复此前其它工具。";
    if (action == "takeOver")
        message += "\n\n将替换现有工具或目录的接管并关闭入口过滤配置。\n当前命令：" +
                   target["currentDebugger"].toString();
    message += "\n\n本目录入口：" + target["executablePath"].toString();
    auto label = new QLabel(message, d);
    label->setWordWrap(true);
    layout->addWidget(label);
    auto error = new QLabel(d);
    error->setWordWrap(true);
    error->setStyleSheet("color:#A4262C;");
    layout->addWidget(error);
    auto buttons = new QHBoxLayout();
    buttons->addStretch();
    auto cancel = button("暂不更改", d);
    auto confirm = button(enable ? "确认接管" : "确认取消接管", d, true);
    buttons->addWidget(cancel);
    buttons->addWidget(confirm);
    layout->addLayout(buttons);
    connect(cancel, &QPushButton::clicked, d, &QDialog::reject);
    connect(confirm, &QPushButton::clicked, d, [this, d, target, enable, action, error, cancel, confirm] {
        d->setProperty("busy", true);
        cancel->setEnabled(false);
        confirm->setEnabled(false);
        confirm->setText("正在写入并读回…");
        api_.post("desktop/task-manager-integration",
                  {{"enabled", enable},
                   {"expectedRevision", target["revision"]},
                   {"repair", action == "repair"},
                   {"takeOver", action == "takeOver"}},
                  d,
                  [this, d, enable, error, cancel, confirm](const QJsonObject& result, const QString& failure,
                                                            int) {
                      d->setProperty("busy", false);
                      cancel->setEnabled(true);
                      confirm->setEnabled(true);
                      confirm->setText(enable ? "确认接管" : "确认取消接管");
                      if (!failure.isEmpty()) {
                          error->setText(failure +
                                         "\n写入结果可能已变化；请关闭并重新读取实际状态后再决定下一步。");
                          confirm->setEnabled(false);
                          readIntegration();
                          return;
                      }
                      const auto actual = result["state"].toObject();
                      if (actual["enabled"].isNull() || actual["enabled"].toBool() != enable) {
                          error->setText("尚未读回确认，不能标记成功。请重新读取实际状态。");
                          confirm->setEnabled(false);
                          readIntegration();
                          return;
                      }
                      integration_ = actual;
                      d->accept();
                      notify(result["message"].toString());
                      preferences_["taskManagerPromptDismissed"] = true;
                      saveSoon();
                      readIntegration();
                  });
    });
    d->open();
    cancel->setFocus();
}
void MainWindow::readUpdates() {
    if (!connected_)
        return;
    api_.get("updates", this, [this](const QJsonObject& state, const QString& error, int) {
        if (!error.isEmpty()) {
            updateStatus_->setText(error);
            return;
        }
        updates_ = state;
        const QHash<QString, QString> labels{
            {"checking", "正在检查"},         {"upToDate", "当前已是最新版本"},
            {"available", "发现可用更新"},    {"downloading", "正在下载并校验"},
            {"ready", "更新已下载并校验"},    {"scheduled", "已安排下次启动更新"},
            {"installing", "正在交接安装器"}, {"error", "更新操作失败"},
            {"unavailable", "当前不可用"}};
        QStringList parts{QString("当前 %1 · %2")
                              .arg(state["currentVersion"].toString(),
                                   labels.value(state["status"].toString(), state["status"].toString()))};
        if (!state["candidateVersion"].isNull())
            parts << "可用版本 " + state["candidateVersion"].toString();
        if (state["status"].toString() == "downloading")
            parts << formatValue(state["downloadedBytes"], "bytes") + " / " +
                         formatValue(state["totalBytes"], "bytes");
        if (!state["error"].isNull())
            parts << state["error"].toObject()["message"].toString();
        if (!state["installReason"].isNull())
            parts << state["installReason"].toString();
        updateStatus_->setText(parts.join("\n"));
        checkUpdate_->setEnabled(state["canCheck"].toBool());
        downloadUpdate_->setEnabled(state["canDownload"].toBool());
        cancelUpdate_->setEnabled(state["canCancel"].toBool());
        installNow_->setEnabled(state["canInstall"].toBool());
        installNext_->setEnabled(state["canInstall"].toBool());
    });
}
void MainWindow::updateCommand(const QString& command, const QString& mode) {
    for (auto b : {checkUpdate_, downloadUpdate_, installNow_, installNext_, cancelUpdate_})
        b->setEnabled(false);
    QJsonObject body;
    if (command != "check")
        body["expectedRevision"] = updates_["revision"];
    if (!mode.isEmpty())
        body["mode"] = mode;
    api_.post(
        "updates/" + command, body, this,
        [this](const QJsonObject& state, const QString& error, int) {
            if (!error.isEmpty())
                notify(error, true);
            else {
                updates_ = state;
                notify("更新状态已提交，后端将继续核验。");
            }
            readUpdates();
        },
        30000);
}
