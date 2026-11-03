#pragma once
#include <QColor>
#include <QProxyStyle>

namespace Theme {
void paintChevron(QPainter* painter, const QRect& bounds, const QColor& color, Qt::ArrowType direction);
// Complete the small native primitives that QSS does not theme. Qt continues
// to own control semantics, keyboard handling, hit testing and accessibility.
class FluentStyle final : public QProxyStyle {
  public:
    FluentStyle();
    void drawPrimitive(PrimitiveElement element, const QStyleOption* option, QPainter* painter,
                       const QWidget* widget = nullptr) const override;
};
} // namespace Theme
