// Real owning Host + Windows only. Requests are observed, never replaced.
#include "MainWindow.h"
#include "Theme.h"
#include <QCommandLineParser>
#include <QDir>
#include <QDateTime>
#include <QElapsedTimer>
#include <QFileInfo>
#include <QJsonDocument>
#include <QNetworkReply>
#include <QSaveFile>
#include <QThread>
#include <QUrlQuery>
#include <cmath>
#include <stdexcept>

namespace {
QJsonObject report;
QJsonArray checks;
QString output;
QNetworkAccessManager* observed = nullptr;
int maximumActive = 0;
struct Sample { QString route; qint64 time; int status; QJsonObject json; };
QVector<Sample> samples;
QElapsedTimer observationClock;
int active() {
    int count = 0;
    if (observed) for (auto reply : observed->findChildren<QNetworkReply*>())
        if (!reply->isFinished() && reply->url().path() == "/api/snapshot") ++count;
    maximumActive = std::max(maximumActive, count);
    return count;
}
void save() {
    report["checks"] = checks;
    QSaveFile file(output);
    if (!file.open(QIODevice::WriteOnly)) throw std::runtime_error("Cannot write snapshot regression report");
    file.write(QJsonDocument(report).toJson(QJsonDocument::Indented));
    if (!file.commit()) throw std::runtime_error("Cannot commit snapshot regression report");
}
void check(bool valid, const QString& message) {
    if (!valid) throw std::runtime_error(message.toStdString());
    checks.append(message); save();
}
void waitFor(const std::function<bool()>& ready, const char* message, int timeout = 20000) {
    QElapsedTimer elapsed; elapsed.start();
    while (!ready()) {
        active();
        if (elapsed.elapsed() > timeout) throw std::runtime_error(message);
        QCoreApplication::processEvents(QEventLoop::AllEvents, 10); QThread::msleep(5);
    }
    active();
}
void pump(int milliseconds) {
    QElapsedTimer elapsed; elapsed.start();
    waitFor([&] { return elapsed.elapsed() >= milliseconds; }, "Timed event observation", milliseconds + 1000);
}
void phase(const QString& name) {
    report["phase"] = name; save();
    const auto directory = qEnvironmentVariable("TASKMANAGER_QT_SNAPSHOT_CAPTURE_ACK");
    if (!directory.isEmpty()) waitFor([&] { return QFileInfo::exists(QDir(directory).filePath(name + ".ack")); },
                                      "1080P capture acknowledgement", 25000);
}
QPushButton* button(QWidget& parent, const QString& name) {
    for (auto value : parent.findChildren<QPushButton*>())
        if (value->accessibleName() == name) return value;
    throw std::runtime_error(("Missing native button: " + name).toStdString());
}
QJsonObject request(ApiClient& client, const QString& route, const QJsonObject* body = nullptr) {
    QObject context; bool done = false; QJsonObject result; QString failure;
    auto callback = [&](const QJsonObject& value, const QString& error, int) { result = value; failure = error; done = true; };
    if (body) client.post(route, *body, &context, callback); else client.get(route, &context, callback);
    waitFor([&] { return done; }, "Real production HTTP timeout");
    if (!failure.isEmpty()) throw std::runtime_error((route + ": " + failure).toStdString());
    return result;
}
struct Response { int status; QByteArray bytes, etag, encoding; QJsonObject json; };
Response fetch(QNetworkAccessManager& manager, QUrl origin, const QString& query,
               const QByteArray& etag = {}, const QByteArray& encoding = "identity") {
    origin.setPath("/api/snapshot"); origin.setQuery(query);
    QNetworkRequest request(origin);
    request.setRawHeader("Accept-Encoding", encoding); // Test wire bytes, without automatic decompression.
    if (!etag.isEmpty()) request.setRawHeader("If-None-Match", etag);
    request.setTransferTimeout(10000);
    auto reply = manager.get(request);
    waitFor([&] { return reply->isFinished(); }, "Real snapshot HTTP timeout");
    Response response{reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt(), reply->readAll(),
                      reply->rawHeader("ETag"), reply->rawHeader("Content-Encoding"), {}};
    if (encoding == "identity") response.json = QJsonDocument::fromJson(response.bytes).object();
    reply->deleteLater();
    return response;
}
const Sample* last(const QString& view, int after = 0) {
    for (int i = samples.size() - 1; i >= after; --i)
        if (samples[i].route == view && samples[i].status == 200) return &samples[i];
    return nullptr;
}
}

int main(int argc, char** argv) {
    QApplication app(argc, argv); app.setApplicationName("Takao"); Theme::apply(app);
    QCommandLineParser parser; parser.addOption({"origin", "Origin", "url"});
    parser.addOption({"host-pid", "Host", "pid"}); parser.process(app);
    output = qEnvironmentVariable("TASKMANAGER_QT_SNAPSHOT_RESULT");
    if (output.isEmpty()) return 2;
    const QUrl origin(parser.value("origin")); const auto hostPid = parser.value("host-pid").toLongLong();
    MainWindow window(origin, hostPid); window.resize(1920, 1080); window.show();
    observed = window.findChild<QNetworkAccessManager*>();
    observationClock.start();
    QObject::connect(observed, &QNetworkAccessManager::finished, &app, [](QNetworkReply* reply) {
        if (reply->url().path() != "/api/snapshot") return;
        const auto view = QUrlQuery(reply->url()).queryItemValue("view");
        samples.append({view.isEmpty() ? "full" : view, observationClock.elapsed(),
            reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt(),
            QJsonDocument::fromJson(reply->peek(reply->bytesAvailable())).object()});
    });
    ApiClient inspector(origin, &app); QNetworkAccessManager wire;
    report = {{"result", "RUNNING"}, {"phase", "starting"}, {"hostPid", hostPid},
              {"qtPid", QCoreApplication::applicationPid()},
              {"scope", "Production HTTP and real native Qt components; no physical input or simulated Windows rows"}};
    save();
    QTimer::singleShot(0, &window, [&] {
        try {
            const auto bootstrap = request(inspector, "bootstrap"); inspector.setToken(bootstrap["token"].toString());
            check(bootstrap["hostPid"].toInteger() == hostPid && bootstrap["desktopProcessId"].toInteger() == QCoreApplication::applicationPid(),
                  "The production Host owns this exact Qt test child");
            auto table = window.findChild<QTreeView*>("dataTable");
            auto search = window.findChild<QLineEdit*>("globalSearch");
            waitFor([&] { return table && search && !search->text().isEmpty() && last("live"); }, "Initial real live view and preferences");
            search->setText("Takao");
            check(!last("live")->json.contains("history") && !last("live")->json.contains("performanceHistory"),
                  "Default Qt polling receives live data without unrequested histories");
            phase("processes"); search->clear();

            // Pause this dedicated Host only, to compare every projection at one revision.
            const QJsonObject paused{{"intervalMs", 1000}, {"paused", true}};
            request(inspector, "settings", &paused); pump(400);
            const auto full = fetch(wire, origin, "");
            const auto live = fetch(wire, origin, "view=live");
            const auto history = fetch(wire, origin, "view=history");
            const auto performance = fetch(wire, origin, "view=performance");
            check(full.status == 200 && live.status == 200 && history.status == 200 && performance.status == 200,
                  "Default and all three standard HTTP projections are available");
            check(full.json.contains("history") && full.json.contains("performanceHistory") &&
                  !live.json.contains("history") && !live.json.contains("performanceHistory") &&
                  history.json.contains("history") && !history.json.contains("performanceHistory") &&
                  !performance.json.contains("history") && performance.json.contains("performanceHistory"),
                  "Each projection independently serializes exactly its requested history fields");
            check(full.json["sampleId"] == live.json["sampleId"] && live.json["sampleId"] == history.json["sampleId"] &&
                  history.json["sampleId"] == performance.json["sampleId"] && full.json["processes"] == live.json["processes"] &&
                  full.json["processes"] == history.json["processes"] && full.json["processes"] == performance.json["processes"],
                  "Same-revision projections preserve the exact real process identities, data and reasons");
            check(full.etag != live.etag && live.etag != history.etag && history.etag != performance.etag &&
                  full.etag != performance.etag, "Projection validators are distinct");
            const auto same = fetch(wire, origin, "view=live", live.etag);
            const auto crossed = fetch(wire, origin, "view=history", live.etag);
            check(same.status == 304 && same.bytes.isEmpty() && crossed.status == 200 && crossed.json.contains("history"),
                  "Same-view 304 has no body; cross-view validators never suppress required history");
            const auto invalid = fetch(wire, origin, "view=not-a-view");
            check(invalid.status == 400 && invalid.json["error"].toObject()["code"] == "invalidSnapshotView",
                  "Unknown projection is rejected with an explicit 400 status");
            const auto gzipFull = fetch(wire, origin, "", {}, "gzip");
            const auto gzipLive = fetch(wire, origin, "view=live", {}, "gzip");
            check(gzipFull.encoding == "gzip" && gzipLive.encoding == "gzip" && gzipFull.bytes.startsWith(QByteArray::fromHex("1f8b")) &&
                  gzipLive.bytes.size() < gzipFull.bytes.size(), "Actual negotiated gzip live bytes are smaller than the complete snapshot");
            report["wire"] = QJsonObject{{"processCount", full.json["processes"].toArray().size()},
                {"historyRows", full.json["history"].toArray().size()}, {"performancePoints", full.json["performanceHistory"].toArray().size()},
                {"plainFullBytes", full.bytes.size()}, {"plainLiveBytes", live.bytes.size()},
                {"gzipFullBytes", gzipFull.bytes.size()}, {"gzipLiveBytes", gzipLive.bytes.size()}}; save();
            const QJsonObject empty;
            request(inspector, "history/reset", &empty);
            const auto reset = fetch(wire, origin, "view=history", history.etag);
            check(reset.status == 200 && reset.etag != history.etag && reset.json["history"].toArray().isEmpty(),
                  "History reset invalidates every cached projection at unchanged sampleId");

            // Native settings signals control production sampling; observe only automatic snapshot responses.
            button(window, "设置")->click();
            QComboBox* interval = nullptr; QCheckBox* freeze = nullptr;
            for (auto value : window.findChildren<QComboBox*>()) if (value->accessibleName() == "采样间隔") interval = value;
            for (auto value : window.findChildren<QCheckBox*>()) if (value->accessibleName() == "冻结当前显示") freeze = value;
            check(interval && freeze, "Production settings expose the actual sampling and freeze controls");
            button(window, "进程")->click();
            QPushButton* sampling = nullptr;
            for (auto value : window.findChildren<QPushButton*>())
                if (value->accessibleName() == "继续采集" || value->accessibleName() == "暂停采集") sampling = value;
            check(sampling, "Production pause/resume button is present");
            // Refresh after the independently paused API so the button reflects real server state.
            button(window, "刷新")->click();
            waitFor([&] { return sampling->accessibleName() == "继续采集" && sampling->isEnabled(); }, "Paused production snapshot");
            button(window, "继续采集")->click();
            waitFor([&] { const auto s = last("live"); return s && !s->json["paused"].toBool(); }, "Immediate resume snapshot");
            QJsonArray timings;
            for (const auto milliseconds : {1000, 2000, 5000}) {
                const int before = samples.size();
                interval->setCurrentIndex(interval->findData(milliseconds));
                if (milliseconds == 1000) button(window, "刷新")->click();
                waitFor([&] { const auto s = last("live", before); return s && s->json["intervalMs"].toInt() == milliseconds; }, "Sampling interval update");
                const int start = samples.size();
                pump(milliseconds * 2 + 300);
                QVector<qint64> times;
                for (int i = start; i < samples.size(); ++i) if (samples[i].route == "live") times << samples[i].time;
                check(times.size() >= 2 && times.size() <= 3 && times.back() - times.front() >= milliseconds * .9,
                      QString("Automatic real snapshot cadence follows %1 ms instead of fixed 500 ms").arg(milliseconds));
                timings.append(QJsonObject{{"intervalMs", milliseconds}, {"responses", times.size()}, {"lastGapMs", times.back() - times[times.size()-2]}});
            }
            report["timings"] = timings; save();
            button(window, "暂停采集")->click();
            waitFor([&] { const auto s = last("live"); return s && s->json["paused"].toBool(); }, "Pause snapshot");
            const int pauseStart = samples.size(); pump(5400);
            int pauseCount = 0; for (int i = pauseStart; i < samples.size(); ++i) if (samples[i].route == "live") ++pauseCount;
            check(pauseCount == 1 && samples.back().status == 304 && table->model()->rowCount() > 0,
                  "Paused polling waits five seconds and a real 304 preserves the displayed table");
            button(window, "继续采集")->click();
            waitFor([&] { const auto s = last("live"); return s && !s->json["paused"].toBool(); }, "Resume after five-second pause");
            interval->setCurrentIndex(interval->findData(1000));
            waitFor([&] { const auto s = last("live"); return s && s->json["intervalMs"].toInt() == 1000; }, "Restore dedicated test period");

            // Force a page change before an actual live request can finish; no delay/mocked response is inserted.
            button(window, "刷新")->click(); active();
            const int raceStart = samples.size();
            button(window, "应用历史记录")->click(); active();
            check(table->model()->rowCount() == 0, "Unrequested history enters a loading state before any history response");
            button(window, "性能")->click(); active();
            button(window, "进程")->click(); active();
            button(window, "应用历史记录")->click(); active();
            waitFor([&] { return last("history", raceStart) && table->model()->rowCount() > 0; }, "Current history view survives in-flight rapid navigation");
            auto model = window.findChild<DataModel*>();
            check(model && model->iconKind == "history" && table->model()->index(0,0).data(DataModel::ObjectRole).toMap().contains("observedCpuTime100ns"),
                  "Late live/performance responses never replace the requested history rows");
            search->setText("Takao"); phase("history"); search->clear();
            button(window, "性能")->click();
            waitFor([&] { return last("performance", raceStart) && !window.findChildren<QListWidget*>().isEmpty() &&
                !window.findChildren<QListWidget*>().first()->item(0)->data(Qt::UserRole+1).toList().isEmpty(); }, "Actual performance history and curve points");
            phase("performance");

            // Abort this test's real full response once to exercise transport failure and retry.
            waitFor([&] { return active() == 0; }, "No outstanding periodic snapshot");
            freeze->setChecked(true);
            QNetworkReply* pendingFull = nullptr;
            for (auto reply : observed->findChildren<QNetworkReply*>())
                if (!reply->isFinished() && reply->url().path() == "/api/snapshot" && reply->url().query().isEmpty()) pendingFull = reply;
            check(pendingFull && !freeze->isChecked() && !freeze->isEnabled(),
                  "Freeze remains uncommitted and disabled until a real complete snapshot succeeds");
            pendingFull->abort();
            waitFor([&] { return freeze->isEnabled(); }, "Freeze transport failure completion");
            check(!freeze->isChecked(), "A failed complete read never freezes a partial projection");
            freeze->setChecked(true);
            waitFor([&] { return freeze->isChecked() && freeze->isEnabled() && last("full"); }, "Retry complete real freeze");
            const auto frozen = last("full")->json;
            check(frozen.contains("history") && frozen.contains("performanceHistory"), "Successful freeze captures both histories at one real sampleId");
            button(window, "应用历史记录")->click(); pump(2300);
            check(table->model()->rowCount() == frozen["history"].toArray().size(), "Frozen history keeps the full snapshot's rows while background sampling advances");
            const auto newer = request(inspector, "snapshot?view=live");
            check(newer["sampleId"].toInteger() > frozen["sampleId"].toInteger(), "Host sampling continues behind the frozen display");
            button(window, "性能")->click();
            auto resources = window.findChild<QListWidget*>();
            const auto frozenX = QDateTime::fromString(frozen["performanceHistory"].toArray().last().toObject()["capturedAtUtc"].toString(), Qt::ISODateWithMs).toMSecsSinceEpoch()/1000.;
            check(resources && std::abs(resources->item(0)->data(Qt::UserRole+1).toList().last().toPointF().x() - frozenX) < .001,
                  "Frozen performance uses the identical complete snapshot timestamp after switching pages");
            const int unfreezeStart = samples.size(); freeze->setChecked(false);
            waitFor([&] { const auto s=last("performance",unfreezeStart); return s &&
                resources->item(0)->data(Qt::UserRole+1).toList().last().toPointF().x() > frozenX; }, "Unfreeze retrieves current page's newest real curve");
            check(maximumActive == 1, "Periodic, page-change, action and freeze snapshot reads never overlap");
            report["maximumActiveSnapshots"] = maximumActive;
            report["result"] = "PASS"; report["phase"] = "done"; save();
        } catch (const std::exception& error) {
            report["result"] = "FAIL"; report["error"] = QString::fromUtf8(error.what()); save();
        }
        window.close(); QTimer::singleShot(3000, &app, &QCoreApplication::quit);
    });
    const auto result = app.exec(); return report["result"] == "PASS" ? result : 1;
}
