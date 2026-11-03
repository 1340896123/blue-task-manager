#include "MainWindow.h"
#include "Theme.h"
#include <QCommandLineParser>
#include <QDir>
#include <QFile>
#include <QMessageBox>

int main(int argc, char** argv) {
    QApplication app(argc, argv);
    app.setApplicationName("Takao");
    app.setOrganizationName("Takao");
    app.setApplicationVersion("1.1.1");
    Theme::apply(app);
    const QIcon icon(QDir(QCoreApplication::applicationDirPath()).filePath("takao.ico"));
    if (!icon.isNull())
        app.setWindowIcon(icon);
    QCommandLineParser parser;
    parser.setApplicationDescription("Takao Qt native desktop");
    parser.addHelpOption();
    parser.addVersionOption();
    parser.addOption({"origin", "Host local HTTP origin", "url"});
    parser.addOption({"host-pid", "Owning .NET Host PID", "pid"});
    parser.process(app);
    const QUrl origin(parser.value("origin"));
    bool validPid = false;
    const auto pid = parser.value("host-pid").toLongLong(&validPid);
    if (!origin.isValid() || origin.scheme() != "http" || origin.host() != "127.0.0.1" ||
        origin.port() < 1024 || origin.port() > 65535 || !origin.userInfo().isEmpty() ||
        !origin.query().isEmpty() || !origin.fragment().isEmpty() ||
        (!origin.path().isEmpty() && origin.path() != "/") || !validPid || pid <= 0) {
        QMessageBox::critical(nullptr, "Takao", "请从同目录的 Takao.Host.exe 启动完整的 Qt + .NET 程序。");
        return 2;
    }
    MainWindow window(origin, pid);
    window.show();
    return app.exec();
}
