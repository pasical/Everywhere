"""Persistent manual demo and native accessibility fixture for Everywhere.

Run: python tests/Everywhere.Linux.Probes/fixture.py [state.json]
Only this application's synthetic data and widget geometry are recorded.
"""
import json
import sys
from pathlib import Path

import gi

gi.require_version("Gtk", "4.0")
from gi.repository import GLib, Gtk


class Fixture(Gtk.Application):
    def __init__(self):
        super().__init__(application_id="com.sylinko.EverywhereControlledFixture")
        self.connect("activate", self.activate_fixture)
        self.clicks = 0
        self.widgets = {}
        self.state_path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("/tmp/everywhere-demo/fixture.json")
        self.state_path.parent.mkdir(parents=True, exist_ok=True)

    def remember(self, name, widget):
        self.widgets[name] = widget
        return widget

    def activate_fixture(self, application):
        self.window = Gtk.ApplicationWindow(application=application, title="Everywhere controlled fixture")
        self.window.set_default_size(860, 680)
        page = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=16)
        for edge in ("top", "bottom", "start", "end"):
            getattr(page, "set_margin_" + edge)(24)
        heading = Gtk.Label(label="Everywhere · Linux / Hyprland Demo", xalign=0)
        heading.add_css_class("title-1")
        page.append(heading)
        page.append(Gtk.Label(label="F1 屏幕 / F2 窗口 / F3 元素 · 截屏可用 F4 框选 · Esc 取消", xalign=0))
        page.append(Gtk.Label(label="依次悬停：输入框、按钮、复选框、长文本和彩色色块，观察选框范围。", xalign=0))

        controls = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=20, homogeneous=True)
        inputs = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
        inputs.append(Gtk.Label(label="Unicode / editable input", xalign=0))
        self.entry = self.remember("entry", Gtk.Entry(text="Initial native text 🌏 漢字 é"))
        self.entry.connect("changed", lambda *_: self.record())
        inputs.append(self.entry)
        self.check = self.remember("checkbox", Gtk.CheckButton(label="Everywhere demo checkbox"))
        self.check.connect("toggled", lambda *_: self.record())
        inputs.append(self.check)
        self.slider = self.remember("slider", Gtk.Scale.new_with_range(Gtk.Orientation.HORIZONTAL, 0, 100, 1))
        self.slider.set_value(35)
        self.slider.connect("value-changed", lambda *_: self.record())
        inputs.append(self.slider)
        self.dropdown = self.remember("dropdown", Gtk.DropDown.new_from_strings(["First item", "Second item", "第三项 🌏"]))
        inputs.append(self.dropdown)
        controls.append(inputs)

        actions = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
        button = self.remember("button", Gtk.Button(label="Everywhere target button"))
        button.connect("clicked", self.clicked)
        actions.append(button)
        self.status = self.remember("counter", Gtk.Label(label="Click count: 0", xalign=0))
        actions.append(self.status)
        change = self.remember("change", Gtk.Button(label="Change live text / 更新内容"))
        change.connect("clicked", lambda *_: self.entry.set_text("Updated live text · 当前内容 🌏 " + str(self.clicks)))
        actions.append(change)
        reset = Gtk.Button(label="Reset counter / 重置计数")
        reset.connect("clicked", self.reset)
        actions.append(reset)
        controls.append(actions)
        page.append(controls)

        page.append(Gtk.Label(label="Scrollable text · 选择元素后滚动，再读取或截屏", xalign=0))
        text = self.remember("document", Gtk.TextView(wrap_mode=Gtk.WrapMode.WORD_CHAR))
        text.get_buffer().set_text("\n".join(
            f"Line {number:02d}: controlled demo text — 中文、emoji 🌏 and é. No private data."
            for number in range(1, 61)))
        scroll = Gtk.ScrolledWindow(vexpand=True, min_content_height=150)
        scroll.set_child(text)
        page.append(scroll)

        pattern = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, homogeneous=True)
        for index, name in enumerate(["RED · 红", "GREEN · 绿", "BLUE · 蓝", "WHITE · 白"]):
            label = self.remember("color" + str(index), Gtk.Label(label=name, height_request=64))
            label.add_css_class("ew-color-" + str(index))
            pattern.append(label)
        page.append(pattern)
        css = Gtk.CssProvider()
        css.load_from_data(b".ew-color-0{background:#cf4141;color:white;}.ew-color-1{background:#21844c;color:white;}.ew-color-2{background:#295fc9;color:white;}.ew-color-3{background:#f7f7f7;color:#202020;}")
        Gtk.StyleContext.add_provider_for_display(self.window.get_display(), css, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
        self.window.set_child(page)
        self.window.present()
        GLib.timeout_add(250, self.record)

    def clicked(self, *_):
        self.clicks += 1
        self.status.set_label(f"Click count: {self.clicks}")
        self.record()

    def reset(self, *_):
        self.clicks = 0
        self.status.set_label("Click count: 0")
        self.record()

    def record(self):
        if not hasattr(self, "status"):
            return True
        geometry = {}
        for name, widget in self.widgets.items():
            valid, bounds = widget.compute_bounds(self.window)
            if valid:
                geometry[name] = [round(bounds.get_x()), round(bounds.get_y()), round(bounds.get_width()), round(bounds.get_height())]
        state = {"clicks": self.clicks, "text": self.entry.get_text(), "checked": self.check.get_active(),
                 "slider": self.slider.get_value(), "widgets": geometry}
        temporary = self.state_path.with_suffix(".tmp")
        temporary.write_text(json.dumps(state, ensure_ascii=False, indent=2))
        temporary.replace(self.state_path)
        return True


Fixture().run([])
