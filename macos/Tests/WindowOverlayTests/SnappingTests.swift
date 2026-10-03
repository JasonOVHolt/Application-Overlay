import XCTest
@testable import WindowOverlay

/// Mirrors linux/tests/test_snapping.py, in AppKit's y-up coordinates.
/// A 1440x900 screen with the Dock along the bottom (visible frame starts 70 pt up) and a 25 pt menu bar.
final class SnappingTests: XCTestCase {
    let screen = CGRect(x: 0, y: 0, width: 1440, height: 900)
    let visible = CGRect(x: 0, y: 70, width: 1440, height: 805)

    func move(_ r: CGRect, _ others: [CGRect] = []) -> CGRect {
        let (xs, ys) = Snapping.lines(for: r, screenFrame: screen, visibleFrame: visible, others: others)
        return Snapping.snapMove(r, xs: xs, ys: ys)
    }

    func testFreePlacementAwayFromEdges() {
        XCTAssertEqual(move(CGRect(x: 400, y: 300, width: 300, height: 200)), CGRect(x: 400, y: 300, width: 300, height: 200))
    }

    func testSnapsToDockEdge() {
        XCTAssertEqual(move(CGRect(x: 1130, y: 80, width: 300, height: 200)), CGRect(x: 1140, y: 70, width: 300, height: 200))
    }

    func testSnapsToRealScreenBottomOverDock() {
        XCTAssertEqual(move(CGRect(x: 1130, y: 8, width: 300, height: 200)), CGRect(x: 1140, y: 0, width: 300, height: 200))
    }

    func testSnapsBelowMenuBar() {
        XCTAssertEqual(move(CGRect(x: 400, y: 665, width: 300, height: 200)), CGRect(x: 400, y: 675, width: 300, height: 200))
    }

    func testSnapsBesideAndAlignsWithAnotherOverlay() {
        let other = CGRect(x: 800, y: 300, width: 300, height: 200)
        XCTAssertEqual(move(CGRect(x: 490, y: 308, width: 300, height: 200), [other]),
                       CGRect(x: 500, y: 300, width: 300, height: 200))
    }

    func testIgnoresOverlayThatIsNotAlongside() {
        let other = CGRect(x: 800, y: 600, width: 300, height: 150)
        XCTAssertEqual(move(CGRect(x: 490, y: 200, width: 300, height: 200), [other]),
                       CGRect(x: 490, y: 200, width: 300, height: 200))
    }

    func testResizeFromStartRespectsMinimum() {
        let start = CGRect(x: 100, y: 100, width: 200, height: 100)
        XCTAssertEqual(Snapping.resizeFromStart(start, edges: .left, dx: 500, dy: 0, minSize: CGSize(width: 80, height: 50)),
                       CGRect(x: 220, y: 100, width: 80, height: 100))
        XCTAssertEqual(Snapping.resizeFromStart(start, edges: [.top, .right], dx: 30, dy: 20, minSize: CGSize(width: 80, height: 50)),
                       CGRect(x: 100, y: 100, width: 230, height: 120))
    }

    func testFitLetterboxes() {
        XCTAssertEqual(Snapping.fit(CGSize(width: 1920, height: 1080), in: CGRect(x: 0, y: 0, width: 400, height: 400)),
                       CGRect(x: 0, y: 87.5, width: 400, height: 225))
    }
}
