#pragma once
#include <QString>
class QWidget;

namespace Theme {
bool confirm(QWidget* parent, const QString& title, const QString& message,
             const QString& action, bool destructive = false);
void alert(QWidget* parent, const QString& title, const QString& message);
} // namespace Theme
