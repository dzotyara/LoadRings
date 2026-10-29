import SwiftUI

/// What sits inside each ring.
enum IconStyle: String, CaseIterable, Identifiable {
    case iconAndLabel, labels, outline, filled

    var id: Self { self }

    var title: String {
        switch self {
        case .iconAndLabel: return "Icon + Label"
        case .labels: return "Labels Only"
        case .outline: return "Icons"
        case .filled: return "Filled Icons"
        }
    }
}

/// Settings exposed through the widget's right-click menu, persisted in UserDefaults.
final class WidgetSettings: ObservableObject {
    @Published var locked = UserDefaults.standard.bool(forKey: "locked") {
        didSet { UserDefaults.standard.set(locked, forKey: "locked") }
    }
    @Published var iconStyle = IconStyle(rawValue: UserDefaults.standard.string(forKey: "iconStyle") ?? "")
        ?? .iconAndLabel {
        didSet { UserDefaults.standard.set(iconStyle.rawValue, forKey: "iconStyle") }
    }
    @Published var launchAtLogin = LoginItem.isEnabled
}

struct WidgetView: View {
    @ObservedObject var monitor: SystemMonitor
    @ObservedObject var settings: WidgetSettings
    var onLockChange: (Bool) -> Void

    var body: some View {
        RingGrid(metrics: monitor.metrics, style: settings.iconStyle)
            .contentShape(Rectangle())
            .contextMenu {
                Picker("Icon Style", selection: $settings.iconStyle) {
                    ForEach(IconStyle.allCases) { Text($0.title).tag($0) }
                }
                Toggle("Lock Position", isOn: Binding(
                    get: { settings.locked },
                    set: { settings.locked = $0; onLockChange($0) }))
                Toggle("Launch at Login", isOn: Binding(
                    get: { settings.launchAtLogin },
                    set: { settings.launchAtLogin = LoginItem.set(enabled: $0) }))
                Divider()
                Button("Quit LoadRings") { NSApp.terminate(nil) }
            }
    }
}

/// The 2×2 widget face, without window chrome or menus.
struct RingGrid: View {
    let metrics: [Metric]
    let style: IconStyle

    private let columns = [GridItem(.flexible(), spacing: 14), GridItem(.flexible(), spacing: 14)]

    var body: some View {
        LazyVGrid(columns: columns, spacing: 14) {
            ForEach(metrics) { RingGauge(metric: $0, style: style) }
        }
        .padding(16)
        .frame(width: WidgetLayout.size, height: WidgetLayout.size)
        // Darken the frosted wallpaper the way desktop widgets do.
        .background(Color.black.opacity(0.32))
    }
}

struct RingGauge: View {
    let metric: Metric
    let style: IconStyle

    private let lineWidth: CGFloat = 6.5

    var body: some View {
        let value = metric.value ?? 0
        ZStack {
            Circle()
                .stroke(.white.opacity(0.14), lineWidth: lineWidth)
            Circle()
                .trim(from: 0, to: max(0.005, value))
                .stroke(tint(for: value), style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .animation(.easeInOut(duration: 0.6), value: value)
            center
                .foregroundStyle(.white)
        }
        .padding(lineWidth / 2)
        .aspectRatio(1, contentMode: .fit)
        .help(metric.detail)
    }

    @ViewBuilder private var center: some View {
        switch style {
        case .iconAndLabel:
            VStack(spacing: 0) {
                Image(systemName: metric.kind.symbol(filled: false))
                    .font(.system(size: 11, weight: .semibold))
                    .padding(.bottom, 1)
                Text(metric.kind.label)
                    .font(.system(size: 9, weight: .bold, design: .rounded))
                percentText
            }
        case .labels:
            VStack(spacing: 0) {
                Text(metric.kind.label)
                    .font(.system(size: 13, weight: .bold, design: .rounded))
                percentText
            }
        case .outline, .filled:
            VStack(spacing: 1) {
                Image(systemName: metric.kind.symbol(filled: style == .filled))
                    .font(.system(size: 17, weight: .medium))
                percentText
            }
        }
    }

    private var percentText: some View {
        Text(metric.value.map { "\(Int(($0 * 100).rounded()))%" } ?? "–")
            .font(.system(size: 10, weight: .semibold, design: .rounded))
            .monospacedDigit()
            .opacity(0.75)
    }

    /// White like the system battery widget, warming up under heavy load.
    private func tint(for value: Double) -> Color {
        switch value {
        case ..<0.75: return .white
        case ..<0.9: return Color(red: 1, green: 0.78, blue: 0.2)
        default: return Color(red: 1, green: 0.36, blue: 0.33)
        }
    }
}

enum WidgetLayout {
    static let size: CGFloat = 164
    static let cornerRadius: CGFloat = 22
}
