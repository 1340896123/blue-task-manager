#include "FluentStyle.h"
#include "Theme.h"
#include <QStyleFactory>
#include <QStyleOptionHeader>

namespace Theme {
void paintChevron(QPainter* painter, const QRect& bounds, const QColor& color, Qt::ArrowType direction) {
    painter->save();
    painter->setRenderHint(QPainter::Antialiasing);
    painter->setPen(QPen(color, 1.6, Qt::SolidLine, Qt::RoundCap, Qt::RoundJoin));
    painter->setBrush(Qt::NoBrush);
    painter->translate(QRectF(bounds).center());
    switch (direction) {
    case Qt::UpArrow: painter->rotate(180); break;
    case Qt::LeftArrow: painter->rotate(90); break;
    case Qt::RightArrow: painter->rotate(-90); break;
    default: break;
    }
    const auto half = std::min(4., std::max(2., std::min(bounds.width(), bounds.height()) / 3.));
    painter->drawPolyline(QPolygonF{QPointF(-half, -half / 2), QPointF(0, half / 2),
                                   QPointF(half, -half / 2)});
    painter->restore();
}
} // namespace Theme
namespace {
void checkMark(QPainter* painter, const QRectF& bounds, const QColor& color, bool partial) {
    painter->save();
    painter->setRenderHint(QPainter::Antialiasing);
    painter->setPen(QPen(color, 1.8, Qt::SolidLine, Qt::RoundCap, Qt::RoundJoin));
    painter->setBrush(Qt::NoBrush);
    const auto point = [&](qreal x, qreal y) {
        return QPointF(bounds.left() + x * bounds.width(), bounds.top() + y * bounds.height());
    };
    if (partial)
        painter->drawLine(point(.25, .5), point(.75, .5));
    else
        painter->drawPolyline(QPolygonF{point(.23, .51), point(.43, .71), point(.79, .30)});
    painter->restore();
}
} // namespace

namespace Theme {
FluentStyle::FluentStyle() : QProxyStyle(QStyleFactory::create(QStringLiteral("Fusion"))) {}

void FluentStyle::drawPrimitive(PrimitiveElement element, const QStyleOption* option, QPainter* painter,
                                const QWidget* widget) const {
    if (!option || !painter)
        return;
    const bool enabled = option->state.testFlag(State_Enabled);
    const auto glyph = enabled ? text : disabledText;
    switch (element) {
    case PE_IndicatorCheckBox: {
        const bool checked = option->state.testFlag(State_On);
        const bool partial = option->state.testFlag(State_NoChange);
        const bool active = checked || partial;
        const bool down = option->state.testFlag(State_Sunken);
        const bool over = option->state.testFlag(State_MouseOver);
        const auto fill = !enabled ? (active ? strokeControl : surfaceSubtle)
                                  : active ? (down ? accentPressed : over ? accentHover : accent)
                                           : (down ? pressed : over ? content : surface);
        const auto outline = !enabled ? strokeControl : active ? fill : border;
        const QRectF bounds = QRectF(option->rect).adjusted(1, 1, -1, -1);
        painter->save();
        painter->setRenderHint(QPainter::Antialiasing);
        painter->setPen(QPen(outline, 1.));
        painter->setBrush(fill);
        painter->drawRoundedRect(bounds, controlRadius, controlRadius);
        painter->restore();
        if (active)
            checkMark(painter, bounds, enabled ? surface : disabledText, partial);
        return;
    }
    case PE_IndicatorMenuCheckMark:
        checkMark(painter, option->rect, glyph, option->state.testFlag(State_NoChange));
        return;
    case PE_IndicatorArrowUp:
    case PE_IndicatorSpinUp:
        paintChevron(painter, option->rect, glyph, Qt::UpArrow);
        return;
    case PE_IndicatorArrowDown:
    case PE_IndicatorSpinDown:
        paintChevron(painter, option->rect, glyph, Qt::DownArrow);
        return;
    case PE_IndicatorArrowLeft:
        paintChevron(painter, option->rect, glyph, Qt::LeftArrow);
        return;
    case PE_IndicatorArrowRight:
        paintChevron(painter, option->rect, glyph, Qt::RightArrow);
        return;
    case PE_IndicatorHeaderArrow:
        // On Windows Qt's SortDown denotes the ascending (upward) glyph.
        // Keep QHeaderView's actual ordering rather than inferring it from
        // State_UpArrow, which the header option does not populate.
        if (const auto header = qstyleoption_cast<const QStyleOptionHeader*>(option)) {
            if (header->sortIndicator != QStyleOptionHeader::None)
                paintChevron(painter, option->rect, glyph,
                        header->sortIndicator == QStyleOptionHeader::SortDown ? Qt::UpArrow : Qt::DownArrow);
        } else {
            paintChevron(painter, option->rect, glyph,
                    option->state.testFlag(State_UpArrow) ? Qt::UpArrow : Qt::DownArrow);
        }
        return;
    case PE_IndicatorBranch:
        if (option->state.testFlag(State_Children))
            paintChevron(painter, option->rect, glyph,
                    option->state.testFlag(State_Open) ? Qt::DownArrow
                    : option->direction == Qt::RightToLeft ? Qt::LeftArrow : Qt::RightArrow);
        return;
    default:
        QProxyStyle::drawPrimitive(element, option, painter, widget);
    }
}
} // namespace Theme
