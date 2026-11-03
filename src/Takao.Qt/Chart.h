#pragma once
#include "Theme.h"
#include <QPointF>
#include <QVector>
#include <QWidget>
struct Series {
    QString label;
    QVector<QPointF> points;
    QColor color = Theme::accent;
    Qt::PenStyle style = Qt::SolidLine;
};
class Chart final : public QWidget {
    Q_OBJECT
  public:
    explicit Chart(QWidget* parent = nullptr);
    void setData(QString title, QString unit, QVector<Series> series, bool percent, double intervalSeconds);
  signals:
    void activated();

  protected:
    void paintEvent(QPaintEvent*) override;
    void mouseDoubleClickEvent(QMouseEvent*) override;

  private:
    QString title_, unit_;
    QVector<Series> series_;
    bool percent_ = true;
    double interval_ = 1;
};
