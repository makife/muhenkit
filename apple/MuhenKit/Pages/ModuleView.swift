import SwiftUI

/// Hesap modülü ekranı (modüller sırayla taşınıyor)
struct ModuleView: View {
    let route: RouteId

    var body: some View {
        let info = Routes.info(route)
        VStack(spacing: 14) {
            Image(systemName: info.symbol).font(.system(size: 48)).foregroundStyle(Theme.accent)
            Text(info.title).font(.title2.weight(.semibold))
            Text(info.subtitle).foregroundStyle(Theme.muted)
            Text("Hesap çekirdeği: \(Calc.table("cables").count > 1 ? "hazır" : "yüklenemedi")").font(.caption).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Theme.page)
        .navigationTitle(info.title)
    }
}
