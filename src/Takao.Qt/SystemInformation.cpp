#include "MainWindow.h"
#include "Theme.h"

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
