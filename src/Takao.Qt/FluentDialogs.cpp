#include "FluentDialogs.h"
#include "Theme.h"
#include <QHBoxLayout>
#include <QScrollArea>

namespace {
void messageBody(QDialog& dialog, QVBoxLayout* layout, const QString& message) {
    auto scroll = new QScrollArea(&dialog);
    scroll->setWidgetResizable(true);
    scroll->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    auto body = new QLabel(message, scroll);
    body->setTextFormat(Qt::PlainText);
    body->setWordWrap(true);
    body->setTextInteractionFlags(Qt::TextSelectableByMouse | Qt::TextSelectableByKeyboard);
    body->setAlignment(Qt::AlignLeft | Qt::AlignTop);
    Theme::type(body, Theme::Type::BodyLarge);
    scroll->setWidget(body);
    layout->addWidget(scroll, 1);
}
QPushButton* actionButton(const QString& text, QDialog& dialog, bool primary) {
    auto result = new QPushButton(text, &dialog);
    result->setAccessibleName(text);
    result->setMinimumHeight(Theme::commandHeight);
    result->setMinimumWidth(96);
    result->setAutoDefault(false);
    result->setProperty("primary", primary);
    return result;
}
} // namespace

namespace Theme {
bool confirm(QWidget* parent, const QString& title, const QString& message,
             const QString& action, bool destructive) {
    QDialog dialog(parent);
    dialog.setObjectName("fluentConfirmation");
    dialog.setWindowTitle(title);
    dialog.setModal(true);
    dialog.resize(dialogWidth, 280);
    auto layout = dialogLayout(&dialog);
    messageBody(dialog, layout, message);
    auto buttons = new QHBoxLayout();
    buttons->setSpacing(space2);
    buttons->addStretch();
    auto cancel = actionButton(QStringLiteral("取消"), dialog, false);
    auto accept = actionButton(action, dialog, true);
    accept->setProperty("danger", destructive);
    cancel->setDefault(true);
    buttons->addWidget(cancel);
    buttons->addWidget(accept);
    layout->addLayout(buttons);
    QObject::connect(cancel, &QPushButton::clicked, &dialog, &QDialog::reject);
    QObject::connect(accept, &QPushButton::clicked, &dialog, &QDialog::accept);
    cancel->setFocus(Qt::OtherFocusReason);
    return dialog.exec() == QDialog::Accepted;
}
void alert(QWidget* parent, const QString& title, const QString& message) {
    QDialog dialog(parent);
    dialog.setObjectName("fluentAlert");
    dialog.setWindowTitle(title);
    dialog.resize(dialogWidth, 260);
    auto layout = dialogLayout(&dialog);
    messageBody(dialog, layout, message);
    auto close = actionButton(QStringLiteral("关闭"), dialog, true);
    close->setDefault(true);
    layout->addWidget(close, 0, Qt::AlignRight);
    QObject::connect(close, &QPushButton::clicked, &dialog, &QDialog::accept);
    close->setFocus(Qt::OtherFocusReason);
    dialog.exec();
}
} // namespace Theme
