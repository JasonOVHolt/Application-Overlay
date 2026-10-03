"""Snapping geometry, kept free of GTK so it can be unit-tested.

Rectangles are (left, top, right, bottom) tuples in the same pixel space as window positions.
"""

from typing import Iterable, List, Sequence, Tuple

Rect = Tuple[int, int, int, int]

# Resize edges
LEFT, RIGHT, TOP, BOTTOM = 1, 2, 4, 8


def snap_lines(rect: Rect, monitor: Rect, workarea: Rect, others: Iterable[Rect],
               distance: int) -> Tuple[List[int], List[int]]:
    """Vertical (x) and horizontal (y) lines the rectangle can snap to.

    Includes both the work area edges (which stop at panels/docks) and the full monitor edges.
    Other overlays only count when they are alongside this one on the other axis, so they act as
    neighbours instead of tugging from across the screen.
    """
    xs = [workarea[0], workarea[2], monitor[0], monitor[2]]
    ys = [workarea[1], workarea[3], monitor[1], monitor[3]]
    left, top, right, bottom = rect
    for o in others:
        if top <= o[3] + distance and bottom >= o[1] - distance:
            xs += [o[0], o[2]]
        if left <= o[2] + distance and right >= o[0] - distance:
            ys += [o[1], o[3]]
    return xs, ys


def snap_offset(edges: Sequence[int], lines: Sequence[int], distance: int) -> int:
    """Offset that puts one of `edges` on the closest line within `distance`, or 0."""
    best, best_distance = 0, None
    for edge in edges:
        for line in lines:
            d = abs(line - edge)
            if d <= distance and (best_distance is None or d < best_distance):
                best, best_distance = line - edge, d
    return best


def snap_move(rect: Rect, xs: Sequence[int], ys: Sequence[int], distance: int) -> Rect:
    left, top, right, bottom = rect
    dx = snap_offset((left, right), xs, distance)
    dy = snap_offset((top, bottom), ys, distance)
    return left + dx, top + dy, right + dx, bottom + dy


def snap_resize(rect: Rect, edges: int, xs: Sequence[int], ys: Sequence[int], distance: int) -> Rect:
    """Snap only the edges being dragged."""
    left, top, right, bottom = rect
    if edges & LEFT:
        left += snap_offset((left,), xs, distance)
    if edges & RIGHT:
        right += snap_offset((right,), xs, distance)
    if edges & TOP:
        top += snap_offset((top,), ys, distance)
    if edges & BOTTOM:
        bottom += snap_offset((bottom,), ys, distance)
    return left, top, right, bottom


def resize_from_start(start: Rect, edges: int, dx: int, dy: int, min_w: int, min_h: int) -> Rect:
    """The unsnapped rectangle for a resize: the start rectangle with the dragged edges moved."""
    left, top, right, bottom = start
    if edges & LEFT:
        left = min(left + dx, right - min_w)
    if edges & RIGHT:
        right = max(right + dx, left + min_w)
    if edges & TOP:
        top = min(top + dy, bottom - min_h)
    if edges & BOTTOM:
        bottom = max(bottom + dy, top + min_h)
    return left, top, right, bottom


def fit_rect(content_w: int, content_h: int, area_w: int, area_h: int) -> Tuple[float, float, float, float]:
    """Letterbox content into an area, preserving aspect ratio. Returns (x, y, w, h)."""
    if content_w <= 0 or content_h <= 0 or area_w <= 0 or area_h <= 0:
        return 0, 0, 0, 0
    scale = min(area_w / content_w, area_h / content_h)
    w, h = content_w * scale, content_h * scale
    return (area_w - w) / 2, (area_h - h) / 2, w, h
