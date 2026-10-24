import CoreGraphics

/// Edges being dragged during a resize. AppKit coordinates: y grows upward, so `top` is `maxY`.
struct ResizeEdges: OptionSet {
    let rawValue: Int
    static let left = ResizeEdges(rawValue: 1)
    static let right = ResizeEdges(rawValue: 2)
    static let top = ResizeEdges(rawValue: 4)
    static let bottom = ResizeEdges(rawValue: 8)
}

/// Crop amounts as fractions (0...1) of the source size. Left+right and top+bottom stay below 1.
struct Crop: Equatable {
    var left = 0.0
    var top = 0.0
    var right = 0.0
    var bottom = 0.0

    var isZero: Bool { left == 0 && top == 0 && right == 0 && bottom == 0 }
}

/// Snapping geometry, free of AppKit so it can be unit-tested. Sizes are in points, so they
/// already account for display scaling.
enum Snapping {
    static let distance: CGFloat = 20

    /// Lines a rectangle can snap to: the visible frame (stops at the menu bar and Dock) and the full
    /// screen frame, plus edges of other overlays that are alongside it on the other axis.
    static func lines(for rect: CGRect, screenFrame: CGRect, visibleFrame: CGRect,
                      others: [CGRect]) -> (xs: [CGFloat], ys: [CGFloat]) {
        var xs = [visibleFrame.minX, visibleFrame.maxX, screenFrame.minX, screenFrame.maxX]
        var ys = [visibleFrame.minY, visibleFrame.maxY, screenFrame.minY, screenFrame.maxY]
        for o in others {
            if rect.minY <= o.maxY + distance && rect.maxY >= o.minY - distance { xs += [o.minX, o.maxX] }
            if rect.minX <= o.maxX + distance && rect.maxX >= o.minX - distance { ys += [o.minY, o.maxY] }
        }
        return (xs, ys)
    }

    /// Offset that puts one of `edges` on the closest line within `distance`, or 0.
    static func offset(edges: [CGFloat], lines: [CGFloat]) -> CGFloat {
        var best: CGFloat = 0
        var bestDistance = CGFloat.infinity
        for edge in edges {
            for line in lines {
                let d = abs(line - edge)
                if d <= distance && d < bestDistance {
                    best = line - edge
                    bestDistance = d
                }
            }
        }
        return best
    }

    static func snapMove(_ r: CGRect, xs: [CGFloat], ys: [CGFloat]) -> CGRect {
        r.offsetBy(dx: offset(edges: [r.minX, r.maxX], lines: xs),
                   dy: offset(edges: [r.minY, r.maxY], lines: ys))
    }

    /// Snaps only the edges being dragged.
    static func snapResize(_ r: CGRect, edges: ResizeEdges, xs: [CGFloat], ys: [CGFloat]) -> CGRect {
        var minX = r.minX, maxX = r.maxX, minY = r.minY, maxY = r.maxY
        if edges.contains(.left) { minX += offset(edges: [minX], lines: xs) }
        if edges.contains(.right) { maxX += offset(edges: [maxX], lines: xs) }
        if edges.contains(.bottom) { minY += offset(edges: [minY], lines: ys) }
        if edges.contains(.top) { maxY += offset(edges: [maxY], lines: ys) }
        return CGRect(x: minX, y: minY, width: maxX - minX, height: maxY - minY)
    }

    /// The unsnapped frame for a resize: the start frame with the dragged edges moved by the mouse delta.
    static func resizeFromStart(_ s: CGRect, edges: ResizeEdges, dx: CGFloat, dy: CGFloat, minSize: CGSize) -> CGRect {
        var minX = s.minX, maxX = s.maxX, minY = s.minY, maxY = s.maxY
        if edges.contains(.left) { minX = min(minX + dx, maxX - minSize.width) }
        if edges.contains(.right) { maxX = max(maxX + dx, minX + minSize.width) }
        if edges.contains(.bottom) { minY = min(minY + dy, maxY - minSize.height) }
        if edges.contains(.top) { maxY = max(maxY + dy, minY + minSize.height) }
        return CGRect(x: minX, y: minY, width: maxX - minX, height: maxY - minY)
    }

    /// Letterboxes `content` into `area`, preserving aspect ratio.
    static func fit(_ content: CGSize, in area: CGRect) -> CGRect {
        guard content.width > 0, content.height > 0, area.width > 0, area.height > 0 else { return .zero }
        let scale = min(area.width / content.width, area.height / content.height)
        let size = CGSize(width: content.width * scale, height: content.height * scale)
        return CGRect(x: area.midX - size.width / 2, y: area.midY - size.height / 2,
                      width: size.width, height: size.height)
    }
}
