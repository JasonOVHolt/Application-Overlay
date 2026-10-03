"""Run with:  python3 -m unittest discover -s tests   (from the linux/ folder)"""

import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from window_overlay.snapping import (  # noqa: E402
    BOTTOM, LEFT, RIGHT, TOP, fit_rect, resize_from_start, snap_lines, snap_move, snap_offset, snap_resize,
)

# A 1920x1080 monitor with a 40 px panel at the bottom, and a second one with a panel on the left.
MONITOR = (0, 0, 1920, 1080)
WORK = (0, 0, 1920, 1040)
MONITOR2 = (1920, 0, 4480, 1440)
WORK2 = (1968, 0, 4480, 1440)
D = 20


def move(rect, others=(), monitor=MONITOR, work=WORK):
    xs, ys = snap_lines(rect, monitor, work, others, D)
    return snap_move(rect, xs, ys, D)


class SnapTests(unittest.TestCase):
    def test_free_placement_when_far_from_edges(self):
        self.assertEqual(move((500, 300, 900, 600)), (500, 300, 900, 600))

    def test_snaps_to_panel_edge(self):
        self.assertEqual(move((1500, 700, 1910, 1030)), (1510, 710, 1920, 1040))

    def test_snaps_to_real_screen_bottom_over_panel(self):
        self.assertEqual(move((1500, 740, 1910, 1070)), (1510, 750, 1920, 1080))

    def test_panel_on_left_of_second_monitor(self):
        self.assertEqual(move((1975, 100, 2400, 400), monitor=MONITOR2, work=WORK2), (1968, 100, 2393, 400))
        self.assertEqual(move((1925, 100, 2350, 400), monitor=MONITOR2, work=WORK2), (1920, 100, 2345, 400))

    def test_snaps_flush_beside_another_overlay(self):
        other = (1000, 200, 1400, 500)
        self.assertEqual(move((590, 160, 990, 460), [other]), (600, 160, 1000, 460))

    def test_snaps_beside_and_aligns_tops(self):
        other = (1000, 200, 1400, 500)
        self.assertEqual(move((590, 208, 990, 508), [other]), (600, 200, 1000, 500))

    def test_ignores_overlay_that_is_not_alongside(self):
        other = (1000, 800, 1400, 1000)  # far below
        self.assertEqual(move((590, 100, 990, 400), [other]), (590, 100, 990, 400))

    def test_nearest_line_wins(self):
        self.assertEqual(snap_offset((100,), (90, 105), D), 5)

    def test_resize_snaps_only_dragged_edge(self):
        xs, ys = snap_lines((100, 100, 1905, 500), MONITOR, WORK, [], D)
        self.assertEqual(snap_resize((100, 100, 1905, 500), RIGHT, xs, ys, D), (100, 100, 1920, 500))
        self.assertEqual(snap_resize((100, 100, 1905, 500), LEFT | TOP, xs, ys, D), (100, 100, 1905, 500))

    def test_resize_from_start_respects_minimum(self):
        self.assertEqual(resize_from_start((100, 100, 300, 200), LEFT, 500, 0, 80, 50), (220, 100, 300, 200))
        self.assertEqual(resize_from_start((100, 100, 300, 200), BOTTOM, 0, -500, 80, 50), (100, 100, 300, 150))
        self.assertEqual(resize_from_start((100, 100, 300, 200), TOP | RIGHT, 30, -20, 80, 50), (100, 80, 330, 200))

    def test_fit_rect_letterboxes(self):
        self.assertEqual(fit_rect(1920, 1080, 400, 400), (0, 87.5, 400, 225))
        self.assertEqual(fit_rect(0, 1080, 400, 400), (0, 0, 0, 0))


if __name__ == "__main__":
    unittest.main()
