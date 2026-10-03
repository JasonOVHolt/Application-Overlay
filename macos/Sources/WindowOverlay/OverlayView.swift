import AppKit
import QuartzCore

/// Borderless, always-on-top panel that never activates the app. Click-through is `ignoresMouseEvents`.
final class OverlayPanel: NSPanel {
    init(frame: CGRect) {
        super.init(contentRect: frame, styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .floating
        // Show on every Space, including next to full-screen apps.
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        isOpaque = false
        backgroundColor = .clear // transparent letterbox
        hasShadow = false
        hidesOnDeactivate = false
        isReleasedWhenClosed = false
        isMovable = false // moving is done by OverlayView so it can snap
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

/// Shows the captured frames (cropped and letterboxed) and handles move/resize with snapping.
final class OverlayView: NSView {
    static let minSize = CGSize(width: 80, height: 50)
    private static let accent = NSColor(srgbRed: 76 / 255, green: 141 / 255, blue: 1, alpha: 1)
    private static let inactive = NSColor(srgbRed: 62 / 255, green: 62 / 255, blue: 72 / 255, alpha: 1)
    private static let editBackground = NSColor(srgbRed: 24 / 255, green: 24 / 255, blue: 28 / 255, alpha: 1)

    // Points, so they already follow the display's scaling.
    private let border: CGFloat = 4
    private let grip: CGFloat = 10

    var onClicked: (() -> Void)?
    /// Returns snap lines for a proposed frame, or nil when snapping is off.
    var snapLines: ((CGRect) -> (xs: [CGFloat], ys: [CGFloat]))?

    var isLocked = false { didSet { updateAppearance() } }
    var isSelected = true { didSet { updateAppearance() } }
    var crop = Crop() { didSet { needsLayout = true } }
    private(set) var sourcePixelSize: CGSize = .zero

    private let clipLayer = CALayer()
    private let contentLayer = CALayer()
    private let placeholder = NSTextField(labelWithString: "Waiting for the source…")

    private var dragStartMouse = CGPoint.zero
    private var dragStartFrame = CGRect.zero
    private var dragEdges: ResizeEdges = []

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        clipLayer.masksToBounds = true
        contentLayer.contentsGravity = .resize
        contentLayer.magnificationFilter = .linear
        contentLayer.minificationFilter = .trilinear
        clipLayer.addSublayer(contentLayer)
        layer?.addSublayer(clipLayer)

        placeholder.textColor = NSColor(white: 0.85, alpha: 1)
        placeholder.alignment = .center
        addSubview(placeholder)
        updateAppearance()
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    override var isFlipped: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    // MARK: Frames

    func show(surface: IOSurfaceRef, size: CGSize) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        contentLayer.contents = surface
        if size != sourcePixelSize {
            sourcePixelSize = size
            layoutContent()
        }
        CATransaction.commit()
        placeholder.isHidden = true
    }

    func showPlaceholder(_ text: String) {
        contentLayer.contents = nil
        placeholder.stringValue = text
        placeholder.isHidden = false
        needsLayout = true
    }

    /// Source size after cropping, in pixels.
    func croppedSize(of size: CGSize) -> CGSize {
        CGSize(width: size.width * (1 - crop.left - crop.right), height: size.height * (1 - crop.top - crop.bottom))
    }

    override func layout() {
        super.layout()
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        layoutContent()
        CATransaction.commit()
        placeholder.sizeToFit()
        placeholder.frame.origin = CGPoint(x: (bounds.width - placeholder.frame.width) / 2,
                                           y: (bounds.height - placeholder.frame.height) / 2)
    }

    private func layoutContent() {
        let inset = isLocked ? 0 : border
        let area = bounds.insetBy(dx: inset, dy: inset)
        let w = sourcePixelSize.width, h = sourcePixelSize.height
        guard w > 0, h > 0 else { return }
        let visible = croppedSize(of: sourcePixelSize)
        let fitted = Snapping.fit(visible, in: area)
        guard fitted.width > 0 else { return }
        let scale = fitted.width / visible.width
        // The clip layer shows the cropped part; the full image sits inside it, shifted so the
        // crop's left/bottom edges line up with the clip's origin (y grows upward here).
        clipLayer.frame = fitted
        contentLayer.frame = CGRect(x: -w * crop.left * scale, y: -h * crop.bottom * scale,
                                    width: w * scale, height: h * scale)
    }

    private func updateAppearance() {
        layer?.backgroundColor = isLocked ? NSColor.clear.cgColor : Self.editBackground.cgColor
        layer?.borderWidth = isLocked ? 0 : border
        layer?.borderColor = (isSelected ? Self.accent : Self.inactive).cgColor
        needsLayout = true
        window?.invalidateCursorRects(for: self)
    }

    // MARK: Move / resize

    private func edges(at p: CGPoint) -> ResizeEdges {
        var e: ResizeEdges = []
        if p.x < grip { e.insert(.left) } else if p.x >= bounds.width - grip { e.insert(.right) }
        if p.y < grip { e.insert(.bottom) } else if p.y >= bounds.height - grip { e.insert(.top) }
        return e
    }

    override func mouseDown(with event: NSEvent) {
        guard let window, !isLocked else { return }
        onClicked?()
        // Work from the start of the drag: snapping the window's current (already snapped)
        // position instead would make it stick to edges.
        dragStartMouse = NSEvent.mouseLocation
        dragStartFrame = window.frame
        dragEdges = edges(at: convert(event.locationInWindow, from: nil))
    }

    override func mouseDragged(with event: NSEvent) {
        guard let window, !isLocked else { return }
        let mouse = NSEvent.mouseLocation
        let dx = mouse.x - dragStartMouse.x, dy = mouse.y - dragStartMouse.y
        var frame = dragEdges.isEmpty
            ? dragStartFrame.offsetBy(dx: dx, dy: dy)
            : Snapping.resizeFromStart(dragStartFrame, edges: dragEdges, dx: dx, dy: dy, minSize: Self.minSize)
        if let snapLines {
            let (xs, ys) = snapLines(frame)
            frame = dragEdges.isEmpty
                ? Snapping.snapMove(frame, xs: xs, ys: ys)
                : Snapping.snapResize(frame, edges: dragEdges, xs: xs, ys: ys)
        }
        window.setFrame(frame, display: true)
    }

    override func resetCursorRects() {
        guard !isLocked else { return }
        let w = bounds.width, h = bounds.height
        addCursorRect(CGRect(x: 0, y: grip, width: grip, height: max(0, h - 2 * grip)), cursor: .resizeLeftRight)
        addCursorRect(CGRect(x: w - grip, y: grip, width: grip, height: max(0, h - 2 * grip)), cursor: .resizeLeftRight)
        addCursorRect(CGRect(x: grip, y: 0, width: max(0, w - 2 * grip), height: grip), cursor: .resizeUpDown)
        addCursorRect(CGRect(x: grip, y: h - grip, width: max(0, w - 2 * grip), height: grip), cursor: .resizeUpDown)
        addCursorRect(bounds.insetBy(dx: grip, dy: grip), cursor: .openHand)
    }
}
