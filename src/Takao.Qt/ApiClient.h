#pragma once
#include <QJsonObject>
#include <QNetworkAccessManager>
#include <QObject>
#include <QUrl>
#include <functional>

class ApiClient final : public QObject {
    Q_OBJECT
  public:
    using Callback = std::function<void(const QJsonObject&, const QString&, int)>;
    explicit ApiClient(QUrl origin, QObject* parent = nullptr);
    void get(const QString& route, QObject* context, Callback callback, const QByteArray& etag = {});
    void post(const QString& route, const QJsonObject& body, QObject* context, Callback callback,
              int timeoutMs = 15000);
    void image(const QString& route, QObject* context, std::function<void(const QByteArray&)> callback);
    void setToken(const QString& token) {
        token_ = token.toUtf8();
    }
    QByteArray snapshotEtag;

  private:
    QNetworkAccessManager manager_;
    QUrl origin_;
    QByteArray token_;
    void request(const QString&, const QJsonObject*, QObject*, Callback, const QByteArray&, int);
};
