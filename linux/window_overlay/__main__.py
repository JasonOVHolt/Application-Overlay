"""Entry point: python3 -m window_overlay"""

import os
import sys


def main():
    # Overlays need X11 features (global positioning, keep-above, window capture, key grabs).
    # On a Wayland session this runs through XWayland and can only mirror X11/XWayland apps.
    os.environ.setdefault("GDK_BACKEND", "x11")

    try:
        import gi
        gi.require_version("Gtk", "3.0")
        gi.require_version("Gdk", "3.0")
        gi.require_version("GdkPixbuf", "2.0")
        gi.require_version("Gst", "1.0")
        gi.require_foreign("cairo")
        from gi.repository import Gst
        Gst.init(None)
        missing = [name for name in ("ximagesrc", "videoconvert", "appsink") if Gst.ElementFactory.find(name) is None]
    except (ImportError, ValueError) as error:
        sys.exit(f"Missing dependency: {error}\nSee linux/README.md for the packages to install.")
    if missing:
        sys.exit(f"Missing GStreamer elements: {', '.join(missing)}\n"
                 "Install the GStreamer 'good' and 'base' plugins (see linux/README.md).")

    from .app import run
    try:
        run()
    except RuntimeError as error:  # e.g. no X server
        sys.exit(str(error))


if __name__ == "__main__":
    main()
