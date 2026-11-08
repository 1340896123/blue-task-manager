#pragma once
#include <QJsonObject>
#include <QScrollArea>
#include <QVector>

// Hardware facts arrive only from the owning Host's independent native cache.
class SystemInformationView final : public QScrollArea {
  public:
    explicit SystemInformationView(QWidget* parent = nullptr);
    void setInformation(const QJsonObject&);
    int filter(const QString&);
    int scrollPosition() const;
    void restoreScroll(int);
    QString plainText() const;
  private:
    QWidget* content_{};
    QVector<QWidget*> groups_;
    QJsonObject information_;
    QString query_;
    QStringList text_;
};
