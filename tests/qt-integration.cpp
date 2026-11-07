// Native widget component integration with the real .NET/CsWin32 backend.
// This harness invokes public Qt component methods. It does not synthesize
// Windows mouse/keyboard input or claim to validate physical input devices.
#include "MainWindow.h"
#include "Theme.h"
#include <QApplication>
#include <QCommandLineParser>
#include <QDialog>
#include <QElapsedTimer>
#include <QFontInfo>
#include <QJsonDocument>
#include <QHeaderView>
#include <QPlainTextEdit>
#include <QSaveFile>
#include <QScrollBar>
#include <QScrollArea>
#include <QTabWidget>
#include <QThread>
#include <stdexcept>

namespace {
QJsonArray checks;
QString output, phase;
QJsonObject report;
void save() {
    report["checks"] = checks;
    report["phase"] = phase;
    const auto evidence = QJsonDocument(report).toJson(QJsonDocument::Indented);
    // The WGC observer reads this file concurrently. Windows can briefly deny
    // the atomic rename while that reader owns a handle without delete sharing.
    for (int attempt = 0; attempt < 10; ++attempt) {
        QSaveFile file(output);
        if (file.open(QIODevice::WriteOnly) && file.write(evidence) == evidence.size() && file.commit())
            return;
        QThread::msleep(25);
    }
    throw std::runtime_error("Cannot commit test evidence after bounded retries");
}
void check(bool condition, const QString& message) {
    if (!condition) throw std::runtime_error(message.toStdString());
    checks.append(message); save();
}
void waitFor(const std::function<bool()>& predicate, const QString& message, int timeout = 15000) {
    QElapsedTimer timer; timer.start();
    while (!predicate()) {
        if (timer.elapsed() > timeout) throw std::runtime_error(message.toStdString());
        QCoreApplication::processEvents(QEventLoop::AllEvents, 40); QThread::msleep(15);
    }
    QCoreApplication::processEvents(QEventLoop::AllEvents, 40);
}
void hold(int duration) {
    QElapsedTimer timer; timer.start();
    waitFor([&] { return timer.elapsed() >= duration; }, "Timed UI observation", duration+2000);
}
void observe(const QString& name) { phase=name; save(); hold(4200); }
QPushButton* button(QWidget* parent, const QString& name) {
    for (auto b : parent->findChildren<QPushButton*>())
        if (b->text().remove('&') == name || b->accessibleName() == name) return b;
    throw std::runtime_error(("Button missing: "+name).toStdString());
}
QDialog* dialog(const QString& prefix) {
    for (auto widget : QApplication::topLevelWidgets())
        if (auto d = qobject_cast<QDialog*>(widget); d && d->isVisible() && d->windowTitle().startsWith(prefix)) return d;
    return nullptr;
}
QJsonObject request(ApiClient& client, const QString& route, const QJsonObject* body=nullptr) {
    QObject context; bool done=false; QJsonObject result; QString failure;
    auto callback=[&](const QJsonObject& value,const QString& error,int){ result=value;failure=error;done=true; };
    if (body) client.post(route,*body,&context,callback); else client.get(route,&context,callback);
    waitFor([&] { return done; }, "Real HTTP request timed out: "+route);
    if (!failure.isEmpty()) throw std::runtime_error((route+": "+failure).toStdString());
    return result;
}
QJsonObject processByIdentity(const QJsonObject& snapshot, const QString& identity) {
    for (const auto& value : snapshot["processes"].toArray())
        if (value.toObject()["identity"].toString()==identity) return value.toObject();
    return {};
}
void verifyAliases(QTreeView* table, const QList<QPair<QString,QString>>& fields, const QString& page) {
    bool matching=true;
    for(int row=0;row<table->model()->rowCount();++row) {
        const auto object=QJsonObject::fromVariantMap(table->model()->index(row,0).data(DataModel::ObjectRole).toMap());
        for(const auto& field:fields) if(object.value(field.first)!=object.value(field.second))matching=false;
    }
    check(table->model()->rowCount()>0 && matching,page+": native display aliases preserve the actual immutable DTO values");
}
void verifyComboPopup(QComboBox* combo) {
    const auto current=combo->currentIndex();
    const auto value=combo->currentData();
    combo->showPopup();
    hold(250);
    const auto view=combo->view();
    bool complete=view->isVisible() && view->width()>=combo->width();
    for(int row=0;row<combo->count();++row) {
        const auto rect=view->visualRect(combo->model()->index(row,combo->modelColumn(),combo->rootModelIndex()));
        complete=complete && rect.height()>=Theme::controlHeight && view->viewport()->rect().contains(rect);
    }
    check(complete,"Fluent popup shows every real option in full-width rows: "+combo->accessibleName());
    check(combo->style()->styleHint(QStyle::SH_ComboBox_Popup,nullptr,combo)==0 &&
          dynamic_cast<Theme::ComboItemDelegate*>(combo->itemDelegate()) &&
          view->window()->windowFlags().testFlag(Qt::NoDropShadowWindowHint),
          "Fluent native list avoids menu delegates, scroller arrows and heavy shadow: "+combo->accessibleName());
    combo->hidePopup();
    check(combo->currentIndex()==current && combo->currentData()==value,
          "Closing the native popup preserves the real current option: "+combo->accessibleName());
}
}

int main(int argc,char** argv) {
    QApplication app(argc,argv); app.setApplicationName("Takao"); Theme::apply(app);
    QCommandLineParser parser; parser.addOption({"origin","Origin","url"}); parser.addOption({"host-pid","Host","pid"}); parser.process(app);
    const QUrl origin(parser.value("origin")); const auto hostPid=parser.value("host-pid").toLongLong();
    output=qEnvironmentVariable("TASKMANAGER_QT_COMPONENT_RESULT");
    if (output.isEmpty() || origin.host()!="127.0.0.1" || hostPid<=0) return 2;
    report={{"result","RUNNING"},{"scope","Qt native component methods + real HTTP/JSON.NET/CsWin32; no physical input assertions"},{"display","1920x1080 visible window pixels, 100%; WGC validates exact bounds"}};
    MainWindow window(origin,hostPid); window.resize(1920,1080); window.show();
    ApiClient inspector(origin,&app); QString targetIdentity; QJsonObject owned;
    QTimer::singleShot(0,&window,[&] {
        try {
            auto bootstrap=request(inspector,"bootstrap"); inspector.setToken(bootstrap["token"].toString());
            check(bootstrap["hostPid"].toInteger()==hostPid,"Real bootstrap verifies the exact owning Host");
            check(bootstrap["desktopProcessId"].toInteger()==QCoreApplication::applicationPid(),"Host owns this exact Qt integration child");
            auto table=window.findChild<QTreeView*>("dataTable"); auto search=window.findChild<QLineEdit*>("globalSearch");
            waitFor([&]{if(auto first=dialog("替换标准任务管理器")) first->reject();return table && table->model()->rowCount()>0 && request(inspector,"preferences")["preferences"].toObject()["taskManagerPromptDismissed"].toBool();},"Initial real Qt process table and explicit first-launch decline");
            hold(1500);
            check(window.width()<=1920 && window.height()<=1080 && window.devicePixelRatioF()==1,"Content fits the 1080P window at 100%; WGC independently checks the full 1920x1080 frame");
            check(table->uniformRowHeights() && table->isSortingEnabled(),"Native model/view uses bounded row painting and sorting");
            check(search->focusPolicy()!=Qt::NoFocus && !search->accessibleName().isEmpty(),"Shared search has native keyboard focus and an accessible name");
            check(qApp->font().pixelSize()==14,"Fluent desktop body typography uses the specified 14px system font");
            report["font"] = QJsonObject{{"requested",qApp->font().families().join(", ")},{"resolved",QFontInfo(qApp->font()).family()},{"search",QFontInfo(search->font()).family()},{"table",QFontInfo(table->font()).family()},{"navigation",QFontInfo(button(&window,"进程")->font()).family()}};
            // Observe actual production HTTP-loaded icons, without inserting test images.
            auto data = qobject_cast<DataModel*>(qobject_cast<DataProxy*>(table->model())->sourceModel());
            QString hostIdentity;
            for (const auto& value : request(inspector,"snapshot")["processes"].toArray()) {
                const auto row = value.toObject();
                if (row["pid"].toInteger() == hostPid) hostIdentity = row["identity"].toString();
            }
            check(data && !hostIdentity.isEmpty(),"Icon cache probe uses the real owning Host creation identity");
            const auto previousQuery = search->text();
            search->setText(QString::number(hostPid));
            waitFor([&] {
                const auto row = data->find(hostIdentity);
                return row.isValid() && !data->icons.icon(row.data(DataModel::IconKeyRole).toString()).isNull();
            },"The visible real Host icon loads through the production HTTP PNG endpoint");
            const auto cachedKey = data->find(hostIdentity).data(DataModel::IconKeyRole).toString();
            const auto decodedKey = data->icons.icon(cachedKey).cacheKey();
            check(decodedKey != 0,"A real decoded application icon is present in the bounded cache");
            search->setText(previousQuery);
            for (const auto& page : QStringList{"详细信息","应用历史记录","进程"}) {
                button(&window,page)->click();
                waitFor([&] { return data->find(hostIdentity).isValid(); },"Owning Host identity is present: "+page);
                hold(200);
                const auto row = data->find(hostIdentity);
                check(row.data(DataModel::IconKeyRole).toString() == cachedKey &&
                          row.data(Qt::DecorationRole).value<QIcon>().cacheKey() == decodedKey,
                      "The same HTTP-decoded icon is reused across page navigation: "+page);
                check(data->icons.count() <= IconCache::capacity && data->icons.inFlight() <= IconCache::concurrency,
                      "Real page icon loading remains within cache and request limits: "+page);
            }
            search->setText(previousQuery);
            const QStringList pages{"进程","性能","应用历史记录","启动应用","用户","详细信息","服务","设置"};
            for(int i=0;i<pages.size();++i) {
                auto nav=button(&window,pages[i]); check(nav->minimumHeight()>=44 && nav->focusPolicy()!=Qt::NoFocus,"Navigation target/focus: "+pages[i]);
                nav->click();
                auto pageReady=[&] {
                    for(auto label:window.findChildren<QLabel*>())
                        if(label->property("title").toBool() && label->text().startsWith(pages[i]) && label->isVisible() && nav->isChecked())return true;
                    return false;
                };
                waitFor(pageReady,"Page navigation did not settle: "+pages[i]);
                check(pageReady(),"Native page and selected navigation: "+pages[i]);
                bool typography=false;
                for(auto label:window.findChildren<QLabel*>())if(label->property("title").toBool() && label->isVisible())typography=label->font().pixelSize()==28 && label->font().weight()>=QFont::DemiBold;
                check(typography && ((i==1 || i==7) || table->header()->height()==40),"Fluent page typography and table-header geometry: "+pages[i]);
                if(i==2) verifyAliases(table,{{"cpu","observedCpuTime100ns"},{"last","lastObservedAtUtc"}},"History");
                if(i==3) verifyAliases(table,{{"location","locationPath"}},"Startup");
                if(i==4) verifyAliases(table,{{"name","userName"},{"session","sessionId"},{"station","stationName"},{"count","processCount"},{"cpu","cpuPercent"},{"memory","workingSetBytes"}},"Users");
                if(i==6) verifyAliases(table,{{"display","displayName"}},"Services");
                observe(QString("%1-page").arg(i+1,2,10,QLatin1Char('0')));
                if(i==7) {
                    auto settingsContent=window.findChild<QWidget*>("settingsContent");
                    check(settingsContent && settingsContent->width()<=1080,"Fluent settings content is limited to 1080px");
                    for(auto box:window.findChildren<QComboBox*>())
                        if(box->accessibleName()=="采样间隔" || box->accessibleName()=="数据密度" || box->accessibleName()=="文字大小") {
                            waitFor([&]{return box->isVisible();},"Settings field is not visible: "+box->accessibleName());
                            check(box->width()<=240 && box->isVisible(),"Settings field keeps a readable control width: "+box->accessibleName()+QString(" (width=%1, visible=%2, hidden=%3, search=%4)").arg(box->width()).arg(box->isVisible()).arg(box->isHidden()).arg(search->text()));
                            verifyComboPopup(box);
                        }
                    QScrollArea* settingsScroll=nullptr;
                    for(auto area:window.findChildren<QScrollArea*>())if(area->isVisible() && settingsContent && area->isAncestorOf(settingsContent))settingsScroll=area;
                    check(settingsScroll && settingsScroll->verticalScrollBar()->maximum()>0,"Settings groups remain reachable through the native scroll area");
                    settingsScroll->verticalScrollBar()->setValue(settingsScroll->verticalScrollBar()->maximum());hold(300);observe("08-settings-lower");
                    settingsScroll->verticalScrollBar()->setValue(0);
                }
            }
            // Scroll restores after returning to a populated model.
            button(&window,"进程")->click(); hold(250);table->verticalScrollBar()->setValue(640);hold(100);
            const auto scroll=table->verticalScrollBar()->value();button(&window,"服务")->click();hold(150);button(&window,"进程")->click();hold(350);
            check(scroll>0 && table->verticalScrollBar()->value()>0,"Changing pages restores the saved process scroll after layout");
            table->verticalScrollBar()->setValue(0);
            // Native performance controls retain actual source IDs and current values.
            button(&window,"性能")->click();
            QListWidget* resources=nullptr;QComboBox* cpuMode=nullptr;
            for(auto list:window.findChildren<QListWidget*>())if(list->accessibleName()=="性能资源")resources=list;
            for(auto box:window.findChildren<QComboBox*>())if(box->accessibleName()=="CPU 图表模式")cpuMode=box;
            check(resources && resources->count()>=3 && cpuMode,"Real resources and CPU mode controls exist");
            verifyComboPopup(cpuMode);
            cpuMode->setCurrentIndex(1);hold(500);check(!window.findChildren<Chart*>().isEmpty(),"Logical CPU mode creates native charts from real data");observe("09-logical-cpu");cpuMode->setCurrentIndex(0);
            for(const auto& prefix:QStringList{"disk:","network:","gpu:","hwinfo:"}) {
                for(int i=0;i<resources->count();++i) if(resources->item(i)->data(Qt::UserRole).toString().startsWith(prefix)) {
                    resources->setCurrentRow(i);hold(450);observe("resource-"+prefix.left(prefix.size()-1));break;
                }
            }
            button(&window,"设置")->click();QComboBox *interval=nullptr,*density=nullptr;QCheckBox* freeze=nullptr;
            for(auto box:window.findChildren<QComboBox*>()){if(box->accessibleName()=="采样间隔")interval=box;if(box->accessibleName()=="数据密度")density=box;}
            for(auto box:window.findChildren<QCheckBox*>())if(box->accessibleName()=="冻结当前显示")freeze=box;
            check(interval && density && freeze,"Sampling, density and freeze use named native controls");
            interval->setCurrentIndex(interval->findData(2000));waitFor([&]{return request(inspector,"bootstrap")["settings"].toObject()["intervalMs"].toInt()==2000;},"Apply real interval");check(true,"Qt sampling interval reaches the real backend");
            auto pause=button(&window,"暂停采集");pause->click();waitFor([&]{return request(inspector,"snapshot")["paused"].toBool();},"Pause native sampling");
            const auto paused=request(inspector,"snapshot")["sampleId"].toInteger();hold(1300);check(request(inspector,"snapshot")["sampleId"].toInteger()==paused,"Paused sampling does not invent new samples");
            const auto pausedSystem=request(inspector,"snapshot")["system"].toObject();
            const QList<QPair<QString,QString>> summaryFields{{"cpuSummary","cpuPercent"},{"memorySummary","memoryPercent"},{"processSummary","processCount"}};
            for(const auto& field:summaryFields) {
                auto label=window.findChild<QLabel*>(field.first);
                const auto expected=formatValue(pausedSystem[field.second],field.second=="processCount"?"number":"percent");
                waitFor([&]{return label && label->text()==expected;},"Paused resource summary matches the independently read production value: "+field.second);
                check(label->font().pixelSize()==24 && label->font().weight()>=QFont::DemiBold &&
                      label->palette().color(QPalette::WindowText)==Theme::accent && !label->accessibleName().isEmpty(),
                      "Resource summary is readable, named and visually prominent: "+field.second);
            }
            check(window.findChild<QWidget*>("appTopArea")->height()==56,"Prominent resource summaries retain the existing 56px top bar");
            button(&window,"进程")->click();hold(200);
            const auto processPosition=table->mapTo(&window,QPoint());
            const auto processExtent=table->size();
            auto caption=window.findChild<QLabel*>("pageCaption");
            search->setText("Takao-no-matching-process-during-pause");hold(650);
            check(table->model()->rowCount()==0 && caption && caption->text().contains("暂停") &&
                  table->mapTo(&window,QPoint())==processPosition && table->size()==processExtent,
                  "Empty search preserves the paused page status and the process table geometry");
            check(window.findChild<QLabel*>("processSummary")->text()==formatValue(pausedSystem["processCount"],"number"),
                  "Search filters do not replace the true whole-system process count");
            search->clear();hold(150);
            bool notice=false;
            for(auto label:window.findChildren<QLabel*>())notice=notice || label->property("notice").toBool();
            for(auto control:window.findChildren<QPushButton*>())notice=notice || control->text()=="关闭提示";
            check(!notice,"The main window contains no layout notification bars or dismiss-notice control");
            observe("10-paused");button(&window,"设置")->click();pause->click();
            waitFor([&]{return !request(inspector,"snapshot")["paused"].toBool();},"Resume native sampling");hold(350);
            interval->setCurrentIndex(interval->findData(1000));waitFor([&]{return request(inspector,"bootstrap")["settings"].toObject()["intervalMs"].toInt()==1000;},"Restore real one-second interval");
            density->setCurrentIndex(density->findData("standard"));check(table->property("rowHeight").toInt()==40,"Standard density applies the actual native row height");density->setCurrentIndex(density->findData("compact"));
            freeze->setChecked(true);const auto frozenSample=request(inspector,"snapshot")["sampleId"].toInteger();waitFor([&]{return request(inspector,"snapshot")["sampleId"].toInteger()>frozenSample;},"Sampling continues during UI freeze",6000);check(true,"Freeze keeps the real backend sampling");freeze->setChecked(false);
            // The Qt run dialog goes through production validation and CreateProcess.
            button(&window,"进程")->click();search->setText("Takao-unique-empty-search");hold(200);check(table->model()->rowCount()==0,"A real empty search displays no fabricated process rows");observe("11-empty-search");search->clear();
            button(&window,"运行新任务")->click();waitFor([&]{return dialog("运行新任务");},"Open native run dialog");auto runDialog=dialog("运行新任务");
            auto path=runDialog->findChild<QLineEdit*>("executablePath");auto arguments=runDialog->findChild<QLineEdit*>("executableArguments");
            check(path && arguments && runDialog->focusWidget()==path,"Run dialog exposes labelled fields and assigns focus to the executable path");
            path->setText(qEnvironmentVariable("TASKMANAGER_QT_TEST_DIRECTORY")+"/not-present.exe");button(runDialog,"运行(R)")->click();
            waitFor([&]{for(auto label:runDialog->findChildren<QLabel*>())if(label->accessibleName()=="操作错误"&&!label->text().isEmpty())return true;return false;},"Real invalid executable error");
            check(runDialog->isVisible() && path->text().endsWith("not-present.exe"),"Real run failure retains the modal and entered input");
            path->setText(qEnvironmentVariable("TASKMANAGER_QT_WORKLOAD"));arguments->setText("\""+qEnvironmentVariable("TASKMANAGER_QT_TEST_DIRECTORY")+"/workload\" 180");observe("12-run-dialog");
            auto run=button(runDialog,"运行(R)");run->click();check(!run->isEnabled(),"Run command disables immediately while the actual request is pending");
            waitFor([&]{return !dialog("运行新任务");},"Real workload starts");
            waitFor([&]{for(const auto& value:request(inspector,"snapshot")["processes"].toArray()){const auto row=value.toObject();if(row["name"].toString()=="Takao.TestWorkload.exe"&&row["parentPid"].toInteger()==hostPid){owned=row;return true;}}return false;},"Observe the exact test workload");
            targetIdentity=owned["identity"].toString();check(!targetIdentity.isEmpty() && owned["canTerminate"].toBool(),"Created workload has a real creation identity");
            check(table->mapTo(&window,QPoint())==processPosition && table->size()==processExtent,
                  "Successful real process creation does not shift the table for a success notice");
            search->setText(targetIdentity);waitFor([&]{return table->model()->rowCount()==1;},"Unique identity search");table->setCurrentIndex(table->model()->index(0,0));table->setFocus();
            check(table->currentIndex().data(DataModel::IdentityRole).toString()==targetIdentity,"Selection remains tied to the exact test identity");
            button(&window,"详情")->click();waitFor([&]{return dialog("进程详情");},"Open real process details");auto detail=dialog("进程详情");auto tabs=detail->findChild<QTabWidget*>();tabs->setCurrentIndex(1);
            QTreeView* endpoints=nullptr;for(auto view:detail->findChildren<QTreeView*>())if(view->accessibleName()=="真实 TCP UDP 端点")endpoints=view;
            waitFor([&]{return endpoints && endpoints->model()->rowCount()>=2;},"Observe multiple real TCP/UDP endpoints");
            const auto routes="processes/"+QString::number(owned["pid"].toInteger())+"/connections?creationTimeFileTime="+owned["creationTimeFileTime"].toString();const auto actual=request(inspector,routes)["connections"].toArray();
            check(endpoints->model()->rowCount()==actual.size(),"Qt connection table retains every actual endpoint without identity collisions");observe("13-process-connections");detail->close();hold(150);
            button(&window,"结束任务")->click();waitFor([&]{return dialog("结束任务");},"Open termination confirmation");auto confirmDialog=dialog("结束任务");
            bool correctPid=false;for(auto label:confirmDialog->findChildren<QLabel*>())if(label->text().contains(QString::number(owned["pid"].toInteger())))correctPid=true;
            check(correctPid && confirmDialog->focusWidget()==button(confirmDialog,"取消"),"Destructive confirmation states the real PID and defaults focus to Cancel");observe("14-terminate-confirm");button(confirmDialog,"取消")->click();
            check(!processByIdentity(request(inspector,"snapshot"),targetIdentity).isEmpty(),"Cancel leaves the exact dedicated process alive");
            button(&window,"结束任务")->click();waitFor([&]{return dialog("结束任务");},"Reopen identity confirmation");auto confirm=button(dialog("结束任务"),"确认结束任务");confirm->click();check(!confirm->isEnabled(),"Termination disables while waiting for actual Windows completion");
            waitFor([&]{return !dialog("结束任务") && processByIdentity(request(inspector,"snapshot"),targetIdentity).isEmpty();},"Confirm actual dedicated process exit");targetIdentity.clear();
            check(table->mapTo(&window,QPoint())==processPosition && table->size()==processExtent,
                  "Confirmed real process termination closes the modal without a notification layout shift");observe("15-termination-feedback");
            check(formatValue(1.25,"number").contains("1.25")||formatValue(1.25,"number").contains("1,25"),"Decimal sensor values are preserved");
            check(formatValue(QJsonValue(QJsonValue::Null),"number")==QStringLiteral("—"),"Unknown sensor values stay unknown");
            report["result"]="PASS";phase="completed";save();window.close();
        } catch(const std::exception& error) {
            report["result"]="FAIL";report["error"]=QString::fromUtf8(error.what());save();
            if(!targetIdentity.isEmpty()){try{QJsonObject body{{"creationTimeFileTime",owned["creationTimeFileTime"]}};request(inspector,"processes/"+QString::number(owned["pid"].toInteger())+"/terminate",&body);}catch(...){}}
            // A run request can complete before the test observes its response.
            // Clean only dedicated workloads created by this exact test Host.
            try { for(const auto& item:request(inspector,"snapshot")["processes"].toArray()) {
                const auto row=item.toObject();
                if(row["parentPid"].toInteger()==hostPid && row["path"].toString().compare(qEnvironmentVariable("TASKMANAGER_QT_WORKLOAD"),Qt::CaseInsensitive)==0 && row["creationTimeFileTime"].isString()) {
                    QJsonObject body{{"creationTimeFileTime",row["creationTimeFileTime"]}};
                    request(inspector,"processes/"+QString::number(row["pid"].toInteger())+"/terminate",&body);
                }
            }} catch(...) {}
            window.close();QTimer::singleShot(3000,&app,&QCoreApplication::quit);
        }
    });
    const auto result=app.exec();return report["result"].toString()=="PASS"?result:1;
}
