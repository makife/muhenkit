import SwiftUI

/// Hakkında: sürüm, sorumluluk notu, gizlilik
struct AboutView: View {
    static let privacyURL = URL(string: "https://elegant-cannoli-ecf0d2.netlify.app")!
    @Environment(\.dismiss) private var dismiss

    private var version: String {
        let i = Bundle.main.infoDictionary
        return "\(i?["CFBundleShortVersionString"] as? String ?? "") (\(i?["CFBundleVersion"] as? String ?? ""))"
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    HStack(spacing: 12) {
                        Image(systemName: "bolt.fill").font(.title).foregroundStyle(Theme.page)
                            .frame(width: 52, height: 52)
                            .background(LinearGradient(colors: [Theme.gold, Theme.accent], startPoint: .topLeading, endPoint: .bottomTrailing),
                                        in: RoundedRectangle(cornerRadius: 12))
                        VStack(alignment: .leading) {
                            Text("MühenKit Elektrik").font(.title2.weight(.bold))
                            Text("Sürüm \(version)").foregroundStyle(Theme.muted)
                        }
                    }
                    Text("Elektrik proje çizimi (DXF) ve mühendislik hesapları. İnternet gerektirmez; projeleriniz yalnızca bu cihazda saklanır, kişisel veri toplanmaz.")
                    Text("Sorumluluk notu: Hesaplar ve tablolar ön tasarım ve kontrol amaçlıdır. Projenin yönetmeliklere uygunluğu ve sonuçların doğruluğu projeyi hazırlayan mühendisin sorumluluğundadır.")
                        .font(.footnote).foregroundStyle(Theme.muted)
                    Link("Destek: makifergan@gmail.com", destination: URL(string: "mailto:makifergan@gmail.com")!)
                    Link("Gizlilik politikası", destination: Self.privacyURL)
                }
                .padding(20)
            }
            .navigationTitle("Hakkında")
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Kapat") { dismiss() } } }
        }
        #if os(macOS)
        .frame(minWidth: 420, minHeight: 360)
        #endif
    }
}
