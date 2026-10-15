import SwiftUI

/// Ana sayfa: üstte çizim ve projeler (büyük kartlar), altta hesap modülleri
struct HomeView: View {
    @EnvironmentObject private var nav: AppNav

    private var main: [RouteInfo] { Routes.all.filter { $0.id == .cad || $0.id == .projects } }
    private var modules: [RouteInfo] { Routes.all.filter { $0.id != .cad && $0.id != .projects } }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 22) {
                header
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 300), spacing: 14)], spacing: 14) {
                    ForEach(main, id: \.id) { r in
                        Button { nav.open(r.id) } label: { BigCard(info: r) }.buttonStyle(.plain)
                    }
                }
                Text("Hesaplar ve rehberler").font(.headline).foregroundStyle(Theme.muted)
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 160), spacing: 12)], spacing: 12) {
                    ForEach(modules, id: \.id) { r in
                        Button { nav.open(r.id) } label: { ModuleCard(info: r) }.buttonStyle(.plain)
                    }
                }
            }
            .padding(20)
            .frame(maxWidth: 1200)
            .frame(maxWidth: .infinity)
        }
        .background(background.ignoresSafeArea())
        #if os(iOS)
        .navigationBarHidden(true)
        #endif
    }

    private var header: some View {
        HStack(spacing: 14) {
            Image(systemName: "bolt.fill")
                .font(.system(size: 26, weight: .bold))
                .foregroundStyle(Theme.page)
                .frame(width: 52, height: 52)
                .background(LinearGradient(colors: [Theme.gold, Theme.accent], startPoint: .topLeading, endPoint: .bottomTrailing),
                            in: RoundedRectangle(cornerRadius: 14))
            VStack(alignment: .leading, spacing: 2) {
                Text("MühenKit").font(.system(size: 30, weight: .bold))
                Text("Elektrik proje çizimi ve hesapları").foregroundStyle(Theme.muted)
            }
        }
        .padding(.top, 12)
    }

    private var background: some View {
        ZStack {
            Theme.page
            RadialGradient(colors: [Color(red: 0.1, green: 0.22, blue: 0.45).opacity(0.55), .clear], center: .topTrailing, startRadius: 10, endRadius: 700)
            RadialGradient(colors: [Theme.accent.opacity(0.10), .clear], center: .bottomLeading, startRadius: 10, endRadius: 600)
        }
    }
}

private struct BigCard: View {
    let info: RouteInfo

    var body: some View {
        HStack(spacing: 16) {
            Image(systemName: info.symbol)
                .font(.system(size: 28))
                .foregroundStyle(Theme.gold)
                .frame(width: 58, height: 58)
                .background(Theme.gold.opacity(0.12), in: RoundedRectangle(cornerRadius: 14))
            VStack(alignment: .leading, spacing: 4) {
                Text(info.title).font(.title3.weight(.semibold))
                Text(info.subtitle).font(.subheadline).foregroundStyle(Theme.muted).lineLimit(2)
            }
            Spacer(minLength: 0)
            Image(systemName: "chevron.right").foregroundStyle(.secondary)
        }
        .padding(18)
        .background(Theme.card, in: RoundedRectangle(cornerRadius: 18))
        .overlay(RoundedRectangle(cornerRadius: 18).stroke(Theme.line))
        .contentShape(Rectangle())
    }
}

private struct ModuleCard: View {
    let info: RouteInfo

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Image(systemName: info.symbol)
                .font(.system(size: 20))
                .foregroundStyle(Theme.accent)
                .frame(width: 38, height: 38)
                .background(Theme.accent.opacity(0.12), in: RoundedRectangle(cornerRadius: 10))
            Text(info.title).font(.headline).lineLimit(1)
            Text(info.subtitle).font(.caption).foregroundStyle(Theme.muted).lineLimit(2, reservesSpace: true)
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Theme.card, in: RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(Theme.line))
        .contentShape(Rectangle())
    }
}
