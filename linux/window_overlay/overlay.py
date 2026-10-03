"""An always-on-top, optionally click-through window that mirrors another X11 window.

Capture: GStreamer `ximagesrc xid=...` -> appsink. Frames are drawn with cairo, letterboxed and cropped,
on an RGBA window so the letterbox area is transparent when a compositor is running.
"""

import threading
from dataclasses import dataclass
from typing import Callable, Iterable, Optional, Tuple

import cairo
from gi.repository import Gdk, GLib, Gst, Gtk

from . import snapping
from .x11 import X11

ACCENT = (76 / 255, 141 / 255, 1.0)
INACTIVE = (62 / 255, 62 / 255, 72 / 255)
EDIT_BG = (24 / 255, 24 / 255, 28 / 255)

# Sizes in logical pixels (GTK applies the display's scale factor).
BORDER = 4
GRIP = 10
MIN_W, MIN_H = 80, 50
SNAP_DISTANCE = 20
FRAME_RATE = 30


@dataclass
class Crop:
    left: float = 0.0
    top: float = 0.0
    right: float = 0.0
    bottom: float = 0.0

    def is_zero(self) -> bool:
        return not (self.left or self.top or self.right or self.bottom)


class OverlayWindow(Gtk.Window):
    def __init__(self, x11: X11, source_xid: int):
        super().__init__(type=Gtk.WindowType.TOPLEVEL)
        self.x11 = x11
        self.source_xid = source_xid
        self.client_area_only = True
        self.crop = Crop()
        self.click_through = False
        self.selected = True
        self.snap_to_edges = False
        self.other_overlays: Callable[[], Iterable[Tuple[int, int, int, int]]] = lambda: []
        self.on_source_lost: Callable[[], None] = lambda: None
        self.on_clicked: Callable[[], None] = lambda: None

        self._pipeline: Optional[Gst.Pipeline] = None
        self._capture_xid = 0
        self._capture_size = (0, 0)
        self._frame_lock = threading.Lock()
        self._frame: Optional[Tuple[bytearray, int, int, int]] = None  # data, w, h, stride
        self._redraw_pending = False
        self._drag = None  # (edges, start_rect, start_pointer)

        self.set_title("Overlay")
        self.set_decorated(False)
        self.set_keep_above(True)
        self.set_skip_taskbar_hint(True)
        self.set_skip_pager_hint(True)
        self.set_type_hint(Gdk.WindowTypeHint.UTILITY)
        self.stick()  # show on every workspace
        self.set_app_paintable(True)
        visual = self.get_screen().get_rgba_visual()
        if visual is not None:
            self.set_visual(visual)  # transparent letterbox when a compositor is running

        self.add_events(Gdk.EventMask.BUTTON_PRESS_MASK | Gdk.EventMask.BUTTON_RELEASE_MASK
                        | Gdk.EventMask.POINTER_MOTION_MASK)
        self.connect("draw", self._on_draw)
        self.connect("button-press-event", self._on_button_press)
        self.connect("button-release-event", self._on_button_release)
        self.connect("motion-notify-event", self._on_motion)
        self.connect("realize", lambda *_: self._apply_click_through())
        self.connect("destroy", lambda *_: self._stop_pipeline())

        self._watch_id = GLib.timeout_add(250, self._watch_source)
        self._restart_capture()

    # ------------------------------------------------------------------ public settings

    def set_click_through(self, value: bool):
        self.click_through = value
        self._apply_click_through()
        self.queue_draw()

    def set_overlay_opacity(self, value: float):
        self.set_opacity(max(0.1, min(1.0, value)))

    def set_client_area_only(self, value: bool):
        if self.client_area_only != value:
            self.client_area_only = value
            self._restart_capture()

    def set_crop(self, crop: Crop):
        self.crop = crop
        self.queue_draw()

    def set_selected(self, value: bool):
        self.selected = value
        self.queue_draw()

    def fit_to_source(self):
        """Resize (keeping width) so the window matches the cropped source's aspect ratio."""
        w, h = self._cropped_source_size()
        if w <= 0 or h <= 0:
            return
        inset = 0 if self.click_through else BORDER * 2
        width, _ = self.get_size()
        height = round((width - inset) * h / w) + inset
        self.resize(width, max(MIN_H, height))

    def rect(self) -> Tuple[int, int, int, int]:
        x, y = self.get_position()
        w, h = self.get_size()
        return x, y, x + w, y + h

    def close_overlay(self):
        if self._watch_id:
            GLib.source_remove(self._watch_id)
            self._watch_id = 0
        self._stop_pipeline()
        self.destroy()

    # ------------------------------------------------------------------ capture

    def _target_xid(self) -> int:
        return self.source_xid if self.client_area_only else self.x11.frame_window(self.source_xid)

    def _restart_capture(self):
        self._stop_pipeline()
        with self._frame_lock:
            self._frame = None
        self.queue_draw()
        if not self.x11.is_viewable(self.source_xid):
            return  # minimised: _watch_source restarts it when it comes back

        xid = self._target_xid()
        self._capture_xid = xid
        self._capture_size = self.x11.size(xid)
        pipeline = Gst.parse_launch(
            f"ximagesrc xid={xid} use-damage=false show-pointer=false "
            f"! video/x-raw,framerate={FRAME_RATE}/1 ! videoconvert ! video/x-raw,format=BGRx "
            f"! appsink name=sink emit-signals=true max-buffers=1 drop=true sync=false")
        sink = pipeline.get_by_name("sink")
        sink.connect("new-sample", self._on_sample)
        bus = pipeline.get_bus()
        bus.add_signal_watch()
        bus.connect("message::error", self._on_pipeline_error)
        self._pipeline = pipeline
        pipeline.set_state(Gst.State.PLAYING)

    def _stop_pipeline(self):
        if self._pipeline is not None:
            self._pipeline.get_bus().remove_signal_watch()
            self._pipeline.set_state(Gst.State.NULL)
            self._pipeline = None

    def _on_pipeline_error(self, _bus, _message):
        # Usually the source was resized, minimised or closed mid-frame; the watcher recovers.
        self._stop_pipeline()

    def _on_sample(self, sink):
        # Runs on a GStreamer thread: copy the frame, then redraw on the main thread.
        sample = sink.emit("pull-sample")
        if sample is None:
            return Gst.FlowReturn.OK
        structure = sample.get_caps().get_structure(0)
        width, height = structure.get_value("width"), structure.get_value("height")
        buffer = sample.get_buffer()
        ok, info = buffer.map(Gst.MapFlags.READ)
        if ok:
            try:
                data = bytearray(info.data)
            finally:
                buffer.unmap(info)
            stride = len(data) // height if height else 0
            with self._frame_lock:
                self._frame = (data, width, height, stride)
            if not self._redraw_pending:
                self._redraw_pending = True
                GLib.idle_add(self._redraw)
        return Gst.FlowReturn.OK

    def _redraw(self):
        self._redraw_pending = False
        self.queue_draw()
        return False

    def _watch_source(self) -> bool:
        if not self.x11.exists(self.source_xid):
            self._stop_pipeline()
            self._watch_id = 0  # returning False removes this timer
            self.on_source_lost()
            return False
        viewable = self.x11.is_viewable(self.source_xid)
        if not viewable:
            if self._pipeline is not None:
                self._restart_capture()  # stops and shows the "minimised" placeholder
        elif self._pipeline is None or self.x11.size(self._target_xid()) != self._capture_size:
            self._restart_capture()  # restored, resized, or recovered from an error
        return True

    def _source_size(self) -> Tuple[int, int]:
        with self._frame_lock:
            if self._frame:
                return self._frame[1], self._frame[2]
        return self._capture_size

    def _crop_rect(self, w: int, h: int) -> Tuple[int, int, int, int]:
        left, top = int(w * self.crop.left), int(h * self.crop.top)
        right, bottom = w - int(w * self.crop.right), h - int(h * self.crop.bottom)
        return left, top, max(left + 1, right), max(top + 1, bottom)

    def _cropped_source_size(self) -> Tuple[int, int]:
        w, h = self._source_size()
        if w <= 0 or h <= 0:
            return 0, 0
        l, t, r, b = self._crop_rect(w, h)
        return r - l, b - t

    # ------------------------------------------------------------------ drawing

    def _on_draw(self, _widget, cr):
        width, height = self.get_allocated_width(), self.get_allocated_height()
        cr.set_operator(cairo.OPERATOR_SOURCE)
        if self.click_through:
            cr.set_source_rgba(0, 0, 0, 0)
        else:
            cr.set_source_rgba(*EDIT_BG, 1)
        cr.paint()
        cr.set_operator(cairo.OPERATOR_OVER)

        inset = 0 if self.click_through else BORDER
        area_w, area_h = width - 2 * inset, height - 2 * inset

        with self._frame_lock:
            frame = self._frame
        if frame:
            data, fw, fh, stride = frame
            l, t, r, b = self._crop_rect(fw, fh)
            cw, ch = r - l, b - t
            x, y, dw, dh = snapping.fit_rect(cw, ch, area_w, area_h)
            if dw > 0 and dh > 0:
                surface = cairo.ImageSurface.create_for_data(data, cairo.FORMAT_RGB24, fw, fh, stride)
                cr.save()
                cr.rectangle(inset + x, inset + y, dw, dh)
                cr.clip()
                cr.translate(inset + x, inset + y)
                cr.scale(dw / cw, dh / ch)
                cr.set_source_surface(surface, -l, -t)
                cr.get_source().set_filter(cairo.FILTER_BILINEAR)
                cr.paint()
                cr.restore()
        elif not self.click_through:
            self._draw_placeholder(cr, width, height)

        if not self.click_through:
            cr.set_source_rgb(*(ACCENT if self.selected else INACTIVE))
            cr.set_line_width(BORDER)
            cr.rectangle(BORDER / 2, BORDER / 2, width - BORDER, height - BORDER)
            cr.stroke()
        return True

    def _draw_placeholder(self, cr, width, height):
        text = "Source is minimised" if not self.x11.is_viewable(self.source_xid) else "Waiting for the source…"
        cr.set_source_rgb(0.85, 0.85, 0.88)
        cr.select_font_face("Sans", cairo.FONT_SLANT_NORMAL, cairo.FONT_WEIGHT_NORMAL)
        cr.set_font_size(13)
        extents = cr.text_extents(text)
        cr.move_to((width - extents.width) / 2, (height + extents.height) / 2)
        cr.show_text(text)

    # ------------------------------------------------------------------ click-through

    def _apply_click_through(self):
        if self.get_window() is None:
            return
        if self.click_through:
            self.input_shape_combine_region(cairo.Region())  # empty input region: clicks pass through
            self.set_accept_focus(False)
        else:
            self.input_shape_combine_region(None)
            self.set_accept_focus(True)

    # ------------------------------------------------------------------ move / resize with snapping

    def _edges_at(self, x: float, y: float) -> int:
        width, height = self.get_size()
        edges = 0
        if x < GRIP:
            edges |= snapping.LEFT
        elif x >= width - GRIP:
            edges |= snapping.RIGHT
        if y < GRIP:
            edges |= snapping.TOP
        elif y >= height - GRIP:
            edges |= snapping.BOTTOM
        return edges

    _CURSORS = {
        snapping.LEFT: "w-resize", snapping.RIGHT: "e-resize", snapping.TOP: "n-resize", snapping.BOTTOM: "s-resize",
        snapping.TOP | snapping.LEFT: "nw-resize", snapping.TOP | snapping.RIGHT: "ne-resize",
        snapping.BOTTOM | snapping.LEFT: "sw-resize", snapping.BOTTOM | snapping.RIGHT: "se-resize",
    }

    def _on_button_press(self, _widget, event):
        if self.click_through or event.button != 1:
            return False
        self.on_clicked()
        # Remember the start so movement is computed from the real pointer delta. Snapping the
        # window's current (already snapped) position instead would make it stick to edges.
        self._drag = (self._edges_at(event.x, event.y), self.rect(), (event.x_root, event.y_root))
        return True

    def _on_button_release(self, _widget, event):
        if event.button == 1:
            self._drag = None
        return False

    def _on_motion(self, _widget, event):
        if self.click_through:
            return False
        if self._drag is None:
            name = self._CURSORS.get(self._edges_at(event.x, event.y), "move")
            self.get_window().set_cursor(Gdk.Cursor.new_from_name(self.get_display(), name))
            return False

        edges, start, (px, py) = self._drag
        dx, dy = int(event.x_root - px), int(event.y_root - py)
        if edges == 0:
            rect = (start[0] + dx, start[1] + dy, start[2] + dx, start[3] + dy)
        else:
            rect = snapping.resize_from_start(start, edges, dx, dy, MIN_W, MIN_H)

        if self.snap_to_edges:
            xs, ys = self._snap_lines(rect)
            rect = (snapping.snap_move(rect, xs, ys, SNAP_DISTANCE) if edges == 0
                    else snapping.snap_resize(rect, edges, xs, ys, SNAP_DISTANCE))

        left, top, right, bottom = rect
        self.move(left, top)
        if edges:
            self.resize(right - left, bottom - top)
        return True

    def _snap_lines(self, rect):
        # Monitor geometry is read on every move, so panel changes and monitor setups are always current.
        display = self.get_display()
        cx, cy = (rect[0] + rect[2]) // 2, (rect[1] + rect[3]) // 2
        monitor = display.get_monitor_at_point(cx, cy)
        g, w = monitor.get_geometry(), monitor.get_workarea()
        full = (g.x, g.y, g.x + g.width, g.y + g.height)
        work = (w.x, w.y, w.x + w.width, w.y + w.height)
        return snapping.snap_lines(rect, full, work, self.other_overlays(), SNAP_DISTANCE)

    def do_configure_event(self, event):
        result = Gtk.Window.do_configure_event(self, event)
        self.queue_draw()
        return result
