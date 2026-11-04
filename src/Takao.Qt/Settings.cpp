#include "MainWindow.h"
#include "Theme.h"
#include <QApplication>
#include <QDesktopServices>
#include <QDialog>
#include <QFormLayout>
#include <QFrame>
#include <QHBoxLayout>
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
    settingsPage_->setObjectName("settingsPage");
    auto outer = new QVBoxLayout(settingsPage_);
    outer->setContentsMargins(0, 0, 0, 0);
    auto scroll = new QScrollArea(settingsPage_);
    scroll->setObjectName("settingsScroll");
    scroll->setWidgetResizable(true);
    scroll->setAlignment(Qt::AlignLeft | Qt::AlignTop);
    auto content = new QWidget(scroll);
    content->setObjectName("settingsContent");
    content->setMaximumWidth(Theme::settingsWidth);
    auto layout = new QVBoxLayout(content);
    layout->setContentsMargins(0, 0, 0, Theme::space4);
    layout->setSpacing(Theme::space6);
    auto group = [&](const QString& title) {
        auto section = new QWidget(content);
        section->setProperty("settingsGroup", true);
        section->setProperty("groupTitle", title);
        auto sectionLayout = new QVBoxLayout(section);
        sectionLayout->setContentsMargins(0, 0, 0, 0);
        sectionLayout->setSpacing(Theme::space1);
        auto heading = new QLabel(title, section);
        Theme::type(heading, Theme::Type::Section, true);
        sectionLayout->addWidget(heading);
        sectionLayout->addSpacing(Theme::space2);
        layout->addWidget(section);
        return section;
    };
    auto card = [&](QWidget* section, const QString& title, const QString& note, QWidget* control, int glyph) {
        auto frame = new QFrame(section);
        frame->setProperty("settingsCard", true);
        frame->setMinimumHeight(Theme::settingMinHeight);
        auto row = new QHBoxLayout(frame);
        row->setContentsMargins(Theme::space4, Theme::space4, Theme::space4, Theme::space4);
        row->setSpacing(Theme::space6);
        auto icon = new QLabel(frame);
        icon->setPixmap(Theme::icon(glyph).pixmap(Theme::iconSize, Theme::iconSize));
        icon->setFixedSize(Theme::iconSize, Theme::iconSize);
        row->addWidget(icon, 0, Qt::AlignVCenter);
        auto copy = new QVBoxLayout();
        copy->setSpacing(Theme::space1);
        auto name = new QLabel(title, frame);
        name->setWordWrap(true);
        Theme::type(name, Theme::Type::Body, true);
        copy->addWidget(name);
        if (!note.isEmpty())
            copy->addWidget(description(note, frame));
        row->addLayout(copy, 1);
        if (control) {
            control->setParent(frame);
            row->addWidget(control, 0, Qt::AlignVCenter);
        }
        qobject_cast<QVBoxLayout*>(section->layout())->addWidget(frame);
        return frame;
    };
    auto choice = [](QComboBox* control) {
        control->setFixedWidth(Theme::settingControlWidth);
        control->setFixedHeight(Theme::controlHeight);
    };
    auto stateLabel = [&](const QString& text) {
        auto label = description(text, content);
        label->setFixedWidth(Theme::pathWidth);
        label->setTextInteractionFlags(Qt::TextSelectableByMouse | Qt::TextSelectableByKeyboard);
        return label;
    };
    auto sampling = group("采样与显示");
    interval_ = new Theme::ComboBox(content);
    interval_->addItem("每 1 秒", 1000);
    interval_->addItem("每 2 秒", 2000);
    interval_->addItem("每 5 秒", 5000);
    interval_->setAccessibleName("采样间隔");
    choice(interval_);
    card(sampling, "更新间隔", "控制 Windows 资源和进程数据的采样频率。", interval_, 2);
    freezeBox_ = new QCheckBox("冻结当前显示，后台仍继续采集", content);
    freezeBox_->setAccessibleName("冻结当前显示");
    card(sampling, "冻结显示", "保留当前画面，后台继续采集。暂停采集请使用页头按钮。", freezeBox_, 1);
    auto appearance = group("外观与窗口");
    density_ = new Theme::ComboBox(content);
    density_->addItem("紧凑 · 32 px 数据行", "compact");
    density_->addItem("标准 · 40 px 数据行", "standard");
    density_->setAccessibleName("数据密度");
    choice(density_);
    card(appearance, "列表密度", "调整数据行间距，保留全部表格信息。", density_, 5);
    zoomBox_ = new Theme::ComboBox(content);
    for (double factor : {.75, 1., 1.25, 1.5, 1.75, 2.})
        zoomBox_->addItem(QString::number(factor * 100, 'f', 0) + " %", factor);
    zoomBox_->setAccessibleName("文字大小");
    choice(zoomBox_);
    card(appearance, "文字大小", "调整界面字体大小，偏好保存在当前账户中。", zoomBox_, 7);
    topmost_ = new QCheckBox("使 Takao 始终置顶", content);
    topmost_->setAccessibleName("始终置顶");
    card(appearance, "始终置顶", "当前状态由 Windows 实际读回；尚未确认时保留未知状态。", topmost_, 0);
    preferenceStatus_ = description("界面偏好保存在当前 Windows 账户中。", appearance);
    Theme::type(preferenceStatus_, Theme::Type::Caption);
    qobject_cast<QVBoxLayout*>(appearance->layout())->addWidget(preferenceStatus_);
    auto integration = group("任务管理器入口");
    integrationStatus_ = stateLabel("正在读取真实入口…");
    integrationStatus_->setAccessibleName("任务管理器入口实际状态");
    card(integration, "当前入口", "查看 Windows 任务管理器入口当前指向的位置。", integrationStatus_, 0);
    auto integrationControls = new QWidget(content);
    auto integrationButtons = new QGridLayout(integrationControls);
    integrationButtons->setContentsMargins(0, 0, 0, 0);
    integrationButtons->setSpacing(Theme::space2);
    auto read = button("刷新入口实际状态", integrationControls);
    integrationAction_ = button("启用接管…", integrationControls);
    disableIntegration_ = button("取消接管…", integrationControls);
    integrationButtons->addWidget(read, 0, 0, 1, 2);
    integrationButtons->addWidget(integrationAction_, 1, 0);
    integrationButtons->addWidget(disableIntegration_, 1, 1);
    card(integration, "管理任务管理器入口",
         "接管后，任务栏“任务管理器”、Ctrl+Shift+Esc 等入口将打开 Takao，影响所有用户。"
         "启用前需确认；取消仅恢复 Windows 标准任务管理器。移动或删除程序目录前请先取消接管。",
         integrationControls, 6);
    auto updates = group("版本与更新");
    updateStatus_ = stateLabel("正在读取当前版本…");
    updateStatus_->setAccessibleName("版本与更新实际状态");
    card(updates, "当前版本", "检查可用版本、下载进度与安装资格。", updateStatus_, 9);
    auto updateControls = new QWidget(content);
    auto updateButtons = new QGridLayout(updateControls);
    updateButtons->setContentsMargins(0, 0, 0, 0);
    updateButtons->setSpacing(Theme::space2);
    checkUpdate_ = button("检查更新", updateControls);
    downloadUpdate_ = button("下载更新", updateControls);
    installNow_ = button("立即更新…", updateControls);
    installNext_ = button("下次启动更新", updateControls);
    cancelUpdate_ = button("取消下载", updateControls);
    updateButtons->addWidget(checkUpdate_, 0, 0);
    updateButtons->addWidget(downloadUpdate_, 0, 1);
    updateButtons->addWidget(cancelUpdate_, 0, 2);
    updateButtons->addWidget(installNow_, 1, 0);
    updateButtons->addWidget(installNext_, 1, 1, 1, 2);
    card(updates, "获取更新", "从项目 GitHub Releases 下载并校验。"
         "便携目录可检查与下载；自动安装按实际安装资格提供。", updateControls, 9);
    auto sources = group("来源与能力");
    sourceStatus_ = stateLabel("正在等待 Windows 首次真实采样…");
    sourceStatus_->setAccessibleName("Windows 数据采样状态");
    card(sources, "Windows 实时数据", "显示本机资源、进程、启动项、登录会话与服务。"
         "无法读取或尚未预热的指标保留具体原因。", sourceStatus_, 1);
    auto sourceControls = new QWidget(content);
    auto sourceButtons = new QGridLayout(sourceControls);
    sourceButtons->setContentsMargins(0, 0, 0, 0);
    sourceButtons->setSpacing(Theme::space2);
    auto status = button("查看采样状态", sourceControls);
    auto startup = button("Windows 启动设置", sourceControls);
    auto service = button("Windows 服务管理器", sourceControls);
    auto reset = button("重置界面偏好…", sourceControls);
    sourceButtons->addWidget(status, 0, 0);
    sourceButtons->addWidget(startup, 0, 1);
    sourceButtons->addWidget(service, 1, 0);
    sourceButtons->addWidget(reset, 1, 1);
    card(sources, "系统工具与界面偏好", "查看详细来源、打开 Windows 官方管理工具，"
         "或重置搜索、列宽与界面设置。", sourceControls, 7);
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
        const auto normalSize = normalGeometry().size();
        auto w = preferences_["window"].toObject();
        w["width"] = std::clamp(normalSize.width(), 960, 3840);
        w["height"] = std::clamp(normalSize.height(), 640, 2160);
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
        if (Theme::confirm(this, "立即更新", "更新将关闭当前程序并运行已校验的安装器，完成后重新启动。",
                           "立即更新"))
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
            "后端来源  Microsoft.Windows.CsWin32", networkStatusText(latest_["processNetwork"].toObject()),
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
        if (!Theme::confirm(this, "重置界面偏好",
                            "重置搜索、列宽和界面设置；保留首次入口选择标记，不更改 Windows 配置。",
                            "重置偏好", true))
            return;
        const auto dismissed = preferences_["taskManagerPromptDismissed"];
        const auto normalSize = normalGeometry().size();
        auto window = preferences_["window"].toObject();
        window["width"] = std::clamp(normalSize.width(), 960, 3840);
        window["height"] = std::clamp(normalSize.height(), 640, 2160);
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
        sidebar_->setFixedWidth(Theme::sidebarWidth);
        for (int i = 0; i < nav_.size(); ++i) {
            nav_[i]->setText(titles_[i]);
            nav_[i]->setProperty("collapsed", false);
            Theme::repolish(nav_[i]);
        }
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
    sourceStatus_->setText(QString("最近真实采样  %1\n%2\n%3")
        .arg(formatValue(latest_["capturedAtUtc"], "date"),
             connected_ ? "已连接本机 Windows · 不可读指标保留未知及原因。" : "连接中断 · 保留最后成功采样。",
             networkStatusText(latest_["processNetwork"].toObject())));
}
void MainWindow::applyAppearance(bool restoreWindowState) {
    const auto window = preferences_["window"].toObject();
    zoom_ = std::clamp(window["zoomFactor"].toDouble(1), .75, 2.);
    auto font = qApp->font();
    font.setPixelSize(qRound(static_cast<int>(Theme::Type::Body) * zoom_));
    qApp->setFont(font);
    Theme::refreshTypography();
    if (!shown_.isEmpty())
        rebuildResources();
    // Larger text retains its existing preference behavior without clipping inputs.
    for (auto control : findChildren<QWidget*>()) {
        if (qobject_cast<QLineEdit*>(control) || qobject_cast<QComboBox*>(control) || qobject_cast<QSpinBox*>(control))
            control->setFixedHeight(std::max(control == search_ ? Theme::searchHeight : Theme::controlHeight,
                                             control->fontMetrics().height() + Theme::space3));
        if (auto push = qobject_cast<QPushButton*>(control)) {
            const auto standard = push->property("command").toBool() && !push->property("iconCommand").toBool()
                                      ? Theme::secondaryCommandHeight : Theme::commandHeight;
            push->setFixedHeight(std::max(standard, push->fontMetrics().height() + Theme::space3));
        }
    }
    if (auto top = findChild<QWidget*>("appTopArea"))
        top->setFixedHeight(std::max(Theme::topHeight, search_->height() + Theme::space2));
    const auto rowHeight = qRound((preferences_["density"].toString() == "standard" ? Theme::standardRow : Theme::compactRow) * zoom_);
    for (auto view : {table_, sensors_}) {
        const auto scroll = view->verticalScrollBar()->value();
        view->setProperty("rowHeight", rowHeight);
        view->doItemsLayout();
        view->verticalScrollBar()->setValue(scroll);
    }
    sidebar_->setFixedWidth(preferences_["sidebarCollapsed"].toBool() ? Theme::sidebarCompact : Theme::sidebarWidth);
    for (int i = 0; i < nav_.size(); ++i) {
        nav_[i]->setText(preferences_["sidebarCollapsed"].toBool() ? QString() : titles_[i]);
        nav_[i]->setProperty("collapsed", preferences_["sidebarCollapsed"].toBool());
        Theme::repolish(nav_[i]);
    }
    navUpdate_->setText(preferences_["sidebarCollapsed"].toBool() ? QString() : "可用更新");
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
                const auto normalSize = normalGeometry().size();
                auto window = preferences_["window"].toObject();
                window["width"] = std::clamp(normalSize.width(), 960, 3840);
                window["height"] = std::clamp(normalSize.height(), 640, 2160);
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
                     Theme::state(integrationStatus_, "danger");
                     integrationAction_->setEnabled(false);
                     disableIntegration_->setEnabled(false);
                     if (first)
                         promptFirstLaunch();
                     return;
                 }
                 integration_ = state;
                 Theme::state(integrationStatus_, state["status"].toString() == "enabled" ? "success"
                     : state["status"].toString() == "disabled" ? "neutral" : "warning");
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
    d->resize(Theme::detailDialogWidth, 420);
    auto layout = Theme::dialogLayout(d);
    auto text = new QLabel("让任务栏“任务管理器”、Ctrl+Shift+Esc 等 Windows 入口打开 "
                           "Takao。\n\n影响此计算机所有用户，系统启动时仍请求管理员授权。可在设置取消并恢复 "
                           "Windows 标准任务管理器；移动目录前先取消接管。\n\n实际状态：" +
                               integrationLabel(integration_),
                           d);
    text->setWordWrap(true);
    text->setTextInteractionFlags(Qt::TextSelectableByMouse | Qt::TextSelectableByKeyboard);
    layout->addWidget(text);
    auto buttons = new QHBoxLayout();
    buttons->setSpacing(Theme::space2);
    buttons->addStretch();
    auto defer = button("暂不替换", d);
    auto settings = button("前往设置管理", d);
    defer->setDefault(true);
    buttons->addWidget(defer);
    buttons->addWidget(settings);
    auto replace = button("替换标准任务管理器", d, true);
    replace->setEnabled(integration_["status"].toString() == "disabled" &&
                        integration_["canEnable"].toBool() && integration_["canWrite"].toBool());
    buttons->addWidget(replace);
    auto failure = new QLabel(d);
    failure->setWordWrap(true);
    failure->setProperty("fieldError", true);
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
    d->resize(Theme::detailDialogWidth, 440);
    auto layout = Theme::dialogLayout(d);
    QString message = enable ? "系统任务管理器入口将启动本目录 Takao，影响全部账户。启动时 Windows "
                               "请求管理员授权。\n移动或删除目录前必须先取消接管。"
                             : "取消后恢复 Windows 标准任务管理器，不恢复此前其它工具。";
    if (action == "takeOver")
        message += "\n\n将替换现有工具或目录的接管并关闭入口过滤配置。\n当前命令：" +
                   target["currentDebugger"].toString();
    message += "\n\n本目录入口：" + target["executablePath"].toString();
    auto label = new QLabel(message, d);
    label->setWordWrap(true);
    label->setTextInteractionFlags(Qt::TextSelectableByMouse | Qt::TextSelectableByKeyboard);
    layout->addWidget(label);
    auto error = new QLabel(d);
    error->setWordWrap(true);
    error->setProperty("fieldError", true);
    layout->addWidget(error);
    auto buttons = new QHBoxLayout();
    buttons->setSpacing(Theme::space2);
    buttons->addStretch();
    auto cancel = button("暂不更改", d);
    auto confirm = button(enable ? "确认接管" : "确认取消接管", d, true);
    cancel->setDefault(true);
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
            Theme::state(updateStatus_, "danger");
            return;
        }
        updates_ = state;
        Theme::state(updateStatus_, state["status"].toString() == "error" ? "danger"
            : state["status"].toString() == "unavailable" ? "warning" : "neutral");
        const bool available = state["canDownload"].toBool() || state["canInstall"].toBool() ||
                               state["status"].toString() == "downloading";
        navUpdate_->setVisible(available);
        navUpdate_->setText(preferences_["sidebarCollapsed"].toBool() ? QString() : "可用更新");
        navUpdate_->setToolTip(QString("%1 · %2").arg(state["candidateVersion"].toString(),
                            state["status"].toString() == "downloading" ? "正在下载并校验" : "查看可用更新"));
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
