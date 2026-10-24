"""Main window: pick windows, manage overlays and their settings."""

from dataclasses import dataclass, replace
from typing import Dict, List, Optional, Tuple

from gi.repository import Gdk, GdkPixbuf, GLib, Gst, Gtk, Pango

from .overlay import OverlayWindow
from .settings import Settings
from .x11 import CONTROL_MASK, MOD1_MASK, MOD4_MASK, SHIFT_MASK, X11, HotkeyGrabber, WindowInfo

CSS = b"""
window.main { background-color: #18181c; color: #f0f0f5; }
.title { font-size: 21px; font-weight: 600; }
.muted { color: #9696a5; }
.small { font-size: 11px; }
.section { font-size: 15px; font-weight: 600; }
.card { background-color: #222228; border: 1px solid #383842; border-radius: 10px; padding: 14px 16px; }
.card list, .card scrolledwindow, .card viewport { background-color: transparent; }
.card list row { border-radius: 8px; padding: 6px 8px; margin: 1px 0; }
.card list row:hover { background-color: #2c2c34; }
.card list row:selected { background-color: rgba(76, 141, 255, 0.20); box-shadow: inset 0 0 0 1px rgba(76, 141, 255, 0.55); }
.row-title { font-weight: 600; }
.badge { background-color: rgba(61, 214, 140, 0.16); color: #3dd68c; border-radius: 10px; padding: 1px 8px;
         font-size: 11px; font-weight: 700; }
button.suggested-action { background-image: none; background-color: #4c8dff; color: white; border-color: #4c8dff; }
button.suggested-action:hover { background-color: #68a0ff; }
.dot-idle { color: #9696a5; }
.dot-live { color: #3dd68c; }
.dot-warning { color: #f5b43c; }
entry.hotkey { font-weight: 600; }
"""

# Each side can crop up to 98%; opposite sides are kept from overlapping (at least 2% stays visible).
MAX_CROP_PERCENT = 98
OPPOSITE_SIDE = {"Left": "Right", "Right": "Left", "Top": "Bottom", "Bottom": "Top"}

X_MODIFIERS = {
    Gdk.ModifierType.CONTROL_MASK: CONTROL_MASK,
    Gdk.ModifierType.SHIFT_MASK: SHIFT_MASK,
    Gdk.ModifierType.MOD1_MASK: MOD1_MASK,   # Alt
    Gdk.ModifierType.SUPER_MASK: MOD4_MASK,
    Gdk.ModifierType.MOD4_MASK: MOD4_MASK,
}


def _label(text: str, *classes: str, xalign: float = 0.0, ellipsize: bool = False, wrap: bool = False) -> Gtk.Label:
    label = Gtk.Label(label=text, xalign=xalign)
    for c in classes:
        label.get_style_context().add_class(c)
    if ellipsize:
        label.set_ellipsize(Pango.EllipsizeMode.END)
    if wrap:
        label.set_line_wrap(True)
    return label


def _icon_pixbuf(icon, size: int) -> Optional[GdkPixbuf.Pixbuf]:
    if not icon:
        return None
    w, h, rgba = icon
    pixbuf = GdkPixbuf.Pixbuf.new_from_bytes(GLib.Bytes.new(rgba), GdkPixbuf.Colorspace.RGB, True, 8, w, h, w * 4)
    return pixbuf.scale_simple(size, size, GdkPixbuf.InterpType.BILINEAR)


def _icon_image(pixbuf: Optional[GdkPixbuf.Pixbuf], size: int) -> Gtk.Image:
    if pixbuf is not None:
        return Gtk.Image.new_from_pixbuf(pixbuf)
    image = Gtk.Image.new_from_icon_name("application-x-executable", Gtk.IconSize.LARGE_TOOLBAR)
    image.set_pixel_size(size)
    return image


def _switch_row(title: str, description: str) -> Tuple[Gtk.Box, Gtk.Switch, Gtk.Label]:
    box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
    text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
    text.pack_start(_label(title), False, False, 0)
    desc = _label(description, "muted", "small", ellipsize=True)
    text.pack_start(desc, False, False, 0)
    box.pack_start(text, True, True, 0)
    switch = Gtk.Switch(valign=Gtk.Align.CENTER)
    box.pack_end(switch, False, False, 0)
    return box, switch, desc


@dataclass
class OverlayEntry:
    number: int
    xid: int
    title: str
    icon: Optional[GdkPixbuf.Pixbuf]
    window: OverlayWindow
    opacity: float = 1.0
    removed: bool = False

    @property
    def summary(self) -> str:
        text = f"Overlay {self.number} · {round(self.opacity * 100)}% opacity"
        return text + (" · cropped" if not self.window.crop.is_zero() else "")


class WindowRow(Gtk.ListBoxRow):
    def __init__(self, info: WindowInfo):
        super().__init__()
        self.info = info
        box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
        box.pack_start(_icon_image(_icon_pixbuf(info.icon, 24), 24), False, False, 4)
        text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
        text.pack_start(_label(info.title, "row-title", ellipsize=True), False, False, 0)
        text.pack_start(_label(info.app_name, "muted", "small", ellipsize=True), False, False, 0)
        box.pack_start(text, True, True, 0)
        self.badge = _label("LIVE", "badge")
        self.badge.set_no_show_all(True)
        box.pack_end(self.badge, False, False, 4)
        self.add(box)

    def set_live_count(self, count: int):
        self.badge.set_text("LIVE" if count <= 1 else f"LIVE ×{count}")
        self.badge.set_visible(count > 0)


class OverlayRow(Gtk.ListBoxRow):
    def __init__(self, entry: OverlayEntry, on_remove):
        super().__init__()
        self.entry = entry
        box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
        box.pack_start(_icon_image(entry.icon, 20), False, False, 4)
        text = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
        text.pack_start(_label(entry.title, "row-title", ellipsize=True), False, False, 0)
        self.summary = _label(entry.summary, "muted", "small", ellipsize=True)
        text.pack_start(self.summary, False, False, 0)
        box.pack_start(text, True, True, 0)
        close = Gtk.Button.new_from_icon_name("window-close-symbolic", Gtk.IconSize.BUTTON)
        close.set_relief(Gtk.ReliefStyle.NONE)
        close.set_tooltip_text("Close this overlay")
        close.connect("clicked", lambda *_: on_remove(entry))
        box.pack_end(close, False, False, 0)
        self.add(box)

    def refresh(self):
        self.summary.set_text(self.entry.summary)


class MainWindow(Gtk.Window):
    def __init__(self):
        super().__init__(title="Window Overlay")
        self.get_style_context().add_class("main")
        self.set_default_size(1020, 820)
        self.set_icon_name("window-overlay")

        Gtk.Settings.get_default().set_property("gtk-application-prefer-dark-theme", True)
        provider = Gtk.CssProvider()
        provider.load_from_data(CSS)
        Gtk.StyleContext.add_provider_for_screen(Gdk.Screen.get_default(), provider,
                                                 Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)

        self.x11 = X11()
        self.settings = Settings.load()
        self.hotkey = HotkeyGrabber(self.x11, self._on_hotkey)
        GLib.io_add_watch(self.hotkey.fd, GLib.PRIORITY_DEFAULT, GLib.IO_IN, self._on_x_events)

        self.overlays: List[OverlayEntry] = []
        self.next_number = 1
        self.loading_settings = False
        self.recording_hotkey = False

        self._build()
        self.connect("delete-event", self._on_close)
        self.refresh_windows()
        self._update_overlay_ui()
        if not self._grab_hotkey(self.settings.toggle_hotkey):
            self.set_status(f"{self._hotkey_label(self.settings.toggle_hotkey)} is already used by another app. "
                            "Pick another hotkey or use the switch.", "warning")

    # ================================================================== layout

    def _build(self):
        root = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
        root.set_margin_top(18)
        root.set_margin_bottom(10)
        root.set_margin_start(22)
        root.set_margin_end(22)
        self.add(root)

        header = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
        header.pack_start(_label("Window Overlay", "title"), False, False, 0)
        header.pack_start(_label("Mirror any windows into always-on-top overlays that clicks pass straight through.",
                                 "muted"), False, False, 0)
        root.pack_start(header, False, False, 0)

        main = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=14)
        main.pack_start(self._build_windows_card(), True, True, 0)
        side = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
        side.set_size_request(390, -1)
        side.pack_start(self._build_overlays_card(), False, False, 0)
        side.pack_start(self._build_settings_card(), False, False, 0)
        main.pack_start(side, False, False, 0)
        root.pack_start(main, True, True, 0)

        status = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        self.status_dot = _label("●", "dot-idle")
        self.status_text = _label("", ellipsize=True)
        status.pack_start(self.status_dot, False, False, 0)
        status.pack_start(self.status_text, True, True, 0)
        root.pack_start(status, False, False, 0)

    @staticmethod
    def _card() -> Gtk.Box:
        card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
        card.get_style_context().add_class("card")
        return card

    def _build_windows_card(self) -> Gtk.Widget:
        card = self._card()
        toolbar = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        toolbar.pack_start(_label("Windows", "section"), False, False, 0)
        self.search = Gtk.SearchEntry(placeholder_text="Search windows")
        self.search.connect("search-changed", lambda *_: self.window_list.invalidate_filter())
        self.search.connect("activate", lambda *_: self._activate_first_window())
        toolbar.pack_start(self.search, True, True, 8)
        refresh = Gtk.Button.new_from_icon_name("view-refresh-symbolic", Gtk.IconSize.BUTTON)
        refresh.set_tooltip_text("Refresh window list")
        refresh.connect("clicked", lambda *_: self.refresh_windows())
        toolbar.pack_start(refresh, False, False, 0)
        card.pack_start(toolbar, False, False, 0)

        self.window_list = Gtk.ListBox()
        self.window_list.set_filter_func(self._filter_window_row)
        self.window_list.connect("row-activated", lambda *_: self.add_overlay())
        scroller = Gtk.ScrolledWindow(hscrollbar_policy=Gtk.PolicyType.NEVER)
        scroller.add(self.window_list)
        card.pack_start(scroller, True, True, 0)

        card.pack_start(_label("Double-click a window (or press Enter) to add an overlay. Add the same window more than "
                               "once to show different cropped parts of it. Sources can be covered by other windows "
                               "but must not be minimised.", "muted", "small", wrap=True), False, False, 0)
        return card

    def _build_overlays_card(self) -> Gtk.Widget:
        card = self._card()
        card.pack_start(_label("Overlays", "section"), False, False, 0)

        buttons = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8, homogeneous=False)
        add = Gtk.Button(label="Add overlay")
        add.set_image(Gtk.Image.new_from_icon_name("list-add-symbolic", Gtk.IconSize.BUTTON))
        add.set_always_show_image(True)
        add.get_style_context().add_class("suggested-action")
        add.connect("clicked", lambda *_: self.add_overlay())
        self.stop_all_button = Gtk.Button(label="Stop all")
        self.stop_all_button.set_image(Gtk.Image.new_from_icon_name("media-playback-stop-symbolic", Gtk.IconSize.BUTTON))
        self.stop_all_button.set_always_show_image(True)
        self.stop_all_button.connect("clicked", lambda *_: self.stop_all())
        buttons.pack_start(add, True, True, 0)
        buttons.pack_start(self.stop_all_button, True, True, 0)
        card.pack_start(buttons, False, False, 0)

        self.overlay_list = Gtk.ListBox()
        self.overlay_list.connect("row-selected", lambda *_: self._on_overlay_selected())
        self.overlay_list.connect("key-press-event", self._on_overlay_list_key)
        scroller = Gtk.ScrolledWindow(hscrollbar_policy=Gtk.PolicyType.NEVER)
        scroller.set_size_request(-1, 150)
        scroller.add(self.overlay_list)
        self.overlay_stack = Gtk.Stack()
        self.overlay_stack.add_named(scroller, "list")
        self.overlay_stack.add_named(_label("No overlays yet.\nPick a window and press Add overlay.",
                                            "muted", "small", xalign=0.5), "empty")
        card.pack_start(self.overlay_stack, False, False, 0)

        row, self.click_through_switch, self.click_through_desc = _switch_row(
            "Overlay mode", "Lock all overlays and let clicks pass through")
        self.click_through_switch.connect("notify::active", lambda *_: self._apply_click_through())
        card.pack_start(row, False, False, 0)

        hotkey_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
        hotkey_row.pack_start(_label("Toggle hotkey"), True, True, 0)
        self.hotkey_entry = Gtk.Entry(editable=False, width_chars=18)
        self.hotkey_entry.get_style_context().add_class("hotkey")
        self.hotkey_entry.set_icon_from_icon_name(Gtk.EntryIconPosition.PRIMARY, "input-keyboard-symbolic")
        self.hotkey_entry.set_tooltip_text("Click, then press keys. Esc cancels, Backspace clears.")
        self.hotkey_entry.connect("focus-in-event", self._on_hotkey_focus_in)
        self.hotkey_entry.connect("focus-out-event", self._on_hotkey_focus_out)
        self.hotkey_entry.connect("key-press-event", self._on_hotkey_key)
        hotkey_row.pack_end(self.hotkey_entry, False, False, 0)
        card.pack_start(hotkey_row, False, False, 0)

        row, self.snap_switch, _ = _switch_row("Snap to edges", "Overlays stick to screen edges and to each other")
        self.snap_switch.set_active(self.settings.snap_to_edges)
        self.snap_switch.connect("notify::active", lambda *_: self._on_snap_changed())
        card.pack_start(row, False, False, 0)

        self._update_hotkey_text()
        return card

    def _build_settings_card(self) -> Gtk.Widget:
        card = self._card()
        header = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=4)
        header.pack_start(_label("Overlay settings", "section"), True, True, 0)
        self.fit_button = Gtk.Button.new_from_icon_name("zoom-fit-best-symbolic", Gtk.IconSize.BUTTON)
        self.fit_button.set_tooltip_text("Fit overlay to the source's shape")
        self.fit_button.connect("clicked", lambda *_: self.selected and self.selected.window.fit_to_source())
        self.remove_button = Gtk.Button.new_from_icon_name("user-trash-symbolic", Gtk.IconSize.BUTTON)
        self.remove_button.set_tooltip_text("Close this overlay")
        self.remove_button.connect("clicked", lambda *_: self.selected and self.remove_overlay(self.selected))
        for b in (self.fit_button, self.remove_button):
            b.set_relief(Gtk.ReliefStyle.NONE)
            header.pack_end(b, False, False, 0)
        card.pack_start(header, False, False, 0)

        self.settings_subtitle = _label("", "muted", "small", ellipsize=True)
        card.pack_start(self.settings_subtitle, False, False, 0)

        self.settings_body = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
        card.pack_start(self.settings_body, False, False, 0)

        opacity_header = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL)
        opacity_header.pack_start(_label("Opacity"), True, True, 0)
        self.opacity_value = _label("100%", "row-title")
        opacity_header.pack_end(self.opacity_value, False, False, 0)
        self.settings_body.pack_start(opacity_header, False, False, 0)
        self.opacity_scale = Gtk.Scale.new_with_range(Gtk.Orientation.HORIZONTAL, 10, 100, 1)
        self.opacity_scale.set_draw_value(False)
        self.opacity_scale.set_value(100)
        self.opacity_scale.connect("value-changed", lambda *_: self._apply_selected_settings())
        self.settings_body.pack_start(self.opacity_scale, False, False, 0)

        row, self.client_only_switch, _ = _switch_row("Client area only", "Hide the title bar and borders")
        self.client_only_switch.set_active(True)
        self.client_only_switch.connect("notify::active", lambda *_: self._apply_selected_settings())
        self.settings_body.pack_start(row, False, False, 0)

        crop_header = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL)
        crop_header.pack_start(_label("Crop"), True, True, 0)
        reset = Gtk.Button(label="Reset")
        reset.set_relief(Gtk.ReliefStyle.NONE)
        reset.connect("clicked", lambda *_: [s.set_value(0) for s in self.crop_scales.values()])
        crop_header.pack_end(reset, False, False, 0)
        self.settings_body.pack_start(crop_header, False, False, 0)

        grid = Gtk.Grid(column_spacing=8, row_spacing=4)
        self.crop_scales: Dict[str, Gtk.Scale] = {}
        for i, name in enumerate(("Left", "Right", "Top", "Bottom")):
            row_index, col = divmod(i, 2)
            scale = Gtk.Scale.new_with_range(Gtk.Orientation.HORIZONTAL, 0, MAX_CROP_PERCENT, 1)
            scale.set_draw_value(False)
            scale.set_hexpand(True)
            value = _label("0%", "muted", "small", xalign=1.0)
            value.set_width_chars(4)
            scale.connect("value-changed", lambda s, v=value: v.set_text(f"{int(s.get_value())}%"))
            scale.connect("value-changed", lambda s, n=name: self._on_crop_scale_changed(n))
            grid.attach(_label(name, "muted"), col * 3, row_index, 1, 1)
            grid.attach(scale, col * 3 + 1, row_index, 1, 1)
            grid.attach(value, col * 3 + 2, row_index, 1, 1)
            self.crop_scales[name] = scale
        self.settings_body.pack_start(grid, False, False, 0)
        return card

    def _on_crop_scale_changed(self, name: str):
        """Applies one crop side. Only that side changes; opposite sides can't overlap."""
        scale, opposite = self.crop_scales[name], self.crop_scales[OPPOSITE_SIDE[name]]
        if self.loading_settings:
            return
        if scale.get_value() + opposite.get_value() > MAX_CROP_PERCENT:
            scale.set_value(MAX_CROP_PERCENT - opposite.get_value())  # re-enters with the clamped value
            return
        entry = self.selected
        if entry is None:
            return
        entry.window.set_crop(replace(entry.window.crop, **{name.lower(): scale.get_value() / 100}))
        for row in self.overlay_list.get_children():
            row.refresh()

    # ================================================================== window list

    def refresh_windows(self):
        selected = self.window_list.get_selected_row()
        selected_xid = selected.info.xid if selected else 0
        for row in self.window_list.get_children():
            self.window_list.remove(row)
        for info in self.x11.list_windows():
            row = WindowRow(info)
            self.window_list.add(row)
            if info.xid == selected_xid:
                self.window_list.select_row(row)
        self.window_list.show_all()
        self._update_live_badges()

    def _filter_window_row(self, row: WindowRow) -> bool:
        text = self.search.get_text().strip().lower()
        return not text or text in row.info.title.lower() or text in row.info.app_name.lower()

    def _activate_first_window(self):
        row = self.window_list.get_selected_row()
        if row is None or not row.get_child_visible():
            for candidate in self.window_list.get_children():
                if candidate.get_child_visible() and self._filter_window_row(candidate):
                    row = candidate
                    break
        if row is not None:
            self.window_list.select_row(row)
            self.add_overlay()

    def _update_live_badges(self):
        counts: Dict[int, int] = {}
        for entry in self.overlays:
            counts[entry.xid] = counts.get(entry.xid, 0) + 1
        for row in self.window_list.get_children():
            row.set_live_count(counts.get(row.info.xid, 0))

    # ================================================================== overlays

    @property
    def selected(self) -> Optional[OverlayEntry]:
        row = self.overlay_list.get_selected_row()
        return row.entry if row else None

    def add_overlay(self):
        row = self.window_list.get_selected_row()
        if row is None:
            self.set_status("Select a window first.", "warning")
            return
        info = row.info
        if not self.x11.exists(info.xid):
            self.set_status("That window no longer exists.", "warning")
            self.refresh_windows()
            return

        window = OverlayWindow(self.x11, info.xid)
        entry = OverlayEntry(self.next_number, info.xid, info.title, _icon_pixbuf(info.icon, 20), window)
        self.next_number += 1
        window.snap_to_edges = self.snap_switch.get_active()
        window.click_through = self.click_through_switch.get_active()
        window.other_overlays = lambda w=window: [e.window.rect() for e in self.overlays if e.window is not w]
        window.on_source_lost = lambda e=entry: self._on_source_lost(e)
        window.on_clicked = lambda e=entry: self._select_overlay(e)
        window.connect("delete-event", lambda *_, e=entry: self.remove_overlay(e) or True)  # e.g. Alt+F4

        # Default placement: bottom-right of this window's monitor, cascading new overlays.
        monitor = self.get_display().get_monitor_at_window(self.get_window())
        area = monitor.get_workarea()
        width, height = area.width // 3, area.height // 3
        margin = 24 + (len(self.overlays) % 6) * 36
        window.set_default_size(width, height)
        window.show_all()
        window.move(area.x + area.width - width - margin, area.y + area.height - height - margin)
        window.fit_to_source()
        _, fitted_height = window.get_size()
        window.move(area.x + area.width - width - margin, max(area.y, area.y + area.height - fitted_height - margin))

        self.overlays.append(entry)
        overlay_row = OverlayRow(entry, self.remove_overlay)
        self.overlay_list.add(overlay_row)
        overlay_row.show_all()
        self.overlay_list.select_row(overlay_row)
        self._update_overlay_ui()
        if not self.x11.is_viewable(info.xid):
            self.set_status(f"{info.title} is minimised. Restore it to see it in the overlay.", "warning")

    def remove_overlay(self, entry: OverlayEntry):
        if entry.removed:
            return
        entry.removed = True
        self.overlays.remove(entry)
        for row in self.overlay_list.get_children():
            if row.entry is entry:
                index = row.get_index()
                self.overlay_list.remove(row)
                remaining = self.overlay_list.get_children()
                if remaining:
                    self.overlay_list.select_row(remaining[min(index, len(remaining) - 1)])
                break
        entry.window.close_overlay()
        self._update_overlay_ui()

    def stop_all(self):
        for entry in list(self.overlays):
            self.remove_overlay(entry)
        self.click_through_switch.set_active(False)

    def _on_source_lost(self, entry: OverlayEntry):
        self.remove_overlay(entry)
        self.set_status(f"Overlay {entry.number} closed because its window ({entry.title}) was closed.", "warning")
        self.refresh_windows()

    def _select_overlay(self, entry: OverlayEntry):
        for row in self.overlay_list.get_children():
            if row.entry is entry:
                self.overlay_list.select_row(row)

    def _on_overlay_list_key(self, _widget, event):
        if event.keyval == Gdk.KEY_Delete and self.selected:
            self.remove_overlay(self.selected)
            return True
        return False

    def _apply_click_through(self):
        for entry in self.overlays:
            entry.window.set_click_through(self.click_through_switch.get_active())
        self._update_overlay_ui()

    def _on_snap_changed(self):
        self.settings.snap_to_edges = self.snap_switch.get_active()
        self.settings.save()
        for entry in self.overlays:
            entry.window.snap_to_edges = self.settings.snap_to_edges

    def _on_overlay_selected(self):
        selected = self.selected
        for entry in self.overlays:
            entry.window.set_selected(entry is selected)
        if selected:
            # Load the overlay's settings into the controls without re-applying them.
            self.loading_settings = True
            crop = selected.window.crop
            self.opacity_scale.set_value(round(selected.opacity * 100))
            self.client_only_switch.set_active(selected.window.client_area_only)
            for name, value in (("Left", crop.left), ("Top", crop.top), ("Right", crop.right), ("Bottom", crop.bottom)):
                self.crop_scales[name].set_value(round(value * 100))
            self.loading_settings = False
        self._update_settings_ui()

    def _apply_selected_settings(self):
        self.opacity_value.set_text(f"{int(self.opacity_scale.get_value())}%")
        entry = self.selected
        if self.loading_settings or entry is None:
            return
        entry.opacity = self.opacity_scale.get_value() / 100
        entry.window.set_overlay_opacity(entry.opacity)
        entry.window.set_client_area_only(self.client_only_switch.get_active())
        for row in self.overlay_list.get_children():
            row.refresh()

    def _update_overlay_ui(self):
        count = len(self.overlays)
        self.stop_all_button.set_sensitive(count > 0)
        self.overlay_stack.set_visible_child_name("list" if count else "empty")
        self._update_live_badges()
        self._update_settings_ui()
        if count == 0:
            self.set_status("No overlays running.")
        else:
            locked = self.click_through_switch.get_active()
            self.set_status(f"{count} overlay{'s' if count != 1 else ''} running · "
                            + ("locked (click-through)" if locked else "unlocked: drag to move, drag edges to resize"),
                            "live")

    def _update_settings_ui(self):
        entry = self.selected
        self.settings_body.set_sensitive(entry is not None)
        self.fit_button.set_sensitive(entry is not None)
        self.remove_button.set_sensitive(entry is not None)
        self.settings_subtitle.set_text(f"Overlay {entry.number} · {entry.title}" if entry
                                        else "Select an overlay to change its settings.")

    # ================================================================== hotkey

    @staticmethod
    def _hotkey_label(accel: str) -> str:
        if not accel:
            return "None"
        keyval, mods = Gtk.accelerator_parse(accel)
        return Gtk.accelerator_get_label(keyval, mods) if keyval else accel

    def _grab_hotkey(self, accel: str) -> bool:
        if not accel:
            self.hotkey.ungrab()
            return True
        keyval, mods = Gtk.accelerator_parse(accel)
        x_mods = 0
        for gdk_mask, x_mask in X_MODIFIERS.items():
            if mods & gdk_mask:
                x_mods |= x_mask
        return self.hotkey.grab(keyval, x_mods)

    def _on_x_events(self, _fd, _condition):
        self.hotkey.process_events()
        return True

    def _on_hotkey(self):
        if not self.recording_hotkey:
            self.click_through_switch.set_active(not self.click_through_switch.get_active())

    def _update_hotkey_text(self):
        label = self._hotkey_label(self.settings.toggle_hotkey)
        self.hotkey_entry.set_text(label)
        self.click_through_desc.set_text("Lock all overlays and let clicks pass through" if not self.settings.toggle_hotkey
                                         else f"Lock all, click-through · {label}")

    def _on_hotkey_focus_in(self, *_):
        # Release the global grab while recording so pressing the current combination is captured here.
        self.recording_hotkey = True
        self.hotkey.ungrab()
        self.hotkey_entry.set_text("Press a key combination…")
        return False

    def _on_hotkey_focus_out(self, *_):
        if self.recording_hotkey:
            self.recording_hotkey = False
            self._grab_hotkey(self.settings.toggle_hotkey)
            self._update_hotkey_text()
        return False

    def _on_hotkey_key(self, _widget, event):
        mods = event.state & Gtk.accelerator_get_default_mod_mask()
        keyval = Gdk.keyval_to_lower(event.keyval)
        if event.is_modifier:
            return True
        if not mods and keyval == Gdk.KEY_Escape:
            self._finish_hotkey_recording()
            return True
        if not mods and keyval in (Gdk.KEY_BackSpace, Gdk.KEY_Delete):
            self._set_hotkey("")
            return True
        is_function_key = Gdk.KEY_F1 <= keyval <= Gdk.KEY_F35
        needs = Gdk.ModifierType.CONTROL_MASK | Gdk.ModifierType.MOD1_MASK | Gdk.ModifierType.SUPER_MASK
        if not (mods & needs) and not is_function_key:
            # Plain letters would hijack normal typing everywhere.
            self.hotkey_entry.set_text("Add Ctrl, Alt or Super")
            return True
        self._set_hotkey(Gtk.accelerator_name(keyval, mods))
        return True

    def _set_hotkey(self, accel: str):
        self.recording_hotkey = False
        if not self._grab_hotkey(accel):
            self.set_status(f"{self._hotkey_label(accel)} is already used by another app. "
                            f"Kept {self._hotkey_label(self.settings.toggle_hotkey)}.", "warning")
            self._grab_hotkey(self.settings.toggle_hotkey)
        else:
            self.settings.toggle_hotkey = accel
            self.settings.save()
            self.set_status("Toggle hotkey cleared." if not accel else f"Toggle hotkey set to {self._hotkey_label(accel)}.")
        self._finish_hotkey_recording()

    def _finish_hotkey_recording(self):
        self._update_hotkey_text()
        self.window_list.grab_focus()  # move focus off the entry so the next key press isn't captured

    # ================================================================== misc

    def set_status(self, text: str, kind: str = "idle"):
        context = self.status_dot.get_style_context()
        for k in ("idle", "live", "warning"):
            context.remove_class(f"dot-{k}")
        context.add_class(f"dot-{kind}")
        self.status_text.set_text(text)

    def _on_close(self, *_):
        for entry in list(self.overlays):
            self.remove_overlay(entry)
        self.hotkey.ungrab()
        Gtk.main_quit()
        return False


def run():
    Gst.init(None)
    window = MainWindow()
    window.show_all()
    Gtk.main()
