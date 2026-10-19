import Combine
import CoreWLAN
import Foundation

/// Counts minutes spent on Louisa Wi-Fi, per day and store.
/// Timers don't fire while the Mac sleeps, so a closed lid isn't counted.
final class Tracker: ObservableObject {
    static let shared = Tracker()
    static let unknownStore = "未知門市"

    /// "yyyy-MM-dd" → store name → seconds
    @Published private(set) var usage: [String: [String: Double]]
    private static let url = supportDir.appendingPathComponent("usage.json")

    static let dayKey: DateFormatter = {
        let f = DateFormatter()
        f.calendar = Calendar(identifier: .gregorian)
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()

    private init() {
        let data = try? Data(contentsOf: Self.url)
        usage = data.flatMap { try? JSONDecoder().decode([String: [String: Double]].self, from: $0) } ?? [:]
    }

    func start() {
        Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in self?.tick() }
    }

    private func tick() {
        guard let i = CWWiFiClient.shared().interface(), i.rssiValue() != 0, i.ssid() == louisaSSID else { return }
        let store = Joiner.shared.currentStore() ?? Self.unknownStore
        usage[Self.dayKey.string(from: Date()), default: [:]][store, default: 0] += 60
        try? FileManager.default.createDirectory(at: supportDir, withIntermediateDirectories: true)
        try? JSONEncoder().encode(usage).write(to: Self.url)
    }
}
