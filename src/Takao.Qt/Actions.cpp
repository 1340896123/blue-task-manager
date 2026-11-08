#include "MainWindow.h"
#include <QApplication>
#include <QClipboard>
#include <QDialog>
#include <QFileDialog>
#include <QFormLayout>
#include <QHBoxLayout>
#include <QHeaderView>
#include <QMenu>
#include <QPlainTextEdit>
#include <QPointer>
#include <QSaveFile>
#include <QScrollArea>
#include <QScrollBar>
#include <QSignalBlocker>
#include <QTabWidget>
#include <QTextStream>
#include <QUrlQuery>
#include <QVBoxLayout>

namespace {
class ActionDialog final : public QDialog {
  public:
    using QDialog::QDialog;
    void reject() override {
        if (!property("busy").toBool())
            QDialog::reject();
    }
};
QLabel* errorLabel(QWidget* parent) {
    auto label = new QLabel(parent);
    label->setWordWrap(true);
    label->setProperty("fieldError", true);
    label->setAccessibleName("操作错误");
    return label;
}
QString identityText(const QJsonObject& target) {
    return target["name"].toString() + QString("  (PID %1)").arg(target["pid"].toInteger());
}
} // namespace
QString MainWindow::processRoute(const QJsonObject& target, const QString& action, bool query) const {
    QString route = QString("processes/%1/%2").arg(target["pid"].toInteger()).arg(action);
    if (query) {
        QUrlQuery q;
        q.addQueryItem("creationTimeFileTime", target["creationTimeFileTime"].toString());
        route += "?" + q.toString(QUrl::FullyEncoded);
    }
    return route;
}
void MainWindow::runTask() {
    if (!connected_) {
        notify("本机服务尚未连接，请等待或重试。", true);
        return;
    }
    auto d = new ActionDialog(this);
    d->setAttribute(Qt::WA_DeleteOnClose);
    d->setWindowTitle("运行新任务");
    d->setModal(true);
    d->resize(Theme::detailDialogWidth, 340);
    auto layout = Theme::dialogLayout(d);
    layout->setSpacing(Theme::space4);
    auto intro = new QLabel("输入要运行的程序完整路径和可选参数。程序将使用当前管理员权限启动。", d);
    intro->setWordWrap(true);
    Theme::type(intro, Theme::Type::BodyLarge);
    layout->addWidget(intro);
    auto form = new QFormLayout();
    form->setSpacing(Theme::space3);
    auto path = new QLineEdit(d);
    path->setAccessibleName("程序路径");
    path->setObjectName("executablePath");
    auto arguments = new QLineEdit(d);
    arguments->setAccessibleName("参数");
    arguments->setObjectName("executableArguments");
    path->setFixedHeight(std::max(Theme::controlHeight, path->fontMetrics().height() + Theme::space3));
    arguments->setFixedHeight(std::max(Theme::controlHeight, arguments->fontMetrics().height() + Theme::space3));
    auto row = new QHBoxLayout();
    row->addWidget(path, 1);
    auto browse = button("浏览…", d);
    row->addWidget(browse);
    auto pathLabel = new QLabel("程序路径(&P)", d);
    pathLabel->setBuddy(path);
    form->addRow(pathLabel, row);
    auto argumentLabel = new QLabel("参数(&A)", d);
    argumentLabel->setBuddy(arguments);
    form->addRow(argumentLabel, arguments);
    layout->addLayout(form);
    auto error = errorLabel(d);
    layout->addWidget(error);
    auto buttons = new QHBoxLayout();
    buttons->setSpacing(Theme::space2);
    buttons->addStretch();
    auto cancel = button("取消", d);
    auto run = button("运行(&R)", d, true);
    run->setDefault(true);
    buttons->addWidget(cancel);
    buttons->addWidget(run);
    layout->addLayout(buttons);
    QWidget::setTabOrder(path, arguments);
    QWidget::setTabOrder(arguments, browse);
    QWidget::setTabOrder(browse, cancel);
    QWidget::setTabOrder(cancel, run);
    connect(cancel, &QPushButton::clicked, d, &QDialog::reject);
    connect(browse, &QPushButton::clicked, d, [d, path] {
        const auto file =
            QFileDialog::getOpenFileName(d, "选择要运行的程序", path->text(), "Windows 可执行程序 (*.exe)");
        if (!file.isEmpty())
            path->setText(file);
    });
    connect(run, &QPushButton::clicked, d, [this, d, path, arguments, error, run, cancel, browse] {
        if (path->text().trimmed().isEmpty()) {
            error->setText("请先输入现存程序的完整路径。");
            path->setFocus();
            return;
        }
        d->setProperty("busy", true);
        run->setText("正在启动…");
        for (auto b : {run, cancel, browse})
            b->setEnabled(false);
        path->setEnabled(false);
        arguments->setEnabled(false);
        api_.post("processes/start",
                  {{"executable", path->text().trimmed()}, {"arguments", arguments->text()}}, d,
                  [this, d, path, arguments, error, run, cancel, browse](const QJsonObject& result,
                                                                         const QString& failure, int) {
                      d->setProperty("busy", false);
                      for (auto b : {run, cancel, browse})
                          b->setEnabled(true);
                      path->setEnabled(true);
                      arguments->setEnabled(true);
                      run->setText("运行(&R)");
                      if (!failure.isEmpty()) {
                          error->setText(failure);
                          path->setFocus();
                          return;
                      }
                      d->accept();
                      notify(QString("已运行新任务 · PID %1。%2")
                                 .arg(result["pid"].toInteger())
                                 .arg(result["message"].toString()));
                      api_.snapshotEtag.clear();
                      poll();
                  });
    });
    d->open();
    path->setFocus();
}
void MainWindow::terminateProcess(QJsonObject target) {
    if (target.isEmpty())
        target = selected();
    if (!processPage() || target.isEmpty())
        return;
    if (!connected_ || !target["canTerminate"].toBool() || !target["creationTimeFileTime"].isString()) {
        notify("该进程受保护或无法确认创建身份，不能结束。", true);
        return;
    }
    auto d = new ActionDialog(this);
    d->setAttribute(Qt::WA_DeleteOnClose);
    d->setModal(true);
    d->setWindowTitle("结束任务");
    d->resize(Theme::dialogWidth, 280);
    auto layout = Theme::dialogLayout(d);
    auto label = new QLabel(
        "确定结束 " + identityText(target) + "？\n未保存的数据可能丢失。仅结束已核验的这个进程身份。", d);
    label->setWordWrap(true);
    Theme::type(label, Theme::Type::BodyLarge);
    layout->addWidget(label);
    auto error = errorLabel(d);
    layout->addWidget(error);
    auto buttons = new QHBoxLayout();
    buttons->setSpacing(Theme::space2);
    buttons->addStretch();
    auto cancel = button("取消", d);
    auto confirm = button("结束任务(&E)", d, true);
    cancel->setDefault(true);
    confirm->setProperty("danger", true);
    confirm->setAccessibleName("确认结束任务");
    buttons->addWidget(cancel);
    buttons->addWidget(confirm);
    layout->addLayout(buttons);
    connect(cancel, &QPushButton::clicked, d, &QDialog::reject);
    connect(confirm, &QPushButton::clicked, d, [this, d, target, error, cancel, confirm] {
        d->setProperty("busy", true);
        cancel->setEnabled(false);
        confirm->setEnabled(false);
        confirm->setText("正在结束并确认…");
        api_.post(processRoute(target, "terminate"),
                  {{"creationTimeFileTime", target["creationTimeFileTime"]}}, d,
                  [this, d, error, cancel, confirm](const QJsonObject& result, const QString& failure, int) {
                      d->setProperty("busy", false);
                      cancel->setEnabled(true);
                      confirm->setEnabled(true);
                      confirm->setText("结束任务(&E)");
                      if (!failure.isEmpty()) {
                          error->setText(failure);
                          return;
                      }
                      if (!result["completed"].toBool()) {
                          error->setText(
                              result["message"].toString("已发出请求，尚未确认进程退出。请刷新列表核验。"));
                          return;
                      }
                      d->accept();
                      notify(result["message"].toString("任务已结束，已确认真实进程退出。"));
                      api_.snapshotEtag.clear();
                      poll();
                  });
    });
    d->open();
    cancel->setFocus();
}
void MainWindow::terminateTree(const QJsonObject& target) {
    api_.post(
        processRoute(target, "tree-preview"), {{"creationTimeFileTime", target["creationTimeFileTime"]}},
        this, [this, target](const QJsonObject& preview, const QString& error, int) {
            if (!error.isEmpty()) {
                notify(error, true);
                return;
            }
            if (!preview["canTerminate"].toBool()) {
                presentText("不能结束进程树", preview["message"].toString());
                return;
            }
            auto d = new ActionDialog(this);
            d->setAttribute(Qt::WA_DeleteOnClose);
            d->setWindowTitle("结束进程树");
            d->setModal(true);
            d->resize(Theme::detailDialogWidth, 500);
            auto layout = Theme::dialogLayout(d);
            auto description = new QLabel(
                "请核对以下真实进程身份。结束后未保存的数据可能丢失；变化的成员将由后端重新拒绝。", d);
            description->setWordWrap(true);
            layout->addWidget(description);
            auto list = new QPlainTextEdit(d);
            list->setReadOnly(true);
            QStringList membersText;
            QJsonArray members;
            for (const auto& item : preview["members"].toArray()) {
                const auto member = item.toObject();
                membersText << identityText(member) + " · " + member["identity"].toString();
                members.append(QJsonObject{{"pid", member["pid"]},
                                           {"creationTimeFileTime", member["creationTimeFileTime"]}});
            }
            list->setPlainText(membersText.join("\n"));
            layout->addWidget(list);
            auto resultLabel = errorLabel(d);
            layout->addWidget(resultLabel);
            auto buttons = new QHBoxLayout();
            buttons->setSpacing(Theme::space2);
            buttons->addStretch();
            auto cancel = button("取消", d);
            auto confirm = button("确认结束进程树(&E)", d, true);
            cancel->setDefault(true);
            confirm->setProperty("danger", true);
            buttons->addWidget(cancel);
            buttons->addWidget(confirm);
            layout->addLayout(buttons);
            connect(cancel, &QPushButton::clicked, d, &QDialog::reject);
            connect(
                confirm, &QPushButton::clicked, d, [this, d, target, members, resultLabel, cancel, confirm] {
                    d->setProperty("busy", true);
                    cancel->setEnabled(false);
                    confirm->setEnabled(false);
                    api_.post(
                        processRoute(target, "terminate-tree"),
                        {{"creationTimeFileTime", target["creationTimeFileTime"]}, {"members", members}}, d,
                        [this, d, resultLabel, cancel, confirm](const QJsonObject& result,
                                                                const QString& failure, int) {
                            d->setProperty("busy", false);
                            cancel->setEnabled(true);
                            confirm->setEnabled(true);
                            if (!failure.isEmpty()) {
                                resultLabel->setText(failure);
                                return;
                            }
                            if (result["completed"].toBool()) {
                                d->accept();
                                notify(result["message"].toString());
                            } else {
                                QStringList messages;
                                for (const auto& item : result["results"].toArray()) {
                                    const auto row = item.toObject();
                                    messages << QString("PID %1 · %2 · %3")
                                                    .arg(row["pid"].toInteger())
                                                    .arg(row["status"].toString(), row["message"].toString());
                                }
                                resultLabel->setText(result["message"].toString() + "\n" +
                                                     messages.join("\n"));
                                confirm->setEnabled(false);
                            }
                            api_.snapshotEtag.clear();
                            poll();
                        });
                });
            d->open();
            cancel->setFocus();
        });
}
void MainWindow::presentText(const QString& title, const QString& text) {
    auto d = new QDialog(this);
    d->setAttribute(Qt::WA_DeleteOnClose);
    d->setWindowTitle(title);
    d->resize(Theme::detailDialogWidth, 560);
    auto layout = Theme::dialogLayout(d);
    auto output = new QPlainTextEdit(d);
    output->setReadOnly(true);
    output->setAccessibleName(title);
    output->setPlainText(text);
    layout->addWidget(output);
    auto close = button("关闭", d);
    layout->addWidget(close, 0, Qt::AlignRight);
    connect(close, &QPushButton::clicked, d, &QDialog::accept);
    d->open();
}
void MainWindow::details(QJsonObject target) {
    if (target.isEmpty())
        target = selected();
    if (target.isEmpty())
        return;
    if (page_ == 3) {
        startupDetails(target);
        return;
    }
    if (page_ == 6) {
        serviceDetails(target);
        return;
    }
    if (page_ == 4) {
        locateSession(target);
        return;
    }
    if (page_ == 2) {
        historyDetails(target);
        return;
    }
    if (!target["creationTimeFileTime"].isString()) {
        presentText("进程详情", identityText(target) + "\n" + reasonText(target));
        return;
    }
    auto d = new QDialog(this);
    d->setAttribute(Qt::WA_DeleteOnClose);
    d->setWindowTitle("进程详情 · " + identityText(target));
    d->resize(1020, 720);
    auto layout = Theme::dialogLayout(d);
    auto tabs = new QTabWidget(d);
    auto info = new QPlainTextEdit(d);
    info->setReadOnly(true);
    info->setAccessibleName("实时进程详情与不可用原因");
    tabs->addTab(info, "进程");
    auto connPage = new QWidget(tabs);
    auto connLayout = new QVBoxLayout(connPage);
    auto status = new QLabel(connPage);
    status->setWordWrap(true);
    status->setProperty("secondary", true);
    connLayout->addWidget(status);
    auto m = new DataModel(d);
    auto p = new DataProxy(d);
    auto view = createTable(m, p, connPage);
    view->setAccessibleName("真实 TCP UDP 端点");
    m->setColumns({{"protocol", "协议", "", 90},
                   {"addressFamily", "地址族", "", 100},
                   {"localAddress", "本地地址", "", 230},
                   {"localPort", "本地端口", "number", 100},
                   {"remoteAddress", "远端地址", "", 230},
                   {"remotePort", "远端端口", "number", 100},
                   {"state", "状态", "state", 150}});
    for (int i = 0; i < m->columns.size(); ++i)
        view->setColumnWidth(i, m->columns[i].width);
    connLayout->addWidget(view);
    tabs->addTab(connPage, "TCP / UDP 端点");
    layout->addWidget(tabs);
    auto close = button("关闭", d);
    layout->addWidget(close, 0, Qt::AlignRight);
    connect(close, &QPushButton::clicked, d, &QDialog::accept);
    auto refresh = [this, d, target, info, m, status] {
        if (d->property("reading").toBool())
            return;
        d->setProperty("reading", true);
        api_.get(processRoute(target, "details", true), d,
                 [d, info](const QJsonObject& result, const QString& error, int) {
                     d->setProperty("reading", false);
                     if (!error.isEmpty()) {
                         info->setPlainText(info->property("lastData").toString() +
                                            "\n读取失败：" + error +
                                            "\n以上保留最后有效观测；可关闭后重新选择真实进程。");
                         return;
                     }
                     auto process = result["process"].toObject();
                     QStringList output{
                         identityText(process),
                         "创建身份  " + process["creationTimeFileTime"].toString(),
                         "创建时间  " + formatValue(result["startedAtUtc"], "date"),
                         "采集时间  " + formatValue(result["capturedAtUtc"], "date"),
                         "快照时间  " + formatValue(result["snapshotCapturedAtUtc"], "date"),
                         "父 PID  " + formatValue(process["parentPid"], "number"),
                         "会话 ID  " + formatValue(process["sessionId"], "number"),
                         "路径  " + formatValue(process["path"], {}),
                         "发布者（版本元数据）  " + formatValue(process["publisher"], {}),
                         "用户  " + formatValue(process["userName"], {}),
                         "CPU  " + formatValue(process["cpuPercent"], "percent"),
                         "累计 CPU 时间  " + formatValue(process["totalCpuTime100ns"], "cpuTime"),
                         "优先级  " + formatValue(process["priority"], {}),
                         "工作集  " + formatValue(process["workingSetBytes"], "bytes"),
                         "专用内存  " + formatValue(process["privateBytes"], "bytes"),
                         "句柄  " + formatValue(process["handleCount"], "number"),
                         "线程  " + formatValue(process["threadCount"], "number"),
                         "I/O 读取速度  " + formatValue(process["ioReadBytesPerSecond"], "rate"),
                         "I/O 写入速度  " + formatValue(process["ioWriteBytesPerSecond"], "rate"),
                         "累计 I/O 读取  " + formatValue(process["ioReadBytes"], "bytes"),
                         "累计 I/O 写入  " + formatValue(process["ioWriteBytes"], "bytes"),
                         "网络接收  " + formatValue(process["networkReceiveBytesPerSecond"], "rate"),
                         "网络发送  " + formatValue(process["networkSendBytesPerSecond"], "rate"),
                         "网络窗口时间  " + formatValue(process["networkCapturedAtUtc"], "date"),
                         "GPU  " + formatValue(process["gpuPercent"], "percent"),
                         "GPU 引擎  " + formatValue(process["gpuEngine"], {}),
                         "GPU 采集时间  " + formatValue(process["gpuCapturedAtUtc"], "date"),
                         "运行时间  " + formatValue(result["uptimeSeconds"], "duration"),
                         "服务来源时间  " + formatValue(result["relatedServicesCapturedAtUtc"], "date"),
                         reasonText(result)};
                     for (const auto& service : result["relatedServices"].toArray())
                         output << "关联服务  " + service.toObject()["name"].toString();
                     const auto scroll = info->verticalScrollBar()->value();
                     info->setProperty("lastData", output.join("\n"));
                     info->setPlainText(output.join("\n"));
                     info->verticalScrollBar()->setValue(scroll);
                 });
        api_.get(processRoute(target, "connections", true), d,
                 [m, status](const QJsonObject& result, const QString& error, int) {
                     if (!error.isEmpty()) {
                         status->setText(error);
                         return;
                     }
                     m->setRows(result["connections"].toArray());
                     status->setText(
                         QString("%1 个真实端点 · %2\n%3")
                             .arg(result["connections"].toArray().size())
                             .arg(formatValue(result["capturedAtUtc"], "date"), reasonText(result)));
                 });
    };
    auto timer = new QTimer(d);
    timer->setInterval(2000);
    connect(timer, &QTimer::timeout, d, refresh);
    timer->start();
    d->open();
    refresh();
}
void MainWindow::historyDetails(const QJsonObject& target) {
    auto d = new QDialog(this);
    d->setAttribute(Qt::WA_DeleteOnClose);
    d->setWindowTitle("本次会话观测 · " + identityText(target));
    d->resize(Theme::dialogWidth, 550);
    auto layout = Theme::dialogLayout(d);
    auto text = new QPlainTextEdit(d);
    text->setReadOnly(true);
    text->setAccessibleName("本次会话历史详情");
    text->setPlainText(QStringList{
        identityText(target), "进程身份  " + target["identity"].toString(),
        "状态  " + formatValue(target["isRunning"], "running"),
        "首次观测  " + formatValue(target["firstObservedAtUtc"], "date"),
        "最后观测  " + formatValue(target["lastObservedAtUtc"], "date"),
        "观测 CPU 时间  " + formatValue(target["observedCpuTime100ns"], "cpuTime"),
        "观测 I/O 读取  " + formatValue(target["observedIoReadBytes"], "bytes"),
        "观测 I/O 写入  " + formatValue(target["observedIoWriteBytes"], "bytes"),
        "仅累计本程序观测期间的有效增量；已退出条目保留历史，未知不表示零。",
        reasonText(target, "cpu"), reasonText(target, "observedIoReadBytes"),
        reasonText(target, "observedIoWriteBytes")}.join("\n"));
    layout->addWidget(text);
    auto commands = new QHBoxLayout();
    auto locate = button("查看当前进程", d);
    locate->setEnabled(connected_ && target["isRunning"].toBool());
    locate->setToolTip(locate->isEnabled() ? "重新核对创建身份后定位到详细信息。" : "该身份已退出，历史观测仍可查看。");
    commands->addWidget(locate);
    commands->addStretch();
    auto close = button("关闭", d);
    commands->addWidget(close);
    layout->addLayout(commands);
    connect(close, &QPushButton::clicked, d, &QDialog::accept);
    connect(locate, &QPushButton::clicked, d, [this, d, target] {
        if (locateProcess(target["pid"].toInteger(), target["identity"].toString()))
            d->accept();
    });
    d->open();
}
void MainWindow::locateSession(const QJsonObject& target) {
    if (!target["sessionId"].isDouble()) {
        notify("无法确认此 Windows 会话 ID，请刷新来源后重试。", true);
        return;
    }
    switchPage(0);
    search_->clear();
    proxy_->onlyCurrentUser = false;
    proxy_->state = "all";
    proxy_->session = target["sessionId"].toInt();
    proxy_->refreshFilter();
    capturePagePreference();
    saveSoon();
    table_->setFocus();
    notify(QString("正在显示 Windows 会话 %1 的真实进程。").arg(proxy_->session));
}
void MainWindow::startupDetails(const QJsonObject& target) {
    presentText(
        "启动应用 · " + target["name"].toString(),
        "名称  " + target["name"].toString() + "\n状态  " +
            (target["enabled"].isNull() ? QString("无法确定") : formatValue(target["enabled"], "startup")) +
            "\n原因  " + target["enabledReason"].toString() + "\n来源  " + target["source"].toString() +
            "\n启动命令  " + target["command"].toString() + "\n来源位置  " +
            target["locationPath"].toString() + "\n" + target["locationReason"].toString());
}
void MainWindow::serviceDetails(const QJsonObject& target) {
    api_.get("services/" + QString::fromUtf8(QUrl::toPercentEncoding(target["name"].toString())) + "/details",
             this, [this](const QJsonObject& result, const QString& error, int) {
                 if (!error.isEmpty()) {
                     notify(error, true);
                     return;
                 }
                 const auto missing = result["unavailable"].toObject();
                 const auto rawDependencies = result["dependencies"];
                 QString dependencies = QStringLiteral("—（未能读取依赖服务）");
                 if (rawDependencies.isArray()) {
                     QStringList names;
                     for (const auto& value : rawDependencies.toArray())
                         if (value.isString())
                             names << value.toString();
                     if (!names.isEmpty())
                         dependencies =
                             names.join(", ") +
                             (missing.contains("dependencies") ? QStringLiteral("（部分结果）") : QString());
                     else if (!missing.contains("configuration") && !missing.contains("dependencies"))
                         dependencies = QStringLiteral("无依赖服务");
                 }
                 const QHash<QString, QString> startTypes{{"SERVICE_AUTO_START", "自动"},
                                                          {"SERVICE_DEMAND_START", "手动"},
                                                          {"SERVICE_DISABLED", "已禁用"},
                                                          {"SERVICE_BOOT_START", "引导"},
                                                          {"SERVICE_SYSTEM_START", "系统"}};
                 const auto rawStart = result["startType"];
                 const auto startType = rawStart.isNull()
                                            ? QStringLiteral("—")
                                            : startTypes.value(rawStart.toString(), rawStart.toString());
                 QStringList lines{"名称  " + formatValue(result["name"], {}),
                                   "显示名称  " + formatValue(result["displayName"], {}),
                                   "状态  " + formatValue(result["state"], "state"),
                                   "PID  " + formatValue(result["pid"], "number"),
                                   "路径  " + formatValue(result["binaryPath"], {}),
                                   "启动类型  " + startType,
                                   "账户  " + formatValue(result["account"], {}),
                                   "依赖服务  " + dependencies,
                                   "采集时间  " + formatValue(result["capturedAtUtc"], "date")};
                 const auto reason = reasonText(result);
                 if (!reason.isEmpty())
                     lines << "不可用原因\n" + reason;
                 presentText("服务详情 · " + result["name"].toString(), lines.join("\n"));
             });
}
void MainWindow::setPriority(const QJsonObject& target, const QString& priority) {
    if (!Theme::confirm(this, "更改优先级",
                        "更改 " + identityText(target) + " 的优先级可能影响系统响应。确定继续？",
                        "更改优先级"))
        return;
    post(processRoute(target, "priority"),
         {{"creationTimeFileTime", target["creationTimeFileTime"]}, {"priority", priority}});
}
void MainWindow::efficiency(const QJsonObject& target) {
    api_.get(processRoute(target, "efficiency", true), this,
             [this, target](const QJsonObject& result, const QString& error, int) {
                 if (!error.isEmpty()) {
                     notify(error, true);
                     return;
                 }
                 if (!result["available"].toBool()) {
                     presentText("效率模式不可用", result["reason"].toString());
                     return;
                 }
                 const bool next = !result["enabled"].toBool();
                 if (!next && !result["canRestore"].toBool()) {
                     presentText("效率模式", result["reason"].toString("当前状态无法由本程序恢复。"));
                     return;
                 }
                 if (Theme::confirm(this, "效率模式",
                                    (next ? "启用" : "关闭") + QString(" %1 的效率模式？").arg(identityText(target)),
                                    next ? "启用效率模式" : "关闭效率模式"))
                     post(processRoute(target, "efficiency"),
                          {{"creationTimeFileTime", target["creationTimeFileTime"]}, {"enabled", next}});
             });
}
void MainWindow::controlService(const QJsonObject& target, const QString& command) {
    const auto name = target["name"].toString();
    const auto route = "services/" + QString::fromUtf8(QUrl::toPercentEncoding(name));
    api_.get(
        route + "/details", this,
        [this, route, command](const QJsonObject& current, const QString& error, int) {
            if (!error.isEmpty()) {
                notify(error, true);
                return;
            }
            const bool allowed = current[command == "start"  ? "canStart"
                                         : command == "stop" ? "canStop"
                                                             : "canRestart"]
                                     .toBool();
            if (!allowed) {
                presentText("服务操作不可用", reasonText(current) + "\n请核对当前状态和服务实际控制权限。");
                return;
            }
            const auto verb = command == "start" ? "启动" : command == "stop" ? "停止" : "重启";
            if (!Theme::confirm(
                    this, "服务控制",
                    QString("确定%1服务 %2？可能影响正在使用它的应用；不会自动控制依赖服务。")
                        .arg(verb, current["displayName"].toString()),
                    QString(verb) + "服务", command != "start"))
                return;
            notify("正在等待 Windows 服务实际状态…");
            post(
                route + "/control",
                {{"command", command}, {"expectedState", current["state"]}, {"expectedPid", current["pid"]}},
                [this](const QJsonObject& result) {
                    notify(result["message"].toString(), !result["completed"].toBool());
                    post("refresh", {});
                },
                35000);
        });
}
bool MainWindow::locateProcess(qint64 pid, const QString& expectedIdentity) {
    if (expectedIdentity.isEmpty()) {
        notify("缺少可验证的进程创建身份，不能仅凭 PID 跨页定位。", true);
        return false;
    }
    QJsonObject row;
    for (const auto& value : latest_["processes"].toArray())
        if (value.toObject()["pid"].toInteger() == pid &&
            value.toObject()["identity"].toString() == expectedIdentity) {
            row = value.toObject();
            break;
        }
    if (row.isEmpty()) {
        notify("所选进程身份已退出或当前快照尚未读取；未跳转到复用该 PID 的其它进程。", true);
        return false;
    }
    if (frozen_) {
        bool visible = false;
        for (const auto& item : shown_["processes"].toArray())
            visible = visible || item.toObject()["identity"].toString() == expectedIdentity;
        if (!visible) {
            notify("冻结的展示快照不包含此进程身份。请先取消冻结，再重新定位。", true);
            return false;
        }
    }
    switchPage(5);
    search_->clear();
    proxy_->session = -1;
    proxy_->onlyCurrentUser = false;
    proxy_->state = "all";
    proxy_->refreshFilter();
    const auto idx = proxy_->mapFromSource(model_->find(rowIdentity(row)));
    if (idx.isValid()) {
        table_->setCurrentIndex(idx);
        table_->scrollTo(idx);
        table_->setFocus();
        capturePagePreference();
        saveSoon();
        return true;
    }
    notify("当前筛选或展示快照中未找到该创建身份，未定位其它进程。", true);
    return false;
}
void MainWindow::locateServiceProcess(const QJsonObject& service) {
    const auto name = service["name"].toString();
    const auto route = "services/" + QString::fromUtf8(QUrl::toPercentEncoding(name)) + "/details";
    const auto matches = [service](const QJsonObject& current) {
        return current["name"].toString().compare(service["name"].toString(), Qt::CaseInsensitive) == 0 &&
               current["state"] == service["state"] && current["pid"] == service["pid"] &&
               current["pid"].isDouble() && current["pid"].toInteger() > 0;
    };
    api_.get(
        route, this, [this, route, matches, name](const QJsonObject& current, const QString& error, int) {
            if (!error.isEmpty() || !matches(current)) {
                notify(error.isEmpty() ? QStringLiteral("服务关联已变化，请刷新后重新选择。") : error, true);
                return;
            }
            api_.get(
                "snapshot", this,
                [this, route, matches, current, name](const QJsonObject& snapshot, const QString& failure,
                                                      int) {
                    if (!failure.isEmpty()) {
                        notify(failure, true);
                        return;
                    }
                    QJsonObject process;
                    for (const auto& value : snapshot["processes"].toArray()) {
                        const auto row = value.toObject();
                        if (row["pid"] == current["pid"] && row["identity"].isString()) {
                            process = row;
                            break;
                        }
                    }
                    if (process.isEmpty()) {
                        notify("当前服务关联进程已退出或尚未取得创建身份，请刷新后重试。", true);
                        return;
                    }
                    api_.get(
                        route, this,
                        [this, matches, process, snapshot, name](const QJsonObject& confirmed,
                                                                 const QString& readError, int) {
                            if (!readError.isEmpty() || !matches(confirmed)) {
                                notify(readError.isEmpty()
                                           ? QStringLiteral("核验期间服务关联发生变化，未定位其它进程。")
                                           : readError,
                                       true);
                                return;
                            }
                            if (snapshot["sampleId"].toInteger() >= latest_["sampleId"].toInteger())
                                latest_ = snapshot;
                            if (!frozen_)
                                shown_ = latest_;
                            if (locateProcess(process["pid"].toInteger(), process["identity"].toString()))
                                notify("已定位服务 " + name + " 的当前关联进程；此关联不表示历史进程身份。");
                        });
                });
        });
}
void MainWindow::contextMenu(const QPoint& point) {
    if (!point.isNull()) {
        const auto index = table_->indexAt(point);
        if (index.isValid())
            table_->setCurrentIndex(index);
    }
    const auto target = selected();
    if (target.isEmpty())
        return;
    QMenu menu(this);
    menu.setAccessibleName("所选项目操作");
    menu.setToolTipsVisible(true);
    auto add = [&](const QString& title, auto callback, bool enabled = true) {
        auto action = menu.addAction(title);
        action->setEnabled(enabled && connected_);
        if (!action->isEnabled())
            action->setToolTip(connected_ ? "当前目标状态不允许此操作；操作前仍会核对 Windows 实际权限和状态。"
                                         : "本机数据源尚未连接。");
        connect(action, &QAction::triggered, this, callback);
    };
    add("查看详情", [this, target] { details(target); });
    if (processPage()) {
        const bool identity = target["creationTimeFileTime"].isString();
        const bool can = target["canTerminate"].toBool();
        add("结束任务…", [this, target] { terminateProcess(target); }, can);
        add("结束进程树…", [this, target] { terminateTree(target); }, can);
        auto priority = menu.addMenu("设置优先级");
        for (const auto& pair : QList<QPair<QString, QString>>{{"空闲", "idle"},
                                                               {"低于正常", "belowNormal"},
                                                               {"正常", "normal"},
                                                               {"高于正常", "aboveNormal"},
                                                               {"高", "high"}}) {
            auto a = priority->addAction(pair.first);
            a->setEnabled(identity && connected_);
            connect(a, &QAction::triggered, this, [this, target, pair] { setPriority(target, pair.second); });
        }
        add("效率模式…", [this, target] { efficiency(target); }, identity);
        menu.addSeparator();
        add(
            "打开文件位置",
            [this, target] {
                post(processRoute(target, "file-action"),
                     {{"creationTimeFileTime", target["creationTimeFileTime"]}, {"action", "openLocation"}});
            },
            identity && target["path"].isString());
        add(
            "文件属性",
            [this, target] {
                post(processRoute(target, "file-action"),
                     {{"creationTimeFileTime", target["creationTimeFileTime"]}, {"action", "properties"}});
            },
            identity && target["path"].isString());
        add(
            "复制路径", [target] { QApplication::clipboard()->setText(target["path"].toString()); },
            target["path"].isString());
        add("转到详细信息",
            [this, target] { locateProcess(target["pid"].toInteger(), target["identity"].toString()); });
    } else if (page_ == 3) {
        add(
            "打开来源位置", [this, target] { post("startup/open-location", {{"id", target["id"]}}); },
            target["canOpenLocation"].toBool());
        add("复制启动命令", [this, target] {
            QApplication::clipboard()->setText(target["command"].toString());
            notify("已复制启动命令。");
        });
        add("Windows 启动设置",
            [this] { post("desktop/open-system-location", {{"location", "startupSettings"}}); });
    } else if (page_ == 6) {
        menu.addSeparator();
        const auto state = target["state"].toString();
        const bool stopped = state == "SERVICE_STOPPED" || state == "stopped";
        const bool active = state == "SERVICE_RUNNING" || state == "SERVICE_PAUSED" || state == "running" || state == "paused";
        add("启动服务…", [this, target] { controlService(target, "start"); }, stopped);
        add("停止服务…", [this, target] { controlService(target, "stop"); }, active);
        add("重启服务…", [this, target] { controlService(target, "restart"); }, active);
        add(
            "转到当前关联进程", [this, target] { locateServiceProcess(target); },
            target["pid"].toInteger() > 0);
    } else if (page_ == 4)
        add("查看该会话的进程", [this, target] { locateSession(target); });
    else if (page_ == 2)
        add("查看当前进程", [this, target] {
            locateProcess(target["pid"].toInteger(), target["identity"].toString());
        }, target["isRunning"].toBool());
    menu.addSeparator();
    add("复制名称", [target] { QApplication::clipboard()->setText(target["name"].toString()); });
    menu.exec(point.isNull()
                  ? table_->viewport()->mapToGlobal(table_->visualRect(table_->currentIndex()).bottomLeft())
                  : table_->viewport()->mapToGlobal(point));
    table_->setFocus();
}
void MainWindow::moreMenu() {
    QMenu menu(this);
    for (auto control : overflowCommands_) {
        auto action = menu.addAction(control->icon(), control->accessibleName());
        action->setEnabled(control->isEnabled());
        connect(action, &QAction::triggered, control, &QPushButton::click);
    }
    if (!overflowCommands_.isEmpty()) menu.addSeparator();
    auto add = [&](const QString& text, auto callback) {
        connect(menu.addAction(text), &QAction::triggered, this, callback);
    };
    if (page_ != 1 && page_ != 7 && page_ != 8) {
        add("导出当前筛选 CSV…", [this] { exportCsv(); });
        add("显示列 / 设置列宽…", [this] { columnMenu(); });
    }
    if (processPage()) {
        auto tree = menu.addAction("进程树视图");
        tree->setCheckable(true);
        tree->setChecked(preferences_["processView"].toString() == "tree");
        connect(tree, &QAction::triggered, this, [this](bool value) {
            preferences_["processView"] = value ? "tree" : "flat";
            table_->setRootIsDecorated(page_ == 0 && value);
            table_->setItemsExpandable(table_->rootIsDecorated());
            updateTable();
            saveSoon();
        });
        add("筛选进程…", [this] { filters(); });
    }
    if (page_ == 2 && !overflowCommands_.contains(pageAction_))
        add("清除本次会话统计…", [this] {
            if (Theme::confirm(this, "清除统计",
                               "仅清除本程序本次采集会话的历史，不修改 Windows 应用使用历史。",
                               "清除统计", true))
                post("history/reset", {});
        });
    if (page_ == 3 && !overflowCommands_.contains(pageAction_))
        add("Windows 启动设置",
            [this] { post("desktop/open-system-location", {{"location", "startupSettings"}}); });
    if (page_ == 6 && !overflowCommands_.contains(pageAction_))
        add("Windows 服务管理器",
            [this] { post("desktop/open-system-location", {{"location", "services"}}); });
    menu.addSeparator();
    auto freeze = menu.addAction("冻结显示（后台继续采集）");
    freeze->setCheckable(true);
    freeze->setChecked(frozen_);
    connect(freeze, &QAction::triggered, this, [this](bool checked) {
        frozen_ = checked;
        if (!checked)
            shown_ = latest_;
        applySnapshot();
    });
    auto collapsed = menu.addAction("折叠侧栏");
    collapsed->setCheckable(true);
    collapsed->setChecked(preferences_["sidebarCollapsed"].toBool());
    connect(collapsed, &QAction::triggered, this, [this](bool value) {
        preferences_["sidebarCollapsed"] = value;
        sidebar_->setFixedWidth(value ? Theme::sidebarCompact : Theme::sidebarWidth);
        for (int i = 0; i < nav_.size(); ++i) {
            nav_[i]->setText(value ? QString() : titles_[i]);
            nav_[i]->setProperty("collapsed", value);
            Theme::repolish(nav_[i]);
        }
        navUpdate_->setText(value ? QString() : "可用更新");
        saveSoon();
    });
    add("来源与采样状态", [this] {
        QStringList out{"采集时间  " + formatValue(latest_["capturedAtUtc"], "date"),
                        "本次会话开始  " + formatValue(latest_["sessionStartedAtUtc"], "date"),
                        "后台来源时间  " + formatValue(latest_["slowDataCapturedAtUtc"], "date"),
                        "真实采样耗时  " + formatValue(latest_["samplingDurationMs"], "number") + " ms",
                        networkStatusText(latest_["processNetwork"].toObject())};
        for (const auto& w : latest_["warnings"].toArray())
            out << w.toObject()["message"].toString();
        presentText("数据来源与状态", out.join("\n"));
    });
    menu.exec(more_->mapToGlobal(QPoint(more_->width() - menu.sizeHint().width(), more_->height())));
}
void MainWindow::columnMenu() {
    if (page_ == 1 || page_ == 7 || page_ == 8)
        return;
    auto d = new QDialog(this);
    d->setAttribute(Qt::WA_DeleteOnClose);
    d->setWindowTitle("显示列与列宽");
    d->resize(Theme::dialogWidth, 650);
    auto layout = Theme::dialogLayout(d);
    auto scroll = new QScrollArea(d);
    scroll->setWidgetResizable(true);
    auto content = new QWidget(scroll);
    auto form = new QFormLayout(content);
    QVector<QCheckBox*> checks;
    QVector<QSpinBox*> widths;
    for (int i = 0; i < model_->columns.size(); ++i) {
        const auto& col = model_->columns[i];
        auto row = new QWidget(content);
        auto line = new QHBoxLayout(row);
        line->setContentsMargins(0, 0, 0, 0);
        auto check = new QCheckBox(col.title, row);
        check->setChecked(!table_->isColumnHidden(i));
        if (col.key == "name" || (processPage() && col.key == "pid"))
            check->setEnabled(false);
        auto width = new Theme::SpinBox(row);
        width->setAccessibleName(col.title + "列宽");
        width->setRange(64, 1600);
        width->setSuffix(" px");
        width->setValue(table_->columnWidth(i));
        line->addWidget(check, 1);
        line->addWidget(width);
        form->addRow(row);
        checks << check;
        widths << width;
    }
    scroll->setWidget(content);
    layout->addWidget(new QLabel("列头可拖动调宽；也可在此通过键盘设置数值。名称和 PID 为必要列。", d));
    layout->addWidget(scroll);
    auto apply = button("应用", d, true);
    layout->addWidget(apply, 0, Qt::AlignRight);
    const int page = page_;
    connect(apply, &QPushButton::clicked, d, [this, d, checks, widths, page] {
        if (page != page_) {
            d->reject();
            return;
        }
        for (int i = 0; i < checks.size(); ++i) {
            table_->setColumnHidden(i, !checks[i]->isChecked());
            table_->setColumnWidth(i, widths[i]->value());
        }
        capturePagePreference();
        saveSoon();
        d->accept();
    });
    d->open();
}
void MainWindow::filters() {
    auto d = new QDialog(this);
    d->setAttribute(Qt::WA_DeleteOnClose);
    d->setWindowTitle("筛选进程");
    auto layout = Theme::dialogLayout(d);
    auto form = new QFormLayout();
    form->setSpacing(Theme::space3);
    layout->addLayout(form);
    auto user = new QCheckBox("仅当前账户", d);
    user->setChecked(proxy_->onlyCurrentUser);
    user->setEnabled(!bootstrap_["currentUserName"].toString().isEmpty());
    form->addRow(user);
    auto session = new Theme::SpinBox(d);
    session->setRange(-1, 65535);
    session->setSpecialValueText("全部会话");
    session->setValue(proxy_->session);
    form->addRow("会话 ID", session);
    auto state = new Theme::ComboBox(d);
    state->addItem("全部状态", "all");
    state->addItem("运行中", "running");
    state->addItem("受限", "restricted");
    state->setCurrentIndex(state->findData(proxy_->state));
    form->addRow("状态", state);
    auto apply = button("应用筛选", d, true);
    layout->addWidget(apply, 0, Qt::AlignRight);
    connect(apply, &QPushButton::clicked, d, [this, d, user, session, state] {
        proxy_->onlyCurrentUser = user->isChecked();
        proxy_->session = session->value();
        proxy_->state = state->currentData().toString();
        proxy_->refreshFilter();
        auto pages = preferences_["pages"].toObject();
        auto p = pages[keys_[page_]].toObject();
        p["filters"] =
            QJsonObject{{"currentUser", proxy_->onlyCurrentUser},
                        {"sessionId", proxy_->session < 0 ? QJsonValue() : QJsonValue(proxy_->session)},
                        {"state", proxy_->state}};
        pages[keys_[page_]] = p;
        preferences_["pages"] = pages;
        saveSoon();
        d->accept();
    });
    d->open();
}
void MainWindow::exportCsv() {
    const auto file =
        QFileDialog::getSaveFileName(this, "导出当前筛选", titles_[page_] + ".csv", "CSV (*.csv)");
    if (file.isEmpty())
        return;
    QSaveFile out(file);
    if (!out.open(QIODevice::WriteOnly)) {
        notify("无法写入所选位置，请检查权限。", true);
        return;
    }
    QTextStream stream(&out);
    stream.setEncoding(QStringConverter::Utf8);
    stream.setGenerateByteOrderMark(true);
    auto quote = [](QString value) {
        value.replace('"', "\"\"");
        return '"' + value + '"';
    };
    QStringList header;
    QList<int> cols;
    for (int col = 0; col < model_->columns.size(); ++col)
        if (!table_->isColumnHidden(col)) {
            cols << col;
            header << quote(model_->columns[col].title);
        }
    stream << header.join(',') << "\r\n";
    std::function<void(const QModelIndex&)> rows = [&](const QModelIndex& parent) {
        for (int row = 0; row < proxy_->rowCount(parent); ++row) {
            QStringList values;
            for (const auto col : cols)
                values << quote(proxy_->index(row, col, parent).data().toString());
            stream << values.join(',') << "\r\n";
            rows(proxy_->index(row, 0, parent));
        }
    };
    rows({});
    stream.flush();
    if (!out.commit()) {
        notify("导出保存失败，原文件未替换。", true);
        return;
    }
    notify("已导出当前全部筛选结果及可见列。数值来自当前真实展示快照。");
}
