import AppKit
import ServiceManagement
import SwiftUI

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let monitor = SystemMonitor()
    private let settings = WidgetSettings()
    private var window: NSPanel?

    func applicationDidFinishLaunching(_ notification: Notification) {
        monitor.start()

        let frame = NSRect(x: 0, y: 0, width: WidgetLayout.size, height: WidgetLayout.size)
        let panel = NSPanel(contentRect: frame, styleMask: [.borderless, .nonactivatingPanel],
                            backing: .buffered, defer: false)
        // Sit on the desktop next to the system widgets: below app windows, above the wallpaper.
        panel.level = NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.desktopIconWindow)) + 1)
        panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.isMovableByWindowBackground = !settings.locked

        // Frosted background, clipped to the widget's rounded shape.
        let background = NSVisualEffectView(frame: frame)
        background.material = .hudWindow
        background.blendingMode = .behindWindow
        background.state = .active
        background.maskImage = Self.roundedMask(size: frame.size, radius: WidgetLayout.cornerRadius)

        let content = NSHostingView(rootView: WidgetView(monitor: monitor, settings: settings) { [weak panel] locked in
            panel?.isMovableByWindowBackground = !locked
        })
        content.frame = background.bounds
        content.autoresizingMask = [.width, .height]
        background.addSubview(content)
        panel.contentView = background

        if !panel.setFrameUsingName("LoadRingsWidget") {
            Self.placeTopRight(panel)
        }
        panel.setFrameAutosaveName("LoadRingsWidget")
        panel.orderFrontRegardless()
        window = panel
    }

    /// First launch goes top-right so it doesn't land on top of system widgets (top-left by default).
    private static func placeTopRight(_ panel: NSPanel) {
        guard let screen = NSScreen.main?.visibleFrame else { return }
        panel.setFrameTopLeftPoint(NSPoint(x: screen.maxX - WidgetLayout.size - 24, y: screen.maxY - 24))
    }

    private static func roundedMask(size: NSSize, radius: CGFloat) -> NSImage {
        let image = NSImage(size: size, flipped: false) { rect in
            NSColor.black.setFill()
            NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).fill()
            return true
        }
        image.capInsets = NSEdgeInsets(top: radius, left: radius, bottom: radius, right: radius)
        image.resizingMode = .stretch
        return image
    }
}

enum LoginItem {
    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    /// Returns the resulting state, which stays unchanged if macOS refuses (e.g. running outside an .app bundle).
    static func set(enabled: Bool) -> Bool {
        do {
            if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
        } catch {
            NSLog("LoadRings: launch at login change failed: \(error)")
        }
        return isEnabled
    }
}

MainActor.assumeIsolated {
    let app = NSApplication.shared
    let delegate = AppDelegate()
    app.delegate = delegate
    app.setActivationPolicy(.accessory)
    app.run()
}
