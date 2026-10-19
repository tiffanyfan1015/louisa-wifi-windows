import AppKit
import Foundation

/// Checks GitHub Releases for a newer version and installs it in place.
final class Updater {
    static let shared = Updater()
    static let repo = "madeyexz/louisa-wifi"

    /// Newer release, if any. Main thread only.
    private(set) var available: (version: String, zip: URL)?
    var onChange: (() -> Void)?

    var current: String { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0" }

    func start() {
        check()
        Timer.scheduledTimer(withTimeInterval: 6 * 3600, repeats: true) { [weak self] _ in self?.check() }
    }

    func check(manual: Bool = false) {
        var request = URLRequest(url: URL(string: "https://api.github.com/repos/\(Self.repo)/releases/latest")!)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        URLSession.shared.dataTask(with: request) { data, _, _ in
            let release = data.flatMap { try? JSONDecoder().decode(Release.self, from: $0) }
            let zip = release?.assets.first { $0.name == "LouisaWiFi.zip" }?.browser_download_url
            DispatchQueue.main.async {
                guard let release, let zip else {
                    if manual { Notifier.post("檢查更新失敗，請稍後再試") }
                    return
                }
                let version = release.tag_name.hasPrefix("v") ? String(release.tag_name.dropFirst()) : release.tag_name
                guard version.compare(self.current, options: .numeric) == .orderedDescending else {
                    if manual { Notifier.post("目前已是最新版本（\(self.current)）") }
                    return
                }
                if self.available?.version != version || manual {
                    Notifier.post("有新版本 \(version)，請從 ☕ 選單選擇「更新到 \(version)」")
                }
                self.available = (version, zip)
                self.onChange?()
            }
        }.resume()
    }

    /// Download, unzip, swap the bundle after we quit, relaunch.
    func install() {
        guard let update = available else { return }
        let (version, zip) = update
        let target = Bundle.main.bundleURL
        guard FileManager.default.isWritableFile(atPath: target.deletingLastPathComponent().path) else {
            NSWorkspace.shared.open(URL(string: "https://github.com/\(Self.repo)/releases/latest")!)
            return
        }
        Notifier.post("正在下載 \(version)…")
        URLSession.shared.downloadTask(with: zip) { file, _, _ in
            do {
                guard let file else { throw CocoaError(.fileNoSuchFile) }
                let dir = FileManager.default.temporaryDirectory.appendingPathComponent("LouisaWiFi-\(version)-\(UUID().uuidString)")
                try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
                let archive = dir.appendingPathComponent("LouisaWiFi.zip")
                try FileManager.default.moveItem(at: file, to: archive)
                try Self.run("/usr/bin/ditto", ["-x", "-k", archive.path, dir.path])
                let fresh = dir.appendingPathComponent("LouisaWiFi.app")
                guard FileManager.default.fileExists(atPath: fresh.path) else { throw CocoaError(.fileNoSuchFile) }

                // Runs after we exit; child processes outlive the app.
                let swap = Process()
                swap.executableURL = URL(fileURLWithPath: "/bin/sh")
                swap.arguments = ["-c", "sleep 1; rm -rf \"$1\" && mv \"$2\" \"$1\"; xattr -dr com.apple.quarantine \"$1\"; open \"$1\"",
                                  "sh", target.path, fresh.path]
                try swap.run()
                debugLog("updating to \(version)")
                DispatchQueue.main.async { NSApp.terminate(nil) }
            } catch {
                debugLog("update failed: \(error.localizedDescription)")
                DispatchQueue.main.async { Notifier.post("更新失敗，請到 GitHub 下載新版") }
            }
        }.resume()
    }

    private static func run(_ tool: String, _ args: [String]) throws {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: tool)
        p.arguments = args
        try p.run()
        p.waitUntilExit()
        guard p.terminationStatus == 0 else { throw CocoaError(.fileReadCorruptFile) }
    }

    private struct Release: Decodable {
        let tag_name: String
        let assets: [Asset]
    }

    private struct Asset: Decodable {
        let name: String
        let browser_download_url: URL
    }
}
