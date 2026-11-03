#pragma once
#include <QApplication>
#include <QComboBox>
#include <QDialog>
#include <QIcon>
#include <QLabel>
#include <QPainter>
#include <QPalette>
#include <QPointer>
#include <QPushButton>
#include <QScreen>
#include <QStyle>
#include <QStyleOptionComboBox>
#include <QSvgRenderer>
#include <QVBoxLayout>
#include <algorithm>

// Fluent roles shared by widgets, delegates and charts. Sizes are logical pixels.
namespace Theme {
inline const QColor surface("#FFFFFF"), app("#F3F3F3"), content("#F9F9F9"), surfaceSubtle("#F6F6F6"),
    text("#1A1A1A"), secondary("#5F5F5F"), disabledText("#747474");
inline const QColor accent("#0067C0"), accentHover("#005FB8"), accentPressed("#00539F"), focus("#005FB8"),
    selection("#E8EEF5"), selectionHover("#DEE8F2"), hover("#EDEDED"), pressed("#E4E4E4");
inline const QColor border("#8A8A8A"), strokeSubtle("#E5E5E5"), strokeControl("#D1D1D1"),
    grid("#ECEFF2"), secondLine("#58636F"), danger("#A4262C"), warning("#8A4B00"), success("#107C10"),
    dangerSurface("#FDECEE"), warningSurface("#FFF4CE"), successSurface("#DFF6DD"), infoSurface("#E8EEF5");
inline const QColor dangerHover = danger.darker(110), dangerPressed = danger.darker(120);
inline constexpr int space1 = 4, space2 = 8, space3 = 12, space4 = 16, space6 = 24, space8 = 32;
inline constexpr int sidebarWidth = 204, sidebarCompact = 56, navigationHeight = 44,
    commandHeight = 44, secondaryCommandHeight = 32, controlHeight = 32, searchHeight = 36,
    topHeight = 56, commandBarHeight = 56, headerHeight = 40, compactRow = 32, standardRow = 40,
    panelRadius = 8, controlRadius = 4, focusWidth = 2, indicatorWidth = 3, indicatorHeight = 16,
    resourceWidth = 240, resourceTileHeight = 80, resourceDiskHeight = 96,
    settingsWidth = 1080, settingControlWidth = 240, pathWidth = 440, settingMinHeight = 76,
    dialogWidth = 560, detailDialogWidth = 720, dialogClearance = 96, iconSize = 20,
    statusIconSize = 14, chartMinHeight = 190, logicalChartHeight = 180, mainChartHeight = 330;
inline constexpr qreal maximumHeat = .16, chartLineWidth = 2., sparkLineWidth = 1.5;
enum class Type { Caption = 12, Body = 14, BodyLarge = 16, Section = 20, PageTitle = 28, Metric = 32 };
inline QFont font(Type role = Type::Body, bool strong = false) {
    auto result = qApp->font();
    const auto scale = result.pixelSize() > 0 ? result.pixelSize() / 14. : 1.;
    result.setPixelSize(qRound(static_cast<int>(role) * scale));
    result.setWeight(strong ? QFont::DemiBold : QFont::Normal);
    return result;
}
inline QFont numericFont(Type role = Type::Body, bool strong = false) {
    auto result = font(role, strong);
    result.setFamilies({"Cascadia Mono", "Consolas", "Segoe UI", "Microsoft YaHei UI"});
    return result;
}
inline void type(QWidget* widget, Type role, bool strong = false) {
    widget->setProperty("typeRole", static_cast<int>(role));
    widget->setProperty("typeStrong", strong);
    widget->setFont(font(role, strong));
}
inline void refreshTypography() {
    for (auto widget : QApplication::allWidgets())
        if (widget->property("typeRole").isValid())
            widget->setFont(widget->property("numericType").toBool()
                ? numericFont(static_cast<Type>(widget->property("typeRole").toInt()), widget->property("typeStrong").toBool())
                : font(static_cast<Type>(widget->property("typeRole").toInt()), widget->property("typeStrong").toBool()));
}
inline void repolish(QWidget* widget) {
    widget->style()->unpolish(widget);
    widget->style()->polish(widget);
    widget->update();
}
inline void state(QWidget* widget, const QString& value) {
    if (widget->property("status").toString() != value) {
        widget->setProperty("status", value);
        repolish(widget);
    }
}
inline QColor stateColor(const QString& state) {
    return state == "success" ? success : state == "warning" ? warning
           : state == "danger" ? danger : state == "info" ? accent : secondary;
}
inline QString statusSymbol(const QString& state) {
    return state == "success" ? QStringLiteral("✓") : state == "warning" ? QStringLiteral("!")
           : state == "danger" ? QStringLiteral("×") : state == "info" ? QStringLiteral("i")
                                                                          : QStringLiteral("−");
}
inline void paintStatus(QPainter& painter, const QRectF& rect, const QString& state) {
    painter.save();
    painter.setRenderHint(QPainter::Antialiasing);
    painter.setPen(QPen(stateColor(state), 1.4));
    painter.setBrush(Qt::NoBrush);
    painter.drawEllipse(rect.adjusted(1, 1, -1, -1));
    auto f = font(Type::Caption, true);
    f.setPixelSize(std::max(10, qRound(rect.height() - 3)));
    painter.setFont(f);
    painter.drawText(rect, Qt::AlignCenter, statusSymbol(state));
    painter.restore();
}
inline QIcon icon(int index) {
    static const QStringList paths{
        "<rect x='3' y='3' width='18' height='18' rx='2'/><path d='M8 16v-5m4 5V7m4 9v-3'/>",
        "<path d='M3 12h4l3-8 4 16 3-8h4'/>",
        "<circle cx='12' cy='12' r='9'/><path d='M12 7v5l3 2'/>",
        "<path d='m5 17 4-4m-5 7 3-1-2-2-1 3Z'/><path d='M9 13c-1-5 4-9 11-9 0 7-4 12-9 11Z'/><circle cx='15' cy='9' r='1'/>",
        "<circle cx='9' cy='8' r='3'/><path d='M3 21v-3a6 6 0 0 1 12 0v3m3-16a3 3 0 0 1 0 6m2 10v-3a6 6 0 0 0-3-5'/>",
        "<rect x='3' y='4' width='18' height='16' rx='2'/><path d='M7 8h10M7 12h10M7 16h10'/>",
        "<path d='m14 3-1 4 4 4 4-1a7 7 0 0 1-9 9l-6 3-4-4 7-6a7 7 0 0 1 5-9Z'/>",
        "<circle cx='12' cy='12' r='3'/><path d='m9 3-1 3-3 1-2 4 2 2v4l4 2 3-1 3 1 4-2v-4l2-2-2-4-3-1-1-3Z'/>",
        "<circle cx='10.5' cy='10.5' r='6.5'/><path d='m16 16 5 5'/>",
        "<path d='M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4'/>"};
    const auto svg = QStringLiteral(
        "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24' fill='none' "
        "stroke='%1' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'>%2</svg>")
                         .arg(text.name(), paths.value(index));
    QPixmap image(24, 24);
    image.fill(Qt::transparent);
    QPainter painter(&image);
    QSvgRenderer renderer(svg.toUtf8());
    renderer.render(&painter);
    return QIcon(image);
}
class ComboBox final : public QComboBox {
  public:
    using QComboBox::QComboBox;
  protected:
    void paintEvent(QPaintEvent* event) override {
        QComboBox::paintEvent(event);
        QStyleOptionComboBox option;
        initStyleOption(&option);
        const auto arrow = style()->subControlRect(QStyle::CC_ComboBox, &option, QStyle::SC_ComboBoxArrow, this);
        QPainter painter(this);
        painter.setRenderHint(QPainter::Antialiasing);
        painter.setPen(QPen(isEnabled() ? text : disabledText, 1.7, Qt::SolidLine, Qt::RoundCap, Qt::RoundJoin));
        const QPointF center = arrow.center();
        painter.drawPolyline(QPolygonF{center + QPointF(-4, -2), center + QPointF(0, 2), center + QPointF(4, -2)});
    }
};
class NavigationButton final : public QPushButton {
  public:
    using QPushButton::QPushButton;
  protected:
    void paintEvent(QPaintEvent* event) override {
        QPushButton::paintEvent(event);
        if (isChecked()) {
            QPainter painter(this);
            painter.setRenderHint(QPainter::Antialiasing);
            painter.setPen(Qt::NoPen);
            painter.setBrush(accent);
            painter.drawRoundedRect(QRectF(0, (height() - indicatorHeight) / 2., indicatorWidth,
                                           indicatorHeight), 1.5, 1.5);
        }
    }
};
inline QVBoxLayout* dialogLayout(QDialog* dialog) {
    dialog->setProperty("fluentDialog", true);
    if (auto screen = dialog->screen())
        dialog->setMaximumHeight(std::max(240, screen->availableGeometry().height() - dialogClearance));
    const QPointer<QWidget> restore = QApplication::focusWidget();
    QObject::connect(dialog, &QDialog::finished, dialog, [restore] {
        if (restore && restore->isVisible() && restore->isEnabled())
            restore->setFocus(Qt::OtherFocusReason);
    });
    auto layout = new QVBoxLayout(dialog);
    layout->setContentsMargins(space6, space6, space6, space4);
    layout->setSpacing(space4);
    auto title = new QLabel(dialog->windowTitle(), dialog);
    title->setWordWrap(true);
    type(title, Type::Section, true);
    layout->addWidget(title);
    return layout;
}
inline void apply(QApplication& application) {
    application.setStyle(QStringLiteral("Fusion"));
    QPalette p;
    p.setColor(QPalette::Window, app);
    p.setColor(QPalette::WindowText, text);
    p.setColor(QPalette::Base, surface);
    p.setColor(QPalette::AlternateBase, surfaceSubtle);
    p.setColor(QPalette::Text, text);
    p.setColor(QPalette::Button, surface);
    p.setColor(QPalette::ButtonText, text);
    p.setColor(QPalette::Highlight, selection);
    p.setColor(QPalette::HighlightedText, text);
    p.setColor(QPalette::ToolTipBase, surface);
    p.setColor(QPalette::ToolTipText, text);
    p.setColor(QPalette::PlaceholderText, secondary);
    p.setColor(QPalette::Disabled, QPalette::Text, disabledText);
    p.setColor(QPalette::Disabled, QPalette::ButtonText, disabledText);
    application.setPalette(p);
    QFont base;
    base.setFamilies({"Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI"});
    base.setPixelSize(static_cast<int>(Type::Body));
    application.setFont(base);
    // Focus compensation keeps border + padding constant in every state.
    QString sheet = QStringLiteral(R"(
      QWidget { color:@text; }
      QMainWindow,QDialog { background:@app; }
      QWidget#contentSurface { background:@content; border:1px solid @strokeSubtle; border-radius:8px; }
      QWidget[settingsGroup="true"],QWidget#settingsContent,QWidget#settingsPage { background:transparent; }
      QFrame[settingsCard="true"] { background:@surface; border:1px solid @strokeSubtle; border-radius:8px; }
      QPushButton { background:@surface; border:1px solid @strokeControl; border-radius:4px; padding:6px 14px; }
      QPushButton:hover { background:@hover; } QPushButton:pressed { background:@pressed; }
      QPushButton:focus { border:2px solid @focus; padding:5px 13px; }
      QPushButton:disabled { color:@disabledText; background:@surfaceSubtle; border-color:@strokeSubtle; }
      QPushButton[primary="true"] { background:@accent; border-color:@accent; color:@surface; }
      QPushButton[primary="true"]:hover { background:@accentHover; border-color:@accentHover; }
      QPushButton[primary="true"]:pressed { background:@accentPressed; border-color:@accentPressed; }
      QPushButton[primary="true"]:focus { border-color:@text; }
      QPushButton[primary="true"]:disabled { background:@surfaceSubtle; color:@disabledText; border-color:@strokeSubtle; }
      QPushButton[danger="true"] { color:@danger; }
      QPushButton[primary="true"][danger="true"] { color:@surface; background:@danger; border-color:@danger; }
      QPushButton[primary="true"][danger="true"]:hover { background:@dangerHover; border-color:@dangerHover; }
      QPushButton[primary="true"][danger="true"]:pressed { background:@dangerPressed; border-color:@dangerPressed; }
      QPushButton[primary="true"][danger="true"]:focus { border-color:@text; }
      QPushButton[primary="true"][danger="true"]:disabled { background:@surfaceSubtle; color:@disabledText; border-color:@strokeSubtle; }
      QPushButton[command="true"] { background:transparent; border-color:transparent; }
      QPushButton[command="true"]:hover { background:@hover; } QPushButton[command="true"]:pressed { background:@pressed; }
      QPushButton[command="true"]:focus { border-color:@focus; }
      QPushButton[nav="true"] { background:transparent; color:@text; border:2px solid transparent; text-align:left; padding:0 12px; border-radius:4px; }
      QPushButton[nav="true"]:hover,QPushButton[nav="true"]:checked { background:@hover; color:@text; }
      QPushButton[nav="true"]:pressed { background:@pressed; }
      QPushButton[nav="true"]:focus { border-color:@focus; padding:0 12px; }
      QPushButton[nav="true"][collapsed="true"] { padding:0; text-align:center; }
      QLineEdit,QComboBox,QSpinBox { background:@surface; border:1px solid @strokeControl; border-bottom-color:@border; border-radius:4px; padding:4px 8px; min-height:20px; selection-background-color:@accent; selection-color:@surface; }
      QLineEdit:hover,QComboBox:hover,QSpinBox:hover { background:@content; }
      QLineEdit:focus,QComboBox:focus,QSpinBox:focus { border:2px solid @focus; padding:3px 7px; }
      QLineEdit:disabled,QComboBox:disabled,QSpinBox:disabled { background:@surfaceSubtle; color:@disabledText; border-color:@strokeSubtle; }
      QComboBox::drop-down { border:0; width:24px; }
      QComboBox::down-arrow { image:none; }
      QComboBox QAbstractItemView { border:1px solid @strokeControl; background:@surface; selection-background-color:@selection; selection-color:@text; }
      QTreeView { background:@surface; border:2px solid @surface; border-radius:8px; alternate-background-color:@surface; }
      QTreeView:focus { border-color:@focus; }
      QTreeView::item:selected { background:@selection; color:@text; } QTreeView::item:hover { background:@hover; }
      QTreeView::item:selected:hover { background:@selectionHover; color:@text; }
      QHeaderView { background:@surfaceSubtle; }
      QHeaderView::section { background:@surfaceSubtle; color:@text; border:0; border-bottom:1px solid @strokeSubtle; padding:0 12px; font-weight:600; }
      QHeaderView::section:hover { background:@hover; }
      QListWidget { background:transparent; border:2px solid transparent; border-radius:4px; padding:0; }
      QListWidget:focus { border-color:@focus; }
      QMenu { background:@surface; border:1px solid @strokeControl; border-radius:8px; padding:4px; }
      QMenu::item { padding:6px 24px; border-radius:4px; } QMenu::item:selected { background:@hover; color:@text; }
      QMenu::item:disabled { color:@disabledText; } QMenu::separator { height:1px; background:@strokeSubtle; margin:4px 8px; }
      QToolTip { color:@text; background:@surface; border:1px solid @strokeControl; padding:8px; }
      QScrollArea { border:0; background:transparent; } QScrollArea>QWidget>QWidget { background:transparent; }
      QScrollBar:vertical { background:transparent; width:12px; margin:2px; }
      QScrollBar::handle:vertical { background:@border; min-height:32px; border-radius:4px; margin:2px; }
      QScrollBar::add-line:vertical,QScrollBar::sub-line:vertical { height:0; }
      QScrollBar::add-page:vertical,QScrollBar::sub-page:vertical { background:transparent; }
      QScrollBar:horizontal { background:transparent; height:12px; margin:2px; }
      QScrollBar::handle:horizontal { background:@border; min-width:32px; border-radius:4px; margin:2px; }
      QScrollBar::add-line:horizontal,QScrollBar::sub-line:horizontal { width:0; }
      QScrollBar::add-page:horizontal,QScrollBar::sub-page:horizontal { background:transparent; }
      QCheckBox { spacing:8px; min-height:28px; padding:2px; border:2px solid transparent; border-radius:4px; }
      QCheckBox:focus { border-color:@focus; }
      QCheckBox::indicator { width:18px; height:18px; }
      QLabel[secondary="true"] { color:@secondary; }
      QLabel[notice="true"] { background:@infoSurface; color:@text; padding:12px; border:1px solid @strokeSubtle; border-radius:4px; }
      QLabel[notice="true"][status="danger"] { background:@dangerSurface; color:@danger; }
      QLabel[notice="true"][status="success"] { background:@successSurface; color:@text; }
      QLabel[notice="true"][status="warning"] { background:@warningSurface; color:@text; }
      QLabel[fieldError="true"] { color:@danger; }
      QLabel[status="danger"] { color:@danger; } QLabel[status="warning"] { color:@warning; }
      QLabel[status="success"] { color:@success; }
      QPlainTextEdit,QTextEdit { background:@surface; border:1px solid @strokeSubtle; border-radius:4px; padding:12px; selection-background-color:@selection; selection-color:@text; }
      QPlainTextEdit:focus,QTextEdit:focus { border:2px solid @focus; padding:11px; }
      QTabWidget::pane { background:@surface; border:1px solid @strokeSubtle; border-radius:8px; }
      QTabBar::tab { background:transparent; padding:10px 16px; border:2px solid transparent; border-bottom:2px solid transparent; }
      QTabBar::tab:hover { background:@hover; } QTabBar::tab:selected { border-bottom-color:@accent; }
      QTabBar::tab:focus { border-color:@focus; }
      QMessageBox { background:@content; } QMessageBox QLabel { min-width:320px; }
      QMessageBox QPushButton { min-height:30px; min-width:72px; }
    )");
    const QList<QPair<QString, QColor>> roles{
        {"dangerPressed", dangerPressed}, {"dangerHover", dangerHover},
        {"surfaceSubtle", surfaceSubtle}, {"strokeSubtle", strokeSubtle}, {"strokeControl", strokeControl},
        {"disabledText", disabledText}, {"accentPressed", accentPressed}, {"accentHover", accentHover},
        {"dangerSurface", dangerSurface}, {"warningSurface", warningSurface}, {"successSurface", successSurface},
        {"infoSurface", infoSurface}, {"selectionHover", selectionHover}, {"selection", selection}, {"surface", surface}, {"content", content},
        {"secondary", secondary}, {"warning", warning}, {"success", success}, {"accent", accent},
        {"border", border}, {"focus", focus}, {"danger", danger}, {"pressed", pressed}, {"hover", hover},
        {"text", text}, {"app", app}};
    for (const auto& role : roles)
        sheet.replace("@" + role.first, role.second.name());
    application.setStyleSheet(sheet);
}
} // namespace Theme
