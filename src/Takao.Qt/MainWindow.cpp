#include "MainWindow.h"
#include "Theme.h"
#include <QApplication>
#include <QClipboard>
#include <QCloseEvent>
#include <QDateTime>
#include <QFrame>
#include <QHBoxLayout>
#include <QHeaderView>
#include <QJsonDocument>
#include <QMenu>
#include <QPainterPath>
#include <QPointer>
#include <QResizeEvent>
#include <QRegularExpression>
#include <QScopedValueRollback>
#include <QSaveFile>
#include <QScrollArea>
#include <QScrollBar>
#include <QShortcut>
#include <QSignalBlocker>
#include <QStyledItemDelegate>
#include <QVBoxLayout>
#include <cmath>

namespace {
class RowDelegate final : public QStyledItemDelegate {
  public:
    using QStyledItemDelegate::QStyledItemDelegate;
    void paint(QPainter* painter, const QStyleOptionViewItem& option, const QModelIndex& index) const override {
        QStyleOptionViewItem item(option);
        initStyleOption(&item, index);
        const auto label = item.text;
        const auto icon = item.icon;
        item.text.clear();
        item.icon = {};
        item.state &= ~QStyle::State_HasFocus;
        const auto style = item.widget ? item.widget->style() : QApplication::style();
        style->drawControl(QStyle::CE_ItemViewItem, &item, painter, item.widget);
        auto textRect = option.rect.adjusted(Theme::space3, 0, -Theme::space3, 0);
        const auto status = index.data(DataModel::StatusRole).toString();
        if (!status.isEmpty()) {
            Theme::paintStatus(*painter, QRectF(textRect.left(), textRect.center().y() - Theme::statusIconSize / 2.,
                                               Theme::statusIconSize, Theme::statusIconSize), status);
            textRect.setLeft(textRect.left() + Theme::statusIconSize + Theme::space2);
        } else if (!icon.isNull()) {
            icon.paint(painter, QRect(textRect.left(), textRect.center().y() - Theme::iconSize / 2,
                                      Theme::iconSize, Theme::iconSize));
            textRect.setLeft(textRect.left() + Theme::iconSize + Theme::space2);
        }
        painter->save();
        painter->setFont(item.font);
        painter->setPen(option.state & QStyle::State_Selected ? Theme::text
                            : !status.isEmpty() ? Theme::stateColor(status) : item.palette.color(QPalette::Text));
        painter->drawText(textRect, item.displayAlignment | Qt::AlignVCenter,
                          QFontMetrics(item.font).elidedText(label, Qt::ElideRight, textRect.width()));
        if (option.state & QStyle::State_HasFocus) {
            painter->setPen(QPen(Theme::focus, Theme::focusWidth));
            painter->setBrush(Qt::NoBrush);
            painter->drawRoundedRect(option.rect.adjusted(1, 1, -1, -1), Theme::controlRadius, Theme::controlRadius);
        }
        painter->restore();
    }
    QSize sizeHint(const QStyleOptionViewItem& option, const QModelIndex& index) const override {
        auto size = QStyledItemDelegate::sizeHint(option, index);
        size.setHeight(option.widget ? option.widget->property("rowHeight").toInt() : Theme::compactRow);
        return size;
    }
};
class ResourceDelegate final : public QStyledItemDelegate {
  public:
    using QStyledItemDelegate::QStyledItemDelegate;
    void paint(QPainter* p, const QStyleOptionViewItem& option, const QModelIndex& index) const override {
        p->save();
        p->setRenderHint(QPainter::Antialiasing);
        const auto bounds = option.rect.adjusted(1, 1, -1, -1);
        const bool selected = option.state & QStyle::State_Selected;
        p->setPen(Qt::NoPen);
        p->setBrush(selected ? Theme::selection : option.state & QStyle::State_MouseOver ? Theme::hover : Theme::content);
        p->drawRoundedRect(bounds, Theme::controlRadius, Theme::controlRadius);
        if (selected) {
            p->setBrush(Theme::accent);
            p->drawRoundedRect(QRectF(bounds.left(), bounds.center().y() - Theme::indicatorHeight / 2.,
                                      Theme::indicatorWidth, Theme::indicatorHeight), 1.5, 1.5);
        }
        const auto lines = index.data().toString().split('\n');
        auto font = Theme::font(Theme::Type::Body, true);
        p->setFont(font);
        p->setPen(Theme::text);
        const auto lineHeight = QFontMetrics(font).height();
        const auto textWidth = bounds.width() - Theme::space6;
        p->drawText(QRect(bounds.left() + Theme::space3, bounds.top() + Theme::space2, textWidth, lineHeight),
                    Qt::AlignLeft | Qt::AlignVCenter,
                    QFontMetrics(font).elidedText(lines.value(0), Qt::ElideRight, textWidth));
        font = Theme::font(Theme::Type::Caption);
        p->setFont(font);
        p->setPen(Theme::secondary);
        p->drawText(QRect(bounds.left() + Theme::space3, bounds.top() + Theme::space2 + lineHeight + Theme::space1,
                          bounds.width() - 92, bounds.height() - lineHeight - Theme::space4), Qt::AlignLeft | Qt::AlignTop,
                    lines.mid(1).join("\n"));
        const QRectF plot(bounds.right() - 76, bounds.top() + lineHeight + Theme::space3, 64, 36);
        const auto primary = index.data(Qt::UserRole + 1).toList(),
                   secondary = index.data(Qt::UserRole + 2).toList();
        if (!primary.isEmpty()) {
            p->setBrush(Theme::surface);
            p->setPen(QPen(Theme::grid, 1));
            p->drawRect(plot);
            p->setBrush(Qt::NoBrush);
            double maximum = index.data(Qt::UserRole + 3).toBool() ? 100. : 1.;
            for (const auto& list : {primary, secondary})
                for (const auto& v : list)
                    if (std::isfinite(v.toPointF().y()))
                        maximum = std::max(maximum, v.toPointF().y());
            const auto first = primary.first().toPointF().x(), last = primary.last().toPointF().x();
            const auto span = std::max(1., last - first);
            for (int series = 0; series < 2; ++series) {
                const auto& list = series == 0 ? primary : secondary;
                QPainterPath path;
                bool move = true;
                double previous = 0;
                for (const auto& v : list) {
                    const auto point = v.toPointF();
                    if (!std::isfinite(point.y())) {
                        move = true;
                        continue;
                    }
                    const QPointF screen(plot.left() + (point.x() - first) / span * plot.width(),
                                         plot.bottom() -
                                             std::clamp(point.y() / maximum, 0., 1.) * plot.height());
                    if (move || point.x() - previous > 12)
                        path.moveTo(screen);
                    else
                        path.lineTo(screen);
                    move = false;
                    previous = point.x();
                }
                p->setPen(QPen(series == 0 ? Theme::accent : Theme::secondLine, Theme::sparkLineWidth,
                               series == 0 ? Qt::SolidLine : Qt::DashLine));
                p->drawPath(path);
            }
        }
        if (option.state & QStyle::State_HasFocus) {
            p->setPen(QPen(Theme::focus, Theme::focusWidth));
            p->setBrush(Qt::NoBrush);
            p->drawRoundedRect(bounds.adjusted(2, 2, -2, -2), Theme::controlRadius, Theme::controlRadius);
        }
        p->restore();
    }
};
double numberOrNan(const QJsonValue& value) {
    return value.isDouble() ? value.toDouble() : std::numeric_limits<double>::quiet_NaN();
}
QString textOrUnknown(const QJsonValue& value) {
    return value.isString() && !value.toString().isEmpty() ? value.toString() : QStringLiteral("—");
}
} // namespace
MainWindow::MainWindow(QUrl origin, qint64 hostPid) : api_(std::move(origin), this), expectedHost_(hostPid) {
    setWindowTitle("Takao");
    setMinimumSize(960, 640);
    resize(qEnvironmentVariableIsSet("TASKMANAGER_QT_1080P") ? 1920 : Theme::defaultWindowWidth,
           qEnvironmentVariableIsSet("TASKMANAGER_QT_1080P") ? 1080 : Theme::defaultWindowHeight);
    preferences_ = {{"version", 1},
                    {"density", "compact"},
                    {"sidebarCollapsed", false},
                    {"processView", "flat"},
                    {"taskManagerPromptDismissed", false},
                    {"pages", QJsonObject()}};
    buildUi();
    saveTimer_.setSingleShot(true);
    saveTimer_.setInterval(700);
    connect(&saveTimer_, &QTimer::timeout, this, &MainWindow::savePreferences);
    toastTimer_.setSingleShot(true);
    connect(&toastTimer_, &QTimer::timeout, toastArea_, &QWidget::hide);
    pollTimer_.setInterval(500);
    connect(&pollTimer_, &QTimer::timeout, this, &MainWindow::poll);
    slowTimer_.setInterval(3000);
    connect(&slowTimer_, &QTimer::timeout, this, [this] {
        if (page_ == 1 && selectedResource_ == "hwinfo:")
            readSensors();
        if (page_ == 7)
            readUpdates();
    });
    slowTimer_.start();
    QTimer::singleShot(0, this, &MainWindow::bootstrap);
}
QPushButton* MainWindow::button(const QString& text, QWidget* parent, bool primary) {
    auto b = new QPushButton(text, parent);
    b->setFixedHeight(std::max(Theme::commandHeight, b->fontMetrics().height() + Theme::space3));
    b->setAccessibleName(text);
    if (primary)
        b->setProperty("primary", true);
    return b;
}
QTreeView* MainWindow::createTable(DataModel* model, DataProxy* proxy, QWidget* parent) {
    auto view = new QTreeView(parent);
    proxy->setSourceModel(model);
    view->setModel(proxy);
    view->setSortingEnabled(true);
    view->setSelectionMode(QAbstractItemView::SingleSelection);
    view->setSelectionBehavior(QAbstractItemView::SelectRows);
    view->setEditTriggers(QAbstractItemView::NoEditTriggers);
    view->setAlternatingRowColors(false);
    view->setUniformRowHeights(true);
    view->setRootIsDecorated(false);
    view->setItemsExpandable(false);
    view->setAllColumnsShowFocus(true);
    view->setProperty("rowHeight", Theme::compactRow);
    view->setMouseTracking(true);
    view->setIconSize(QSize(Theme::iconSize, Theme::iconSize));
    view->setItemDelegate(new RowDelegate(view));
    view->setHorizontalScrollMode(QAbstractItemView::ScrollPerPixel);
    view->setVerticalScrollMode(QAbstractItemView::ScrollPerPixel);
    view->header()->setSectionsMovable(false);
    // Keep numeric columns at their chosen width; a last PID column must not
    // stretch its heading and value hundreds of pixels apart. Every column
    // remains interactive so all widths can still be changed by the user.
    view->header()->setStretchLastSection(false);
    view->header()->setMinimumSectionSize(64);
    view->header()->setFixedHeight(Theme::headerHeight);
    view->header()->setDefaultAlignment(Qt::AlignLeft | Qt::AlignVCenter);
    view->setContextMenuPolicy(Qt::CustomContextMenu);
    return view;
}
void MainWindow::buildUi() {
    auto central = new QWidget(this);
    setCentralWidget(central);
    auto root = new QVBoxLayout(central);
    root->setContentsMargins(0, 0, 0, 0);
    root->setSpacing(0);
    auto top = new QWidget(central);
    top->setObjectName("appTopArea");
    top->setFixedHeight(Theme::topHeight);
    auto topLayout = new QHBoxLayout(top);
    topLayout->setContentsMargins(Theme::space6, Theme::space1, Theme::space6, Theme::space1);
    topLayout->setSpacing(Theme::space3);
    auto brand = new QLabel("Takao", top);
    Theme::type(brand, Theme::Type::BodyLarge, true);
    brand->setFixedWidth(Theme::sidebarWidth - Theme::space6 - Theme::space3);
    topLayout->addWidget(brand);
    auto searchLabel = new QLabel("搜索", top);
    search_ = new QLineEdit(top);
    search_->setAccessibleName("搜索名称、PID 或发布者");
    search_->setObjectName("globalSearch");
    search_->setPlaceholderText("搜索名称、PID 或发布者  (Ctrl+F)");
    search_->setMaxLength(256);
    search_->setClearButtonEnabled(true);
    search_->setFixedWidth(480);
    search_->setFixedHeight(Theme::searchHeight);
    search_->addAction(Theme::icon(8), QLineEdit::LeadingPosition);
    searchLabel->setBuddy(search_);
    topLayout->addWidget(searchLabel);
    topLayout->addWidget(search_);
    topLayout->addStretch();
    summary_ = new QLabel("正在连接本机 Windows…", top);
    summary_->setProperty("secondary", true);
    topLayout->addWidget(summary_);
    root->addWidget(top);
    auto body = new QHBoxLayout();
    body->setSpacing(0);
    sidebar_ = new QWidget(central);
    sidebar_->setObjectName("sidebarPane");
    sidebar_->setFixedWidth(Theme::sidebarWidth);
    auto sidebarLayout = new QVBoxLayout(sidebar_);
    sidebarLayout->setContentsMargins(Theme::space2, Theme::space2, Theme::space2, Theme::space2);
    sidebarLayout->setSpacing(Theme::space1);
    for (int i = 0; i < keys_.size(); ++i) {
        auto b = new Theme::NavigationButton(titles_[i], sidebar_);
        b->setFixedHeight(Theme::navigationHeight);
        b->setMinimumWidth(0);
        b->setSizePolicy(QSizePolicy::Ignored, QSizePolicy::Fixed);
        b->setAccessibleName(titles_[i]);
        b->setCheckable(true);
        b->setProperty("nav", true);
        b->setIcon(Theme::icon(i));
        b->setIconSize(QSize(Theme::iconSize, Theme::iconSize));
        b->setToolTip(titles_[i] + QString(" (Alt+%1)").arg(i + 1));
        if (i == 7)
            sidebarLayout->addStretch();
        sidebarLayout->addWidget(b);
        if (i == 7) {
            navUpdate_ = button("", sidebar_);
            navUpdate_->setProperty("command", true);
            navUpdate_->setProperty("iconCommand", true);
            navUpdate_->setMinimumWidth(0);
            navUpdate_->setSizePolicy(QSizePolicy::Ignored, QSizePolicy::Fixed);
            navUpdate_->setIcon(Theme::icon(9));
            navUpdate_->setAccessibleName("下载可用更新");
            navUpdate_->hide();
            sidebarLayout->addWidget(navUpdate_);
            connect(navUpdate_, &QPushButton::clicked, this, [this] {
                if (updates_["canDownload"].toBool())
                    updateCommand("download");
                else
                    switchPage(7);
            });
        }
        nav_ << b;
        connect(b, &QPushButton::clicked, this, [this, i] { switchPage(i); });
        auto shortcut = new QShortcut(QKeySequence(QString("Alt+%1").arg(i + 1)), this);
        connect(shortcut, &QShortcut::activated, this, [this, i] { switchPage(i); });
    }
    body->addWidget(sidebar_);
    auto content = new QWidget(central);
    content->setObjectName("contentSurface");
    content->setAttribute(Qt::WA_StyledBackground);
    auto contentLayout = new QVBoxLayout(content);
    contentLayout->setContentsMargins(Theme::space6, Theme::space4, Theme::space6, Theme::space6);
    contentLayout->setSpacing(Theme::space4);
    auto commandBar = new QWidget(content);
    commandBar->setObjectName("pageCommandBar");
    commandBar->setMinimumHeight(Theme::commandBarHeight);
    auto commands = new QHBoxLayout(commandBar);
    commands->setContentsMargins(0, 0, 0, 0);
    commands->setSpacing(Theme::space2);
    auto heading = new QVBoxLayout();
    heading->setSpacing(0);
    pageTitle_ = new QLabel(content);
    pageTitle_->setProperty("title", true);
    Theme::type(pageTitle_, Theme::Type::PageTitle, true);
    heading->addWidget(pageTitle_);
    count_ = new QLabel(content);
    count_->setProperty("secondary", true);
    Theme::type(count_, Theme::Type::Caption);
    heading->addWidget(count_);
    commands->addLayout(heading);
    commands->addStretch();
    run_ = button("运行新任务", content, true);
    run_->setToolTip("运行新任务 (Ctrl+N)");
    terminate_ = button("结束任务", content);
    terminate_->setProperty("danger", true);
    detail_ = button("详情", content);
    pause_ = button("暂停采集", content);
    refresh_ = button("刷新", content);
    more_ = button("更多", content);
    pageAction_ = button("", content);
    for (auto b : {terminate_, detail_, pause_, refresh_, more_, pageAction_}) {
        b->setProperty("command", true);
        b->setFixedHeight(Theme::secondaryCommandHeight);
    }
    for (auto b : {pageAction_, run_, terminate_, detail_, pause_, refresh_, more_})
        commands->addWidget(b);
    contentLayout->addWidget(commandBar);
    banner_ = new QLabel(content);
    banner_->setProperty("notice", true);
    banner_->setWordWrap(true);
    banner_->hide();
    contentLayout->addWidget(banner_);
    toastArea_ = new QWidget(content);
    auto feedbackLayout = new QHBoxLayout(toastArea_);
    feedbackLayout->setContentsMargins(0, 0, 0, 0);
    feedbackLayout->setSpacing(Theme::space2);
    toast_ = new QLabel(toastArea_);
    toast_->setProperty("notice", true);
    toast_->setWordWrap(true);
    toast_->setAccessibleName("操作结果");
    feedbackLayout->addWidget(toast_, 1);
    auto dismiss = button("关闭提示", toastArea_);
    dismiss->setProperty("command", true);
    dismiss->setFixedHeight(Theme::secondaryCommandHeight);
    connect(dismiss, &QPushButton::clicked, toastArea_, &QWidget::hide);
    feedbackLayout->addWidget(dismiss);
    toastArea_->hide();
    contentLayout->addWidget(toastArea_);
    stack_ = new QStackedWidget(content);
    gridPage_ = new QWidget(stack_);
    auto gridLayout = new QVBoxLayout(gridPage_);
    gridLayout->setContentsMargins(0, 0, 0, 0);
    model_ = new DataModel(this);
    proxy_ = new DataProxy(this);
    table_ = createTable(model_, proxy_, gridPage_);
    table_->setAccessibleName("实时数据表");
    table_->setObjectName("dataTable");
    gridLayout->addWidget(table_);
    stack_->addWidget(gridPage_);
    buildPerformance();
    stack_->addWidget(performance_);
    buildSettings();
    stack_->addWidget(settingsPage_);
    contentLayout->addWidget(stack_, 1);
    body->addWidget(content, 1);
    body->setContentsMargins(0, 0, Theme::space2, Theme::space2);
    root->addLayout(body, 1);
    connect(search_, &QLineEdit::textChanged, this, [this](const QString& text) {
        applySearch(text);
        capturePagePreference();
        saveSoon();
    });
    connect(table_->selectionModel(), &QItemSelectionModel::selectionChanged, this,
            [this] { updateSelectionActions(); });
    connect(table_, &QTreeView::collapsed, this, [this](const QModelIndex& index) {
        if (!updatingModel_)
            collapsed_.insert(index.data(DataModel::IdentityRole).toString());
    });
    connect(table_, &QTreeView::expanded, this, [this](const QModelIndex& index) {
        if (!updatingModel_)
            collapsed_.remove(index.data(DataModel::IdentityRole).toString());
        QTimer::singleShot(0, this, &MainWindow::loadVisibleIcons);
    });
    connect(table_->verticalScrollBar(), &QScrollBar::valueChanged, this, [this] { loadVisibleIcons(); });
    connect(&iconTimer_, &QTimer::timeout, this, &MainWindow::loadVisibleIcons);
    iconTimer_.start(1000);
    connect(table_, &QTreeView::customContextMenuRequested, this, &MainWindow::contextMenu);
    connect(table_, &QTreeView::doubleClicked, this, [this] { details(); });
    table_->header()->setContextMenuPolicy(Qt::CustomContextMenu);
    connect(table_->header(), &QWidget::customContextMenuRequested, this, [this] { columnMenu(); });
    connect(table_->header(), &QHeaderView::sortIndicatorChanged, this, [this] {
        capturePagePreference();
        saveSoon();
        QTimer::singleShot(0, this, &MainWindow::loadVisibleIcons);
    });
    connect(table_->header(), &QHeaderView::sectionResized, this, [this] {
        capturePagePreference();
        saveSoon();
    });
    connect(run_, &QPushButton::clicked, this, &MainWindow::runTask);
    connect(terminate_, &QPushButton::clicked, this, [this] { terminateProcess(); });
    connect(detail_, &QPushButton::clicked, this, [this] { details(); });
    connect(more_, &QPushButton::clicked, this, &MainWindow::moreMenu);
    connect(pageAction_, &QPushButton::clicked, this, [this] {
        if (page_ == 2) {
            if (Theme::confirm(this, "清除统计", "仅清除本程序本次采集会话的历史，不修改 Windows 应用使用历史。",
                               "清除统计", true))
                post("history/reset", {});
        } else if (page_ == 3 || page_ == 6)
            post("desktop/open-system-location", {{"location", page_ == 3 ? "startupSettings" : "services"}});
    });
    connect(pause_, &QPushButton::clicked, this,
            [this] { applySampling(intervalMs_, !latest_["paused"].toBool()); });
    connect(refresh_, &QPushButton::clicked, this, [this] {
        post("refresh", {});
        poll();
        if (page_ == 7) {
            readIntegration();
            readUpdates();
        }
    });
    auto shortcut = [this](const QKeySequence& key, auto action) {
        auto s = new QShortcut(key, this);
        connect(s, &QShortcut::activated, this, action);
    };
    shortcut(QKeySequence::Find, [this] {
        search_->setFocus();
        search_->selectAll();
    });
    shortcut(QKeySequence::New, [this] { runTask(); });
    shortcut(QKeySequence::Refresh, [this] { refresh_->click(); });
    shortcut(QKeySequence(Qt::Key_Delete), [this] {
        if (processPage() && table_->hasFocus())
            terminateProcess();
    });
    shortcut(QKeySequence("Shift+F10"), [this] {
        if (table_->hasFocus())
            contextMenu({});
    });
    auto escape = new QAction(this);
    escape->setShortcut(QKeySequence(Qt::Key_Escape));
    addAction(escape);
    connect(escape, &QAction::triggered, this, [this] {
        if (search_->hasFocus()) {
            search_->clear();
            table_->setFocus();
        }
    });
    switchPage(0);
}
void MainWindow::bootstrap() {
    if (closing_)
        return;
    api_.get("bootstrap", this, [this](const QJsonObject& value, const QString& error, int) {
        if (!error.isEmpty()) {
            connected_ = false;
            setBanner(error, true);
            QTimer::singleShot(2500, this, &MainWindow::bootstrap);
            return;
        }
        if (value["hostPid"].toInteger() != expectedHost_) {
            setBanner("当前服务身份与启动主机不一致，已停止连接。请从完整程序目录重新启动。", true);
            return;
        }
        bootstrap_ = value;
        api_.setToken(value["token"].toString());
        connected_ = true;
        intervalMs_ = value["settings"].toObject()["intervalMs"].toInt(1000);
        proxy_->currentUser = value["currentUserName"].toString();
        pollTimer_.start();
        poll();
        if (loadingPreferences_)
            api_.get("preferences", this, [this](const QJsonObject& result, const QString& e, int) {
                if (e.isEmpty()) {
                    preferences_ = result["preferences"].toObject();
                    loadingPreferences_ = false;
                    auto w = preferences_["window"].toObject();
                    zoom_ = w["zoomFactor"].toDouble(1);
                    if (!qEnvironmentVariableIsSet("TASKMANAGER_QT_1080P") && !w.isEmpty()) {
                        auto size = QSize(w["width"].toInt(Theme::defaultWindowWidth),
                                          w["height"].toInt(Theme::defaultWindowHeight));
                        // Migrate former defaults and the old acceptance-window size;
                        // retain other sizes explicitly chosen by the user.
                        if (size == QSize(1600, 950) || size == QSize(1380, 860) || size == QSize(1920, 1080)) {
                            size = QSize(Theme::defaultWindowWidth, Theme::defaultWindowHeight);
                            w["width"] = size.width();
                            w["height"] = size.height();
                            preferences_["window"] = w;
                            saveSoon();
                        }
                        resize(size);
                    }
                    sidebar_->setFixedWidth(preferences_["sidebarCollapsed"].toBool() ? Theme::sidebarCompact : Theme::sidebarWidth);
                    for (int i = 0; i < nav_.size(); ++i)
                        nav_[i]->setText(preferences_["sidebarCollapsed"].toBool() ? QString() : titles_[i]);
                    restorePagePreference();
                    showColumnsForCurrentPage();
                    applyAppearance(true);
                    applySnapshot();
                    if (!result["warning"].isNull())
                        notify(result["warning"].toObject()["message"].toString(), true);
                    readIntegration(true);
                } else {
                    setBanner(e, true);
                    loadingPreferences_ = false;
                }
                updateSettings();
            });
        readUpdates();
    });
}
void MainWindow::poll() {
    if (polling_ || bootstrap_.isEmpty() || closing_)
        return;
    polling_ = true;
    api_.get(
        "snapshot", this,
        [this](const QJsonObject& value, const QString& error, int status) {
            polling_ = false;
            if (!error.isEmpty()) {
                connected_ = false;
                setBanner(error + " 保留上次成功快照；正在重试。", true);
                updateSelectionActions();
                return;
            }
            connected_ = true;
            if (status != 304) {
                latest_ = value;
                if (!frozen_)
                    shown_ = latest_;
                applySnapshot();
            } else if (!banner_->text().isEmpty() && !frozen_ && !latest_["paused"].toBool()) {
                setBanner({});
                updateSelectionActions();
            }
        },
        api_.snapshotEtag);
}
void MainWindow::applySnapshot() {
    if (shown_.isEmpty())
        return;
    intervalMs_ = latest_["intervalMs"].toInt(1000);
    const auto system = shown_["system"].toObject();
    summary_->setText(QString("CPU %1    内存 %2    %3 个进程")
                          .arg(formatValue(system["cpuPercent"], "percent"),
                               formatValue(system["memoryPercent"], "percent"))
                          .arg(system["processCount"].toInt()));
    summary_->setToolTip(networkStatusText(shown_["processNetwork"].toObject()));
    if (frozen_)
        setBanner("显示已冻结；后台继续真实采集。取消冻结后显示最新快照。");
    else if (latest_["paused"].toBool())
        setBanner("采集已暂停 · 正在显示最后有效快照。继续后重新采样，不补造历史。");
    else
        setBanner({});
    pause_->setText(latest_["paused"].toBool() ? "继续采集" : "暂停采集");
    if (page_ == 1)
        updatePerformance();
    else if (page_ == 7)
        updateSettings();
    else
        updateTable();
    updateSelectionActions();
}
bool MainWindow::processPage() const {
    return page_ == 0 || page_ == 5;
}
void MainWindow::switchPage(int index) {
    if (index < 0 || index >= keys_.size())
        return;
    if (!loadingPreferences_)
        capturePagePreference();
    switchingPage_ = true;
    pendingPageRestore_ = false;
    page_ = index;
    pageTitle_->setText(titles_[index]);
    for (int i = 0; i < nav_.size(); ++i)
        nav_[i]->setChecked(i == index);
    table_->setRootIsDecorated(index == 0 && preferences_["processView"].toString() == "tree");
    table_->setItemsExpandable(table_->rootIsDecorated());
    stack_->setCurrentIndex(index == 1 ? 1 : index == 7 ? 2 : 0);
    terminate_->setVisible(processPage());
    detail_->setVisible(index != 1 && index != 7);
    pause_->setVisible(index == 0 || index == 1 || index == 5 || index == 7);
    run_->setVisible(index != 7);
    count_->clear();
    if (index == 1)
        count_->setText("实时资源 · Windows 实际采样");
    else if (index == 7)
        count_->setText("采样、外观与系统入口");
    pageAction_->setVisible(index == 2 || index == 3 || index == 6);
    pageAction_->setText(index == 2 ? "清除本次会话统计…" : index == 3 ? "Windows 启动设置" : "Windows 服务管理器");
    pageAction_->setAccessibleName(pageAction_->text());
    search_->setPlaceholderText(index == 7 ? "搜索设置  (Ctrl+F)"
                                : index == 1 ? "搜索设备或传感器  (Ctrl+F)"
                                : index == 3 ? "搜索名称、来源或启动命令  (Ctrl+F)"
                                : index == 6 ? "搜索服务名称或状态  (Ctrl+F)"
                                             : "搜索名称、PID 或发布者  (Ctrl+F)");
    if (index != 1 && index != 7) {
        QList<Column> c;
        if (processPage())
            c = {{"name", "名称", "", 250},
                 {"state", "状态", "state", 110},
                 {"cpuPercent", "CPU", "percent", 100},
                 {"workingSetBytes", "内存", "bytes", 130},
                 {"io", "I/O", "rate", 130},
                 {"networkBytesPerSecond", "网络", "rate", 130},
                 {"gpuPercent", "GPU", "percent", 100},
                 {"pid", "PID", "number", 100},
                 {"parentPid", "父 PID", "number", 100},
                 {"sessionId", "会话", "number", 90},
                 {"threadCount", "线程", "number", 100},
                 {"handleCount", "句柄", "number", 100},
                 {"priority", "优先级", "", 120},
                 {"privateBytes", "专用内存", "bytes", 140},
                 {"userName", "用户", "", 180},
                 {"publisher", "发布者", "", 200},
                 {"path", "路径", "", 420},
                 {"gpuEngine", "GPU 引擎", "", 180}};
        else if (index == 2)
            c = {{"name", "名称", "", 250},
                 {"pid", "PID", "number", 100},
                 {"state", "状态", "state", 100},
                 {"cpu", "观测 CPU 时间", "cpuTime", 180},
                 {"observedIoReadBytes", "观测 I/O 读取", "bytes", 190},
                 {"observedIoWriteBytes", "观测 I/O 写入", "bytes", 190},
                 {"last", "最后观测", "date", 200}};
        else if (index == 3)
            c = {{"name", "名称", "", 250},
                 {"source", "来源", "", 250},
                 {"enabled", "状态", "startup", 150},
                 {"command", "启动命令", "", 580},
                 {"location", "来源位置", "", 320}};
        else if (index == 4)
            c = {{"name", "用户", "", 260},        {"session", "会话 ID", "number", 120},
                 {"station", "工作站", "", 160},   {"state", "状态", "state", 140},
                 {"count", "进程", "number", 110}, {"cpu", "CPU", "percent", 140},
                 {"memory", "内存", "bytes", 160}};
        else if (index == 6)
            c = {{"name", "名称", "", 280},
                 {"display", "显示名称", "", 520},
                 {"state", "状态", "state", 180},
                 {"pid", "PID", "number", 130}};
        model_->setColumns(c);
        for (int col = 0; col < c.size(); ++col)
            table_->setColumnWidth(col, c[col].width);
        showColumnsForCurrentPage();
    }
    restorePagePreference();
    applySnapshot();
    applySearch(search_->text());
    switchingPage_ = false;
    if (index == 7) {
        readIntegration();
        readUpdates();
    }
    if (index == 1)
        readSensors();
}
void MainWindow::showColumnsForCurrentPage() {
    const auto p = preferences_["pages"].toObject()[keys_[page_]].toObject();
    const auto visible = p["visibleColumns"].toArray();
    for (int i = 0; i < model_->columns.size(); ++i) {
        bool hide = false;
        if (visible.isEmpty() && processPage())
            hide = i >= 8;
        if (!visible.isEmpty())
            hide = !visible.contains(model_->columns[i].key);
        if (page_ == 5 && visible.isEmpty())
            hide = !(i == 0 || i == 7 || i == 8 || i == 9 || i == 10 || i == 11 || i == 12 || i == 2 ||
                     i == 3 || i == 16);
        table_->setColumnHidden(i, hide);
    }
}
void MainWindow::updateTable() {
    const auto chosen = selected();
    const auto identity =
        (pendingPageRestore_ || chosen.isEmpty())
            ? preferences_["pages"].toObject()[keys_[page_]].toObject()["selectedIdentity"].toString()
            : rowIdentity(chosen);
    const auto scroll = pendingPageRestore_
                            ? preferences_["pages"].toObject()[keys_[page_]].toObject()["scrollTop"].toInt()
                            : table_->verticalScrollBar()->value();
    QJsonArray rows;
    if (processPage())
        rows = shown_["processes"].toArray();
    else if (page_ == 2) {
        for (const auto& item : shown_["history"].toArray()) {
            auto row = item.toObject();
            row["state"] = row["isRunning"].toBool() ? "running" : "exited";
            row["cpu"] = row.value("observedCpuTime100ns");
            row["last"] = row.value("lastObservedAtUtc");
            rows.append(row);
        }
    } else if (page_ == 3) {
        for (const auto& item : shown_["startupItems"].toArray()) {
            auto row = item.toObject();
            row["location"] = row.value("locationPath");
            rows.append(row);
        }
    } else if (page_ == 4) {
        for (const auto& item : shown_["users"].toArray()) {
            auto row = item.toObject();
            row["name"] = row.value("userName");
            row["identity"] = QString::number(row["sessionId"].toInt());
            row["session"] = row.value("sessionId");
            row["station"] = row.value("stationName");
            row["count"] = row.value("processCount");
            row["cpu"] = row.value("cpuPercent");
            row["memory"] = row.value("workingSetBytes");
            rows.append(row);
        }
    } else if (page_ == 6) {
        for (const auto& item : shown_["services"].toArray()) {
            auto row = item.toObject();
            row["display"] = row.value("displayName");
            rows.append(row);
        }
    }
    updatingModel_ = true;
    model_->iconKind = processPage() ? "process" : page_ == 2 ? "history" : page_ == 3 ? "startup"
                                                 : page_ == 6 ? "service" : QString{};
    model_->setRows(rows, page_ == 0 && preferences_["processView"].toString() == "tree");
    if (table_->rootIsDecorated()) {
        table_->expandAll();
        for (const auto& id : collapsed_) {
            const auto idx = proxy_->mapFromSource(model_->find(id));
            if (idx.isValid())
                table_->collapse(idx);
        }
    }
    const auto restored = proxy_->mapFromSource(model_->find(identity));
    if (restored.isValid())
        table_->setCurrentIndex(restored);
    // setColumns resets the model and clamps its old scrollbar to zero.
    // Restore page position only after actual rows and view geometry exist.
    table_->doItemsLayout();
    table_->verticalScrollBar()->setValue(scroll);
    if (!rows.isEmpty() || processPage() || !shown_["slowDataCapturedAtUtc"].isNull())
        pendingPageRestore_ = false;
    updatingModel_ = false;
    count_->setText(page_ == 2 ? QString("本次会话 · %1 项 · 会话开始 %2").arg(rows.size()).arg(formatValue(shown_["sessionStartedAtUtc"], "date"))
                    : page_ == 3 ? QString("%1 个启动项 · Windows 实际来源").arg(rows.size())
                    : page_ == 4 ? QString("%1 个 Windows 登录会话").arg(rows.size())
                    : page_ == 6 ? QString("%1 项服务 · Windows 服务控制管理器").arg(rows.size())
                    : page_ == 5 ? QString("%1 个进程 · 完整详细信息").arg(rows.size())
                                 : QString("%1 个进程").arg(rows.size()));
    applySearch(search_->text());
    if (rows.isEmpty()) {
        const auto slow = shown_["slowDataCapturedAtUtc"];
        if (!processPage() && slow.isNull())
            setBanner("正在读取 Windows 后台来源；尚未取得首个结果。");
        else
            setBanner("当前来源没有可显示的项目。");
    }
}
QJsonObject MainWindow::selected() const {
    const auto idx = table_->currentIndex();
    return idx.isValid() ? QJsonObject::fromVariantMap(idx.data(DataModel::ObjectRole).toMap())
                         : QJsonObject();
}
void MainWindow::updateSelectionActions() {
    const auto row = selected();
    terminate_->setEnabled(connected_ && processPage() && row["canTerminate"].toBool() &&
                           !row["creationTimeFileTime"].isNull());
    terminate_->setToolTip(terminate_->isEnabled() ? "结束所选进程 (Delete)"
                                                   : "请选择具有可验证身份且未受保护的进程。");
    detail_->setEnabled(connected_ && !row.isEmpty());
    run_->setEnabled(connected_);
    pause_->setEnabled(connected_);
    refresh_->setEnabled(connected_);
    pageAction_->setEnabled(connected_);
}
void MainWindow::notify(const QString& message, bool error) {
    if (message.isEmpty())
        return;
    toast_->setText((error ? QStringLiteral("×  ") : QStringLiteral("✓  ")) + message);
    toast_->setAccessibleDescription(error ? "操作失败" : "操作完成");
    Theme::state(toast_, error ? "danger" : "success");
    toast_->show();
    toastArea_->show();
    toastTimer_.stop();
    if (!error)
        toastTimer_.start(4000);
}
void MainWindow::setBanner(const QString& message, bool error) {
    banner_->setText(message.isEmpty() ? QString() : (error ? QStringLiteral("×  ") : QStringLiteral("i  ")) + message);
    Theme::state(banner_, error ? "danger" : "info");
    banner_->setAccessibleName(error ? "连接或来源错误" : "数据状态");
    banner_->setVisible(!message.isEmpty());
}
void MainWindow::post(const QString& route, const QJsonObject& body,
                      std::function<void(const QJsonObject&)> done, int timeout) {
    if (pendingActions_.contains(route)) {
        notify("该操作正在等待真实结果，请稍候。");
        return;
    }
    pendingActions_.insert(route);
    api_.post(
        route, body, this,
        [this, route, done](const QJsonObject& result, const QString& error, int) {
            pendingActions_.remove(route);
            if (!error.isEmpty()) {
                notify(error, true);
                return;
            }
            if (done)
                done(result);
            else
                notify(result["message"].toString("操作已完成。"));
            poll();
        },
        timeout);
}
void MainWindow::applySampling(int interval, bool paused) {
    pause_->setEnabled(false);
    post("settings", {{"intervalMs", interval}, {"paused", paused}}, [this](const QJsonObject& result) {
        intervalMs_ = result["intervalMs"].toInt(intervalMs_);
        notify(result["paused"].toBool() ? "已暂停采集。" : "已继续真实采集。");
        api_.snapshotEtag.clear();
        poll();
    });
}
void MainWindow::capturePagePreference() {
    if (loadingPreferences_ || switchingPage_ || updatingModel_ || pendingPageRestore_ || page_ < 0 ||
        page_ >= keys_.size())
        return;
    auto pages = preferences_["pages"].toObject();
    auto pref = pages[keys_[page_]].toObject();
    pref["query"] = search_->text();
    if (processPage())
        pref["filters"] = QJsonObject{{"currentUser", proxy_->onlyCurrentUser},
                                     {"sessionId", proxy_->session < 0 ? QJsonValue() : QJsonValue(proxy_->session)},
                                     {"state", proxy_->state}};
    if (page_ != 1 && page_ != 7 && !model_->columns.isEmpty()) {
        const auto column =
            std::clamp(table_->header()->sortIndicatorSection(), 0, int(model_->columns.size()) - 1);
        pref["sortKey"] = model_->columns.value(column).key;
        pref["sortDirection"] =
            table_->header()->sortIndicatorOrder() == Qt::AscendingOrder ? "ascending" : "descending";
        pref["scrollTop"] = table_->verticalScrollBar()->value();
        pref["selectedIdentity"] = rowIdentity(selected());
        QJsonArray visible;
        QJsonObject widths;
        for (int i = 0; i < model_->columns.size(); ++i) {
            if (!table_->isColumnHidden(i))
                visible.append(model_->columns[i].key);
            widths[model_->columns[i].key] = std::clamp(table_->columnWidth(i), 64, 1600);
        }
        pref["visibleColumns"] = visible;
        pref["columnWidths"] = widths;
    } else {
        if (!pref.contains("sortKey"))
            pref["sortKey"] = "name";
        if (!pref.contains("sortDirection"))
            pref["sortDirection"] = "ascending";
        if (!pref.contains("scrollTop"))
            pref["scrollTop"] = 0;
    }
    pages[keys_[page_]] = pref;
    preferences_["pages"] = pages;
    preferences_["searchQuery"] = search_->text();
}
void MainWindow::restorePagePreference() {
    const QScopedValueRollback<bool> restoring(switchingPage_, true);
    const auto p = preferences_["pages"].toObject()[keys_[page_]].toObject();
    if (page_ == 1) {
        const auto perf = preferences_["pages"].toObject()["performance"].toObject();
        if (!perf["resourceType"].isNull() && !perf["resourceType"].toString().isEmpty())
            selectedResource_ = perf["resourceType"].toString() + ":" + perf["resourceId"].toString();
        const QSignalBlocker modeBlock(cpuMode_);
        cpuMode_->setCurrentIndex(perf["cpuGraphMode"].toString() == "logicalProcessors" ? 1 : 0);
    }
    const QSignalBlocker blocker(search_);
    // Legacy global search only migrates to the initial process page. A new
    // page must start clear, rather than filtering unrelated settings/devices.
    const bool legacyQuery = page_ == 0 && preferences_["pages"].toObject().isEmpty();
    search_->setText(p["query"].toString(legacyQuery ? preferences_["searchQuery"].toString() : QString()));
    const auto filter = p["filters"].toObject();
    proxy_->onlyCurrentUser = filter["currentUser"].toBool();
    proxy_->session = filter["sessionId"].isDouble() ? filter["sessionId"].toInt() : -1;
    proxy_->state = filter["state"].toString("all");
    proxy_->query = search_->text();
    proxy_->refreshFilter();
    sensorProxy_->query = search_->text();
    sensorProxy_->refreshFilter();
    if (page_ != 1 && page_ != 7) {
        const auto widths = p["columnWidths"].toObject();
        for (int i = 0; i < model_->columns.size(); ++i) {
            if (widths.contains(model_->columns[i].key))
                table_->setColumnWidth(i, widths[model_->columns[i].key].toInt());
            if (model_->columns[i].key == p["sortKey"].toString())
                table_->sortByColumn(i, p["sortDirection"].toString() == "descending" ? Qt::DescendingOrder
                                                                                      : Qt::AscendingOrder);
        }
        pendingPageRestore_ = true;
    }
}
void MainWindow::saveSoon() {
    if (!loadingPreferences_ && !closing_)
        saveTimer_.start();
}
void MainWindow::savePreferences() {
    if (loadingPreferences_ || bootstrap_.isEmpty())
        return;
    if (saving_) {
        savePending_ = true;
        return;
    }
    saving_ = true;
    const auto requestedPreferences = preferences_;
    api_.post("preferences", requestedPreferences, this,
              [this, requestedPreferences](const QJsonObject& result, const QString& error, int) {
                  saving_ = false;
                  if (savePending_) {
                      savePending_ = false;
                      saveSoon();
                  }
                  if (!error.isEmpty()) {
                      const auto diagnostic = qEnvironmentVariable("TASKMANAGER_QT_PREFERENCE_DIAGNOSTIC");
                      if (!diagnostic.isEmpty()) {
                          QSaveFile file(diagnostic);
                          if (file.open(QIODevice::WriteOnly)) {
                              file.write(QJsonDocument(requestedPreferences).toJson(QJsonDocument::Indented));
                              file.commit();
                          }
                      }
                      notify("偏好未保存：" + error, true);
                      return;
                  }
                  if (!result["warning"].isNull())
                      notify(result["warning"].toObject()["message"].toString(), true);
              });
}
void MainWindow::resizeEvent(QResizeEvent* event) {
    QMainWindow::resizeEvent(event);
    QTimer::singleShot(0, this, &MainWindow::loadVisibleIcons);
    // Windows can deliver the resize before Qt's window-state change. Observe
    // the settled state on the next event turn so maximized bounds never
    // replace the user's normal restore size.
    QTimer::singleShot(0, this, [this] {
        if (closing_ || loadingPreferences_ || isMaximized() || isMinimized())
            return;
        const auto normalSize = normalGeometry().size();
        auto window = preferences_["window"].toObject();
        window["width"] = std::clamp(normalSize.width(), 960, 3840);
        window["height"] = std::clamp(normalSize.height(), 640, 2160);
        window["zoomFactor"] = zoom_;
        if (actualTopmost_)
            window["alwaysOnTop"] = *actualTopmost_;
        preferences_["window"] = window;
        saveSoon();
    });
}
void MainWindow::closeEvent(QCloseEvent* event) {
    if (closing_) {
        event->accept();
        return;
    }
    capturePagePreference();
    saveTimer_.stop();
    pollTimer_.stop();
    slowTimer_.stop();
    closing_ = true;
    if (bootstrap_.isEmpty() || loadingPreferences_) {
        event->accept();
        return;
    }
    event->ignore();
    api_.post("preferences", preferences_, this,
              [this](const QJsonObject&, const QString&, int) { close(); });
    QTimer::singleShot(2000, this, [this] { close(); });
}
void MainWindow::buildPerformance() {
    performance_ = new QWidget();
    auto layout = new QHBoxLayout(performance_);
    layout->setContentsMargins(0, 0, 0, 0);
    layout->setSpacing(Theme::space4);
    resources_ = new QListWidget(performance_);
    resources_->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    resources_->setAccessibleName("性能资源");
    resources_->setFixedWidth(Theme::resourceWidth);
    resources_->setSpacing(Theme::space1);
    resources_->setMouseTracking(true);
    resources_->setItemDelegate(new ResourceDelegate(resources_));
    layout->addWidget(resources_);
    auto right = new QVBoxLayout();
    right->setSpacing(Theme::space3);
    auto titleLine = new QHBoxLayout();
    resourceTitle_ = new QLabel("CPU");
    resourceTitle_->setProperty("metric", true);
    Theme::type(resourceTitle_, Theme::Type::Metric, true);
    resourceInfo_ = new QLabel();
    resourceInfo_->setProperty("secondary", true);
    resourceInfo_->setWordWrap(true);
    resourceInfo_->setTextInteractionFlags(Qt::TextSelectableByMouse | Qt::TextSelectableByKeyboard);
    titleLine->addWidget(resourceTitle_);
    titleLine->addStretch();
    resourceValue_ = new QLabel();
    resourceValue_->setProperty("metric", true);
    resourceValue_->setProperty("numericType", true);
    Theme::type(resourceValue_, Theme::Type::Metric, true);
    resourceValue_->setFont(Theme::numericFont(Theme::Type::Metric, true));
    titleLine->addWidget(resourceValue_);
    right->addLayout(titleLine);
    right->addWidget(resourceInfo_);
    resourceAvailability_ = new QLabel();
    resourceAvailability_->setAccessibleName("设备来源状态");
    resourceAvailability_->setWordWrap(true);
    resourceAvailability_->setProperty("secondary", true);
    right->addWidget(resourceAvailability_);
    cpuMode_ = new Theme::ComboBox();
    cpuMode_->setAccessibleName("CPU 图表模式");
    cpuMode_->addItems({"总体使用率", "逻辑处理器"});
    cpuMode_->setFixedWidth(Theme::settingControlWidth);
    cpuMode_->setFixedHeight(Theme::controlHeight);
    right->addWidget(cpuMode_);
    perfStatus_ = new QLabel();
    perfStatus_->setProperty("secondary", true);
    perfStatus_->setWordWrap(true);
    right->addWidget(perfStatus_);
    perfStack_ = new QStackedWidget();
    auto chartScroll = new QScrollArea();
    chartScroll->setWidgetResizable(true);
    chartsPage_ = new QWidget();
    chartsLayout_ = new QGridLayout(chartsPage_);
    chartsLayout_->setContentsMargins(0, 0, 0, 0);
    chartsLayout_->setSpacing(Theme::space3);
    chartScroll->setWidget(chartsPage_);
    perfStack_->addWidget(chartScroll);
    sensorModel_ = new DataModel(this);
    sensorProxy_ = new DataProxy(this);
    auto sensorPage = new QWidget(perfStack_);
    auto sensorLayout = new QVBoxLayout(sensorPage);
    sensorLayout->setContentsMargins(0, 0, 0, 0);
    sensorEmpty_ = new QLabel("正在读取 HWiNFO 传感器…", sensorPage);
    sensorEmpty_->setAccessibleName("HWiNFO 可用性状态");
    sensorEmpty_->setAlignment(Qt::AlignCenter);
    sensorEmpty_->setWordWrap(true);
    sensorEmpty_->setProperty("secondary", true);
    sensorLayout->addWidget(sensorEmpty_, 1);
    sensors_ = createTable(sensorModel_, sensorProxy_, sensorPage);
    sensors_->setAccessibleName("HWiNFO 真实传感器");
    sensorModel_->setColumns({{"sensor", "传感器", "", 220},
                              {"name", "项目", "", 220},
                              {"unit", "单位", "", 90},
                              {"value", "当前", "number", 130},
                              {"minimum", "最小", "number", 130},
                              {"maximum", "最大", "number", 130},
                              {"average", "平均", "number", 130},
                              {"originalName", "原名", "", 220}});
    for (int i = 0; i < sensorModel_->columns.size(); ++i)
        sensors_->setColumnWidth(i, sensorModel_->columns[i].width);
    sensorLayout->addWidget(sensors_, 1);
    sensors_->hide();
    perfStack_->addWidget(sensorPage);
    right->addWidget(perfStack_, 1);
    facts_ = new QLabel();
    facts_->setProperty("secondary", true);
    facts_->setWordWrap(true);
    facts_->setTextInteractionFlags(Qt::TextSelectableByMouse | Qt::TextSelectableByKeyboard);
    auto factsPanel = new QWidget(performance_);
    factsPanel->setObjectName("performanceFacts");
    factsLayout_ = new QGridLayout(factsPanel);
    factsLayout_->setContentsMargins(0, Theme::space2, 0, 0);
    factsLayout_->setHorizontalSpacing(Theme::space6);
    factsLayout_->setVerticalSpacing(Theme::space1);
    for (int i = 0; i < 4; ++i)
        factsLayout_->setColumnStretch(i, 1);
    right->addWidget(factsPanel);
    right->addWidget(facts_);
    layout->addLayout(right, 1);
    connect(resources_, &QListWidget::currentItemChanged, this, [this](QListWidgetItem* item) {
        if (item) {
            selectedResource_ = item->data(Qt::UserRole).toString();
            chartLayoutKey_.clear();
            updatePerformance();
            auto pages = preferences_["pages"].toObject();
            auto p = pages["performance"].toObject();
            const auto separator = selectedResource_.indexOf(':');
            p["resourceType"] = selectedResource_.left(separator);
            p["resourceId"] = selectedResource_.mid(separator + 1);
            pages["performance"] = p;
            preferences_["pages"] = pages;
            if (selectedResource_ == "hwinfo:")
                readSensors();
            saveSoon();
        }
    });
    connect(cpuMode_, &QComboBox::currentIndexChanged, this, [this] {
        chartLayoutKey_.clear();
        auto pages = preferences_["pages"].toObject();
        auto p = pages["performance"].toObject();
        p["cpuGraphMode"] = cpuMode_->currentIndex() == 1 ? "logicalProcessors" : "total";
        pages["performance"] = p;
        preferences_["pages"] = pages;
        updatePerformance();
        saveSoon();
    });
}
void MainWindow::setFacts(const QList<QPair<QString, QString>>& items) {
    while (factLabels_.size() < items.size()) {
        const auto i = factLabels_.size();
        auto label = new QLabel(performance_);
        label->setProperty("secondary", true);
        Theme::type(label, Theme::Type::Caption);
        auto value = new QLabel(performance_);
        value->setWordWrap(true);
        value->setTextInteractionFlags(Qt::TextSelectableByMouse | Qt::TextSelectableByKeyboard);
        factsLayout_->addWidget(label, (i / 4) * 2, i % 4, Qt::AlignTop);
        factsLayout_->addWidget(value, (i / 4) * 2 + 1, i % 4, Qt::AlignTop);
        factLabels_ << label;
        factValues_ << value;
    }
    for (int i = 0; i < factLabels_.size(); ++i) {
        const auto visible = i < items.size();
        factLabels_[i]->setVisible(visible);
        factValues_[i]->setVisible(visible);
        if (visible) {
            factLabels_[i]->setText(items[i].first);
            factValues_[i]->setText(items[i].second);
            const bool numeric = !items[i].second.contains(QRegularExpression("[\\x{4e00}-\\x{9fff}]"));
            factValues_[i]->setProperty("numericType", numeric);
            Theme::type(factValues_[i], Theme::Type::BodyLarge);
            if (numeric)
                factValues_[i]->setFont(Theme::numericFont(Theme::Type::BodyLarge));
            factValues_[i]->setAccessibleName(items[i].first + " " + items[i].second);
        }
    }
}
void MainWindow::rebuildResources() {
    QStringList ids{"cpu:", "memory:"};
    QStringList names{"CPU", "内存"};
    const auto sys = shown_["system"].toObject();
    QStringList values{formatValue(sys["cpuPercent"], "percent"),
                       formatValue(sys["memoryPercent"], "percent")};
    for (const auto& type : {QString("physicalDisks"), QString("networkAdapters"), QString("gpuAdapters")})
        for (const auto& item : shown_[type].toArray()) {
            const auto o = item.toObject();
            if (o["isTotal"].toBool())
                continue;
            const auto prefix = type == "physicalDisks"     ? "disk:"
                                : type == "networkAdapters" ? "network:"
                                                            : "gpu:";
            ids << prefix + o["id"].toString();
            names << (type == "physicalDisks"     ? "磁盘 " + o["name"].toString()
                      : type == "networkAdapters" ? o["alias"].toString()
                      : o["description"].toString().isEmpty()
                          ? "GPU " + textOrUnknown(o["adapterLuid"]) + " / phys " + formatValue(o["physicalIndex"], "number")
                          : (o["isSoftware"].toBool() ? QString("软件 GPU · ") : QString("GPU · ")) + o["description"].toString());
            values << (type == "physicalDisks" ? "读 " + formatValue(o["readBytesPerSecond"], "rate") +
                                                     "\n写 " + formatValue(o["writeBytesPerSecond"], "rate")
                       : type == "networkAdapters"
                           ? "收 " + formatValue(o["receiveBytesPerSecond"], "rate") + "\n发 " +
                                 formatValue(o["sendBytesPerSecond"], "rate")
                           : formatValue(o["utilizationPercent"], "percent"));
        }
    ids << "hwinfo:";
    names << "HWiNFO 传感器";
    values << QString("%1 项 · %2")
                  .arg(shown_["hwInfo"].toObject()["readingCount"].toInt())
                  .arg(shown_["hwInfo"].toObject()["status"].toString());
    const QSignalBlocker block(resources_);
    bool rebuild = resources_->count() != ids.size();
    if (!rebuild)
        for (int i = 0; i < ids.size(); ++i)
            if (resources_->item(i)->data(Qt::UserRole).toString() != ids[i])
                rebuild = true;
    if (rebuild) {
        resources_->clear();
        for (int i = 0; i < ids.size(); ++i) {
            auto item = new QListWidgetItem(resources_);
            item->setData(Qt::UserRole, ids[i]);
        }
    }
    for (int i = 0; i < ids.size(); ++i) {
        const auto lines = static_cast<int>(values[i].count('\n')) + 1;
        const auto titleHeight = QFontMetrics(Theme::font(Theme::Type::Body, true)).height();
        const auto textHeight = QFontMetrics(Theme::font(Theme::Type::Caption)).lineSpacing() * lines;
        const auto minimum = ids[i].startsWith("disk:") || ids[i].startsWith("network:")
                                 ? Theme::resourceDiskHeight : Theme::resourceTileHeight;
        resources_->item(i)->setSizeHint(QSize(Theme::resourceWidth - Theme::space2,
            std::max(minimum, Theme::space2 + titleHeight + Theme::space1 +
                                  std::max(textHeight, 36) + Theme::space3)));
        resources_->item(i)->setText(names[i] + "\n" + values[i]);
        resources_->item(i)->setToolTip(names[i] + "\n" + values[i]);
        QVariantList firstSeries, secondSeries;
        const auto separator = ids[i].indexOf(':');
        const auto kind = ids[i].left(separator), identity = ids[i].mid(separator + 1);
        for (const auto& sample : shown_["performanceHistory"].toArray()) {
            const auto h = sample.toObject();
            const auto t =
                QDateTime::fromString(h["capturedAtUtc"].toString(), Qt::ISODateWithMs).toMSecsSinceEpoch() /
                1000.;
            double a = std::numeric_limits<double>::quiet_NaN(), b = a;
            if (kind == "cpu")
                a = numberOrNan(h["cpuPercent"]);
            else if (kind == "memory")
                a = numberOrNan(h["memoryPercent"]);
            else if (kind != "hwinfo") {
                const auto key = kind == "disk"      ? "physicalDisks"
                                 : kind == "network" ? "networkAdapters"
                                                     : "gpuAdapters";
                for (const auto& entry : h[key].toArray()) {
                    const auto item = entry.toObject();
                    if (item["id"].toString() == identity) {
                        a = numberOrNan(item[kind == "gpu" ? "percent" : "readBytesPerSecond"]);
                        b = numberOrNan(item["writeBytesPerSecond"]);
                        break;
                    }
                }
            }
            if (kind != "hwinfo")
                firstSeries.append(QPointF(t, a));
            if (kind == "network" || kind == "disk")
                secondSeries.append(QPointF(t, b));
        }
        resources_->item(i)->setData(Qt::UserRole + 1, firstSeries);
        resources_->item(i)->setData(Qt::UserRole + 2, secondSeries);
        resources_->item(i)->setData(Qt::UserRole + 3, kind == "cpu" || kind == "memory" || kind == "gpu");
        if (ids[i] == selectedResource_)
            resources_->setCurrentRow(i);
    }
    if (resources_->currentRow() < 0) {
        resources_->setCurrentRow(0);
        selectedResource_ = "cpu:";
    }
}
void MainWindow::updatePerformance() {
    if (shown_.isEmpty())
        return;
    rebuildResources();
    const auto sep = selectedResource_.indexOf(':');
    const auto type = selectedResource_.left(sep), id = selectedResource_.mid(sep + 1);
    const auto sys = shown_["system"].toObject();
    QStringList unavailableDevices;
    const auto telemetry = shown_["telemetry"].toObject();
    for (const auto& group : QList<QPair<QString, QString>>{{"physicalDisks", "磁盘"},
                                                           {"gpuAdapters", "GPU"},
                                                           {"networkAdapters", "硬件网卡"}}) {
        bool present = false;
        for (const auto& item : shown_[group.first].toArray())
            present = present || !item.toObject()["isTotal"].toBool();
        if (!present) {
            const auto reason = group.first == "networkAdapters"
                                    ? reasonText(sys, "networkReceiveBytesPerSecond")
                                    : reasonText(telemetry, group.first == "physicalDisks" ? "disk" : "gpu");
            unavailableDevices << group.second + "：" +
                (reason.isEmpty() ? QString("当前 Windows 来源没有返回可显示的设备；可刷新来源重试。") : reason);
        }
    }
    resourceAvailability_->setText(unavailableDevices.join("\n"));
    resourceAvailability_->setVisible(!unavailableDevices.isEmpty());
    QJsonObject device;
    const auto arrayKey = type == "disk"      ? "physicalDisks"
                          : type == "network" ? "networkAdapters"
                                              : "gpuAdapters";
    for (const auto& value : shown_[arrayKey].toArray())
        if (value.toObject()["id"].toString() == id)
            device = value.toObject();
    cpuMode_->setVisible(type == "cpu");
    perfStack_->setCurrentIndex(type == "hwinfo" ? 1 : 0);
    resourceTitle_->setText(type == "cpu"       ? "CPU"
                            : type == "memory"  ? "内存"
                            : type == "disk"    ? "磁盘"
                            : type == "network" ? "以太网 / Wi-Fi"
                            : type == "gpu"     ? "GPU"
                                                : "HWiNFO");
    resourceInfo_->setText(type == "disk"
                               ? textOrUnknown(device["model"]) + "  " + textOrUnknown(device["mediaType"]) +
                                     " / " + textOrUnknown(device["busType"])
                           : type == "network" ? device["description"].toString()
                           : type == "gpu"     ? device["description"].toString()
                                               : QString());
    resourceInfo_->setVisible(!resourceInfo_->text().isEmpty());
    resourceValue_->setText(type == "cpu"      ? formatValue(sys["cpuPercent"], "percent")
                            : type == "memory" ? formatValue(sys["usedPhysicalBytes"], "bytes")
                            : type == "gpu"    ? formatValue(device["utilizationPercent"], "percent")
                                               : QString());
    if (type == "hwinfo") {
        const auto state = shown_["hwInfo"].toObject();
        perfStatus_->setText(reasonText(state));
        facts_->setText("可选只读 SM2 来源 · 保留 HWiNFO "
                        "原始分组、单位及会话最小/最大/平均值。\n"
                        "需要此来源时，请运行 HWiNFO 的传感器模式，在其设置中启用 Shared Memory Support，"
                        "并确认当前版本/许可允许共享内存；本页会自动重读。其他 Windows 性能数据继续可用。");
        perfStatus_->setVisible(!perfStatus_->text().isEmpty());
        setFacts({});
        facts_->show();
        return;
    }
    const bool logical = type == "cpu" && cpuMode_->currentIndex() == 1;
    const auto processors = shown_["logicalProcessors"].toArray();
    const auto key = selectedResource_ + (logical ? "logical" : "total") +
                     QString::number(logical ? processors.size() : 1);
    if (chartLayoutKey_ != key) {
        chartLayoutKey_ = key;
        for (auto chart : charts_)
            delete chart;
        charts_.clear();
        const auto n = logical ? processors.size() : 1;
        const int cols = logical ? std::max(1, std::min(4, chartsPage_->width() / 230)) : 1;
        for (int i = 0; i < n; ++i) {
            auto chart = new Chart(chartsPage_);
            chart->setMinimumHeight(logical ? Theme::logicalChartHeight : Theme::mainChartHeight);
            chartsLayout_->addWidget(chart, i / cols, i % cols);
            connect(chart, &Chart::activated, this, [this] {
                if (selectedResource_ == "cpu:")
                    cpuMode_->setCurrentIndex(1 - cpuMode_->currentIndex());
            });
            charts_ << chart;
        }
    }
    const auto history = shown_["performanceHistory"].toArray();
    for (int chartIndex = 0; chartIndex < charts_.size(); ++chartIndex) {
        QVector<Series> series;
        Series primary, secondary;
        primary.label = type == "disk"      ? "读取"
                        : type == "network" ? "接收"
                        : type == "memory"  ? "使用率"
                                            : "使用率";
        secondary.label = type == "disk" ? "写入" : "发送";
        secondary.color = Theme::secondLine;
        secondary.style = Qt::DashLine;
        const auto cpuId = logical ? processors[chartIndex].toObject()["id"].toString() : QString();
        for (const auto& h : history) {
            const auto point = h.toObject();
            const double timestamp =
                QDateTime::fromString(point["capturedAtUtc"].toString(), Qt::ISODateWithMs)
                    .toMSecsSinceEpoch() /
                1000.;
            double a = std::numeric_limits<double>::quiet_NaN(), b = a;
            if (logical) {
                for (const auto& item : point["logicalProcessors"].toArray())
                    if (item.toObject()["id"].toString() == cpuId)
                        a = numberOrNan(item.toObject()["percent"]);
            } else if (type == "cpu")
                a = numberOrNan(point["cpuPercent"]);
            else if (type == "memory")
                a = numberOrNan(point["memoryPercent"]);
            else {
                for (const auto& item : point[arrayKey].toArray())
                    if (item.toObject()["id"].toString() == id) {
                        const auto r = item.toObject();
                        a = numberOrNan(r[type == "gpu" ? "percent" : "readBytesPerSecond"]);
                        b = numberOrNan(r["writeBytesPerSecond"]);
                    }
            }
            primary.points.append({timestamp, a});
            if (type == "disk" || type == "network")
                secondary.points.append({timestamp, b});
        }
        series << primary;
        if (type == "disk" || type == "network")
            series << secondary;
        const QString name =
            logical          ? QString("组 %1 · CPU %2 · %3")
                                   .arg(processors[chartIndex].toObject()["group"].toInt())
                                   .arg(processors[chartIndex].toObject()["index"].toInt())
                                   .arg(formatValue(processors[chartIndex].toObject()["percent"], "percent"))
            : type == "disk" ? "磁盘读写吞吐"
            : type == "network" ? "网络吞吐"
                                : resourceTitle_->text() + "使用率";
        charts_[chartIndex]->setData(name, type == "disk" || type == "network" ? "B/s" : "%", series,
                                     type != "disk" && type != "network",
                                     shown_["intervalMs"].toDouble(1000) / 1000.);
    }
    QStringList reasons;
    if (type == "cpu") {
        for (const auto& field : {"cpuPercent", "threadCount", "handleCount"})
            reasons << reasonText(sys, field);
        if (logical) {
            reasons << reasonText(telemetry, "logicalProcessors");
            for (const auto& processor : processors)
                reasons << reasonText(processor.toObject(), "percent");
        }
    } else if (type == "memory") {
        for (const auto& field : {"memoryPercent", "usedPhysicalBytes", "availablePhysicalBytes",
                                  "totalPhysicalBytes", "commitBytes", "commitLimitBytes", "cacheBytes"})
            reasons << reasonText(sys, field);
    } else if (type == "disk") {
        // Activity time is deliberately absent from the visible disk contract.
        for (const auto& field : {"readBytesPerSecond", "writeBytesPerSecond", "model", "mediaType", "busType", "deviceNumber"})
            reasons << reasonText(device, field);
    } else
        reasons << reasonText(device);
    reasons.removeDuplicates();
    reasons.removeAll({});
    perfStatus_->setText(reasons.join("\n"));
    perfStatus_->setVisible(!perfStatus_->text().isEmpty());
    facts_->clear();
    facts_->hide();
    if (type == "cpu")
        setFacts({{"利用率", formatValue(sys["cpuPercent"], "percent")},
                  {"逻辑处理器", formatValue(sys["logicalProcessorCount"], "number")},
                  {"进程", formatValue(sys["processCount"], "number")},
                  {"线程", formatValue(sys["threadCount"], "number")},
                  {"句柄", formatValue(sys["handleCount"], "number")},
                  {"运行时间", formatValue(sys["uptimeSeconds"], "duration")},
                  {"最近采样", formatValue(shown_["capturedAtUtc"], "date")}});
    else if (type == "memory")
        setFacts({{"使用中", formatValue(sys["usedPhysicalBytes"], "bytes")},
                  {"可用", formatValue(sys["availablePhysicalBytes"], "bytes")},
                  {"总物理内存", formatValue(sys["totalPhysicalBytes"], "bytes")},
                  {"已提交", formatValue(sys["commitBytes"], "bytes") + " / " + formatValue(sys["commitLimitBytes"], "bytes")},
                  {"已缓存", formatValue(sys["cacheBytes"], "bytes")},
                  {"最近采样", formatValue(shown_["capturedAtUtc"], "date")}});
    else if (type == "disk")
        setFacts({{"读取速度", formatValue(device["readBytesPerSecond"], "rate")},
                  {"写入速度", formatValue(device["writeBytesPerSecond"], "rate")},
                  {"介质", textOrUnknown(device["mediaType"])},
                  {"总线", textOrUnknown(device["busType"])},
                  {"型号", textOrUnknown(device["model"])},
                  {"设备编号", formatValue(device["deviceNumber"], "number")},
                  {"最近采样", formatValue(shown_["capturedAtUtc"], "date")}});
    else if (type == "network")
        setFacts({{"接收", formatValue(device["receiveBytesPerSecond"], "rate")},
                  {"发送", formatValue(device["sendBytesPerSecond"], "rate")},
                  {"接口", textOrUnknown(device["alias"])},
                  {"状态", textOrUnknown(device["status"])},
                  {"接收链路", formatValue(device["receiveLinkSpeedBitsPerSecond"], "number") + " bit/s"},
                  {"发送链路", formatValue(device["transmitLinkSpeedBitsPerSecond"], "number") + " bit/s"},
                  {"最近采样", formatValue(shown_["capturedAtUtc"], "date")}});
    else
        setFacts({{"利用率", formatValue(device["utilizationPercent"], "percent")},
                  {"最忙引擎", textOrUnknown(device["busiestEngine"])},
                  {"引擎数", formatValue(device["engineCount"], "number")},
                  {"设备", textOrUnknown(device["description"])},
                  {"Adapter LUID", textOrUnknown(device["adapterLuid"])},
                  {"最近采样", formatValue(shown_["capturedAtUtc"], "date")}});
}
void MainWindow::readSensors() {
    if (!connected_ || selectedResource_ != "hwinfo:")
        return;
    api_.get("hwinfo/sensors", this, [this](const QJsonObject& source, const QString& error, int) {
        if (selectedResource_ != "hwinfo:")
            return;
        if (!error.isEmpty()) {
            perfStatus_->setText(error);
            perfStatus_->show();
            sensorEmpty_->setText("无法读取 HWiNFO 传感器\n" + error);
            sensorEmpty_->show();
            sensors_->hide();
            return;
        }
        sensorsData_ = source;
        QJsonArray rows;
        for (const auto& value : source["sensors"].toArray()) {
            const auto sensor = value.toObject();
            for (const auto& reading : sensor["readings"].toArray()) {
                auto row = reading.toObject();
                row["identity"] = row.value("id");
                row["sensor"] = sensor["name"];
                row["name"] = row.value("label");
                row["originalName"] = row.value("originalLabel");
                rows.append(row);
            }
        }
        sensorModel_->setRows(rows);
        perfStatus_->setText(reasonText(source));
        perfStatus_->setVisible(!perfStatus_->text().isEmpty());
        sensors_->setVisible(!rows.isEmpty());
        sensorEmpty_->setVisible(rows.isEmpty());
        sensorEmpty_->setText("当前没有可显示的 HWiNFO 传感器\n" + reasonText(source));
    });
}
void MainWindow::loadVisibleIcons() {
    if (!connected_ || closing_ || updatingModel_ || page_ == 1 || page_ == 4 || page_ == 7 ||
        !table_->isVisible() || model_->icons.inFlight() >= IconCache::concurrency)
        return;
    auto index = table_->indexAt(QPoint(12, 6));
    if (!index.isValid())
        index = proxy_->index(0, 0);
    const QString kind = processPage() ? "process"
                         : page_ == 2  ? "history"
                         : page_ == 3  ? "startup"
                                       : "service";
    for (int count = 0; index.isValid() && count < 80 && model_->icons.inFlight() < IconCache::concurrency;
         ++count, index = table_->indexBelow(index)) {
        if (table_->visualRect(index).top() > table_->viewport()->height() + 32)
            break;
        const auto id = index.data(DataModel::IdentityRole).toString();
        const auto key = index.data(DataModel::IconKeyRole).toString();
        if (id.isEmpty() || !model_->icons.beginRequest(key))
            continue;
        const auto query =
            "application-icon?kind=" + kind + "&id=" + QString::fromUtf8(QUrl::toPercentEncoding(id));
        api_.image(query, this, [this, id, key](const QByteArray& bytes) {
            if (model_->icons.complete(key, bytes)) {
                const auto source = model_->find(id);
                if (source.isValid() && source.data(DataModel::IconKeyRole).toString() == key)
                    emit model_->dataChanged(source, source, {Qt::DecorationRole});
            }
            QTimer::singleShot(0, this, &MainWindow::loadVisibleIcons);
        });
    }
}
void MainWindow::applySearch(const QString& query) {
    proxy_->query = query;
    sensorProxy_->query = query;
    proxy_->refreshFilter();
    sensorProxy_->refreshFilter();
    if (page_ == 7) {
        int visible = 0;
        for (auto group : settingsPage_->findChildren<QWidget*>()) {
            if (!group->property("settingsGroup").toBool())
                continue;
            QString text = group->property("groupTitle").toString();
            for (auto label : group->findChildren<QLabel*>())
                text += " " + label->text();
            for (auto b : group->findChildren<QAbstractButton*>())
                text += " " + b->text();
            for (auto combo : group->findChildren<QComboBox*>())
                for (int i = 0; i < combo->count(); ++i)
                    text += " " + combo->itemText(i);
            const bool match = query.isEmpty() || text.contains(query, Qt::CaseInsensitive);
            group->setVisible(match);
            visible += match;
        }
        if (!visible)
            setBanner("没有匹配的设置。请清空搜索或使用其它关键词。");
        else if (!frozen_ && !latest_["paused"].toBool())
            setBanner({});
    } else if (page_ != 1) {
        if (!query.isEmpty() && proxy_->rowCount() == 0 && !shown_.isEmpty())
            setBanner("没有匹配的项目。请清空搜索或使用其它名称 / PID。");
        else if (!frozen_ && !latest_["paused"].toBool() && connected_)
            setBanner({});
    }
    loadVisibleIcons();
}
