#include "ApiClient.h"
#include <QJsonDocument>
#include <QNetworkReply>
#include <QPointer>

ApiClient::ApiClient(QUrl origin, QObject* parent)
    : QObject(parent), manager_(this), origin_(std::move(origin)) {}
void ApiClient::get(const QString& route, QObject* context, Callback callback, const QByteArray& etag) {
    request(route, nullptr, context, std::move(callback), etag, 10000);
}
void ApiClient::post(const QString& route, const QJsonObject& body, QObject* context, Callback callback,
                     int timeoutMs) {
    request(route, &body, context, std::move(callback), {}, timeoutMs);
}
void ApiClient::request(const QString& route, const QJsonObject* body, QObject* context, Callback callback,
                        const QByteArray& etag, int timeoutMs) {
    QUrl url(origin_);
    const auto queryAt = route.indexOf(u'?');
    url.setPath(QStringLiteral("/api/") + (queryAt < 0 ? route : route.left(queryAt)));
    if (queryAt >= 0)
        url.setQuery(route.mid(queryAt + 1));
    QNetworkRequest request(url);
    request.setTransferTimeout(timeoutMs);
    request.setAttribute(QNetworkRequest::RedirectPolicyAttribute, QNetworkRequest::ManualRedirectPolicy);
    request.setRawHeader(QByteArrayLiteral("Accept"), QByteArrayLiteral("application/json"));
    request.setRawHeader(
        QByteArrayLiteral("Origin"),
        origin_.toString(QUrl::RemovePath | QUrl::RemoveQuery | QUrl::RemoveFragment).toUtf8());
    if (!etag.isEmpty())
        request.setRawHeader(QByteArrayLiteral("If-None-Match"), etag);
    QNetworkReply* reply;
    if (body) {
        request.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
        request.setRawHeader(QByteArrayLiteral("X-TaskManager-Token"), token_);
        reply = manager_.post(request, QJsonDocument(*body).toJson(QJsonDocument::Compact));
    } else
        reply = manager_.get(request);
    connect(context, &QObject::destroyed, reply, &QNetworkReply::abort);
    connect(reply, &QIODevice::readyRead, reply, [reply] {
        if (reply->bytesAvailable() > 32 * 1024 * 1024)
            reply->abort();
    });
    const QPointer<QObject> guard(context);
    connect(reply, &QNetworkReply::finished, this,
            [this, reply, guard, callback = std::move(callback), route]() {
                const auto status = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
                QJsonParseError parse;
                const auto bytes = reply->readAll();
                const auto document = QJsonDocument::fromJson(bytes, &parse);
                const auto object = document.object();
                QString error;
                if (status >= 300 && status != 304)
                    error = object.value(QStringLiteral("error"))
                                .toObject()
                                .value(QStringLiteral("message"))
                                .toString();
                if (status >= 300 && status < 400 && status != 304)
                    error = QStringLiteral("本机服务返回重定向，已拒绝连接其它地址。");
                if (status != 304 && (reply->error() != QNetworkReply::NoError || status >= 400)) {
                    if (error.isEmpty())
                        error = QStringLiteral("无法读取本机服务：%1。请重试。").arg(reply->errorString());
                } else if (status != 304 && (parse.error != QJsonParseError::NoError || !document.isObject()))
                    error = QStringLiteral("本机服务返回的数据格式无效。");
                if (route == QStringLiteral("snapshot") && status == 200 && error.isEmpty())
                    snapshotEtag = reply->rawHeader(QByteArrayLiteral("ETag"));
                reply->deleteLater();
                if (guard)
                    callback(object, error, status);
            });
}
void ApiClient::image(const QString& route, QObject* context,
                      std::function<void(const QByteArray&)> callback) {
    QUrl url(origin_);
    const auto query = route.indexOf('?');
    url.setPath("/api/" + route.left(query));
    url.setQuery(route.mid(query + 1));
    QNetworkRequest request(url);
    request.setTransferTimeout(6000);
    request.setAttribute(QNetworkRequest::RedirectPolicyAttribute, QNetworkRequest::ManualRedirectPolicy);
    request.setRawHeader(
        "Origin", origin_.toString(QUrl::RemovePath | QUrl::RemoveQuery | QUrl::RemoveFragment).toUtf8());
    auto reply = manager_.get(request);
    const QPointer<QObject> guard(context);
    connect(context, &QObject::destroyed, reply, &QNetworkReply::abort);
    connect(reply, &QIODevice::readyRead, reply, [reply] {
        if (reply->bytesAvailable() > 1024 * 1024)
            reply->abort();
    });
    connect(reply, &QNetworkReply::finished, this, [reply, guard, callback = std::move(callback)] {
        QByteArray bytes;
        if (reply->error() == QNetworkReply::NoError &&
            reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt() == 200 &&
            reply->header(QNetworkRequest::ContentTypeHeader).toString().startsWith("image/png"))
            bytes = reply->readAll();
        reply->deleteLater();
        if (guard)
            callback(bytes);
    });
}
