#pragma once
#include <QCache>
#include <QIcon>
#include <QJsonObject>
#include <QSet>
#include <functional>

// Owned by the GUI model: QIcons and their retry state share one bounded LRU.
class IconCache final {
  public:
    using Clock = std::function<qint64()>;
    static constexpr int capacity = 512, concurrency = 2;
    static constexpr qint64 refreshMs = 60000, retryMs = 15000;
    explicit IconCache(Clock clock = {});
    static QString key(const QString& kind, const QString& id, const QJsonObject& row);
    QIcon icon(const QString& key) const;
    bool beginRequest(const QString& key);
    bool complete(const QString& key, const QByteArray& png);
    int count() const { return entries_.size(); }
    int inFlight() const { return pending_.size(); }

  private:
    struct Entry {
        QIcon image;
        qint64 nextRead = 0;
    };
    Clock clock_;
    QCache<QString, Entry> entries_{capacity};
    QSet<QString> pending_;
};
