#include "SystemInformationView.h"
#include "DataModel.h"
#include "Theme.h"
#include <QHBoxLayout>
#include <QJsonArray>
#include <QScrollBar>
#include <QTimer>
#include <QVBoxLayout>
#include <cmath>
#include <limits>

namespace {
QString value(const QJsonObject& object, const QString& key, const QString& unit = {}) {
    const auto data = object[key];
    if (data.isNull() || data.isUndefined() || (data.isString() && data.toString().isEmpty()))
        return QStringLiteral("—");
    return (data.isString() ? data.toString() : QString::number(data.toDouble(), 'g', 12)) + unit;
}
QString bytes(const QJsonObject& object, const QString& key) {
    bool valid = false;
    const auto count = object[key].toString().toULongLong(&valid);
    if (!valid) return QStringLiteral("—");
    const double gib = count / (1024. * 1024. * 1024.);
    return QString::number(gib >= 1024 ? gib / 1024 : gib, 'f', gib >= 1024 ? 2 : (std::floor(gib) == gib ? 0 : 1)) +
           (gib >= 1024 ? QStringLiteral(" TiB") : QStringLiteral(" GiB"));
}
QString fact(const QString& label, const QString& data) { return label + "：" + data; }
QLabel* label(const QString& text, const QString& name, QWidget* parent, bool strong = false, bool secondary = false) {
    auto item = new QLabel(text, parent);
    item->setObjectName(name);
    item->setTextFormat(Qt::PlainText);
    item->setWordWrap(true);
    item->setMinimumWidth(0);
    item->setSizePolicy(QSizePolicy::Ignored, QSizePolicy::Preferred);
    item->setTextInteractionFlags(Qt::TextSelectableByMouse | Qt::TextSelectableByKeyboard);
    item->setProperty("secondary", secondary);
    Theme::type(item, secondary ? Theme::Type::Caption : Theme::Type::Body, strong);
    item->setAccessibleName(text);
    return item;
}
}
SystemInformationView::SystemInformationView(QWidget* parent) : QScrollArea(parent) {
    setObjectName("systemInformationPage");
    setAccessibleName("系统信息硬件概览");
    setWidgetResizable(true);
    setFrameShape(QFrame::NoFrame);
    setFocusPolicy(Qt::StrongFocus);
    setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    setInformation({});
}
void SystemInformationView::setInformation(const QJsonObject& information) {
    auto payload = information;
    payload.remove("capturedAtUtc");
    auto previous = information_;
    previous.remove("capturedAtUtc");
    if (!content_ || payload != previous) {
        const auto scroll = scrollPosition();
        const auto focus = qApp->focusWidget();
        const auto focusName = focus && isAncestorOf(focus) ? focus->objectName() : QString();
        information_ = information;
        groups_.clear();
        text_.clear();
        auto wrapper = new QWidget(this);
        auto outer = new QHBoxLayout(wrapper);
        outer->setContentsMargins(0, 0, Theme::space2, Theme::space2);
        outer->setSpacing(0);
        content_ = new QWidget(wrapper);
        content_->setObjectName("systemInformationContent");
        content_->setMaximumWidth(Theme::settingsWidth);
        auto column = new QVBoxLayout(content_);
        column->setContentsMargins(0, 0, 0, 0);
        column->setSpacing(Theme::space2);
        outer->addWidget(content_, 1, Qt::AlignTop);
        outer->addStretch();
        auto group = [&](const QString& key, const QString& title, int icon) {
            text_ << QString() << title;
            auto row = new QWidget(content_);
            row->setObjectName("hardware-" + key);
            row->setProperty("hardwareGroup", true);
            auto line = new QHBoxLayout(row);
            line->setContentsMargins(0, Theme::space2, 0, Theme::space2);
            line->setSpacing(Theme::space4);
            auto category = new QWidget(row);
            category->setFixedWidth(Theme::systemInfoLabelWidth);
            auto categoryLayout = new QHBoxLayout(category);
            categoryLayout->setContentsMargins(0, 0, 0, 0);
            categoryLayout->setSpacing(Theme::space3);
            auto image = new QLabel(category);
            image->setPixmap(Theme::icon(icon).pixmap(Theme::iconSize, Theme::iconSize));
            image->setFixedSize(Theme::iconSize, Theme::iconSize);
            auto name = new QLabel(title, category);
            Theme::type(name, Theme::Type::Body, true);
            categoryLayout->addWidget(image);
            categoryLayout->addWidget(name, 1);
            line->addWidget(category, 0, Qt::AlignTop);
            auto facts = new QWidget(row);
            auto items = new QVBoxLayout(facts);
            items->setContentsMargins(0, 0, 0, 0);
            items->setSpacing(Theme::space3);
            line->addWidget(facts, 1);
            column->addWidget(row);
            groups_ << row;
            row->setProperty("groupTitle", title);
            return items;
        };
        auto device = [&](QVBoxLayout* items, const QString& key, const QString& name, const QStringList& facts, const QJsonObject& source) {
            text_ << "  " + name;
            if (!facts.isEmpty()) text_ << "    " + facts.join("    ");
            auto entry = new QWidget(items->parentWidget());
            entry->setObjectName("hardware-device-" + key);
            auto block = new QVBoxLayout(entry);
            block->setContentsMargins(0, 0, 0, 0);
            block->setSpacing(Theme::space1);
            block->addWidget(label(name, "hardware-name-" + key, entry, true));
            if (!facts.isEmpty()) block->addWidget(label(facts.join("    "), "hardware-facts-" + key, entry, false, true));
            const auto reason = reasonText(source);
            if (!reason.isEmpty()) {
                text_ << "    部分信息不可用：" + reason;
                entry->setToolTip(reason);
                entry->setAccessibleDescription(reason);
                block->addWidget(label("部分信息不可用：" + reason, "hardware-reason-" + key, entry, false, true));
            }
            items->addWidget(entry);
        };
        auto absent = [&](QVBoxLayout* items, const QString& key) {
            auto reason = reasonText(information, key);
            if (reason.isEmpty()) reason = reasonText(information, "source");
            device(items, key, "—", {reason.isEmpty() ? QStringLiteral("Windows 未返回此类设备信息。") : reason}, {});
        };
        auto processors = group("processors", "处理器", 11);
        for (const auto& entry : information["processors"].toArray()) {
            const auto row = entry.toObject();
            device(processors, row["id"].toString(), value(row, "name"),
                   {fact("核心数", value(row, "coreCount")), fact("线程数", value(row, "logicalProcessorCount")), fact("厂商", value(row, "manufacturer"))}, row);
        }
        if (information["processors"].toArray().isEmpty()) absent(processors, "processors");
        auto graphics = group("graphicsAdapters", "显卡", 12);
        for (const auto& entry : information["graphicsAdapters"].toArray()) {
            const auto row = entry.toObject();
            const auto kind = row["adapterKind"].toString();
            const auto source = row["isSoftware"].toBool() ? QStringLiteral("软件适配器")
                : kind == "physical" ? QStringLiteral("物理显卡")
                : kind == "virtual" ? QStringLiteral("虚拟适配器") : QStringLiteral("逻辑适配器（物理身份未确认）");
            device(graphics, row["id"].toString(), value(row, "name"),
                   {fact("专用显存", bytes(row, "dedicatedMemoryBytes")), source}, row);
        }
        if (information["graphicsAdapters"].toArray().isEmpty()) absent(graphics, "graphicsAdapters");
        auto motherboard = group("motherboard", "主板", 13);
        const auto board = information["motherboard"].toObject();
        device(motherboard, "motherboard", value(board, "product"), {fact("厂商", value(board, "manufacturer")), fact("版本", value(board, "version"))}, board);
        auto memory = group("memory", "内存", 14);
        const auto ram = information["memory"].toObject();
        device(memory, "memory", fact("安装容量", bytes(ram, "installedBytes")), {fact("数量", value(ram, "moduleCount", " 条"))}, ram);
        for (const auto& entry : ram["modules"].toArray()) {
            const auto row = entry.toObject();
            device(memory, row["id"].toString(), value(row, "manufacturer") + "  " + value(row, "partNumber"),
                   {value(row, "memoryType"), bytes(row, "capacityBytes"), fact("插槽", value(row, "deviceLocator")),
                    fact("配置速度（固件）", value(row, "configuredSpeedMtPerSecond", " MT/s")), fact("设备速度（固件）", value(row, "speedMtPerSecond", " MT/s"))}, row);
        }
        auto disks = group("disks", "硬盘", 15);
        const auto driveRows = information["disks"].toArray();
        qulonglong total = 0;
        bool complete = !driveRows.isEmpty() && reasonText(information, "disks").isEmpty();
        for (const auto& entry : driveRows) {
            bool valid = false;
            const auto count = entry.toObject()["capacityBytes"].toString().toULongLong(&valid);
            if (!valid || count > std::numeric_limits<qulonglong>::max() - total) complete = false;
            else total += count;
        }
        if (!driveRows.isEmpty()) device(disks, "disk-summary", QString("%1 块物理磁盘").arg(driveRows.size()),
            {fact("总容量", complete ? bytes({{"total", QString::number(total)}}, "total") : QStringLiteral("—（容量信息不完整）"))}, {});
        for (const auto& entry : driveRows) {
            const auto row = entry.toObject();
            device(disks, row["id"].toString(), value(row, "model"),
                   {fact("实际容量", bytes(row, "capacityBytes")), fact("类型", value(row, "mediaType")), fact("总线", value(row, "busType")), fact("磁盘", value(row, "deviceNumber"))}, row);
        }
        if (driveRows.isEmpty()) absent(disks, "disks");
        auto displays = group("displays", "显示器", 16);
        for (const auto& entry : information["displays"].toArray()) {
            const auto row = entry.toObject();
            QString size = QStringLiteral("—");
            if (row["widthMillimeters"].isDouble() && row["heightMillimeters"].isDouble())
                size = QString::number(std::hypot(row["widthMillimeters"].toDouble(), row["heightMillimeters"].toDouble()) / 25.4, 'f', 1) + " 英寸（EDID）";
            device(displays, row["id"].toString(), value(row, "name"),
                   {fact("分辨率", value(row, "widthPixels") + " × " + value(row, "heightPixels")), fact("刷新率", value(row, "refreshRateHz", " Hz")), fact("屏幕尺寸", size)}, row);
        }
        if (information["displays"].toArray().isEmpty()) absent(displays, "displays");
        auto operatingSystem = group("operatingSystem", "系统", 10);
        const auto os = information["operatingSystem"].toObject();
        device(operatingSystem, "operatingSystem", value(os, "caption"),
               {fact("版本", value(os, "displayVersion")), fact("构建", value(os, "build")), fact("架构", value(os, "architecture"))}, os);
        column->addStretch();
        setWidget(wrapper);
        filter(query_);
        restoreScroll(scroll);
        if (!focusName.isEmpty()) QTimer::singleShot(0, this, [this, focusName] {
            if (auto target = findChild<QWidget*>(focusName); target && target->isVisible()) target->setFocus(Qt::OtherFocusReason);
        });
    }
    information_ = information;
}
QString SystemInformationView::plainText() const { return text_.join('\n').trimmed(); }
int SystemInformationView::filter(const QString& query) {
    query_ = query;
    int visible = 0;
    for (auto group : groups_) {
        QString text = group->property("groupTitle").toString();
        for (auto item : group->findChildren<QLabel*>()) text += " " + item->text();
        const bool match = query.trimmed().isEmpty() || text.contains(query.trimmed(), Qt::CaseInsensitive);
        group->setVisible(match);
        visible += match;
    }
    return visible;
}
int SystemInformationView::scrollPosition() const { return verticalScrollBar()->value(); }
void SystemInformationView::restoreScroll(int position) {
    QTimer::singleShot(0, this, [this, position] { verticalScrollBar()->setValue(position); });
}
