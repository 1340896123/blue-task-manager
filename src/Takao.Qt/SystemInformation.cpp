#include "MainWindow.h"
#include "Theme.h"
#include <QClipboard>

void MainWindow::copySystemInformation() {
    if (page_ != 8 || !copySystemInformation_->isEnabled()) return;
    QStringList text{"系统信息", "采集时间：" + systemInformation_["capturedAtUtc"].toString()};
    const auto status = systemInformation_["status"].toString();
    text << "状态：" + (status == "ready" ? QStringLiteral("可用")
                          : status == "partial" ? QStringLiteral("部分硬件信息不可用")
                          : QStringLiteral("硬件信息不可用"));
    if (!connected_)
        text << "连接中断 · 上次快照已过期" + (connectionError_.isEmpty() ? QString() : " · " + connectionError_);
    if (readingSystemInformation_) text << "正在刷新 · 以下为上次采集的硬件信息";
    if (!systemInformationError_.isEmpty()) text << systemInformationError_;
    const auto reason = reasonText(systemInformation_);
    if (!reason.isEmpty()) text << reason;
    text << QString() << systemInformationPage_->plainText();
    const auto content = text.join('\n');
    copyFeedbackTimer_.stop();
    copySystemInformation_->setText("复制");
    copySystemInformation_->setAccessibleName("复制系统信息");
    auto clipboard = QApplication::clipboard();
    if (clipboard->text() != content) clipboard->setText(content);
    if (clipboard->text() != content) {
        notify("复制系统信息失败，系统剪贴板暂不可用，请重试。", true);
        return;
    }
    notify("系统信息已复制。");
    copySystemInformation_->setText("已复制");
    copySystemInformation_->setAccessibleName("已复制系统信息");
    copyFeedbackTimer_.start();
}

void MainWindow::readSystemInformation(bool refresh) {
    if (!connected_ || closing_ || readingSystemInformation_)
        return;
    readingSystemInformation_ = true;
    systemInformationError_.clear();
    if (page_ == 8) {
        sourceMessage_ = systemInformation_.isEmpty() ? "正在读取 Windows 硬件信息…" : "正在刷新硬件信息…";
        updateCaption();
        updateSelectionActions();
    }
    api_.get(refresh ? "system-information?refresh=true" : "system-information", this,
             [this](const QJsonObject& result, const QString& error, int) {
        readingSystemInformation_ = false;
        if (!error.isEmpty())
            systemInformationError_ = "读取硬件信息失败 · " + error +
                (systemInformation_.isEmpty() ? QString() : QStringLiteral("；显示的上次硬件信息已过期。"));
        else
            systemInformation_ = result;
        if (page_ == 8) {
            updateSystemInformation();
            updateSelectionActions();
        }
    });
}
void MainWindow::updateSystemInformation() {
    if (page_ != 8) return;
    systemInformationPage_->setInformation(systemInformation_);
    pageCaption_ = "真实硬件概览 · " + (systemInformation_["capturedAtUtc"].isString()
        ? "采集于 " + formatValue(systemInformation_["capturedAtUtc"], "date") : QStringLiteral("Windows 原生来源"));
    const auto status = systemInformation_["status"].toString();
    if (readingSystemInformation_)
        sourceMessage_ = "正在读取 Windows 硬件信息…";
    else if (!systemInformationError_.isEmpty())
        sourceMessage_ = systemInformationError_;
    else if (status == "loading" || systemInformation_.isEmpty())
        sourceMessage_ = "正在读取 Windows 硬件信息…";
    else if (status == "partial")
        sourceMessage_ = "部分硬件信息不可用 · " + pageCaption_;
    else if (status == "unavailable")
        sourceMessage_ = "硬件信息不可用 · " + reasonText(systemInformation_);
    else
        sourceMessage_.clear();
    searchMessage_ = systemInformationPage_->filter(search_->text()) == 0
        ? QStringLiteral("没有匹配的硬件信息。请清空搜索或使用其它型号 / 类别。") : QString();
    updateCaption();
}
