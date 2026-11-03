#pragma once
#include "IconCache.h"
#include <QAbstractItemModel>
#include <QIcon>
#include <QJsonArray>
#include <QJsonObject>
#include <QSortFilterProxyModel>
#include <memory>
#include <vector>

struct Column {
    QString key, title, format;
    int width = 150;
};
QString formatValue(const QJsonValue&, const QString& format);
QString reasonText(const QJsonObject& row, const QString& key = {});
QString rowIdentity(const QJsonObject&);

class DataModel final : public QAbstractItemModel {
    Q_OBJECT
  public:
    enum { RawRole = Qt::UserRole + 1, ObjectRole, IdentityRole, StatusRole, IconKeyRole };
    explicit DataModel(QObject* parent = nullptr);
    QModelIndex index(int, int, const QModelIndex& = {}) const override;
    QModelIndex parent(const QModelIndex&) const override;
    int rowCount(const QModelIndex& = {}) const override;
    int columnCount(const QModelIndex& = {}) const override;
    QVariant data(const QModelIndex&, int = Qt::DisplayRole) const override;
    QVariant headerData(int, Qt::Orientation, int = Qt::DisplayRole) const override;
    void setColumns(QList<Column> columns);
    void setRows(const QJsonArray& rows, bool tree = false);
    QModelIndex find(const QString& identity) const;
    QList<Column> columns;
    QString iconKind;
    IconCache icons;

  private:
    struct Node {
        QJsonObject row;
        QString iconKey;
        Node* parent = nullptr;
        std::vector<std::unique_ptr<Node>> children;
        int position = 0;
    };
    std::unique_ptr<Node> root_;
    QHash<QString, Node*> identities_;
    QJsonValue raw(const Node*, const QString&) const;
};

class DataProxy final : public QSortFilterProxyModel {
    Q_OBJECT
  public:
    explicit DataProxy(QObject* parent = nullptr);
    QString query, currentUser, state = QStringLiteral("all");
    bool onlyCurrentUser = false;
    int session = -1;
    void refreshFilter() {
        invalidateFilter();
    }

  protected:
    bool lessThan(const QModelIndex&, const QModelIndex&) const override;
    bool filterAcceptsRow(int, const QModelIndex&) const override;
};
