// Fast page screenshots through the production UI and real owning Host.
// Qt's public click() emits the normal button signals without OS input:
// https://github.com/qt/qtbase/blob/c07c2d5a527a644d36e7853d55132ae38921682f/src/widgets/widgets/qabstractbutton.cpp
#include "MainWindow.h"
#include "Theme.h"
#include <QCommandLineParser>
#include <QDir>
#include <QElapsedTimer>
#include <QEvent>
#include <QFileInfo>
#include <QJsonArray>
#include <QJsonDocument>
#include <QLayout>
#include <QSaveFile>
#include <QThread>
#include <QWindow>
#include <stdexcept>

namespace {
QString progressPath, acknowledgementDirectory;
QJsonObject report;
QJsonArray checks, pages;
QElapsedTimer elapsed;
void save() {
    report["checks"] = checks;
    report["pages"] = pages;
    report["elapsedMs"] = elapsed.elapsed();
    QSaveFile file(progressPath);
    if (!file.open(QIODevice::WriteOnly) ||
        file.write(QJsonDocument(report).toJson(QJsonDocument::Indented)) < 0 || !file.commit())
        throw std::runtime_error("Cannot atomically save page-capture progress");
}
void require(bool condition, const QString& message) {
    if (!condition)
        throw std::runtime_error(message.toStdString());
    checks.append(message);
}
void waitFor(const std::function<bool()>& predicate, const char* message, int timeout = 20000) {
    QElapsedTimer timer;
    timer.start();
    while (!predicate()) {
        if (timer.elapsed() >= timeout)
            throw std::runtime_error(message);
        QCoreApplication::processEvents(QEventLoop::AllEvents, 10);
        QThread::msleep(10);
    }
}
QJsonObject request(ApiClient& client, const QString& route) {
    QObject context;
    bool done = false;
    QJsonObject result;
    QString failure;
    client.get(route, &context, [&](const QJsonObject& value, const QString& error, int) {
        result = value;
        failure = error;
        done = true;
    });
    waitFor([&] { return done; }, "Real backend request timed out");
    require(failure.isEmpty(), "Real backend request succeeded: " + route);
    return result;
}
QPushButton* navigation(MainWindow& window, const QString& title) {
    for (auto button : window.findChildren<QPushButton*>())
        if (button->property("nav").toBool() && button->accessibleName() == title)
            return button;
    throw std::runtime_error("Required native navigation control is missing");
}
} // namespace

int main(int argc, char** argv) {
    QApplication app(argc, argv);
    app.setApplicationName("Takao");
    app.setOrganizationName("Takao");
    Theme::apply(app);
    app.setWindowIcon(QIcon(QDir(QCoreApplication::applicationDirPath()).filePath("takao.ico")));
    QCommandLineParser parser;
    parser.addOption({"origin", "Owning Host HTTP origin", "url"});
    parser.addOption({"host-pid", "Owning Host PID", "pid"});
    parser.process(app);
    const QUrl origin(parser.value("origin"));
    const auto hostPid = parser.value("host-pid").toLongLong();
    progressPath = qEnvironmentVariable("TASKMANAGER_QT_PAGE_PROGRESS");
    acknowledgementDirectory = qEnvironmentVariable("TASKMANAGER_QT_PAGE_ACK_DIRECTORY");
    if (progressPath.isEmpty() || acknowledgementDirectory.isEmpty() ||
        origin.scheme() != "http" || origin.host() != "127.0.0.1" || hostPid <= 0)
        return 2;
    elapsed.start();
    report = {{"result", "RUNNING"}, {"phase", "starting"},
              {"scope", "Production Qt component navigation + real HTTP/JSON.NET/CsWin32; no OS input"},
              {"hostPid", hostPid}, {"qtPid", QCoreApplication::applicationPid()}};
    MainWindow window(origin, hostPid);
    window.show();
    report["hwnd"] = QString::number(static_cast<qulonglong>(window.winId()));
    ApiClient inspector(origin, &app);
    QTimer::singleShot(0, &window, [&] {
        try {
            save();
            const auto bootstrap = request(inspector, "bootstrap");
            require(bootstrap["hostPid"].toInteger() == hostPid &&
                        bootstrap["desktopProcessId"].toInteger() == QCoreApplication::applicationPid(),
                    "Production Host owns the exact page-capture Qt child");
            const auto preferences = request(inspector, "preferences");
            const auto ui = preferences["preferences"].toObject();
            require(preferences["warning"].isNull() && ui["density"].toString() == "compact" &&
                        ui["processView"].toString() == "flat",
                    "Temporary UI preferences load without a recovery warning");
            auto table = window.findChild<QTreeView*>("dataTable");
            waitFor([&] {
                return window.windowHandle() && window.windowHandle()->isExposed() && table &&
                       table->model() && table->model()->rowCount() > 0;
            }, "Initial real process frame did not become ready");
            const auto snapshot = request(inspector, "snapshot");
            require(snapshot["system"].toObject()["totalPhysicalBytes"].toDouble() > 0,
                    "Initial frame uses a real Windows snapshot");
            require(window.size() == QSize(Theme::defaultWindowWidth, Theme::defaultWindowHeight) &&
                        window.devicePixelRatioF() == 1,
                    "Normal production window is 1100x720 client pixels at 100% scale");
            const QStringList titles{"进程", "性能", "应用历史记录", "启动应用", "用户", "详细信息", "服务", "设置"};
            const QStringList keys{"processes", "performance", "history", "startup", "users", "details", "services", "settings"};
            for (int index = 0; index < titles.size(); ++index) {
                QElapsedTimer pageElapsed;
                pageElapsed.start();
                auto nav = navigation(window, titles[index]);
                require(nav->isEnabled(), "Navigation is enabled: " + keys[index]);
                nav->click();
                waitFor([&] {
                    if (!nav->isChecked())
                        return false;
                    for (auto label : window.findChildren<QLabel*>())
                        if (label->property("title").toBool() && label->isVisible() &&
                            label->text().startsWith(titles[index]))
                            return label->width() > 0 && label->height() > 0;
                    return false;
                }, "Page title and selected navigation did not settle");
                QCoreApplication::sendPostedEvents(nullptr, QEvent::LayoutRequest);
                window.layout()->activate();
                QCoreApplication::processEvents(QEventLoop::AllEvents, 20);
                window.repaint();
                const auto phase = QString("%1-%2").arg(index + 1, 2, 10, QLatin1Char('0')).arg(keys[index]);
                const auto png = QDir(acknowledgementDirectory).filePath(phase + ".png");
                require(!QFileInfo::exists(png), "Capture acknowledgement is unique to this run: " + keys[index]);
                report["phase"] = phase;
                report["title"] = titles[index];
                report["clientWidth"] = window.width();
                report["clientHeight"] = window.height();
                report["devicePixelRatio"] = window.devicePixelRatioF();
                save();
                waitFor([&] { return QFileInfo(png).size() > 0; }, "WGC did not acknowledge this page PNG", 25000);
                pages.append(QJsonObject{{"phase", phase}, {"title", titles[index]},
                                         {"elapsedMs", pageElapsed.elapsed()}, {"captureAcknowledged", true}});
            }
            require(pages.size() == 8, "All eight native pages have completed WGC captures");
            report["result"] = "PASS";
            report["phase"] = "completed";
            save();
            window.close();
        } catch (const std::exception& error) {
            report["result"] = "FAIL";
            report["error"] = QString::fromUtf8(error.what());
            try { save(); } catch (...) {}
            app.exit(1);
        }
    });
    return app.exec();
}
