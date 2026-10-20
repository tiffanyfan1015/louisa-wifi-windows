import CoreLocation

/// Keeps the last known location. The Mac has no GPS and often no internet when
/// we need it, so a recent cached fix is the best we can do.
final class Locator: NSObject, CLLocationManagerDelegate {
    static let shared = Locator()
    private let manager = CLLocationManager()
    var onChange: (() -> Void)?

    var location: CLLocation? { manager.location }

    var authorized: Bool {
        ![.notDetermined, .denied, .restricted].contains(manager.authorizationStatus)
    }

    func start() {
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyHundredMeters
        manager.distanceFilter = 100
        manager.requestWhenInUseAuthorization()
        manager.startUpdatingLocation()
    }

    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        onChange?()
    }

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        debugLog("location authorized: \(authorized)")
        if authorized { manager.startUpdatingLocation() }
        onChange?()
    }

    func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {}
}
