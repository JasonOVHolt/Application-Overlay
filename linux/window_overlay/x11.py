"""Minimal Xlib access through ctypes: window list, icons, window state and a global hotkey.

Uses its own X connection, separate from GTK's, and only from the GTK main thread.
"""

import ctypes
import os
from ctypes import (CFUNCTYPE, POINTER, Structure, Union, byref, c_char_p, c_int, c_long, c_ubyte, c_uint,
                    c_ulong, c_void_p)
from dataclasses import dataclass
from typing import Callable, List, Optional, Tuple

Window = c_ulong
Atom = c_ulong

_xlib = ctypes.cdll.LoadLibrary("libX11.so.6")

# --------------------------------------------------------------------------- structures


class XErrorEvent(Structure):
    _fields_ = [("type", c_int), ("display", c_void_p), ("resourceid", c_ulong), ("serial", c_ulong),
                ("error_code", c_ubyte), ("request_code", c_ubyte), ("minor_code", c_ubyte)]


class XWindowAttributes(Structure):
    _fields_ = [("x", c_int), ("y", c_int), ("width", c_int), ("height", c_int), ("border_width", c_int),
                ("depth", c_int), ("visual", c_void_p), ("root", Window), ("class_", c_int),
                ("bit_gravity", c_int), ("win_gravity", c_int), ("backing_store", c_int),
                ("backing_planes", c_ulong), ("backing_pixel", c_ulong), ("save_under", c_int),
                ("colormap", c_ulong), ("map_installed", c_int), ("map_state", c_int),
                ("all_event_masks", c_long), ("your_event_mask", c_long), ("do_not_propagate_mask", c_long),
                ("override_redirect", c_int), ("screen", c_void_p)]


class XKeyEvent(Structure):
    _fields_ = [("type", c_int), ("serial", c_ulong), ("send_event", c_int), ("display", c_void_p),
                ("window", Window), ("root", Window), ("subwindow", Window), ("time", c_ulong),
                ("x", c_int), ("y", c_int), ("x_root", c_int), ("y_root", c_int),
                ("state", c_uint), ("keycode", c_uint), ("same_screen", c_int)]


class XEvent(Union):
    _fields_ = [("type", c_int), ("xkey", XKeyEvent), ("pad", c_long * 24)]


_ErrorHandler = CFUNCTYPE(c_int, c_void_p, POINTER(XErrorEvent))

# --------------------------------------------------------------------------- functions


def _fn(name, restype, *argtypes):
    f = getattr(_xlib, name)
    f.restype = restype
    f.argtypes = list(argtypes)
    return f


XOpenDisplay = _fn("XOpenDisplay", c_void_p, c_char_p)
XDefaultRootWindow = _fn("XDefaultRootWindow", Window, c_void_p)
XInternAtom = _fn("XInternAtom", Atom, c_void_p, c_char_p, c_int)
XGetWindowProperty = _fn("XGetWindowProperty", c_int, c_void_p, Window, Atom, c_long, c_long, c_int, Atom,
                         POINTER(Atom), POINTER(c_int), POINTER(c_ulong), POINTER(c_ulong),
                         POINTER(POINTER(c_ubyte)))
XFree = _fn("XFree", c_int, c_void_p)
XGetWindowAttributes = _fn("XGetWindowAttributes", c_int, c_void_p, Window, POINTER(XWindowAttributes))
XQueryTree = _fn("XQueryTree", c_int, c_void_p, Window, POINTER(Window), POINTER(Window),
                 POINTER(POINTER(Window)), POINTER(c_uint))
XSetErrorHandler = _fn("XSetErrorHandler", c_void_p, _ErrorHandler)
XSync = _fn("XSync", c_int, c_void_p, c_int)
XGrabKey = _fn("XGrabKey", c_int, c_void_p, c_int, c_uint, Window, c_int, c_int, c_int)
XUngrabKey = _fn("XUngrabKey", c_int, c_void_p, c_int, c_uint, Window)
XKeysymToKeycode = _fn("XKeysymToKeycode", c_ubyte, c_void_p, c_ulong)
XConnectionNumber = _fn("XConnectionNumber", c_int, c_void_p)
XPending = _fn("XPending", c_int, c_void_p)
XNextEvent = _fn("XNextEvent", c_int, c_void_p, POINTER(XEvent))

KEY_PRESS = 2
GRAB_MODE_ASYNC = 1
IS_VIEWABLE = 2
SHIFT_MASK, LOCK_MASK, CONTROL_MASK, MOD1_MASK, MOD2_MASK, MOD4_MASK = 1, 2, 4, 8, 16, 64
XA_CARDINAL, XA_WINDOW, XA_ATOM, XA_STRING = 6, 33, 4, 31
ANY_PROPERTY_TYPE = 0
BAD_ACCESS = 10

# --------------------------------------------------------------------------- errors

# X errors are reported to a global handler; without one, Xlib would exit the process
# (e.g. when a mirrored window closes between listing and querying it).
_last_error = 0


def _on_error(_display, event):
    global _last_error
    _last_error = event.contents.error_code
    return 0


_error_handler = _ErrorHandler(_on_error)  # keep a reference so it isn't garbage collected
XSetErrorHandler(_error_handler)


# --------------------------------------------------------------------------- data


@dataclass
class WindowInfo:
    xid: int
    title: str
    app_name: str
    pid: int
    icon: Optional[Tuple[int, int, bytes]]  # (width, height, RGBA bytes)


class X11:
    def __init__(self):
        self.display = XOpenDisplay(None)
        if not self.display:
            raise RuntimeError("Cannot connect to the X server. Window Overlay needs an X11 (or XWayland) session.")
        self.root = XDefaultRootWindow(self.display)
        self._atoms = {}

    def atom(self, name: str) -> int:
        if name not in self._atoms:
            self._atoms[name] = XInternAtom(self.display, name.encode(), 0)
        return self._atoms[name]

    # ------------------------------------------------------------------ properties

    def _get_property(self, window: int, name: str, req_type: int = ANY_PROPERTY_TYPE,
                      max_longs: int = 1 << 20) -> Tuple[int, int, bytes, int]:
        """Returns (actual_type, format, raw bytes, item count). Empty bytes if missing."""
        actual_type, actual_format = Atom(), c_int()
        nitems, bytes_after = c_ulong(), c_ulong()
        data = POINTER(c_ubyte)()
        status = XGetWindowProperty(self.display, window, self.atom(name), 0, max_longs, 0, req_type,
                                    byref(actual_type), byref(actual_format), byref(nitems), byref(bytes_after),
                                    byref(data))
        if status != 0 or not data:
            return 0, 0, b"", 0
        try:
            count = nitems.value
            # Format-32 properties are returned as C longs (8 bytes on 64-bit), not 4-byte ints.
            item_size = {8: 1, 16: ctypes.sizeof(ctypes.c_short), 32: ctypes.sizeof(c_long)}.get(actual_format.value, 1)
            raw = ctypes.string_at(data, count * item_size)
            return actual_type.value, actual_format.value, raw, count
        finally:
            XFree(data)

    def _get_longs(self, window: int, name: str, req_type: int = ANY_PROPERTY_TYPE) -> List[int]:
        _, fmt, raw, count = self._get_property(window, name, req_type)
        if fmt != 32 or count == 0:
            return []
        return list((c_ulong * count).from_buffer_copy(raw))

    def _get_text(self, window: int, name: str) -> str:
        _, fmt, raw, _ = self._get_property(window, name)
        if fmt != 8 or not raw:
            return ""
        return raw.decode("utf-8" if name.startswith("_NET") else "latin-1", errors="replace").split("\0")[0]

    # ------------------------------------------------------------------ windows

    def list_windows(self) -> List[WindowInfo]:
        """Top-level application windows, in the window manager's order (EWMH _NET_CLIENT_LIST)."""
        skip_types = {self.atom(n) for n in (
            "_NET_WM_WINDOW_TYPE_DOCK", "_NET_WM_WINDOW_TYPE_DESKTOP", "_NET_WM_WINDOW_TYPE_SPLASH",
            "_NET_WM_WINDOW_TYPE_MENU", "_NET_WM_WINDOW_TYPE_TOOLBAR", "_NET_WM_WINDOW_TYPE_NOTIFICATION",
            "_NET_WM_WINDOW_TYPE_DROPDOWN_MENU", "_NET_WM_WINDOW_TYPE_POPUP_MENU", "_NET_WM_WINDOW_TYPE_TOOLTIP")}
        skip_taskbar = self.atom("_NET_WM_STATE_SKIP_TASKBAR")
        own_pid = os.getpid()

        ids = self._get_longs(self.root, "_NET_CLIENT_LIST", XA_WINDOW)
        if not ids:
            ids = self._mapped_root_children()  # no EWMH window manager (e.g. WSLg, bare X server)

        result = []
        for xid in reversed(ids):  # most recent first
            if set(self._get_longs(xid, "_NET_WM_WINDOW_TYPE", XA_ATOM)) & skip_types:
                continue
            if skip_taskbar in self._get_longs(xid, "_NET_WM_STATE", XA_ATOM):
                continue
            title = self._get_text(xid, "_NET_WM_NAME") or self._get_text(xid, "WM_NAME")
            if not title.strip():
                continue
            pids = self._get_longs(xid, "_NET_WM_PID", XA_CARDINAL)
            pid = pids[0] if pids else 0
            if pid == own_pid:
                continue
            result.append(WindowInfo(xid, title, self._app_name(xid, pid), pid, self.window_icon(xid)))
        return result

    def _children(self, window: int) -> List[int]:
        root, parent = Window(), Window()
        children = POINTER(Window)()
        count = c_uint()
        if not XQueryTree(self.display, window, byref(root), byref(parent), byref(children), byref(count)):
            return []
        try:
            return [children[i] for i in range(count.value)]
        finally:
            if children:
                XFree(children)

    def _mapped_root_children(self) -> List[int]:
        """Visible application windows straight from the X server (bottom to top).

        Window managers that reparent wrap each app window in an untitled frame, so look inside
        frames for the ICCCM client window (the one with a WM_STATE property).
        """
        result = []
        for xid in self._children(self.root):
            attrs = self.attributes(xid)
            if not attrs or attrs.map_state != IS_VIEWABLE or attrs.override_redirect \
                    or attrs.width <= 1 or attrs.height <= 1:
                continue
            client = self._find_client(xid, depth=3)
            if client:
                result.append(client)
        return result

    def _find_client(self, window: int, depth: int) -> int:
        if self._get_property(window, "WM_STATE")[3]:
            return window
        if depth > 0:
            for child in self._children(window):
                found = self._find_client(child, depth - 1)
                if found:
                    return found
        return 0

    def _app_name(self, xid: int, pid: int) -> str:
        if pid:
            try:
                with open(f"/proc/{pid}/comm", encoding="utf-8") as f:
                    return f.read().strip()
            except OSError:
                pass
        # WM_CLASS is "instance\0Class\0"
        _, fmt, raw, _ = self._get_property(xid, "WM_CLASS", XA_STRING)
        parts = raw.decode("latin-1").split("\0") if raw else []
        return parts[1] if len(parts) > 1 and parts[1] else (parts[0] if parts else "?")

    def window_icon(self, xid: int, want: int = 32) -> Optional[Tuple[int, int, bytes]]:
        """Picks the _NET_WM_ICON image closest to `want` px (preferring >=) and returns RGBA bytes."""
        values = self._get_longs(xid, "_NET_WM_ICON", XA_CARDINAL)
        best = None
        i = 0
        while i + 2 <= len(values):
            w, h = values[i], values[i + 1]
            if w <= 0 or h <= 0 or i + 2 + w * h > len(values):
                break
            better = (best is None
                      or (w >= want and (best[0] < want or w < best[0]))
                      or (best[0] < want and w > best[0]))
            if better:
                best = (w, h, i + 2)
            i += 2 + w * h
        if best is None:
            return None
        w, h, start = best
        rgba = bytearray(w * h * 4)
        for n, argb in enumerate(values[start:start + w * h]):
            rgba[n * 4] = (argb >> 16) & 0xFF
            rgba[n * 4 + 1] = (argb >> 8) & 0xFF
            rgba[n * 4 + 2] = argb & 0xFF
            rgba[n * 4 + 3] = (argb >> 24) & 0xFF
        return w, h, bytes(rgba)

    def attributes(self, xid: int) -> Optional[XWindowAttributes]:
        """Window attributes, or None if the window no longer exists."""
        global _last_error
        _last_error = 0
        attrs = XWindowAttributes()
        if not XGetWindowAttributes(self.display, xid, byref(attrs)) or _last_error:
            return None
        return attrs

    def exists(self, xid: int) -> bool:
        return self.attributes(xid) is not None

    def is_viewable(self, xid: int) -> bool:
        """False if the window is minimised/unmapped (it can't be captured then)."""
        attrs = self.attributes(xid)
        if attrs is None or attrs.map_state != IS_VIEWABLE:
            return False
        return self.atom("_NET_WM_STATE_HIDDEN") not in self._get_longs(xid, "_NET_WM_STATE", XA_ATOM)

    def size(self, xid: int) -> Tuple[int, int]:
        attrs = self.attributes(xid)
        return (attrs.width, attrs.height) if attrs else (0, 0)

    def frame_window(self, xid: int) -> int:
        """The window manager's frame around `xid` (includes the title bar), or `xid` if not reparented."""
        current = xid
        for _ in range(16):
            root, parent = Window(), Window()
            children = POINTER(Window)()
            count = c_uint()
            if not XQueryTree(self.display, current, byref(root), byref(parent), byref(children), byref(count)):
                return xid
            if children:
                XFree(children)
            if parent.value in (0, root.value):
                return current
            current = parent.value
        return xid


class HotkeyGrabber:
    """Global hotkey via XGrabKey on the root window. Feed X events with `process_events()`."""

    def __init__(self, x11: X11, on_press: Callable[[], None]):
        self.x11 = x11
        self.on_press = on_press
        self._grabbed: Optional[Tuple[int, int]] = None

    @property
    def fd(self) -> int:
        return XConnectionNumber(self.x11.display)

    def grab(self, keysym: int, modifiers: int) -> bool:
        """Grabs keysym+modifiers (X modifier mask). Returns False if another app already owns it."""
        global _last_error
        self.ungrab()
        if not keysym:
            return True
        keycode = XKeysymToKeycode(self.x11.display, keysym)
        if not keycode:
            return False
        _last_error = 0
        # Also grab with NumLock / CapsLock on, otherwise the hotkey stops working when they're active.
        for extra in (0, MOD2_MASK, LOCK_MASK, MOD2_MASK | LOCK_MASK):
            XGrabKey(self.x11.display, keycode, modifiers | extra, self.x11.root, 0, GRAB_MODE_ASYNC, GRAB_MODE_ASYNC)
        XSync(self.x11.display, 0)
        if _last_error == BAD_ACCESS:
            self._grabbed = (keycode, modifiers)
            self.ungrab()
            return False
        self._grabbed = (keycode, modifiers)
        return True

    def ungrab(self):
        if not self._grabbed:
            return
        keycode, modifiers = self._grabbed
        for extra in (0, MOD2_MASK, LOCK_MASK, MOD2_MASK | LOCK_MASK):
            XUngrabKey(self.x11.display, keycode, modifiers | extra, self.x11.root)
        XSync(self.x11.display, 0)
        self._grabbed = None

    def process_events(self):
        event = XEvent()
        while XPending(self.x11.display):
            XNextEvent(self.x11.display, byref(event))
            if event.type == KEY_PRESS:
                self.on_press()
