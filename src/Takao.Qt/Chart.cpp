#include "Chart.h"
#include <QDateTime>
#include <QPainterPath>
#include <algorithm>
#include <cmath>

Chart::Chart(QWidget* parent) : QWidget(parent) {
    setMinimumHeight(Theme::chartMinHeight);
    setFocusPolicy(Qt::StrongFocus);
    setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Expanding);
}
void Chart::mouseDoubleClickEvent(QMouseEvent*) {
    emit activated();
}
void Chart::setData(QString title, QString unit, QVector<Series> series, bool percent,
                    double intervalSeconds) {
    title_ = std::move(title);
    unit_ = std::move(unit);
    series_ = std::move(series);
    percent_ = percent;
    interval_ = intervalSeconds;
    QStringList values;
    for (const auto& s : series_)
        if (!s.points.isEmpty())
            values << s.label + " " +
                          (std::isfinite(s.points.last().y())
                               ? QString::number(s.points.last().y(), 'f', 1) + " " + unit_
                               : QStringLiteral("不可用"));
    setAccessibleName(title_ + "，" + values.join("，"));
    setToolTip(accessibleName() + "。仅绘制实际采样，缺失处保留间断。暂停按钮可停止刷新。");
    update();
}
void Chart::paintEvent(QPaintEvent*) {
    QPainter p(this);
    p.setRenderHint(QPainter::Antialiasing);
    p.fillRect(rect(), Theme::surface);
    p.setFont(Theme::font(Theme::Type::Body, true));
    const auto titleHeight = p.fontMetrics().height();
    p.setPen(Theme::text);
    p.drawText(QRect(Theme::space4, Theme::space2, width() - Theme::space8, titleHeight),
                Qt::AlignLeft | Qt::AlignVCenter, title_);
    p.setFont(Theme::font(Theme::Type::Caption));
    const auto captionHeight = p.fontMetrics().height();
    const auto legendTop = Theme::space2 + titleHeight + Theme::space2;
    const auto plotTop = legendTop + captionHeight + Theme::space3;
    const QRectF plot(Theme::space4, plotTop, std::max(10, width() - Theme::space8),
                       std::max(10, height() - plotTop - captionHeight - Theme::space3));
    p.setPen(QPen(Theme::grid, 1));
    for (int i = 0; i <= 4; ++i) {
        const auto y = plot.top() + plot.height() * i / 4;
        p.drawLine(QPointF(plot.left(), y), QPointF(plot.right(), y));
    }
    for (int i = 0; i <= 10; ++i) {
        const auto x = plot.left() + plot.width() * i / 10;
        p.drawLine(QPointF(x, plot.top()), QPointF(x, plot.bottom()));
    }
    p.setPen(QPen(Theme::border, 1));
    p.drawRect(plot);
    double first = std::numeric_limits<double>::max(), last = -first, maximum = percent_ ? 100. : 1.;
    bool any = false;
    for (const auto& s : series_)
        for (const auto& point : s.points) {
            first = std::min(first, point.x());
            last = std::max(last, point.x());
            if (std::isfinite(point.y())) {
                maximum = std::max(maximum, point.y());
                any = true;
            }
        }
    if (!percent_)
        maximum *= 1.12;
    if (first == std::numeric_limits<double>::max()) {
        first = 0;
        last = 1;
    }
    const auto span = std::max(interval_, last - first);
    p.setPen(Theme::secondary);
    const auto maxLabel = percent_ ? QStringLiteral("100 %") : QString::number(maximum, 'g', 3) + " " + unit_;
    p.drawText(QRectF(plot.left(), legendTop, plot.width(), captionHeight), Qt::AlignRight, maxLabel);
    int legendX = Theme::space4;
    for (const auto& s : series_) {
        p.setPen(QPen(s.color, Theme::chartLineWidth, s.style));
        p.drawLine(legendX, legendTop + captionHeight / 2, legendX + Theme::space6, legendTop + captionHeight / 2);
        p.setPen(Theme::secondary);
        p.drawText(QRect(legendX + Theme::space8, legendTop,
                          p.fontMetrics().horizontalAdvance(s.label), captionHeight), Qt::AlignVCenter, s.label);
        legendX += Theme::space8 + p.fontMetrics().horizontalAdvance(s.label) + Theme::space4;
    }
    p.save();
    p.setClipRect(plot.adjusted(-1, -1, 1, 1));
    for (const auto& s : series_) {
        QPainterPath path;
        bool moving = true;
        double previous = 0;
        QPointF finalPoint;
        for (const auto& point : s.points) {
            if (!std::isfinite(point.y())) {
                moving = true;
                continue;
            }
            QPointF screen(plot.left() + (point.x() - first) / span * plot.width(),
                           plot.bottom() - std::clamp(point.y() / maximum, 0., 1.) * plot.height());
            if (moving || point.x() - previous > interval_ * 2.5)
                path.moveTo(screen);
            else
                path.lineTo(screen);
            moving = false;
            previous = point.x();
            finalPoint = screen;
        }
        p.setPen(QPen(s.color, Theme::chartLineWidth, s.style));
        p.drawPath(path);
        if (!moving) {
            p.setBrush(s.color);
            p.drawEllipse(finalPoint, 2, 2);
            p.setBrush(Qt::NoBrush);
        }
    }
    p.restore();
    p.setPen(Theme::secondary);
    p.drawText(QRectF(plot.left(), plot.bottom() + Theme::space1, plot.width(), captionHeight), Qt::AlignLeft,
               QString("最近 %1 秒 · 实际采样").arg(int(std::max(0., last - first))));
    if (!any) {
        p.setPen(Theme::secondary);
        p.drawText(plot, Qt::AlignCenter, QStringLiteral("等待有效采样 · 未知值不会转换为 0"));
    }
    if (hasFocus()) {
        p.setPen(QPen(Theme::focus, Theme::focusWidth));
        p.drawRoundedRect(rect().adjusted(1, 1, -2, -2), Theme::controlRadius, Theme::controlRadius);
    }
}
