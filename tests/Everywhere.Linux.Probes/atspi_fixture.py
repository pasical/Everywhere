"""Synthetic AT-SPI provider for bounded Linux traversal regressions.

Run only inside isolated.py's disposable compositor:
    GTK_A11Y=none python3 atspi_fixture.py SCENARIO REPORT
Scenarios: deep, wide, logical, missing-geometry, cycle, limit, managed,
focus-wide, foreign-focus, defunct-once. The exported tree is synthetic protocol evidence,
not a claim about GTK/Qt/Flutter's own accessibility implementations.
"""
import collections
import json
import os
import sys
from pathlib import Path

os.environ["GTK_A11Y"] = "none"
import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi, Gio, GLib, Gtk

SCENARIO, REPORT = sys.argv[1:3]
TITLE = "Everywhere AT-SPI " + SCENARIO
ROOT = "/org/a11y/atspi/accessible/root"
XML = """<node>
<interface name="org.a11y.atspi.Accessible">
 <property name="Name" type="s" access="read"/>
 <property name="Description" type="s" access="read"/>
 <property name="Parent" type="(so)" access="read"/>
 <property name="ChildCount" type="i" access="read"/>
 <method name="GetChildAtIndex"><arg type="i" direction="in"/><arg type="(so)" direction="out"/></method>
 <method name="GetChildren"><arg type="a(so)" direction="out"/></method>
 <method name="GetApplication"><arg type="(so)" direction="out"/></method>
 <method name="GetRole"><arg type="u" direction="out"/></method>
 <method name="GetRoleName"><arg type="s" direction="out"/></method>
 <method name="GetLocalizedRoleName"><arg type="s" direction="out"/></method>
 <method name="GetState"><arg type="au" direction="out"/></method>
 <method name="GetIndexInParent"><arg type="i" direction="out"/></method>
 <method name="GetInterfaces"><arg type="as" direction="out"/></method>
 <method name="GetAttributes"><arg type="a{ss}" direction="out"/></method>
 <method name="GetRelationSet"><arg type="a(ua(so))" direction="out"/></method>
</interface>
<interface name="org.a11y.atspi.Component">
 <method name="GetAccessibleAtPoint"><arg type="i" direction="in"/><arg type="i" direction="in"/><arg type="u" direction="in"/><arg type="(so)" direction="out"/></method>
 <method name="GetExtents"><arg type="u" direction="in"/><arg type="(iiii)" direction="out"/></method>
</interface>
<interface name="org.a11y.atspi.Application">
 <property name="ToolkitName" type="s" access="read"/>
 <property name="Version" type="s" access="read"/>
 <property name="AtspiVersion" type="s" access="read"/>
 <property name="Id" type="i" access="readwrite"/>
 <method name="GetLocale"><arg type="u" direction="in"/><arg type="s" direction="out"/></method>
</interface>
</node>"""
INFO = Gio.DBusNodeInfo.new_for_xml(XML)
NODES = []
CALLS = collections.Counter()


class Node:
    def __init__(self, role, name="", parent=None, component=True, bounds=(0, 0, 800, 500)):
        self.path = ROOT if not NODES else "/org/a11y/atspi/accessible/" + str(len(NODES))
        self.role, self.name, self.parent = role, name, parent
        self.component, self.bounds, self.children = component, bounds, []
        self.hit = self
        self.focused = False
        self.managed = False
        NODES.append(self)
        if parent:
            parent.children.append(self)

    def reference(self):
        return BUS.get_unique_name(), self.path

    def interfaces(self):
        return ["org.a11y.atspi.Accessible"] + (["org.a11y.atspi.Component"] if self.component else []) + (["org.a11y.atspi.Application"] if self is APP else [])


APP = Node(Atspi.Role.APPLICATION, "Everywhere synthetic provider", component=False)
FRAME = Node(Atspi.Role.FRAME, TITLE, APP)
FOCUSED = None
if SCENARIO in {"deep", "limit"}:
    parent = FRAME
    for _ in range(40 if SCENARIO == "deep" else 270):
        parent = Node(Atspi.Role.PANEL, parent=parent)
    TARGET = Node(Atspi.Role.PUSH_BUTTON, "Expected target", parent)
elif SCENARIO == "wide":
    for _ in range(179):
        Node(Atspi.Role.PUSH_BUTTON, "Outside target", FRAME, bounds=(2000, 2000, 20, 20))
    TARGET = Node(Atspi.Role.PUSH_BUTTON, "Expected target", FRAME)
elif SCENARIO == "logical":
    wrapper = Node(Atspi.Role.PANEL, parent=FRAME, component=False)
    TARGET = Node(Atspi.Role.PUSH_BUTTON, "Expected target", wrapper)
elif SCENARIO == "missing-geometry":
    TARGET = Node(Atspi.Role.PUSH_BUTTON, "Expected target", FRAME)
    leaf = Node(Atspi.Role.LABEL, "No geometry", TARGET, component=False)
    FRAME.hit, TARGET.hit = TARGET, leaf
elif SCENARIO == "cycle":
    TARGET = Node(Atspi.Role.PUSH_BUTTON, "Expected target", FRAME)
    FRAME.hit, TARGET.hit = TARGET, FRAME
elif SCENARIO == "defunct-once":
    TARGET = FRAME
elif SCENARIO == "managed":
    TARGET = Node(Atspi.Role.TABLE, "Managed table", FRAME)
    TARGET.managed = True
    FRAME.hit = TARGET
elif SCENARIO in {"focus-wide", "foreign-focus"}:
    parent = FRAME if SCENARIO == "focus-wide" else Node(Atspi.Role.FRAME, "Other synthetic window", APP)
    for _ in range(40):
        parent = Node(Atspi.Role.PANEL, parent=parent)
    for _ in range(270):
        Node(Atspi.Role.PUSH_BUTTON, "Unfocused sibling", parent)
    TARGET = Node(Atspi.Role.PUSH_BUTTON, "Expected focus", parent)
    TARGET.focused = True
    FOCUSED = TARGET
else:
    raise SystemExit("Unknown scenario")


def method(connection, sender, path, interface, member, parameters, invocation):
    node = BY_PATH[path]
    CALLS[member] += 1
    if interface.endswith("Accessible"):
        if member == "GetChildAtIndex":
            if node.managed:
                CALLS["managedChildEnumeration"] += 1
            index = parameters.unpack()[0]
            result = node.children[index].reference() if 0 <= index < len(node.children) else ("", "/org/a11y/atspi/null")
            value = GLib.Variant("((so))", (result,))
        elif member == "GetChildren":
            value = GLib.Variant("(a(so))", ([child.reference() for child in node.children],))
        elif member == "GetApplication":
            value = GLib.Variant("((so))", (APP.reference(),))
        elif member == "GetRole":
            value = GLib.Variant("(u)", (int(node.role),))
        elif member in {"GetRoleName", "GetLocalizedRoleName"}:
            value = GLib.Variant("(s)", (Atspi.role_get_name(node.role),))
        elif member == "GetState":
            states = [Atspi.StateType.VISIBLE, Atspi.StateType.SHOWING, Atspi.StateType.ENABLED, Atspi.StateType.SENSITIVE]
            if SCENARIO == "defunct-once" and node is FRAME:
                CALLS["frameStates"] += 1
                if CALLS["frameStates"] == 1:
                    states.append(Atspi.StateType.DEFUNCT)
            if node.focused:
                states.append(Atspi.StateType.FOCUSED)
            if node.managed:
                states.append(Atspi.StateType.MANAGES_DESCENDANTS)
            mask = sum(1 << int(state) for state in states)
            value = GLib.Variant("(au)", ([mask & 0xffffffff, mask >> 32],))
        elif member == "GetIndexInParent":
            value = GLib.Variant("(i)", (node.parent.children.index(node) if node.parent else -1,))
        elif member == "GetInterfaces":
            value = GLib.Variant("(as)", (node.interfaces(),))
        elif member == "GetAttributes":
            value = GLib.Variant("(a{ss})", ({},))
        elif member == "GetRelationSet":
            value = GLib.Variant("(a(ua(so)))", ([],))
        else:
            raise RuntimeError(member)
    elif interface.endswith("Component"):
        if member == "GetAccessibleAtPoint":
            value = GLib.Variant("((so))", (node.hit.reference(),))
        else:
            value = GLib.Variant("((iiii))", (node.bounds,))
    else:
        value = GLib.Variant("(s)", ("en_US",))
    invocation.return_value(value)


def property_value(connection, sender, path, interface, name):
    node = BY_PATH[path]
    values = {"Name": ("s", node.name), "Description": ("s", "Synthetic test data"),
              "Parent": ("(so)", node.parent.reference() if node.parent else PARENT),
              "ChildCount": ("i", 5000 if node.managed else len(node.children)),
              "ToolkitName": ("s", "Everywhere synthetic AT-SPI"), "Version": ("s", "1"),
              "AtspiVersion": ("s", "2.1"), "Id": ("i", 0)}
    signature, value = values[name]
    return GLib.Variant(signature, value)


def record():
    Path(REPORT).write_text(json.dumps({"pid": os.getpid(), "scenario": SCENARIO, "title": TITLE,
                                       "nodes": len(NODES), "calls": dict(CALLS)}, indent=2))
    if FOCUSED:
        BUS.emit_signal(None, FOCUSED.path, "org.a11y.atspi.Event.Object", "StateChanged",
                        GLib.Variant("(siiva{sv})", ("focused", 1, 0, GLib.Variant("i", 0), {})))
    return True


SESSION = Gio.bus_get_sync(Gio.BusType.SESSION, None)
address = SESSION.call_sync("org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress", None, GLib.VariantType.new("(s)"), Gio.DBusCallFlags.NONE, 3000, None).unpack()[0]
BUS = Gio.DBusConnection.new_for_address_sync(address, Gio.DBusConnectionFlags.AUTHENTICATION_CLIENT | Gio.DBusConnectionFlags.MESSAGE_BUS_CONNECTION, None, None)
BY_PATH = {node.path: node for node in NODES}
PARENT = ("org.a11y.atspi.Registry", ROOT)
for node in NODES:
    for interface in INFO.interfaces:
        if interface.name in node.interfaces():
            BUS.register_object(node.path, interface, method, property_value, lambda *_: True)
PARENT = BUS.call_sync("org.a11y.atspi.Registry", ROOT, "org.a11y.atspi.Socket", "Embed", GLib.Variant("((so))", (APP.reference(),)), None, Gio.DBusCallFlags.NONE, 3000, None).unpack()[0]
APPLICATION = Gtk.Application(application_id="com.sylinko.EverywhereAtspiFixture")


def activate(application):
    window = Gtk.ApplicationWindow(application=application, title=TITLE, default_width=800, default_height=500)
    window.set_child(Gtk.Label(label="Synthetic AT-SPI regression: " + SCENARIO))
    window.present()
    GLib.timeout_add(100, record)


APPLICATION.connect("activate", activate)
APPLICATION.run([])
