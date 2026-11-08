#pragma once
#include "ApiClient.h"
#include "Chart.h"
#include "DataModel.h"
#include "SystemInformationView.h"
#include <QCheckBox>
#include <QComboBox>
#include <QGridLayout>
#include <QJsonObject>
#include <QLabel>
#include <QLineEdit>
#include <QListWidget>
#include <QMainWindow>
#include <QPushButton>
#include <QSet>
#include <QSpinBox>
#include <QStackedWidget>
#include <QTimer>
#include <QTreeView>
#include <optional>

class MainWindow final : public QMainWindow {
    Q_OBJECT
  public:
    MainWindow(QUrl origin, qint64 hostPid);

  protected:
    void closeEvent(QCloseEvent*) override;
    void resizeEvent(QResizeEvent*) override;

  private:
    const QStringList keys_{"processes", "performance", "history",  "startup",
                            "users",     "details",     "services", "settings", "systemInformation"};
    const QStringList titles_{"进程", "性能", "应用历史记录", "启动应用", "用户", "详细信息", "服务", "设置", "系统信息"};
    ApiClient api_;
    qint64 expectedHost_;
    QJsonObject bootstrap_, preferences_, latest_, shown_, integration_, updates_;
    bool connected_ = false, polling_ = false, frozen_ = false, loadingPreferences_ = true,
         settingsSync_ = false, closing_ = false, firstPrompt_ = false;
    bool updatingModel_ = false, saving_ = false, savePending_ = false;
    bool switchingPage_ = false, pendingPageRestore_ = false, applyingTopmost_ = false;
    std::optional<bool> actualTopmost_;
    QSet<QString> collapsed_, pendingActions_;
    int page_ = 0, intervalMs_ = 1000;
    double zoom_ = 1;
    QTimer pollTimer_, saveTimer_, slowTimer_, iconTimer_, copyFeedbackTimer_;
    QLineEdit* search_{};
    QLabel *pageTitle_{}, *count_{}, *cpuSummary_{}, *memorySummary_{}, *processSummary_{};
    QString pageCaption_, connectionError_, operationError_, sourceMessage_, searchMessage_;
    QWidget* sidebar_{};
    QVector<QPushButton*> nav_;
    QPushButton *pause_{}, *run_{}, *terminate_{}, *more_{}, *refresh_{}, *detail_{}, *pageAction_{}, *navUpdate_{};
    QStackedWidget* stack_{};
    QWidget* gridPage_{};
    QTreeView* table_{};
    DataModel* model_{};
    DataProxy* proxy_{};
    QWidget* performance_{};
    QListWidget* resources_{};
    QLabel *resourceTitle_{}, *resourceInfo_{}, *resourceValue_{}, *perfStatus_{}, *resourceAvailability_{};
    QComboBox* cpuMode_{};
    QStackedWidget* perfStack_{};
    QWidget* chartsPage_{};
    QGridLayout* chartsLayout_{};
    QVector<Chart*> charts_;
    QLabel* facts_{};
    QGridLayout* factsLayout_{};
    QVector<QLabel*> factLabels_, factValues_;
    QString selectedResource_ = QStringLiteral("cpu:"), chartLayoutKey_;
    SystemInformationView* systemInformationPage_{};
    QJsonObject systemInformation_;
    QString systemInformationError_;
    bool readingSystemInformation_ = false;
    QPushButton* copySystemInformation_{};
    void readSystemInformation(bool refresh = false);
    void updateSystemInformation();
    void copySystemInformation();
    QWidget* settingsPage_{};
    QComboBox *interval_{}, *density_{}, *zoomBox_{};
    QCheckBox *topmost_{}, *freezeBox_{};
    QLabel *integrationStatus_{}, *updateStatus_{}, *sourceStatus_{}, *preferenceStatus_{};
    QPushButton *integrationAction_{}, *disableIntegration_{}, *checkUpdate_{}, *downloadUpdate_{},
        *installNow_{}, *installNext_{}, *cancelUpdate_{};
    void buildUi();
    void buildPerformance();
    void buildSettings();
    void bootstrap();
    void poll();
    void applySnapshot();
    void switchPage(int);
    void updateTable();
    void updateSelectionActions();
    void updatePerformance();
    void setFacts(const QList<QPair<QString, QString>>&);
    void rebuildResources();
    void updateSettings();
    void applyAppearance(bool restoreWindowState = false);
    void syncTopmost(std::optional<bool> desired = {}, bool persist = false);
    void readIntegration(bool first = false);
    void readUpdates();
    void applyIntegration(QString action, bool first = false);
    void promptFirstLaunch();
    void notify(const QString&, bool error = false);
    void updateCaption();
    void saveSoon();
    void savePreferences();
    void capturePagePreference();
    void restorePagePreference();
    QJsonObject selected() const;
    bool processPage() const;
    QString processRoute(const QJsonObject&, const QString&, bool query = false) const;
    void post(const QString&, const QJsonObject&, std::function<void(const QJsonObject&)> done = {},
              int timeout = 15000);
    void runTask();
    void terminateProcess(QJsonObject target = {});
    void terminateTree(const QJsonObject&);
    void details(QJsonObject target = {});
    void historyDetails(const QJsonObject&);
    void locateSession(const QJsonObject&);
    void serviceDetails(const QJsonObject&);
    void contextMenu(const QPoint&);
    void moreMenu();
    void columnMenu();
    void exportCsv();
    void filters();
    void setPriority(const QJsonObject&, const QString&);
    void efficiency(const QJsonObject&);
    void startupDetails(const QJsonObject&);
    void controlService(const QJsonObject&, const QString&);
    bool locateProcess(qint64 pid, const QString& expectedIdentity);
    void locateServiceProcess(const QJsonObject& service);
    void applySampling(int interval, bool paused);
    void updateCommand(const QString& command, const QString& mode = {});
    QTreeView* createTable(DataModel*, DataProxy*, QWidget* parent = nullptr);
    QPushButton* button(const QString&, QWidget* parent = nullptr, bool primary = false);
    void presentText(const QString&, const QString&);
    void showColumnsForCurrentPage();
    void loadVisibleIcons();
    void applySearch(const QString&);
};
