#include "DataModel.h"
#include "Theme.h"
#include <QDateTime>
#include <QJsonDocument>
#include <QLocale>
#include <QSet>
#include <cmath>

QString rowIdentity(const QJsonObject& row) {
    for (const auto& key : {"identity", "id", "name"})
        if (row[key].isString())
            return row[key].toString();
    if (row.contains("protocol") && row.contains("localAddress")) {
        // Endpoint identities are tuples, not PIDs. JSON preserves component
        // boundaries even for IPv6 addresses containing ':' characters.
        QJsonArray endpoint;
        for (const auto& key :
             {"pid", "protocol", "addressFamily", "localAddress", "localPort", "remoteAddress", "remotePort"})
            endpoint.append(row[key]);
        return QString::fromUtf8(QJsonDocument(endpoint).toJson(QJsonDocument::Compact));
    }
    return QString::number(row["sessionId"].toInt(-1));
}
QString formatValue(const QJsonValue& value, const QString& format) {
    if (value.isNull() || value.isUndefined())
        return QStringLiteral("—");
    if (format == "bool")
        return value.toBool() ? QStringLiteral("是") : QStringLiteral("否");
    if (format == "startup")
        return value.toBool() ? QStringLiteral("已启用") : QStringLiteral("已禁用");
    if (format == "running")
        return value.toBool() ? QStringLiteral("运行中") : QStringLiteral("已退出");
    bool numeric = value.isDouble();
    const double nValue = value.isString() ? value.toString().toDouble(&numeric) : value.toDouble();
    const bool numericFormat = format == "percent" || format == "bytes" || format == "rate" ||
                               format == "cpuTime" || format == "duration" || format == "number";
    if ((numericFormat && !numeric) || (numeric && !std::isfinite(nValue)))
        return QStringLiteral("—");
    double n = nValue;
    if (format == "percent")
        return QString::number(n, 'f', 1) + " %";
    if (format == "bytes" || format == "rate") {
        const QStringList units{"B", "KB", "MB", "GB", "TB"};
        int unit = 0;
        while (std::abs(n) >= 1024 && unit < units.size() - 1) {
            n /= 1024;
            ++unit;
        }
        return QString::number(n, 'f', unit ? 1 : 0) + " " + units[unit] + (format == "rate" ? "/s" : "");
    }
    if (format == "cpuTime")
        n /= 10000000.;
    if (format == "cpuTime" || format == "duration") {
        const qint64 seconds = static_cast<qint64>(n);
        return QString("%1:%2:%3")
            .arg(seconds / 3600)
            .arg(seconds / 60 % 60, 2, 10, QLatin1Char('0'))
            .arg(seconds % 60, 2, 10, QLatin1Char('0'));
    }
    if (format == "date")
        return QDateTime::fromString(value.toString(), Qt::ISODateWithMs)
            .toLocalTime()
            .toString("yyyy-MM-dd HH:mm:ss");
    if (format == "state") {
        static const QHash<QString, QString> labels{
            {"running", "运行中"}, {"exited", "已退出"},         {"restricted", "受限"},
            {"stopped", "已停止"}, {"startPending", "正在启动"}, {"stopPending", "正在停止"},
            {"paused", "已暂停"},  {"active", "活动"},           {"disconnected", "已断开"},
            {"SERVICE_STOPPED", "已停止"}, {"SERVICE_RUNNING", "运行中"},
            {"SERVICE_START_PENDING", "正在启动"}, {"SERVICE_STOP_PENDING", "正在停止"},
            {"SERVICE_CONTINUE_PENDING", "正在继续"}, {"SERVICE_PAUSE_PENDING", "正在暂停"},
            {"SERVICE_PAUSED", "已暂停"},
            {"WTSActive", "活动"}, {"WTSConnected", "已连接"}, {"WTSConnectQuery", "正在查询连接"},
            {"WTSShadow", "会话监视"}, {"WTSDisconnected", "已断开"}, {"WTSIdle", "空闲"},
            {"WTSListen", "正在侦听"}, {"WTSReset", "正在重置"}, {"WTSDown", "离线"}, {"WTSInit", "正在初始化"},
            {"MIB_TCP_STATE_CLOSED", "已关闭"}, {"MIB_TCP_STATE_LISTEN", "正在侦听"},
            {"MIB_TCP_STATE_SYN_SENT", "已发送 SYN"}, {"MIB_TCP_STATE_SYN_RCVD", "已收到 SYN"},
            {"MIB_TCP_STATE_ESTAB", "已建立"}, {"MIB_TCP_STATE_FIN_WAIT1", "FIN 等待 1"},
            {"MIB_TCP_STATE_FIN_WAIT2", "FIN 等待 2"}, {"MIB_TCP_STATE_CLOSE_WAIT", "等待关闭"},
            {"MIB_TCP_STATE_CLOSING", "正在关闭"}, {"MIB_TCP_STATE_LAST_ACK", "等待最终确认"},
            {"MIB_TCP_STATE_TIME_WAIT", "时间等待"}, {"MIB_TCP_STATE_DELETE_TCB", "连接已删除"}};
        return labels.value(value.toString(), value.toString());
    }
    if (value.isDouble()) {
        // QJsonValue::toInteger returns its default for fractional doubles.
        // Preserve integer IDs exactly, but retain sensor/timing fractions.
        if (std::trunc(n) == n && n >= -9223372036854775808.0 && n < 9223372036854775808.0)
            return QLocale().toString(value.toInteger());
        return QLocale().toString(n, 'g', 15);
    }
    if (value.isBool())
        return value.toBool() ? "是" : "否";
    return value.toString();
}
QString reasonText(const QJsonObject& row, const QString& key) {
    const auto unavailable = row["unavailable"].toObject();
    if (!key.isEmpty()) {
        const auto specific = unavailable[key].toObject()["message"].toString();
        if (!specific.isEmpty())
            return specific;
        if (key == "enabled")
            return row["enabledReason"].toString();
    }
    QStringList parts;
    for (auto it = unavailable.begin(); it != unavailable.end(); ++it)
        parts << it.value().toObject()["message"].toString();
    parts.removeDuplicates();
    parts.removeAll({});
    return parts.join("\n");
}
DataModel::DataModel(QObject* parent) : QAbstractItemModel(parent), root_(std::make_unique<Node>()) {}
QModelIndex DataModel::index(int row, int column, const QModelIndex& parentIndex) const {
    const auto node = parentIndex.isValid() ? static_cast<Node*>(parentIndex.internalPointer()) : root_.get();
    if (row < 0 || column < 0 || column >= columns.size() || row >= static_cast<int>(node->children.size()))
        return {};
    return createIndex(row, column, node->children[row].get());
}
QModelIndex DataModel::parent(const QModelIndex& child) const {
    if (!child.isValid())
        return {};
    const auto p = static_cast<Node*>(child.internalPointer())->parent;
    if (!p || p == root_.get())
        return {};
    return createIndex(p->position, 0, p);
}
int DataModel::rowCount(const QModelIndex& parentIndex) const {
    if (parentIndex.isValid() && parentIndex.column() != 0)
        return 0;
    return static_cast<int>(
        (parentIndex.isValid() ? static_cast<Node*>(parentIndex.internalPointer()) : root_.get())
            ->children.size());
}
int DataModel::columnCount(const QModelIndex&) const {
    return columns.size();
}
QJsonValue DataModel::raw(const Node* node, const QString& key) const {
    if (key == "io") {
        if (!node->row["ioReadBytesPerSecond"].isDouble() || !node->row["ioWriteBytesPerSecond"].isDouble())
            return QJsonValue();
        return node->row["ioReadBytesPerSecond"].toDouble() + node->row["ioWriteBytesPerSecond"].toDouble();
    }
    return node->row[key];
}
QVariant DataModel::data(const QModelIndex& index, int role) const {
    if (!index.isValid())
        return {};
    const auto node = static_cast<Node*>(index.internalPointer());
    const auto& col = columns[index.column()];
    const auto value = raw(node, col.key);
    if (role == RawRole)
        return value.toVariant();
    if (role == ObjectRole)
        return node->row.toVariantMap();
    if (role == IdentityRole)
        return rowIdentity(node->row);
    if (role == Qt::DecorationRole && index.column() == 0)
        return icons.value(rowIdentity(node->row), Theme::icon(0));
    if (role == Qt::DisplayRole || role == Qt::AccessibleTextRole) {
        if (col.format == "startup" && value.isNull())
            return QStringLiteral("无法确定");
        return formatValue(value, col.format);
    }
    if (role == Qt::ToolTipRole || role == Qt::AccessibleDescriptionRole) {
        auto reason = reasonText(node->row, col.key);
        const auto display = formatValue(value, col.format);
        return reason.isEmpty() ? display : display + "\n" + reason;
    }
    if (role == Qt::TextAlignmentRole)
        return QVariant::fromValue(
            int((col.format == "percent" || col.format == "bytes" || col.format == "rate" ||
                 col.format == "number" || col.format == "cpuTime")
                    ? Qt::AlignRight | Qt::AlignVCenter
                    : Qt::AlignLeft | Qt::AlignVCenter));
    if (role == Qt::ForegroundRole && (value.isNull() || value.isUndefined()))
        return Theme::secondary;
    if (role == Qt::ForegroundRole && col.format == "startup")
        return value.toBool() ? Theme::success : Theme::secondary;
    if (role == Qt::BackgroundRole && col.format == "percent" && value.isDouble()) {
        auto c = Theme::accent;
        c.setAlphaF(std::clamp(value.toDouble() / 100 * .22, 0., .22));
        return c;
    }
    return {};
}
QVariant DataModel::headerData(int section, Qt::Orientation orientation, int role) const {
    if (orientation == Qt::Horizontal && role == Qt::DisplayRole && section >= 0 && section < columns.size())
        return columns[section].title;
    return {};
}
void DataModel::setColumns(QList<Column> value) {
    beginResetModel();
    columns = std::move(value);
    root_ = std::make_unique<Node>();
    identities_.clear();
    endResetModel();
}
void DataModel::setRows(const QJsonArray& rows, bool tree) {
    // Model/view paints only visible rows. No permanent cell widgets are created.
    beginResetModel();
    root_ = std::make_unique<Node>();
    identities_.clear();
    QHash<qint64, QJsonObject> byPid;
    if (tree)
        for (const auto& row : rows)
            byPid.insert(row.toObject()["pid"].toInteger(), row.toObject());
    QHash<QString, QJsonObject> pending;
    for (const auto& value : rows) {
        const auto row = value.toObject();
        pending.insert(rowIdentity(row), row);
    }
    std::function<Node*(const QString&, QSet<QString>)> add = [&](const QString& id,
                                                                  QSet<QString> chain) -> Node* {
        if (identities_.contains(id))
            return identities_[id];
        const auto row = pending.value(id);
        Node* parent = root_.get();
        chain.insert(id);
        if (tree) {
            const auto p = byPid.value(row["parentPid"].toInteger());
            const auto pid = rowIdentity(p);
            bool validChild = !p.isEmpty() && !chain.contains(pid) && p["creationTimeFileTime"].isString() &&
                              row["creationTimeFileTime"].isString() &&
                              p["creationTimeFileTime"].toString().toULongLong() <=
                                  row["creationTimeFileTime"].toString().toULongLong();
            if (validChild)
                parent = add(pid, chain);
        }
        auto node = std::make_unique<Node>();
        node->row = row;
        node->parent = parent;
        node->position = static_cast<int>(parent->children.size());
        const auto ptr = node.get();
        parent->children.push_back(std::move(node));
        identities_.insert(id, ptr);
        return ptr;
    };
    for (const auto& value : rows)
        add(rowIdentity(value.toObject()), {});
    endResetModel();
}
QModelIndex DataModel::find(const QString& identity) const {
    const auto node = identities_.value(identity);
    return node ? createIndex(node->position, 0, node) : QModelIndex();
}
DataProxy::DataProxy(QObject* parent) : QSortFilterProxyModel(parent) {
    setDynamicSortFilter(true);
    setRecursiveFilteringEnabled(true);
    setSortCaseSensitivity(Qt::CaseInsensitive);
}
bool DataProxy::lessThan(const QModelIndex& left, const QModelIndex& right) const {
    const auto a = sourceModel()->data(left, DataModel::RawRole),
               b = sourceModel()->data(right, DataModel::RawRole);
    const bool na = !a.isValid() || a.isNull(), nb = !b.isValid() || b.isNull();
    if (na != nb)
        return sortOrder() == Qt::DescendingOrder ? na : !na;
    if (na && nb)
        return sourceModel()->data(left, DataModel::IdentityRole).toString() <
               sourceModel()->data(right, DataModel::IdentityRole).toString();
    const auto model = static_cast<const DataModel*>(sourceModel());
    const auto format = model->columns[left.column()].format;
    if (format == "percent" || format == "bytes" || format == "rate" || format == "number" ||
        format == "cpuTime" || format == "duration") {
        bool okA, okB;
        const auto da = a.toDouble(&okA), db = b.toDouble(&okB);
        if (okA && okB)
            return da < db;
    }
    return QString::localeAwareCompare(a.toString().toCaseFolded(), b.toString().toCaseFolded()) < 0;
}
bool DataProxy::filterAcceptsRow(int row, const QModelIndex& parentIndex) const {
    const auto data = QJsonObject::fromVariantMap(
        sourceModel()->data(sourceModel()->index(row, 0, parentIndex), DataModel::ObjectRole).toMap());
    if (onlyCurrentUser && data["userName"].toString().compare(currentUser, Qt::CaseInsensitive) != 0)
        return false;
    if (session >= 0 && data["sessionId"].toInt(-1) != session)
        return false;
    if (state != "all" && data["state"].toString() != state)
        return false;
    if (query.isEmpty())
        return true;
    for (auto it = data.begin(); it != data.end(); ++it)
        if (it.value().isString() && it.value().toString().contains(query, Qt::CaseInsensitive))
            return true;
    return QString::number(data["pid"].toInteger()).contains(query) ||
           QString::number(data["sessionId"].toInt(-1)).contains(query);
}
