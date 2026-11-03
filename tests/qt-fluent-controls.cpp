#include "Theme.h"
#include <QAccessible>
#include <QCheckBox>
#include <QEventLoop>
#include <QElapsedTimer>
#include <QFile>
#include <QFormLayout>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QScrollBar>
#include <QSpinBox>
#include <QStyleOptionHeader>
#include <QStyleOptionSpinBox>
#include <QTimer>
#include <stdexcept>

namespace {
QJsonObject report;
QJsonArray checks;
QString output;
void save(const QString& phase) {
    report["phase"] = phase;
    report["checks"] = checks;
    QFile file(output);
    if (!file.open(QIODevice::WriteOnly | QIODevice::Truncate))
        throw std::runtime_error("Cannot save Fluent component results");
    file.write(QJsonDocument(report).toJson(QJsonDocument::Indented));
}
void check(bool condition, const QString& message) {
    if (!condition)
        throw std::runtime_error(message.toStdString());
    checks.append(message);
}
void hold(int milliseconds) {
    QEventLoop loop;
    QTimer::singleShot(milliseconds, &loop, &QEventLoop::quit);
    loop.exec();
}
void observe(const QString& phase) {
    save(phase);
    QElapsedTimer timer;
    timer.start();
    while (timer.elapsed() < 15000) {
        QFile acknowledgement(output + ".captured");
        if (acknowledgement.open(QIODevice::ReadOnly) &&
            QString::fromUtf8(acknowledgement.readAll()) == phase)
            return;
        hold(25);
    }
    throw std::runtime_error(("WGC did not capture phase: " + phase).toStdString());
}
int pixels(QWidget* widget, QColor color, const QRect& area = {}) {
    QImage frame(widget->size(), QImage::Format_ARGB32);
    frame.fill(Qt::transparent);
    widget->render(&frame);
    int count = 0;
    const auto bounds = area.isValid() ? area.intersected(frame.rect()) : frame.rect();
    for (int y = bounds.top(); y <= bounds.bottom(); ++y)
        for (int x = bounds.left(); x <= bounds.right(); ++x)
            count += frame.pixelColor(x, y).rgb() == color.rgb();
    return count;
}
class InspectSpin final : public Theme::SpinBox {
  public:
    using Theme::SpinBox::SpinBox;
    QStyleOptionSpinBox option() const {
        QStyleOptionSpinBox result;
        initStyleOption(&result);
        return result;
    }
};
QPushButton* findButton(QDialog* dialog, const QString& text) {
    for (auto button : dialog->findChildren<QPushButton*>())
        if (button->text().remove('&') == text)
            return button;
    return nullptr;
}
}

int main(int argc, char** argv) {
    QApplication app(argc, argv);
    Theme::apply(app);
    output = qEnvironmentVariable("TAKAO_FLUENT_RESULT");
    if (output.isEmpty())
        return 2;
    report = {{"result", "RUNNING"},
              {"scope", "Real Qt controls and rendering; no business fixtures, no physical input assertions"}};
    QWidget window;
    window.setWindowTitle("Takao Fluent 组件");
    window.resize(1920, 1080);
    auto layout = new QVBoxLayout(&window);
    layout->setContentsMargins(32, 24, 32, 24);
    auto title = new QLabel("当前 Qt 组件 · Fluent 状态审查", &window);
    Theme::type(title, Theme::Type::PageTitle, true);
    layout->addWidget(title);
    auto note = new QLabel("技术组件检查：复选框、数值步进框、确认框。此窗口不展示业务数据。", &window);
    layout->addWidget(note);
    auto form = new QFormLayout;
    form->setSpacing(16);
    layout->addLayout(form);
    auto unchecked = new QCheckBox("未选中", &window);
    auto checked = new QCheckBox("已选中", &window);
    checked->setChecked(true);
    auto partial = new QCheckBox("部分选中", &window);
    partial->setTristate(true);
    partial->setCheckState(Qt::PartiallyChecked);
    auto disabled = new QCheckBox("禁用且已选中", &window);
    disabled->setChecked(true);
    disabled->setEnabled(false);
    for (auto box : {unchecked, checked, partial, disabled})
        form->addRow("QCheckBox", box);
    auto spin = new InspectSpin(&window);
    spin->setRange(64, 1600);
    spin->setValue(320);
    spin->setSuffix(" px");
    spin->setFixedWidth(240);
    spin->setMinimumHeight(Theme::controlHeight);
    form->addRow("QSpinBox · 可编辑", spin);
    auto minimum = new InspectSpin(&window);
    minimum->setRange(64, 1600);
    minimum->setValue(64);
    minimum->setFixedWidth(240);
    minimum->setMinimumHeight(Theme::controlHeight);
    form->addRow("QSpinBox · 最小值", minimum);
    auto maximum = new InspectSpin(&window);
    maximum->setRange(64, 1600);
    maximum->setValue(1600);
    maximum->setFixedWidth(240);
    maximum->setMinimumHeight(Theme::controlHeight);
    form->addRow("QSpinBox · 最大值", maximum);
    auto combo = new Theme::ComboBox(&window);
    combo->setAccessibleName("采样间隔组件");
    combo->addItem("每 1 秒", 1000);
    combo->addItem("每 2 秒", 2000);
    combo->addItem("每 5 秒", 5000);
    combo->setFixedSize(240, Theme::controlHeight);
    form->addRow("QComboBox · 普通", combo);
    auto disabledCombo = new Theme::ComboBox(&window);
    disabledCombo->addItem("每 1 秒", 1000);
    disabledCombo->setEnabled(false);
    disabledCombo->setFixedSize(240, Theme::controlHeight);
    form->addRow("QComboBox · 禁用", disabledCombo);
    auto scrollingCombo = new Theme::ComboBox(&window);
    scrollingCombo->setAccessibleName("下拉列表滚动组件");
    for (int i = 1; i <= 20; ++i)
        scrollingCombo->addItem(QString("技术组件选项 %1").arg(i), i);
    scrollingCombo->setFixedSize(240, Theme::controlHeight);
    form->addRow("QComboBox · 滚动", scrollingCombo);
    auto trigger = new QPushButton("确认框发起控件", &window);
    trigger->setMinimumHeight(Theme::commandHeight);
    form->addRow("QPushButton · 焦点恢复", trigger);
    layout->addStretch();
    window.show();
    QTimer::singleShot(1800, &window, [&] {
        try {
            check(pixels(checked, Theme::accent) > 20 && pixels(partial, Theme::accent) > 20,
                  "Checked and partial indicators paint the actual Fluent accent");
            check(pixels(unchecked, Theme::accent) == 0 && pixels(disabled, Theme::accent) == 0,
                  "Unchecked and disabled controls do not retain active accent fill");
            auto accessible = QAccessible::queryAccessibleInterface(checked);
            check(accessible && accessible->role() == QAccessible::CheckBox && accessible->state().checked,
                  "Native checkbox exposes its accessible checked state");
            unchecked->click();
            check(unchecked->isChecked(), "Native checkbox activation toggles state");
            unchecked->click();
            disabled->click();
            check(disabled->isChecked(), "Disabled checkbox ignores activation");
            spin->stepUp();
            check(spin->value() == 321, "Native numeric stepping preserves the real value");
            spin->stepDown();
            minimum->stepDown(); maximum->stepUp();
            check(minimum->value() == 64 && maximum->value() == 1600,
                  "Numeric stepping respects both actual range boundaries");
            const auto option = spin->option();
            const auto up = spin->style()->subControlRect(QStyle::CC_SpinBox, &option, QStyle::SC_SpinBoxUp, spin);
            const auto down = spin->style()->subControlRect(QStyle::CC_SpinBox, &option, QStyle::SC_SpinBoxDown, spin);
            check(up.width() >= 20 && down.width() >= 20 && !up.intersects(down) &&
                      spin->rect().contains(up) && spin->rect().contains(down),
                  "Numeric buttons have separate visible native hit areas inside the field");
            QImage spinFrame(spin->size(), QImage::Format_ARGB32);
            spinFrame.fill(Qt::transparent);
            spin->render(&spinFrame);
            auto visibleGlyph = [&](const QRect& area) {
                int dark = 0;
                const auto inner = area.adjusted(4, 2, -4, -2);
                for (int y = inner.top(); y <= inner.bottom(); ++y)
                    for (int x = inner.left(); x <= inner.right(); ++x) {
                        const auto color = spinFrame.pixelColor(x, y);
                        dark += color.alpha() > 200 && color.red() < 100 &&
                                color.green() < 100 && color.blue() < 100;
                    }
                return dark > 2;
            };
            check(visibleGlyph(up) && visibleGlyph(down),
                  "Both numeric button glyphs remain visible through the actual QSS rendering path");
            check(!(minimum->option().stepEnabled & QAbstractSpinBox::StepDownEnabled) &&
                      !(maximum->option().stepEnabled & QAbstractSpinBox::StepUpEnabled),
                  "Native boundary availability reaches the style options");
            auto headerArrow = [&](QStyleOptionHeader::SortIndicator direction) {
                QImage frame(20, 20, QImage::Format_ARGB32);
                frame.fill(Qt::transparent);
                QPainter painter(&frame);
                QStyleOptionHeader header;
                header.rect = frame.rect();
                header.state = QStyle::State_Enabled;
                header.sortIndicator = direction;
                app.style()->drawPrimitive(QStyle::PE_IndicatorHeaderArrow, &header, &painter);
                return frame;
            };
            check(headerArrow(QStyleOptionHeader::SortUp) != headerArrow(QStyleOptionHeader::SortDown),
                  "Header glyph follows the actual sort indicator in both directions");
            auto comboAccessible = QAccessible::queryAccessibleInterface(combo);
            check(comboAccessible && comboAccessible->role() == QAccessible::ComboBox &&
                      comboAccessible->text(QAccessible::Name) == combo->accessibleName(),
                  "Dropdown retains the native accessible role and field name");
            trigger->setFocus();
            observe("01-controls");
            combo->showPopup(); hold(200);
            bool complete = combo->view()->isVisible() && combo->view()->width() >= combo->width();
            for (int i = 0; i < combo->count(); ++i) {
                const auto rect = combo->view()->visualRect(combo->model()->index(i, 0));
                complete = complete && rect.height() >= Theme::controlHeight &&
                           combo->view()->viewport()->rect().contains(rect);
            }
            check(complete, "Opened dropdown displays every real sample interval in complete full-width rows");
            combo->view()->setCurrentIndex(combo->model()->index(1, 0)); hold(100);
            check(pixels(combo->view()->viewport(), Theme::accent,
                         combo->view()->visualRect(combo->model()->index(0, 0))) > 5 &&
                      pixels(combo->view()->viewport(), Theme::accent,
                         combo->view()->visualRect(combo->model()->index(1, 0))) == 0,
                  "Dropdown selection marker follows the committed value when highlight moves");
            report["popupHwnd"] = QString::number(combo->view()->window()->winId());
            observe("05-dropdown"); combo->hidePopup(); report.remove("popupHwnd");
            check(combo->currentData().toInt() == 1000,
                  "Closing the dropdown leaves the actual current option unchanged");
            scrollingCombo->showPopup(); hold(200);
            check(scrollingCombo->view()->verticalScrollBar()->maximum() > 0 &&
                      scrollingCombo->view()->horizontalScrollBar()->maximum() == 0,
                  "Long dropdown has a single vertical scroll route without horizontal overflow");
            scrollingCombo->view()->scrollToBottom(); hold(150);
            check(scrollingCombo->view()->viewport()->rect().contains(
                      scrollingCombo->view()->visualRect(scrollingCombo->model()->index(19, 0))),
                  "The final dropdown option remains reachable by native scrolling");
            report["popupHwnd"] = QString::number(scrollingCombo->view()->window()->winId());
            observe("06-dropdown-scroll"); scrollingCombo->hidePopup(); report.remove("popupHwnd");
            trigger->setFocus();
            auto inspectDialog = [&](const QString& confirm, const QString& phase, bool accept) {
                QTimer::singleShot(150, &window, [&, confirm, phase, accept] {
                    auto dialog = qobject_cast<QDialog*>(QApplication::activeModalWidget());
                    try {
                        check(dialog && dialog->property("fluentDialog").toBool(),
                              phase + ": shared Fluent dialog is used");
                        auto cancel = findButton(dialog, "取消");
                        auto action = findButton(dialog, confirm);
                        check(cancel && action && cancel->isDefault() && dialog->focusWidget() == cancel &&
                                  action->minimumHeight() >= Theme::commandHeight,
                              phase + ": explicit Chinese action, 44px command and safe default focus");
                        observe(phase);
                        if (accept) action->click(); else dialog->reject();
                    } catch (const std::exception& error) {
                        report["dialogError"] = error.what();
                        if (dialog) dialog->reject();
                    }
                });
            };
            inspectDialog("清除统计", "02-confirm-cancel", false);
            check(!Theme::confirm(&window, "清除统计", "取消应保留当前统计。", "清除统计", true),
                  "Rejecting a shared confirmation returns false");
            if (report.contains("dialogError"))
                throw std::runtime_error(report.value("dialogError").toString().toStdString());
            check(window.focusWidget() == trigger, "Closing a confirmation restores its initiating focus");
            inspectDialog("继续", "03-confirm-accept", true);
            check(Theme::confirm(&window, "组件确认检查", "此处仅验证组件接受结果，不执行业务操作。", "继续"),
                  "Explicit confirmation returns true");
            if (report.contains("dialogError"))
                throw std::runtime_error(report.value("dialogError").toString().toStdString());
            QTimer::singleShot(150, &window, [&] {
                auto dialog = qobject_cast<QDialog*>(QApplication::activeModalWidget());
                try {
                    check(dialog && dialog->objectName() == "fluentAlert",
                          "Startup errors use the shared themed alert");
                    auto close = findButton(dialog, "关闭");
                    check(close && close->isDefault() && close->minimumHeight() >= Theme::commandHeight,
                          "Alert has an explicit Chinese close command");
                    observe("04-alert"); close->click();
                } catch (const std::exception& error) {
                    report["dialogError"] = error.what();
                    if (dialog) dialog->reject();
                }
            });
            Theme::alert(&window, "启动错误组件检查", "请从同目录的 Takao.Host.exe 启动完整的 Qt + .NET 程序。");
            if (report.contains("dialogError"))
                throw std::runtime_error(report.value("dialogError").toString().toStdString());
            report["result"] = "PASS";
            save("completed");
        } catch (const std::exception& error) {
            report["result"] = "FAIL";
            report["error"] = error.what();
            save("failed");
        }
        window.close();
    });
    const int result = app.exec();
    return report["result"] == "PASS" ? result : 1;
}
