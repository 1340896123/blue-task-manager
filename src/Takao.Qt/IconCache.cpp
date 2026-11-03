#include "IconCache.h"
#include <QJsonArray>
#include <QJsonDocument>
#include <QPixmap>
#include <chrono>

IconCache::IconCache(Clock clock) : clock_(std::move(clock)) {
    if (!clock_)
        clock_ = [] {
            return std::chrono::duration_cast<std::chrono::milliseconds>(
                       std::chrono::steady_clock::now().time_since_epoch()).count();
        };
}
QString IconCache::key(const QString& kind, const QString& id, const QJsonObject& row) {
    if (id.isEmpty() || (kind != "process" && kind != "history" && kind != "startup" && kind != "service"))
        return {};
    // A history row retains the same process creation identity. Never merge
    // different process identities by path: packaged apps can share an EXE.
    QJsonArray parts{kind == "history" ? QStringLiteral("process") : kind, id};
    if (kind == "startup") {
        parts.append(row["command"]);
        parts.append(row["locationPath"]);
    }
    return QString::fromUtf8(QJsonDocument(parts).toJson(QJsonDocument::Compact));
}
QIcon IconCache::icon(const QString& key) const {
    const auto entry = entries_.object(key);
    return entry ? entry->image : QIcon{};
}
bool IconCache::beginRequest(const QString& key) {
    if (key.isEmpty() || pending_.contains(key) || pending_.size() >= concurrency)
        return false;
    const auto entry = entries_.object(key);
    if (entry && clock_() < entry->nextRead)
        return false;
    pending_.insert(key);
    return true;
}
bool IconCache::complete(const QString& key, const QByteArray& png) {
    if (!pending_.remove(key))
        return false;
    QPixmap pixels;
    const bool success = !png.isEmpty() && pixels.loadFromData(png, "PNG");
    auto entry = entries_.object(key);
    if (!entry) {
        auto created = new Entry;
        entries_.insert(key, created);
        entry = created;
    }
    if (success)
        entry->image = QIcon(pixels);
    // Keep the last successful image on a transient failure. Empty and invalid
    // PNG responses use the same bounded retry state, without per-row timers.
    entry->nextRead = clock_() + (success ? refreshMs : retryMs);
    return success;
}
