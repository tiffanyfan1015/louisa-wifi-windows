import UserNotifications

enum Notifier {
    static func setUp() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert]) { _, _ in }
    }

    static func post(_ text: String) {
        let content = UNMutableNotificationContent()
        content.title = "路易莎 Wi-Fi"
        content.body = text
        let request = UNNotificationRequest(identifier: "status", content: content, trigger: nil)
        UNUserNotificationCenter.current().add(request)
    }
}
