import CoreLocation
import Foundation

struct Store: Codable {
    let name: String
    let address: String
    let phone: String
    let lat: Double
    let lng: Double
    /// Wi-Fi password candidates derived from the phone number, most likely first.
    let passwords: [String]

    var city: String {
        String(address.split(separator: " ").first ?? "其他").replacingOccurrences(of: "臺", with: "台")
    }
    var location: CLLocation? { lat == 0 ? nil : CLLocation(latitude: lat, longitude: lng) }
}

enum Stores {
    static let all: [Store] = {
        guard let url = Bundle.main.url(forResource: "stores", withExtension: "json"),
              let data = try? Data(contentsOf: url) else { return [] }
        return (try? JSONDecoder().decode([Store].self, from: data)) ?? []
    }()

    static func named(_ name: String) -> Store? { all.first { $0.name == name } }

    static func nearest(to here: CLLocation, limit: Int, in stores: [Store] = all) -> [(store: Store, meters: Double)] {
        let ranked = stores.compactMap { s in s.location.map { (store: s, meters: $0.distance(from: here)) } }
        return Array(ranked.sorted { $0.meters < $1.meters }.prefix(limit))
    }

    private static let cityOrder = ["台北市", "新北市", "基隆市", "桃園市", "新竹市", "新竹縣", "苗栗縣", "台中市",
                                    "彰化縣", "南投縣", "雲林縣", "嘉義市", "嘉義縣", "台南市", "高雄市", "屏東縣",
                                    "宜蘭縣", "花蓮縣", "台東縣", "澎湖縣", "金門縣", "連江縣"]

    static func byCity(_ stores: [Store] = all) -> [(city: String, stores: [Store])] {
        let rank = { (c: String) in cityOrder.firstIndex(of: c) ?? cityOrder.count }
        return Dictionary(grouping: stores, by: \.city)
            .sorted { (rank($0.key), $0.key) < (rank($1.key), $1.key) }
            .map { (city: $0.key, stores: $0.value.sorted { $0.name < $1.name }) }
    }
}
