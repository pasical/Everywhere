// Controlled Qt Widgets accessibility fixture. No user files or network access.
// Build: c++ atspi_widgets.cpp -o /tmp/everywhere-atspi-widgets $(pkg-config --cflags --libs Qt6Widgets)
#include <QApplication>
#include <QLabel>
#include <QLineEdit>
#include <QProgressBar>
#include <QPushButton>
#include <QSpinBox>
#include <QTreeWidget>
#include <QVBoxLayout>
#include <QWidget>

int main(int argc, char **argv)
{
    QApplication application(argc, argv);
    QWidget window;
    window.setWindowTitle("Everywhere AT-SPI Qt Widgets");
    window.resize(800, 600);
    auto *layout = new QVBoxLayout(&window);
    layout->addWidget(new QLabel("Synthetic Qt Widgets controls"));
    auto *editable = new QLineEdit("Editable synthetic text");
    editable->setAccessibleName("Editable field");
    layout->addWidget(editable);
    auto *readOnly = new QLineEdit("Read-only synthetic text");
    readOnly->setAccessibleName("Read-only field");
    readOnly->setReadOnly(true);
    layout->addWidget(readOnly);
    auto *button = new QPushButton("Expected Qt button");
    layout->addWidget(button);
    auto *tree = new QTreeWidget;
    tree->setHeaderLabels({"Synthetic header"});
    new QTreeWidgetItem(tree, {"Synthetic tree item"});
    layout->addWidget(tree);
    auto *progress = new QProgressBar;
    progress->setValue(45);
    progress->setAccessibleName("Synthetic progress");
    layout->addWidget(progress);
    auto *spinner = new QSpinBox;
    spinner->setAccessibleName("Synthetic spinner");
    layout->addWidget(spinner);
    window.show();
    button->setFocus();
    return application.exec();
}
