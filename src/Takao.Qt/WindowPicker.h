#pragma once

#include <QPoint>
#include <QPushButton>
#include <QSet>

class QKeyEvent;
class QMouseEvent;

// A small native-window picker used by the shared process search. The pointer
// can be dragged outside Takao; the released screen position is resolved to a
// top-level HWND and its owning PID on Windows.
class WindowPickerButton final : public QPushButton {
    Q_OBJECT
  public:
    explicit WindowPickerButton(QWidget* parent = nullptr);

    void setIgnoredProcessIds(const QSet<qint64>& processIds) { ignoredProcessIds_ = processIds; }

  signals:
    void pidPicked(qint64 pid);
    void selectionFailed(const QString& message);
    void helpRequested();

  protected:
    void mousePressEvent(QMouseEvent*) override;
    void mouseMoveEvent(QMouseEvent*) override;
    void mouseReleaseEvent(QMouseEvent*) override;
    void keyPressEvent(QKeyEvent*) override;

  private:
    void setDragging(bool dragging);
    qint64 processIdAtCursor(QString* error) const;

    QPoint pressPosition_;
    QSet<qint64> ignoredProcessIds_;
    bool pressed_ = false;
    bool dragging_ = false;
};
