// Pure cache policy tests. Keys below are independent test keys, not Windows
// process samples; image bytes come from the production theme's real SVG.
#include "IconCache.h"
#include "Theme.h"
#include <QApplication>
#include <QBuffer>
#include <QJsonArray>
#include <QJsonDocument>
#include <cstdio>
#include <stdexcept>

int main(int argc, char** argv) {
    QApplication app(argc, argv);
    QJsonArray checks;
    auto check = [&](bool condition, const char* message) {
        if (!condition)
            throw std::runtime_error(message);
        checks.append(QString::fromUtf8(message));
    };
    try {
        for (int i = 0; i < 10; ++i) {
            const auto image = Theme::icon(i);
            check(!image.isNull() && image.cacheKey() == Theme::icon(i).cacheKey(),
                  "Static theme icon shares its QIcon on repeated reads");
        }
        QByteArray png;
        QBuffer buffer(&png);
        buffer.open(QIODevice::WriteOnly);
        check(Theme::icon(0).pixmap(24, 24).save(&buffer, "PNG"), "Production SVG renders to a real PNG");

        qint64 now = 0;
        IconCache cache([&] { return now; });
        check(cache.beginRequest("first") && !cache.beginRequest("first"),
              "The same in-flight key is coalesced");
        check(cache.beginRequest("second") && !cache.beginRequest("third") && cache.inFlight() == 2,
              "At most two requests can run across all keys");
        check(cache.complete("first", png) && cache.complete("second", png) && cache.inFlight() == 0,
              "Completion releases both concurrency slots");
        const auto shared = cache.icon("first").cacheKey();
        check(shared && shared == cache.icon("first").cacheKey() && !cache.beginRequest("first"),
              "A fresh decoded icon is reused without another request or decode");
        now = IconCache::refreshMs;
        check(cache.beginRequest("first") && !cache.complete("first", "invalid PNG"),
              "A successful transport with invalid PNG bytes enters failure backoff");
        check(cache.icon("first").cacheKey() == shared && !cache.beginRequest("first"),
              "Failed revalidation retains the successful icon and suppresses immediate retry");
        now += IconCache::retryMs - 1;
        check(!cache.beginRequest("first"), "The full failure backoff is respected");
        ++now;
        check(cache.beginRequest("first") && cache.complete("first", png),
              "Revalidation can recover after the failure backoff");
        check(cache.beginRequest("missing") && !cache.complete("missing", {}) &&
                  cache.icon("missing").isNull() && !cache.beginRequest("missing"),
              "An empty response is cached as unavailable without fabricating an image");
        now += IconCache::retryMs;
        check(cache.beginRequest("missing") && cache.complete("missing", png),
              "An initially missing icon can recover after the failure backoff");

        IconCache lru([&] { return now; });
        for (int i = 0; i < IconCache::capacity; ++i) {
            const auto key = QString::number(i);
            if (!lru.beginRequest(key) || !lru.complete(key, png))
                throw std::runtime_error("Cannot fill the bounded cache");
        }
        const auto hot = lru.icon("0").cacheKey();
        check(lru.beginRequest("overflow") && lru.complete("overflow", png),
              "A new key loads after visiting 512 independent cache keys");
        check(lru.count() == IconCache::capacity && lru.icon("0").cacheKey() == hot &&
                  lru.icon("1").isNull() && !lru.icon("2").isNull() && !lru.icon("overflow").isNull(),
              "Overflow evicts the least recently used entry and preserves hot and other cached icons");

        const QString id = "cache-policy-key";
        const auto process = IconCache::key("process", id, {});
        check(process == IconCache::key("history", id, {}) &&
                  process != IconCache::key("service", id, {}),
              "Process and history reuse creation identity while service namespaces remain isolated");
        const QJsonObject startup{{"command", "first-source"}, {"locationPath", "first-location"}};
        auto changedCommand = startup;
        changedCommand["command"] = "second-source";
        auto changedLocation = startup;
        changedLocation["locationPath"] = "second-location";
        check(IconCache::key("startup", id, startup) != IconCache::key("startup", id, changedCommand) &&
                  IconCache::key("startup", id, startup) != IconCache::key("startup", id, changedLocation),
              "Observable startup source changes invalidate the cache identity");
        check(IconCache::key("users", id, {}).isEmpty() && !cache.beginRequest({}),
              "Unsupported sources and empty cache identities do not issue icon requests");
        const auto bytes = QJsonDocument(QJsonObject{{"result", "PASS"}, {"checks", checks}})
                               .toJson(QJsonDocument::Indented);
        std::fwrite(bytes.constData(), 1, bytes.size(), stdout);
        return 0;
    } catch (const std::exception& error) {
        std::fprintf(stderr, "Icon cache check failed: %s\n", error.what());
        return 1;
    }
}
