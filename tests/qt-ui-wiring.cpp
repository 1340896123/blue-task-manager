// Focused native component regression against the real owning Host and Windows.
// All rows come from production HTTP; this harness sends no physical input.
#include "MainWindow.h"
#include "Theme.h"
#include <QCommandLineParser>
#include <QDialog>
#include <QElapsedTimer>
#include <QJsonDocument>
#include <QLocale>
#include <QPlainTextEdit>
#include <QSaveFile>
#include <QThread>
#include <stdexcept>

namespace {
QJsonArray checks;
QJsonObject report;
QString output;
void save() {
    report["checks"] = checks;
    QSaveFile file(output);
    if (!file.open(QIODevice::WriteOnly)) throw std::runtime_error("Cannot save wiring evidence");
    file.write(QJsonDocument(report).toJson(QJsonDocument::Indented));
    if (!file.commit()) throw std::runtime_error("Cannot commit wiring evidence");
}
void check(bool valid, const QString& label) {
    if (!valid) throw std::runtime_error(label.toStdString());
    checks.append(label); save();
}
void waitFor(const std::function<bool()>& ready, const char* message, int timeout = 20000) {
    QElapsedTimer elapsed; elapsed.start();
    while (!ready()) {
        if (elapsed.elapsed() > timeout) throw std::runtime_error(message);
        QCoreApplication::processEvents(QEventLoop::AllEvents, 20); QThread::msleep(15);
    }
}
QJsonObject request(ApiClient& client, const QString& route, const QJsonObject* body = nullptr) {
    QObject context; bool done = false; QJsonObject result; QString failure;
    auto callback = [&](const QJsonObject& value, const QString& error, int) {
        result = value; failure = error; done = true;
    };
    if (body) client.post(route, *body, &context, callback); else client.get(route, &context, callback);
    waitFor([&] { return done; }, "Production HTTP timeout");
    if (!failure.isEmpty()) throw std::runtime_error((route + ": " + failure).toStdString());
    return result;
}
QPushButton* button(QWidget& parent, const QString& text) {
    for (auto candidate : parent.findChildren<QPushButton*>())
        if (candidate->accessibleName() == text) return candidate;
    throw std::runtime_error(("Native button missing: " + text).toStdString());
}
QDialog* dialog(const QString& prefix) {
    for (auto widget : QApplication::topLevelWidgets())
        if (auto d = qobject_cast<QDialog*>(widget); d && d->isVisible() && d->windowTitle().startsWith(prefix))
            return d;
    return nullptr;
}
QModelIndex find(QTreeView* table, const QString& identity) {
    for (int i = 0; i < table->model()->rowCount(); ++i) {
        const auto index = table->model()->index(i, 0);
        if (index.data(DataModel::IdentityRole).toString() == identity) return index;
    }
    return {};
}
}
int main(int argc, char** argv) {
    QApplication app(argc, argv); app.setApplicationName("Takao"); Theme::apply(app);
    QCommandLineParser parser; parser.addOption({"origin", "Origin", "url"});
    parser.addOption({"host-pid", "Host", "pid"}); parser.process(app);
    output = qEnvironmentVariable("TASKMANAGER_QT_WIRING_RESULT");
    if (output.isEmpty()) return 2;
    const auto hostPid = parser.value("host-pid").toLongLong();
    MainWindow window(QUrl(parser.value("origin")), hostPid); window.resize(1920, 1080); window.show();
    ApiClient inspector(QUrl(parser.value("origin")), &app); QJsonObject owned;
    report = {{"result", "RUNNING"}, {"scope", "Native Qt components + production Windows HTTP; no physical input assertions"}};
    QTimer::singleShot(0, &window, [&] {
        try {
            const auto bootstrap = request(inspector, "bootstrap"); inspector.setToken(bootstrap["token"].toString());
            check(bootstrap["hostPid"].toInteger() == hostPid && bootstrap["desktopProcessId"].toInteger() == QCoreApplication::applicationPid(),
                  "Production Host owns this exact native wiring harness");
            auto table = window.findChild<QTreeView*>("dataTable");
            auto search = window.findChild<QLineEdit*>("globalSearch");
            waitFor([&] { return table && search && search->text() == "Takao-wiring-loaded" &&
                !request(inspector, "snapshot")["slowDataCapturedAtUtc"].isNull(); }, "Initial real table and applied preference query");
            search->setText("Takao-wiring-unmatched-query");
            const QStringList pages{"性能", "应用历史记录", "启动应用", "用户", "详细信息", "服务", "设置"};
            for (const auto& page : pages) {
                button(window, page)->click();
                check(search->text().isEmpty(), "New page does not inherit an unrelated process query: " + page);
            }
            button(window, "进程")->click();
            check(search->text() == "Takao-wiring-unmatched-query", "Returning restores only that page's actual query");
            search->clear();
            waitFor([&] { return request(inspector, "preferences")["preferences"].toObject()["pages"].toObject().contains("settings"); },
                    "Eight-page preferences must pass the production validator");
            const auto persisted = request(inspector, "preferences");
            check(persisted["warning"].isNull(), "Eight-page navigation saves valid real preferences");
            QJsonObject snapshot = request(inspector, "snapshot");
            QJsonObject hostRow;
            for (const auto& item : snapshot["processes"].toArray())
                if (item.toObject()["pid"].toInteger() == hostPid) hostRow = item.toObject();
            search->setText(hostRow["identity"].toString());
            waitFor([&] { return find(table, hostRow["identity"].toString()).isValid(); }, "Owning Host identity visible");
            table->setCurrentIndex(find(table, hostRow["identity"].toString()));
            button(window, "详情")->click();
            waitFor([&] { auto d = dialog("进程详情"); return d && d->findChild<QPlainTextEdit*>()->toPlainText().contains("累计 I/O 写入"); }, "Detailed telemetry fields");
            const auto details = dialog("进程详情")->findChild<QPlainTextEdit*>()->toPlainText();
            for (const auto& field : QStringList{"父 PID", "会话 ID", "累计 CPU 时间", "优先级", "网络窗口时间", "GPU 引擎", "GPU 采集时间", "服务来源时间"})
                check(details.contains(field), "Actual process details exposes: " + field);
            const auto verified = request(inspector, QString("processes/%1/details?creationTimeFileTime=%2")
                .arg(hostPid).arg(hostRow["creationTimeFileTime"].toString()));
            const auto process = verified["process"].toObject();
            const auto lines = details.split('\n');
            const QList<QPair<QString, QString>> facts{
                {"创建身份", process["creationTimeFileTime"].toString()},
                {"优先级", process["priority"].toString()},
                {"路径", process["path"].toString()}};
            for (const auto& fact : facts)
                check(!fact.second.isEmpty() && lines.contains(fact.first + "  " + fact.second),
                      "Rendered detail equals an independent real API read: " + fact.first);
            for (const auto& field : QList<QPair<QString, QString>>{{"父 PID", "parentPid"}, {"会话 ID", "sessionId"}}) {
                QString rendered;
                for (const auto& line : lines)
                    if (line.startsWith(field.first + "  ")) rendered = line.mid(field.first.size() + 2);
                bool parsed = false;
                const auto number = QLocale().toLongLong(rendered, &parsed);
                check(parsed && process[field.second].isDouble() && number == process[field.second].toInteger(),
                      "Localized numeric detail equals an independent real API read: " + field.first);
            }
            dialog("进程详情")->close();
            waitFor([&] {
                const auto state = request(inspector, "snapshot")["processNetwork"].toObject();
                report["networkObservation"] = state; save();
                return state["status"] == "available";
            },
                    "Production network provider becomes available", 30000);
            const auto network = request(inspector, "snapshot")["processNetwork"].toObject();
            const auto provider = network["source"].toString();
            check(provider == "classicSystemLogger" || provider == "kernelNetworkManifest",
                  "Real network telemetry names its actual standard ETW provider");
            report["networkSource"] = provider;
            button(window, "设置")->click();
            waitFor([&] { for (auto label : window.findChildren<QLabel*>())
                if (label->accessibleName() == "Windows 数据采样状态")
                    return label->text().contains(provider == "kernelNetworkManifest"
                        ? "Windows Kernel-Network ETW" : "Windows 系统 ETW") && label->text().contains("可用");
                return false; }, "Rendered source matches the independently read production provider");
            check(true, "Settings renders the actual ETW provider and available source state");
            button(window, "用户")->click();
            waitFor([&] { return table->model()->rowCount() > 0; }, "Actual WTS sessions");
            table->setCurrentIndex(table->model()->index(0, 0));
            const QJsonValue session = QJsonObject::fromVariantMap(table->currentIndex().data(DataModel::ObjectRole).toMap())["sessionId"];
            button(window, "详情")->click();
            check(search->text().isEmpty(), "Session navigation clears the previous process search");
            waitFor([&] { const auto p = request(inspector, "preferences")["preferences"].toObject()["pages"].toObject()["processes"].toObject();
                return p["filters"].toObject()["sessionId"] == session && p["query"].toString().isEmpty(); }, "Persist the actual selected session filter");
            check(true, "Session navigation persists its real filter through the strict validator");
            // Create and terminate only a dedicated real workload, retaining its historical observation.
            const QJsonObject start{{"executable", qEnvironmentVariable("TASKMANAGER_QT_WORKLOAD")},
                {"arguments", "\"" + qEnvironmentVariable("TASKMANAGER_QT_TEST_DIRECTORY") + "/wiring-workload\" 90"}};
            owned = request(inspector, "processes/start", &start);
            const auto identity = owned["identity"].toString();
            waitFor([&] { for (const auto& item : request(inspector, "snapshot")["history"].toArray())
                if (item.toObject()["identity"].toString() == identity) return true; return false; }, "Observe dedicated workload history");
            const QJsonObject terminate{{"creationTimeFileTime", owned["creationTimeFileTime"]}};
            request(inspector, QString("processes/%1/terminate").arg(owned["pid"].toInteger()), &terminate);
            owned = {};
            button(window, "应用历史记录")->click(); search->setText(identity);
            waitFor([&] { const auto index = find(table, identity); if (!index.isValid()) return false;
                return !QJsonObject::fromVariantMap(index.data(DataModel::ObjectRole).toMap())["isRunning"].toBool(); }, "Exited workload remains in real history");
            table->setCurrentIndex(find(table, identity)); button(window, "详情")->click();
            waitFor([&] { return dialog("本次会话观测"); }, "Open exited observation details");
            auto history = dialog("本次会话观测");
            check(history->findChild<QPlainTextEdit*>()->toPlainText().contains(identity) && !button(*history, "查看当前进程")->isEnabled(),
                  "Exited history exposes its stored identity and disables live navigation");
            history->close();
            report["result"] = "PASS"; save();
        } catch (const std::exception& error) {
            report["result"] = "FAIL"; report["error"] = QString::fromUtf8(error.what()); save();
            if (!owned.isEmpty()) try {
                const QJsonObject terminate{{"creationTimeFileTime", owned["creationTimeFileTime"]}};
                request(inspector, QString("processes/%1/terminate").arg(owned["pid"].toInteger()), &terminate);
            } catch (...) {}
        }
        window.close(); QTimer::singleShot(3000, &app, &QCoreApplication::quit);
    });
    const auto result = app.exec(); return report["result"].toString() == "PASS" ? result : 1;
}
