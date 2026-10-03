"""Opens a plain X11 window that cycles colours, to use as a capture source when testing.

Run from linux/: python3 tests/x11_test_window.py [seconds] [title]
Only needs libX11 (no GTK), so it also works on minimal systems.
"""

import ctypes
import sys
import time
from ctypes import c_char_p, c_int, c_uint, c_ulong, c_void_p

x = ctypes.cdll.LoadLibrary("libX11.so.6")
x.XOpenDisplay.restype = c_void_p
x.XOpenDisplay.argtypes = [c_char_p]
x.XDefaultRootWindow.restype = c_ulong
x.XDefaultRootWindow.argtypes = [c_void_p]
x.XCreateSimpleWindow.restype = c_ulong
x.XCreateSimpleWindow.argtypes = [c_void_p, c_ulong, c_int, c_int, c_uint, c_uint, c_uint, c_ulong, c_ulong]
x.XStoreName.argtypes = [c_void_p, c_ulong, c_char_p]
x.XMapWindow.argtypes = [c_void_p, c_ulong]
x.XSetWindowBackground.argtypes = [c_void_p, c_ulong, c_ulong]
x.XClearWindow.argtypes = [c_void_p, c_ulong]
x.XFlush.argtypes = [c_void_p]

seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 30
title = sys.argv[2] if len(sys.argv) > 2 else "Overlay Test Source"

display = x.XOpenDisplay(None)
window = x.XCreateSimpleWindow(display, x.XDefaultRootWindow(display), 100, 100, 640, 360, 0, 0, 0x4C8DFF)
x.XStoreName(display, window, title.encode())
x.XMapWindow(display, window)
x.XFlush(display)
print(f"0x{window:x}", flush=True)

colours = [0x4C8DFF, 0x3DD68C, 0xF5B43C, 0xE5484D]
end = time.time() + seconds
i = 0
while time.time() < end:
    x.XSetWindowBackground(display, window, colours[i % len(colours)])
    x.XClearWindow(display, window)
    x.XFlush(display)
    i += 1
    time.sleep(0.5)
