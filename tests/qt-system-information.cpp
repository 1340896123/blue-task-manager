// Focused production-HTTP/native-widget checks; no simulated hardware or OS input.
#include "MainWindow.h"
#include "Theme.h"
#include <QCommandLineParser>
#include <QClipboard>
#include <QElapsedTimer>
#include <QJsonDocument>
#include <QSaveFile>
#include <QScrollBar>
#include <QThread>
#include <stdexcept>

namespace {
QString output;
QJsonObject report;
QJsonArray checks;
void save() {
    report["checks"] = checks;
    QSaveFile file(output);
    if (!file.open(QIODevice::WriteOnly) || file.write(QJsonDocument(report).toJson()) < 0 || !file.commit())
        throw std::runtime_error("Cannot write component evidence");
}
void require(bool valid, const QString& text) {
    if (!valid) throw std::runtime_error(text.toStdString());
    checks.append(text); save();
}
void waitFor(const std::function<bool()>& ready, const char* message, int timeout = 20000) {
    QElapsedTimer timer; timer.start();
    while (!ready()) {
        if (timer.elapsed() > timeout) throw std::runtime_error(message);
        QCoreApplication::processEvents(QEventLoop::AllEvents, 20); QThread::msleep(10);
    }
}
QJsonObject request(ApiClient& client, const QString& route) {
    QObject context; bool done = false; QJsonObject result; QString failure;
    client.get(route, &context, [&](const QJsonObject& value, const QString& error, int) { result = value; failure = error; done = true; });
    waitFor([&] { return done; }, "Production HTTP timeout");
    if (!failure.isEmpty()) throw std::runtime_error(failure.toStdString());
    return result;
}
void checkCopy(MainWindow& window, SystemInformationView* page, QLineEdit* search,
               const QJsonObject& hardware, ApiClient& inspector) {
    auto copy = window.findChild<QPushButton*>("copySystemInformation");
    QPushButton* refresh = nullptr;
    for (auto button : window.findChildren<QPushButton*>())
        if (button->accessibleName() == "刷新") refresh = button;
    // Loading completion changes Refresh's text and queues a command-bar
    // layout. Measure the completed layout before checking copy feedback.
    QCoreApplication::processEvents();
    window.findChild<QWidget*>("pageCommandBar")->layout()->activate();
    require(copy && copy->isVisible() && copy->isEnabled() && refresh, "Loaded real hardware enables the native copy command");
    require(copy->accessibleName() == "复制系统信息" && !copy->accessibleDescription().isEmpty(), "Copy has a Chinese accessible name and scope description");
    require(copy->geometry().right() < refresh->geometry().left() && copy->height() == refresh->height(), "Copy sits left of refresh with the same native command height");
    const auto copyBounds = copy->geometry(), refreshBounds = refresh->geometry();
    const auto priorClipboard = QApplication::clipboard()->text();
    search->setFocus();
    copy->click();
    QCoreApplication::processEvents();
    const auto copied = QApplication::clipboard()->text();
    require(copied.startsWith("系统信息\n") && copied.contains(hardware["capturedAtUtc"].toString()), "Actual system clipboard contains the current collected timestamp");
    require(copied.contains("状态：") && copied.contains("GiB"), "Copied plain text retains status and displayed units");
    for (const auto& key : {"processors", "graphicsAdapters", "motherboard", "memory", "disks", "displays", "operatingSystem"}) {
        const auto group = window.findChild<QWidget*>("hardware-" + QString(key));
        require(copied.contains(group->property("groupTitle").toString()), "Clipboard contains real category: " + QString(key));
        for (const auto label : group->findChildren<QLabel*>())
            require(label->text().isEmpty() || copied.contains(label->text()), "Clipboard retains rendered hardware fact or reason");
    }
    require(search->hasFocus() && copy->text() == "已复制", "Copy confirms through its label without stealing the existing input focus");
    require(copy->geometry() == copyBounds && refresh->geometry() == refreshBounds, "Success feedback does not move either command");
    search->setText("内存");
    copy->click();
    QCoreApplication::processEvents();
    if (QApplication::clipboard()->text() != copied || search->text() != "内存") {
        report["copyDifference"] = QJsonObject{{"initialHeader", copied.section('\n', 0, 4)},
            {"filteredHeader", QApplication::clipboard()->text().section('\n', 0, 4)}, {"query", search->text()},
            {"initialLength", copied.size()}, {"filteredLength", QApplication::clipboard()->text().size()}};
        save();
    }
    require(QApplication::clipboard()->text() == copied && search->text() == "内存", "Filtered search still copies the complete snapshot and preserves the query");
    search->setText("Takao-copy-no-matching-hardware");
    copy->click();
    QCoreApplication::processEvents();
    require(QApplication::clipboard()->text() == copied, "An empty search result still copies the complete real snapshot");
    search->clear();
    QCoreApplication::processEvents();
    page->verticalScrollBar()->setValue(page->verticalScrollBar()->maximum());
    const auto scroll = page->scrollPosition();
    report["hardwareScrollMaximum"] = page->verticalScrollBar()->maximum();
    report["hardwareScrollPosition"] = scroll;
    copy->setFocus();
    copy->click();
    QCoreApplication::processEvents();
    require(QApplication::clipboard()->text() == copied && page->scrollPosition() == scroll && copy->hasFocus(),
            scroll > 0 ? "Scrolled copy preserves full content, scroll position and command focus"
                       : "All real hardware fits without scrolling; copy preserves full content and command focus");
    require(request(inspector, "system-information")["capturedAtUtc"] == hardware["capturedAtUtc"], "Copy does not trigger a new hardware collection");
    waitFor([&] { return copy->text() == "复制"; }, "Copy feedback did not reset", 4000);
    require(copy->geometry() == copyBounds, "Copy label resets without changing layout");
    window.findChild<QPushButton*>("navigation-processes")->click();
    require(!copy->isVisible(), "Copy is hidden on the original processes page");
    QApplication::clipboard()->setText(priorClipboard);
}
}
int main(int argc, char** argv) {
    QApplication app(argc, argv); app.setApplicationName("Takao"); Theme::apply(app);
    QCommandLineParser parser; parser.addOption({"origin", "Origin", "url"}); parser.addOption({"host-pid", "Host", "pid"}); parser.process(app);
    output = qEnvironmentVariable("TASKMANAGER_QT_SYSTEM_INFORMATION_RESULT");
    if (output.isEmpty()) return 2;
    const auto hostPid = parser.value("host-pid").toLongLong();
    const bool copyOnly = qEnvironmentVariableIsSet("TASKMANAGER_QT_COPY_ONLY");
    MainWindow window(QUrl(parser.value("origin")), hostPid); window.resize(1920, 1080); window.show();
    ApiClient inspector(QUrl(parser.value("origin")), &app);
    report = {{"result", "RUNNING"}, {"scope", "New system information / existing CPU modes / settings only; real Host and native Qt components; no physical input assertions"}};
    if (copyOnly) report["scope"] = "System information copy only; real Host, native Qt and system clipboard; no physical input assertions";
    QTimer::singleShot(0, &window, [&] {
        try {
            const auto bootstrap = request(inspector, "bootstrap");
            require(bootstrap["hostPid"].toInteger() == hostPid && bootstrap["desktopProcessId"].toInteger() == QCoreApplication::applicationPid(), "Owning production Host has this exact native test child");
            auto search = window.findChild<QLineEdit*>("globalSearch");
            waitFor([&] { return search && search->text() == "Takao-si-loaded"; }, "Initial isolated preference load");
            auto navigation = [&](const QString& key) {
                auto result = window.findChild<QPushButton*>("navigation-" + key);
                if (!result) throw std::runtime_error("Navigation button missing");
                return result;
            };
            auto page = static_cast<SystemInformationView*>(window.findChild<QScrollArea*>("systemInformationPage"));
            if (!page) throw std::runtime_error("System information view missing");
            if (copyOnly) require(!window.findChild<QPushButton*>("copySystemInformation")->isEnabled(), "Copy is disabled before the first real hardware snapshot");
            navigation("systemInformation")->click();
            auto hardware = request(inspector, "system-information");
            const auto processor = hardware["processors"].toArray().first().toObject();
            waitFor([&] { return window.findChild<QLabel*>("hardware-name-" + processor["id"].toString()); }, "Actual hardware has not rendered");
            if (copyOnly) {
                checkCopy(window, page, search, hardware, inspector);
            } else {
            require(navigation("services")->y() < navigation("systemInformation")->y() && navigation("systemInformation")->y() < navigation("settings")->y(), "System information sits immediately after services and before bottom settings");
            require(search->text().isEmpty(), "New hardware page starts with its own search preference");
            for (const auto& key : {"processors", "graphicsAdapters", "motherboard", "memory", "disks", "displays", "operatingSystem"})
                require(window.findChild<QWidget*>("hardware-" + QString(key))->isVisible(), "Real hardware category rendered: " + QString(key));
            for (const auto& key : {"processors", "graphicsAdapters", "disks", "displays"})
                for (const auto& entry : hardware[key].toArray()) {
                    const auto source = entry.toObject();
                    auto name = window.findChild<QLabel*>("hardware-name-" + source["id"].toString());
                    const auto field = QString(key) == "disks" ? "model" : "name";
                    require(name && name->text() == source[field].toString("—"), "Rendered device identity matches real HTTP: " + source["id"].toString());
                    require(name->textInteractionFlags().testFlag(Qt::TextSelectableByKeyboard) && name->wordWrap(), "Actual device model is selectable and wraps");
                }
            for (const auto& entry : hardware["memory"].toObject()["modules"].toArray()) {
                const auto source = entry.toObject();
                auto facts = window.findChild<QLabel*>("hardware-facts-" + source["id"].toString());
                require(facts && facts->text().contains(source["deviceLocator"].toString()) && facts->text().contains("MT/s"), "Individual DIMM uses actual slot and firmware transfer-rate units");
            }
            require(page->horizontalScrollBar()->maximum() == 0, "Hardware overview has no page horizontal overflow");
            search->setFocus(); search->setText("内存");
            require(window.findChild<QWidget*>("hardware-memory")->isVisible() && !window.findChild<QWidget*>("hardware-disks")->isVisible(), "Shared search filters real hardware categories");
            auto refresh = [&] { for (auto b : window.findChildren<QPushButton*>()) if (b->accessibleName() == "刷新") return b; return static_cast<QPushButton*>(nullptr); }();
            require(refresh != nullptr, "Existing refresh command is available on the hardware page");
            const auto captured = hardware["capturedAtUtc"];
            refresh->click();
            waitFor([&] { return refresh->isEnabled() && request(inspector, "system-information")["capturedAtUtc"] != captured; }, "Real hardware refresh did not advance");
            require(search->hasFocus() && search->text() == "内存", "Native refresh preserves actual search focus and query");
            search->setText("Takao-si-no-such-hardware");
            require(window.findChild<QLabel*>("pageCaption")->accessibleDescription().contains("没有匹配"), "Unmatched hardware search has an explicit empty state");
            search->clear();
            QCoreApplication::processEvents();
            const auto scroll = page->verticalScrollBar()->maximum();
            if (scroll > 0) {
                page->verticalScrollBar()->setValue(scroll);
                const auto before = page->scrollPosition();
                refresh->click();
                waitFor([&] { return refresh->isEnabled(); }, "Hardware refresh busy state");
                QCoreApplication::processEvents();
                require(page->scrollPosition() == before, "Actual hardware refresh preserves the scroll position");
            }
            navigation("performance")->click();
            auto resources = window.findChild<QListWidget*>();
            require(resources && resources->currentItem() && resources->currentItem()->data(Qt::UserRole).toString() == "cpu:", "Legacy hwinfo resource preference falls back to CPU");
            for (int i = 0; i < resources->count(); ++i)
                require(!resources->item(i)->text().contains("HWiNFO") && resources->item(i)->data(Qt::UserRole).toString() != "hwinfo:", "Performance resource contains no removed HWiNFO entry");
            QComboBox* mode = nullptr;
            for (auto combo : window.findChildren<QComboBox*>()) if (combo->accessibleName() == "CPU 图表模式") mode = combo;
            require(mode && mode->isVisible(), "CPU graph mode control remains available");
            mode->setCurrentIndex(1);
            require(mode->currentText() == "逻辑处理器", "CPU logical processor mode remains selectable");
            mode->setCurrentIndex(0);
            require(mode->currentText() == "总体使用率", "CPU total usage mode remains selectable");
            navigation("settings")->click();
            require(navigation("settings")->isChecked() && window.findChild<QLabel*>("pageCaption")->accessibleDescription().contains("采样"), "Original settings identity and page are preserved");
            navigation("systemInformation")->click(); search->setText("内存");
            waitFor([&] { return request(inspector, "preferences")["preferences"].toObject()["pages"].toObject()["systemInformation"].toObject()["query"].toString() == "内存"; }, "New hardware preference was not saved");
            require(true, "New hardware query saves through the production preference validator");
            require(request(inspector, "preferences")["preferences"].toObject()["pages"].toObject()["performance"].toObject()["resourceType"].toString() == "cpu", "Migrated HWiNFO selection is persisted as the actual CPU resource");
            }
            report["result"] = "PASS"; save();
        } catch (const std::exception& error) { report["result"] = "FAIL"; report["error"] = error.what(); save(); }
        const auto code = report["result"] == "PASS" ? 0 : 1;
        window.close(); QTimer::singleShot(200, &app, [&app, code] { app.exit(code); });
    });
    return app.exec();
}
