"""Manual check: lists the windows the X11 layer can see. Run from linux/: python3 tests/probe_x11.py"""

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from window_overlay.x11 import X11  # noqa: E402

x11 = X11()
windows = x11.list_windows()
print(f"{len(windows)} windows")
for w in windows:
    icon = f"icon {w.icon[0]}x{w.icon[1]}" if w.icon else "no icon"
    print(f"  0x{w.xid:x}  {w.title!r}  [{w.app_name}]  {icon}  viewable={x11.is_viewable(w.xid)}  size={x11.size(w.xid)}")
