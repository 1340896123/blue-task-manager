#include "WindowPicker.h"
#include "Theme.h"
#include <QApplication>
#include <QKeyEvent>
#include <QMouseEvent>
#include <QToolTip>

#ifdef Q_OS_WIN
#include <windows.h>
#endif

WindowPickerButton::WindowPickerButton(QWidget* parent) : QPushButton(parent) {
    setObjectName("windowPicker");
    setFixedSize(52, Theme::searchHeight);
    setIcon(Theme::icon(10));
    setIconSize(QSize(Theme::iconSize, Theme::iconSize));
    setFocusPolicy(Qt::StrongFocus);
    setCursor(Qt::OpenHandCursor);
    setAccessibleName(QStringLiteral("拖动窗口定位"));
    setAccessibleDescription(QStringLiteral("拖动此图标到其他应用窗口，松开后按该窗口的 PID 定位进程。"));
    setToolTip(QStringLiteral("拖动到窗口以按 PID 定位；也可以直接在搜索框输入 PID。"));
}

void WindowPickerButton::setDragging(bool dragging) {
    if (dragging_ == dragging)
        return;
    dragging_ = dragging;
    setProperty("dragging", dragging_);
    setCursor(dragging_ ? Qt::CrossCursor : Qt::OpenHandCursor);
    if (dragging_) {
        setAccessibleDescription(QStringLiteral("正在拾取窗口；将准星移到目标应用窗口后松开鼠标。按 Esc 取消。"));
        setToolTip(QStringLiteral("正在拾取窗口；松开鼠标以按 PID 定位，Esc 取消。"));
    } else {
        setAccessibleDescription(QStringLiteral("拖动此图标到其他应用窗口，松开后按该窗口的 PID 定位进程。"));
        setToolTip(QStringLiteral("拖动到窗口以按 PID 定位；也可以直接在搜索框输入 PID。"));
    }
    Theme::repolish(this);
}

void WindowPickerButton::mousePressEvent(QMouseEvent* event) {
    if (event->button() != Qt::LeftButton) {
        QPushButton::mousePressEvent(event);
        return;
    }
    pressed_ = true;
    pressPosition_ = event->position().toPoint();
    setFocus(Qt::MouseFocusReason);
    event->accept();
}

void WindowPickerButton::mouseMoveEvent(QMouseEvent* event) {
    if (!pressed_ || dragging_ || !(event->buttons() & Qt::LeftButton)) {
        event->ignore();
        return;
    }
    if ((event->position().toPoint() - pressPosition_).manhattanLength() < QApplication::startDragDistance()) {
        event->accept();
        return;
    }
    setDragging(true);
    grabMouse(Qt::CrossCursor);
    event->accept();
}

qint64 WindowPickerButton::processIdAtCursor(QString* error) const {
#ifdef Q_OS_WIN
    POINT point{};
    if (!GetCursorPos(&point)) {
        if (error)
            *error = QStringLiteral("无法读取鼠标位置。请重试。");
        return 0;
    }
    HWND window = WindowFromPoint(point);
    if (!window) {
        if (error)
            *error = QStringLiteral("当前位置没有可识别的应用窗口。请将准星移到窗口内容后松开。 ");
        return 0;
    }
    HWND root = GetAncestor(window, GA_ROOTOWNER);
    if (!root)
        root = GetAncestor(window, GA_ROOT);
    if (!root)
        root = window;
    DWORD pid = 0;
    if (!GetWindowThreadProcessId(root, &pid) || pid == 0) {
        if (error)
            *error = QStringLiteral("无法读取目标窗口的进程 PID。请重试。 ");
        return 0;
    }
    if (ignoredProcessIds_.contains(static_cast<qint64>(pid))) {
        if (error)
            *error = QStringLiteral("不能选择 Takao 自身窗口，请拖动到其他应用。 ");
        return 0;
    }
    return static_cast<qint64>(pid);
#else
    Q_UNUSED(error);
    return 0;
#endif
}

void WindowPickerButton::mouseReleaseEvent(QMouseEvent* event) {
    if (event->button() != Qt::LeftButton) {
        QPushButton::mouseReleaseEvent(event);
        return;
    }
    const bool wasDragging = dragging_;
    pressed_ = false;
    if (dragging_)
        releaseMouse();
    setDragging(false);
    if (!wasDragging) {
        emit helpRequested();
        QToolTip::showText(mapToGlobal(rect().bottomLeft()),
                           QStringLiteral("拖动到窗口以按 PID 定位；也可以直接在搜索框输入 PID。"), this);
        event->accept();
        return;
    }
    QString error;
    const auto pid = processIdAtCursor(&error);
    if (pid > 0)
        emit pidPicked(pid);
    else
        emit selectionFailed(error.isEmpty() ? QStringLiteral("未能识别目标窗口，请重试。") : error.trimmed());
    event->accept();
}

void WindowPickerButton::keyPressEvent(QKeyEvent* event) {
    if (event->key() == Qt::Key_Space || event->key() == Qt::Key_Return || event->key() == Qt::Key_Enter) {
        emit helpRequested();
        QToolTip::showText(mapToGlobal(rect().bottomLeft()),
                           QStringLiteral("键盘无法移动窗口准星；请按住鼠标拖动图标到目标窗口，或直接输入 PID。"), this);
        event->accept();
        return;
    }
    if (event->key() == Qt::Key_Escape && dragging_) {
        pressed_ = false;
        releaseMouse();
        setDragging(false);
        emit selectionFailed(QStringLiteral("已取消窗口拾取。"));
        event->accept();
        return;
    }
    QPushButton::keyPressEvent(event);
}
