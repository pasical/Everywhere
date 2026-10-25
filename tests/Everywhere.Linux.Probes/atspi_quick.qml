// Controlled Qt Quick accessibility fixture. No user files or network access.
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

ApplicationWindow {
    width: 800
    height: 600
    visible: true
    title: "Everywhere AT-SPI Qt Quick"

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 24
        spacing: 16

        Label { text: "Synthetic Qt Quick controls" }
        TextField {
            text: "Editable synthetic text"
            Accessible.name: "Editable field"
            Layout.fillWidth: true
        }
        TextField {
            text: "Read-only synthetic text"
            readOnly: true
            Accessible.name: "Read-only field"
            Layout.fillWidth: true
        }
        Button {
            text: "Expected Qt button"
            Component.onCompleted: forceActiveFocus()
        }
        ProgressBar {
            value: 0.45
            Accessible.name: "Synthetic progress"
            Layout.fillWidth: true
        }
        SpinBox { Accessible.name: "Synthetic spinner" }
        Item { Layout.fillHeight: true }
    }
}
