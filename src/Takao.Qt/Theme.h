#pragma once
#include <QApplication>
#include <QIcon>
#include <QPainter>
#include <QPalette>
#include <QSvgRenderer>

namespace Theme {
inline const QColor surface(QStringLiteral("#FFFFFF")), app(QStringLiteral("#F3F3F3")),
    text(QStringLiteral("#1A1A1A")), secondary(QStringLiteral("#5F5F5F"));
inline const QColor accent(QStringLiteral("#0067C0")), focus(QStringLiteral("#005FB8")),
    selection(QStringLiteral("#E8EEF5")), border(QStringLiteral("#8A8A8A"));
inline const QColor grid(QStringLiteral("#ECEFF2")), secondLine(QStringLiteral("#58636F")),
    danger(QStringLiteral("#A4262C")), warning(QStringLiteral("#8A4B00")), success(QStringLiteral("#107C10"));
inline QIcon icon(int index) {
    // Unified 24px outline language using simple Lucide-style SVG primitives.
    static const QStringList paths{
        QStringLiteral("<rect x='3' y='3' width='18' height='18' rx='2'/><path d='M8 16v-5m4 5V7m4 9v-3'/>"),
        QStringLiteral("<path d='M3 12h4l3-8 4 16 3-8h4'/>"),
        QStringLiteral("<circle cx='12' cy='12' r='9'/><path d='M12 7v5l3 2'/>"),
        QStringLiteral("<path d='m5 17 4-4m-5 7 3-1-2-2-1 3Z'/><path d='M9 13c-1-5 4-9 11-9 0 7-4 12-9 "
                       "11Z'/><circle cx='15' cy='9' r='1'/>"),
        QStringLiteral("<circle cx='9' cy='8' r='3'/><path d='M3 21v-3a6 6 0 0 1 12 0v3m3-16a3 3 0 0 1 0 6m2 "
                       "10v-3a6 6 0 0 0-3-5'/>"),
        QStringLiteral(
            "<rect x='3' y='4' width='18' height='16' rx='2'/><path d='M7 8h10M7 12h10M7 16h10'/>"),
        QStringLiteral("<path d='m14 3-1 4 4 4 4-1a7 7 0 0 1-9 9l-6 3-4-4 7-6a7 7 0 0 1 5-9Z'/>"),
        QStringLiteral("<circle cx='12' cy='12' r='3'/><path d='m9 3-1 3-3 1-2 4 2 2v4l4 2 3-1 3 1 "
                       "4-2v-4l2-2-2-4-3-1-1-3Z'/>")};
    const auto svg =
        QStringLiteral(
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
inline void apply(QApplication& application) {
    application.setStyle(QStringLiteral("Fusion"));
    QPalette p;
    p.setColor(QPalette::Window, app);
    p.setColor(QPalette::WindowText, text);
    p.setColor(QPalette::Base, surface);
    p.setColor(QPalette::AlternateBase, app);
    p.setColor(QPalette::Text, text);
    p.setColor(QPalette::Button, surface);
    p.setColor(QPalette::ButtonText, text);
    p.setColor(QPalette::Highlight, selection);
    p.setColor(QPalette::HighlightedText, text);
    p.setColor(QPalette::ToolTipBase, surface);
    p.setColor(QPalette::ToolTipText, text);
    p.setColor(QPalette::Disabled, QPalette::Text, secondary);
    p.setColor(QPalette::Disabled, QPalette::ButtonText, secondary);
    application.setPalette(p);
    QFont font(QStringLiteral("Microsoft YaHei UI"));
    font.setPixelSize(14);
    application.setFont(font);
    application.setStyleSheet(QStringLiteral(R"(
      QWidget { color:#1A1A1A; } QMainWindow,QDialog { background:#F3F3F3; }
      QPushButton { background:#FFFFFF; border:1px solid #8A8A8A; border-radius:4px; padding:6px 14px; min-height:28px; }
      QPushButton:hover { background:#E8EEF5; } QPushButton:pressed { background:#D9E8F7; }
      QPushButton:focus,QLineEdit:focus,QComboBox:focus,QSpinBox:focus,QCheckBox:focus,QTreeView:focus,QListWidget:focus { border:2px solid #005FB8; }
      QPushButton:disabled { color:#5F5F5F; background:#F3F3F3; border-color:#C6C6C6; }
      QPushButton[primary="true"] { background:#0067C0; border-color:#0067C0; color:#FFFFFF; }
      QPushButton[primary="true"]:hover { background:#005FB8; } QPushButton[primary="true"]:disabled { background:#E8EEF5; color:#5F5F5F; }
      QPushButton[danger="true"] { color:#A4262C; } QPushButton[nav="true"] { border:0; text-align:left; min-height:32px; padding-left:16px; }
      QPushButton[nav="true"]:checked { background:#E8EEF5; color:#0067C0; border-left:3px solid #0067C0; font-weight:600; }
      QLineEdit,QComboBox,QSpinBox { background:#FFFFFF; border:1px solid #8A8A8A; border-radius:4px; padding:6px 8px; min-height:22px; selection-background-color:#0067C0; selection-color:#FFFFFF; }
      QTreeView { background:#FFFFFF; border:0; alternate-background-color:#FAFBFC; gridline-color:#ECEFF2; }
      QTreeView::item:selected { background:#E8EEF5; color:#1A1A1A; } QTreeView::item:focus { border:1px solid #005FB8; }
      QHeaderView::section { background:#F3F3F3; color:#1A1A1A; border:0; border-right:1px solid #E0E0E0; border-bottom:1px solid #D5D5D5; padding:10px 8px; font-weight:600; }
      QMenu { background:#FFFFFF; border:1px solid #8A8A8A; padding:4px; } QMenu::item { padding:9px 28px; } QMenu::item:selected { background:#E8EEF5; color:#1A1A1A; }
      QGroupBox { border:1px solid #D5D5D5; border-radius:4px; margin-top:16px; padding-top:20px; font-size:18px; font-weight:600; background:#FFFFFF; }
      QGroupBox::title { subcontrol-origin:margin; left:16px; top:2px; }
      QScrollArea { border:0; } QCheckBox { spacing:8px; min-height:32px; }
      QLabel[title="true"] { font-size:24px; font-weight:600; } QLabel[secondary="true"] { color:#5F5F5F; }
      QLabel[metric="true"] { font-size:30px; font-weight:600; } QLabel[notice="true"] { background:#E8EEF5; padding:10px; border-left:3px solid #0067C0; }
      QTabWidget::pane { border:1px solid #D5D5D5; } QTabBar::tab { padding:10px 18px; }
    )"));
}
} // namespace Theme
