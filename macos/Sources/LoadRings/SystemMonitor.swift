import Foundation
import IOKit

enum MetricKind: String {
    case cpu = "CPU", gpu = "GPU", ram = "RAM", ssd = "SSD"

    var label: String { rawValue }

    func symbol(filled: Bool) -> String {
        let base: String
        switch self {
        case .cpu: base = "cpu"
        case .gpu: base = "cube"
        case .ram: base = "memorychip"
        case .ssd: base = "internaldrive"
        }
        return filled ? base + ".fill" : base
    }
}

/// One gauge on the widget.
struct Metric: Identifiable {
    let kind: MetricKind
    /// 0...1, or nil when the value can't be read on this machine.
    let value: Double?
    let detail: String

    var id: MetricKind { kind }
}

/// Samples CPU, GPU, memory and disk usage with public Mach / IOKit APIs.
final class SystemMonitor: ObservableObject {
    @Published private(set) var metrics: [Metric] = []

    private var timer: Timer?
    private var previousCPUTicks: [UInt32]?

    func start(interval: TimeInterval = 2) {
        sample()
        timer = Timer.scheduledTimer(withTimeInterval: interval, repeats: true) { [weak self] _ in
            self?.sample()
        }
    }

    private func sample() {
        let cpu = cpuUsage()
        let gpu = gpuUsage()
        let (memUsed, memTotal) = memoryUsage()
        let (diskUsed, diskTotal) = diskUsage()

        metrics = [
            Metric(kind: .cpu, value: cpu,
                   detail: cpu.map { "CPU \(percent($0))" } ?? "CPU n/a"),
            Metric(kind: .gpu, value: gpu,
                   detail: gpu.map { "GPU \(percent($0))" } ?? "GPU n/a"),
            Metric(kind: .ram, value: ratio(memUsed, memTotal),
                   detail: "RAM \(bytes(memUsed)) / \(bytes(memTotal))"),
            Metric(kind: .ssd, value: ratio(diskUsed, diskTotal),
                   detail: "SSD \(bytes(diskUsed)) / \(bytes(diskTotal))"),
        ]
    }

    // MARK: - CPU

    /// Share of non-idle ticks across all cores since the previous sample.
    private func cpuUsage() -> Double? {
        var cpuCount: natural_t = 0
        var info: processor_info_array_t?
        var infoCount: mach_msg_type_number_t = 0
        guard host_processor_info(mach_host_self(), PROCESSOR_CPU_LOAD_INFO,
                                  &cpuCount, &info, &infoCount) == KERN_SUCCESS,
              let info else { return nil }
        defer {
            vm_deallocate(mach_task_self_, vm_address_t(bitPattern: info),
                          vm_size_t(infoCount) * vm_size_t(MemoryLayout<integer_t>.stride))
        }

        let ticks = (0..<Int(infoCount)).map { UInt32(bitPattern: info[$0]) }
        defer { previousCPUTicks = ticks }
        guard let previous = previousCPUTicks, previous.count == ticks.count else { return nil }

        var busy: UInt64 = 0, total: UInt64 = 0
        let states = Int(CPU_STATE_MAX)
        for core in 0..<Int(cpuCount) {
            for state in 0..<states {
                let i = core * states + state
                let delta = UInt64(ticks[i] &- previous[i])
                total += delta
                if state != Int(CPU_STATE_IDLE) { busy += delta }
            }
        }
        return total > 0 ? Double(busy) / Double(total) : nil
    }

    // MARK: - GPU

    /// "Device Utilization %" reported by the IOAccelerator driver (Apple Silicon and most Intel/AMD GPUs).
    private func gpuUsage() -> Double? {
        var iterator: io_iterator_t = 0
        guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("IOAccelerator"),
                                           &iterator) == KERN_SUCCESS else { return nil }
        defer { IOObjectRelease(iterator) }

        var best: Double?
        while case let service = IOIteratorNext(iterator), service != 0 {
            defer { IOObjectRelease(service) }
            guard let stats = IORegistryEntryCreateCFProperty(service, "PerformanceStatistics" as CFString,
                                                              kCFAllocatorDefault, 0)?
                    .takeRetainedValue() as? [String: Any],
                  let utilization = stats["Device Utilization %"] as? NSNumber else { continue }
            best = max(best ?? 0, utilization.doubleValue / 100)
        }
        return best
    }

    // MARK: - Memory

    /// "Memory Used" as Activity Monitor shows it: app memory + wired + compressed.
    private func memoryUsage() -> (used: UInt64, total: UInt64) {
        let total = ProcessInfo.processInfo.physicalMemory
        var stats = vm_statistics64()
        var count = mach_msg_type_number_t(MemoryLayout<vm_statistics64>.stride / MemoryLayout<integer_t>.stride)
        let result = withUnsafeMutablePointer(to: &stats) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics64(mach_host_self(), HOST_VM_INFO64, $0, &count)
            }
        }
        guard result == KERN_SUCCESS else { return (0, total) }

        let pageSize = UInt64(vm_kernel_page_size)
        let appPages = UInt64(stats.internal_page_count) - min(UInt64(stats.purgeable_count),
                                                               UInt64(stats.internal_page_count))
        let used = (appPages + UInt64(stats.wire_count) + UInt64(stats.compressor_page_count)) * pageSize
        return (min(used, total), total)
    }

    // MARK: - Disk

    private func diskUsage() -> (used: UInt64, total: UInt64) {
        let url = URL(fileURLWithPath: "/")
        guard let values = try? url.resourceValues(forKeys: [.volumeTotalCapacityKey,
                                                             .volumeAvailableCapacityForImportantUsageKey]),
              let total = values.volumeTotalCapacity,
              let free = values.volumeAvailableCapacityForImportantUsage else { return (0, 0) }
        return (UInt64(max(0, Int64(total) - free)), UInt64(total))
    }

    // MARK: - Formatting

    private func ratio(_ used: UInt64, _ total: UInt64) -> Double? {
        total > 0 ? Double(used) / Double(total) : nil
    }

    private func percent(_ value: Double) -> String {
        "\(Int((value * 100).rounded()))%"
    }

    private func bytes(_ value: UInt64) -> String {
        ByteCountFormatter.string(fromByteCount: Int64(value), countStyle: .memory)
    }
}
