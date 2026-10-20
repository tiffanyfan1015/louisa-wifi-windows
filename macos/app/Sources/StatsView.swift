import AppKit
import SwiftUI

/// "連線統計", modeled on GitHub's contribution graph: pick a year on the right,
/// click a day (or use the arrow keys) to see where you were, and break the time
/// down by store, month or year. Clicking a store filters everything to it.
struct StatsView: View {
    @ObservedObject var tracker: Tracker
    @State private var range: StatsRange = .pastYear
    @State private var selected: Date?
    @State private var store: String?
    @State private var breakdown: Breakdown = .stores

    enum Breakdown: String, CaseIterable { case stores = "門市", months = "月份", years = "年份" }

    private var years: [Int] {
        let current = Calendar.mondayFirst.component(.year, from: Date())
        return Array(Set(tracker.usage.keys.compactMap { Int($0.prefix(4)) } + [current])).sorted(by: >)
    }

    /// Seconds per day, limited to the selected store if any.
    private var perDay: [String: Double] {
        tracker.usage.mapValues { stores in store.map { stores[$0] ?? 0 } ?? stores.values.reduce(0, +) }
    }

    var body: some View {
        let period = Period(range)
        let perDay = perDay
        let days = period.days.map { perDay[Tracker.dayKey.string(from: $0)] ?? 0 }
        let total = days.reduce(0, +)
        let visits = days.filter { $0 > 0 }.count
        let stores = storeTotals(period)

        HStack(alignment: .top, spacing: 20) {
            VStack(alignment: .leading, spacing: 22) {
                HStack(spacing: 10) {
                    Text("\(store.map { "\($0)：" } ?? "")\(range.heading)累計連線 \(duration(total))")
                        .font(.title3.weight(.semibold))
                    if store != nil {
                        Button("顯示全部門市") { store = nil }.controlSize(.small)
                    }
                }

                HStack(spacing: 12) {
                    Tile(label: "到訪天數", value: "\(visits) 天")
                    Tile(label: "到訪門市", value: "\(stores.filter { $0.name != Tracker.unknownStore }.count) 家")
                    Tile(label: "最長連續", value: "\(longestStreak(days)) 天")
                    Tile(label: "平均每天", value: visits > 0 ? duration(total / Double(visits)) : "—")
                }

                HeatmapGrid(period: period, perDay: perDay, selected: $selected, caption: caption(for:))

                VStack(alignment: .leading, spacing: 12) {
                    Picker("", selection: $breakdown) {
                        ForEach(Breakdown.allCases, id: \.self) { Text($0.rawValue) }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .fixedSize()

                    switch breakdown {
                    case .stores:
                        BarList(rows: stores.map { BarRow(id: $0.name, label: $0.name, seconds: $0.seconds) },
                                selected: store, empty: "這段期間沒有紀錄。連上路易莎 Wi-Fi 後會自動計時。") { name in
                            store = store == name ? nil : name
                        }
                    case .months:
                        MonthColumns(months: monthTotals(period, perDay))
                    case .years:
                        BarList(rows: yearTotals(perDay).map { BarRow(id: "\($0.year)", label: "\($0.year) 年", seconds: $0.seconds) },
                                selected: selectedYearID,
                                empty: "還沒有紀錄。") { id in
                            if let y = Int(id) { range = .year(y) }
                        }
                    }
                }
            }
            .frame(width: HeatmapGrid.width)

            YearPicker(ranges: [.pastYear] + years.map { .year($0) }, range: $range)
        }
        .padding(24)
        .modifier(ArrowKeys(selected: $selected, period: period))
        .onChange(of: range) { _ in selected = nil }
    }

    /// One line under the grid, like GitHub: the selected day's stores, or a hint.
    private func caption(for date: Date?) -> Text {
        guard let date else {
            return Text("點選格子，查看當天的門市與連線時間").foregroundColor(.secondary)
        }
        let stores = (tracker.usage[Tracker.dayKey.string(from: date)] ?? [:])
            .filter { $0.value > 0 }.sorted { $0.value > $1.value }
        let day = Text("\(dayLabel(date))（\(weekday(date))）").fontWeight(.semibold)
        let detail = stores.isEmpty ? "無紀錄" : stores.map { "\($0.key) \(duration($0.value))" }.joined(separator: "・")
        return day + Text("　\(detail)")
    }

    private var selectedYearID: String? {
        if case .year(let y) = range { return "\(y)" }
        return nil
    }

    private func storeTotals(_ period: Period) -> [(name: String, seconds: Double)] {
        var totals: [String: Double] = [:]
        for day in period.days {
            for (name, s) in tracker.usage[Tracker.dayKey.string(from: day)] ?? [:] { totals[name, default: 0] += s }
        }
        return totals.map { (name: $0.key, seconds: $0.value) }.sorted { $0.seconds > $1.seconds }
    }

    /// The 12 months of the selected year, or the last 12 months.
    private func monthTotals(_ period: Period, _ perDay: [String: Double]) -> [(label: String, seconds: Double)] {
        let cal = Calendar.mondayFirst
        let lastMonth = cal.date(from: cal.dateComponents([.year, .month], from: period.end))!
        return (0..<12).reversed().map { back in
            let month = cal.date(byAdding: .month, value: -back, to: lastMonth)!
            let prefix = String(Tracker.dayKey.string(from: month).prefix(7))
            let seconds = perDay.filter { $0.key.hasPrefix(prefix) }.values.reduce(0, +)
            return (label: "\(cal.component(.month, from: month))月", seconds: seconds)
        }
    }

    private func yearTotals(_ perDay: [String: Double]) -> [(year: Int, seconds: Double)] {
        var totals: [Int: Double] = [:]
        for (day, s) in perDay where s > 0 { if let y = Int(day.prefix(4)) { totals[y, default: 0] += s } }
        return totals.map { (year: $0.key, seconds: $0.value) }.sorted { $0.year > $1.year }
    }

    private func longestStreak(_ days: [Double]) -> Int {
        var best = 0, run = 0
        for s in days {
            run = s > 0 ? run + 1 : 0
            best = max(best, run)
        }
        return best
    }
}

// MARK: - Range

enum StatsRange: Hashable {
    case pastYear
    case year(Int)

    var title: String {
        switch self {
        case .pastYear: return "過去一年"
        case .year(let y): return "\(y)"
        }
    }

    var heading: String {
        switch self {
        case .pastYear: return "過去一年"
        case .year(let y): return "\(y) 年"
        }
    }
}

/// The days a range covers and the Monday-aligned grid that shows them.
struct Period {
    let first: Date      // first day of the range
    let end: Date        // last day of the range (Dec 31 for a year)
    let last: Date       // last day counted, never after today
    let gridStart: Date  // Monday of the first column
    let weeks: Int

    init(_ range: StatsRange) {
        let cal = Calendar.mondayFirst
        let today = cal.startOfDay(for: Date())
        switch range {
        case .pastYear:
            first = cal.date(byAdding: .day, value: -364, to: today)!
            end = today
        case .year(let y):
            first = cal.date(from: DateComponents(year: y, month: 1, day: 1))!
            end = cal.date(from: DateComponents(year: y, month: 12, day: 31))!
        }
        last = min(end, today)
        gridStart = cal.dateInterval(of: .weekOfYear, for: first)!.start
        let lastMonday = cal.dateInterval(of: .weekOfYear, for: end)!.start
        weeks = cal.dateComponents([.day], from: gridStart, to: lastMonday).day! / 7 + 1
    }

    func contains(_ date: Date) -> Bool { date >= first && date <= last }
    func shows(_ date: Date) -> Bool { date >= first && date <= end }

    var days: [Date] {
        var out: [Date] = []
        var d = first
        while d <= last {
            out.append(d)
            d = Calendar.mondayFirst.date(byAdding: .day, value: 1, to: d)!
        }
        return out
    }
}

// MARK: - Heatmap

struct HeatmapGrid: View {
    let period: Period
    let perDay: [String: Double]
    @Binding var selected: Date?
    let caption: (Date?) -> Text
    @State private var hovered: Date?
    @State private var tipSize = CGSize(width: 160, height: 22)

    static let width: CGFloat = 776
    static let gap: CGFloat = 3
    static let labelWidth: CGFloat = 20

    /// Cells stretch so the grid always spans the full width, whatever the week count.
    private var cell: CGFloat { (Self.width - Self.labelWidth - CGFloat(period.weeks) * Self.gap) / CGFloat(period.weeks) }

    var body: some View {
        let cal = Calendar.mondayFirst
        let columns = (0..<period.weeks).map { w in
            (0..<7).map { cal.date(byAdding: .day, value: w * 7 + $0, to: period.gridStart)! }
        }
        let months = monthLabels(columns)

        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: Self.gap) {
                Color.clear.frame(width: Self.labelWidth, height: 1)
                ForEach(0..<period.weeks, id: \.self) { w in
                    Text(months[w] ?? "")
                        .font(.caption2).foregroundStyle(.secondary)
                        .fixedSize().frame(width: cell, alignment: .leading)
                }
            }
            HStack(alignment: .top, spacing: Self.gap) {
                VStack(spacing: Self.gap) {
                    ForEach(0..<7, id: \.self) { d in
                        Text(["一", "", "三", "", "五", "", "日"][d])
                            .font(.caption2).foregroundStyle(.secondary)
                            .frame(width: Self.labelWidth, height: cell, alignment: .leading)
                    }
                }
                ForEach(0..<period.weeks, id: \.self) { w in
                    VStack(spacing: Self.gap) {
                        ForEach(0..<7, id: \.self) { d in cellView(columns[w][d]) }
                    }
                }
            }
            .overlay(alignment: .topLeading) { GeometryReader { _ in tooltip(columns) } }
            HStack(spacing: Self.gap) {
                caption(hovered ?? selected).font(.callout).lineLimit(1)
                Spacer(minLength: 16)
                Text("少").font(.caption2).foregroundStyle(.secondary)
                ForEach([0, 1, 1800, 3600, 7200] as [Double], id: \.self) {
                    RoundedRectangle(cornerRadius: 2).fill(Palette.level($0)).frame(width: 11, height: 11)
                }
                Text("多").font(.caption2).foregroundStyle(.secondary)
            }
        }
        .frame(width: Self.width, alignment: .leading)
    }

    @ViewBuilder private func cellView(_ date: Date) -> some View {
        let inPeriod = period.contains(date)
        let seconds = perDay[Tracker.dayKey.string(from: date)] ?? 0
        RoundedRectangle(cornerRadius: 2)
            .fill(inPeriod ? Palette.level(seconds) : period.shows(date) ? Palette.level(0).opacity(0.5) : Color.clear)
            .overlay(
                RoundedRectangle(cornerRadius: 2)
                    .strokeBorder(Color.primary, lineWidth: 1.5)
                    .opacity(selected == date ? 1 : 0)
            )
            .frame(width: cell, height: cell)
            .contentShape(Rectangle())
            .onHover { inside in
                if inside, inPeriod { hovered = date } else if hovered == date { hovered = nil }
            }
            .onTapGesture {
                guard inPeriod else { return }
                selected = selected == date ? nil : date
            }
    }

    /// GitHub-style bubble above the hovered cell.
    @ViewBuilder private func tooltip(_ columns: [[Date]]) -> some View {
        if let date = hovered, let w = columns.firstIndex(where: { $0.contains(date) }),
           let d = columns[w].firstIndex(of: date) {
            let seconds = perDay[Tracker.dayKey.string(from: date)] ?? 0
            let text = "\(dayLabel(date))（\(weekday(date))）\(seconds > 0 ? duration(seconds) : "無紀錄")"
            let x = Self.labelWidth + Self.gap + CGFloat(w) * (cell + Self.gap) + cell / 2
            let y = CGFloat(d) * (cell + Self.gap)
            Text(text)
                .font(.caption).foregroundStyle(.white).fixedSize()
                .padding(.horizontal, 8).padding(.vertical, 4)
                .background(RoundedRectangle(cornerRadius: 5).fill(Color.black.opacity(0.85)))
                .background(GeometryReader { Color.clear.preference(key: TipSize.self, value: $0.size) })
                .onPreferenceChange(TipSize.self) { tipSize = $0 }
                // Centered above the cell, but kept inside the grid's width.
                .position(x: min(max(x, tipSize.width / 2), Self.width - tipSize.width / 2),
                          y: y - tipSize.height / 2 - 6)
                .allowsHitTesting(false)
        }
    }

    /// Like GitHub: label the first week of each month, skipping labels that would collide.
    private func monthLabels(_ columns: [[Date]]) -> [Int: String] {
        let cal = Calendar.mondayFirst
        var labels: [Int: String] = [:]
        var lastLabeled = -10
        var lastMonth = -1
        for (w, week) in columns.enumerated() {
            guard let day = week.first(where: period.shows) else { continue }
            let month = cal.component(.month, from: day)
            if month != lastMonth && w - lastLabeled >= 3 {
                labels[w] = "\(month)月"
                lastLabeled = w
            }
            lastMonth = month
        }
        return labels
    }
}

private struct TipSize: PreferenceKey {
    static let defaultValue = CGSize.zero
    static func reduce(value: inout CGSize, nextValue: () -> CGSize) { value = nextValue() }
}

/// Arrow keys move the selected day like GitHub's graph: ↑↓ by day, ←→ by week.
struct ArrowKeys: ViewModifier {
    @Binding var selected: Date?
    let period: Period
    @FocusState private var focused: Bool

    func body(content: Content) -> some View {
        if #available(macOS 14, *) {
            content
                .focusable()
                .focusEffectDisabled()
                .focused($focused)
                .onAppear { focused = true }
                .onMoveCommand { direction in
                    let step: Int
                    switch direction {
                    case .up: step = -1
                    case .down: step = 1
                    case .left: step = -7
                    case .right: step = 7
                    @unknown default: return
                    }
                    guard let current = selected else { return selected = period.last }
                    let next = Calendar.mondayFirst.date(byAdding: .day, value: step, to: current)!
                    if period.contains(next) { selected = next }
                }
        } else {
            content
        }
    }
}

// MARK: - Pieces

struct BarRow: Identifiable {
    let id: String
    let label: String
    let seconds: Double
}

/// Horizontal bars, longest first. Rows are clickable.
struct BarList: View {
    let rows: [BarRow]
    let selected: String?
    let empty: String
    let onTap: (String) -> Void

    var body: some View {
        if rows.isEmpty {
            Text(empty).foregroundStyle(.secondary)
        } else {
            ScrollView {
                VStack(spacing: 2) {
                    ForEach(rows) { row in
                        HStack(spacing: 10) {
                            Text(row.label).frame(width: 140, alignment: .leading).lineLimit(1)
                            GeometryReader { geo in
                                RoundedRectangle(cornerRadius: 3)
                                    .fill(Palette.bar)
                                    .frame(width: max(2, geo.size.width * row.seconds / rows[0].seconds))
                            }
                            .frame(height: 14)
                            Text(duration(row.seconds)).foregroundStyle(.secondary).monospacedDigit()
                                .frame(width: 100, alignment: .trailing)
                        }
                        .padding(.vertical, 5)
                        .background(
                            RoundedRectangle(cornerRadius: 6)
                                .fill(selected == row.id ? Color.accentColor.opacity(0.15) : Color.clear)
                                .padding(.horizontal, -8)
                        )
                        .contentShape(Rectangle())
                        .onTapGesture { onTap(row.id) }
                    }
                }
                .padding(.horizontal, 8)  // room for the highlight; undone below so rows stay flush
            }
            .padding(.horizontal, -8)
            .frame(height: min(CGFloat(rows.count) * 26, 182))
        }
    }
}

/// Twelve monthly columns; only the busiest month is labeled, hover for the rest.
struct MonthColumns: View {
    let months: [(label: String, seconds: Double)]

    var body: some View {
        let peak = months.map { $0.seconds }.max() ?? 0
        HStack(alignment: .bottom, spacing: 0) {
            ForEach(Array(months.enumerated()), id: \.offset) { _, month in
                VStack(spacing: 6) {
                    Text(month.seconds == peak && peak > 0 ? duration(month.seconds) : " ")
                        .font(.caption2).foregroundStyle(.secondary).fixedSize()
                    RoundedRectangle(cornerRadius: 3)
                        .fill(month.seconds > 0 ? Palette.bar : Palette.level(0))
                        .frame(width: 28, height: peak > 0 ? max(2, 110 * month.seconds / peak) : 2)
                    Text(month.label).font(.caption).foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity)
                .help("\(month.label)：\(month.seconds > 0 ? duration(month.seconds) : "無紀錄")")
            }
        }
        .frame(height: 150, alignment: .bottom)
    }
}

struct Tile: View {
    let label: String
    let value: String

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(label).font(.caption).foregroundStyle(.secondary)
            Text(value).font(.title2.weight(.semibold)).monospacedDigit()
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 8).fill(.quaternary.opacity(0.5)))
    }
}

struct YearPicker: View {
    let ranges: [StatsRange]
    @Binding var range: StatsRange

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            ForEach(ranges, id: \.self) { r in
                Button { range = r } label: {
                    Text(r.title)
                        .frame(width: 72, alignment: .leading)
                        .padding(.vertical, 6)
                        .padding(.horizontal, 10)
                        .foregroundStyle(range == r ? Color.white : Color.primary)
                        .background(RoundedRectangle(cornerRadius: 6).fill(range == r ? Color.accentColor : Color.clear))
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.top, 4)
    }
}

// MARK: - Shared

extension Calendar {
    static var mondayFirst: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.firstWeekday = 2
        return c
    }
}

/// One-hue sequential ramp (blue), with its own steps for dark mode.
enum Palette {
    private static let empty = dynamic(light: 0xF0EFEC, dark: 0x383835)
    private static let steps = [
        dynamic(light: 0xB7D3F6, dark: 0x184F95),
        dynamic(light: 0x6DA7EC, dark: 0x256ABF),
        dynamic(light: 0x2A78D6, dark: 0x3987E5),
        dynamic(light: 0x104281, dark: 0x86B6EF),
    ]
    static let bar = dynamic(light: 0x2A78D6, dark: 0x3987E5)

    static func level(_ seconds: Double) -> Color {
        switch seconds {
        case ..<1: return empty
        case ..<1800: return steps[0]
        case ..<3600: return steps[1]
        case ..<7200: return steps[2]
        default: return steps[3]
        }
    }

    private static func dynamic(light: UInt32, dark: UInt32) -> Color {
        Color(nsColor: NSColor(name: nil) { appearance in
            let hex = appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? dark : light
            return NSColor(srgbRed: CGFloat(hex >> 16 & 0xFF) / 255, green: CGFloat(hex >> 8 & 0xFF) / 255,
                           blue: CGFloat(hex & 0xFF) / 255, alpha: 1)
        })
    }
}

func weekday(_ date: Date) -> String {
    "星期" + ["日", "一", "二", "三", "四", "五", "六"][Calendar.mondayFirst.component(.weekday, from: date) - 1]
}

func dayLabel(_ date: Date) -> String {
    let c = Calendar.mondayFirst
    return "\(c.component(.month, from: date))月\(c.component(.day, from: date))日"
}

func duration(_ seconds: Double) -> String {
    let minutes = Int(seconds / 60)
    return minutes >= 60 ? "\(minutes / 60) 小時 \(minutes % 60) 分" : "\(minutes) 分"
}
