import SwiftUI

/// Uygulama içi gezinme hedefleri
enum AppRoute: Hashable {
    /// nil: son projeye devam
    case cad(String?)
    case projects
    case module(RouteId)
}

final class AppNav: ObservableObject {
    @Published var path: [AppRoute] = []

    func open(_ id: RouteId) {
        switch id {
        case .cad: path.append(.cad(nil))
        case .projects: path.append(.projects)
        default: path.append(.module(id))
        }
    }

    func home() { path.removeAll() }
}

enum Theme {
    static let accent = Color(red: 0xF2 / 255, green: 0xB2 / 255, blue: 0x66 / 255)
    static let gold = Color(red: 0xE4 / 255, green: 0xC0 / 255, blue: 0x6A / 255)
    static let muted = Color(red: 0x9F / 255, green: 0xB6 / 255, blue: 0xDC / 255)
    static let surface = Color(red: 0x14 / 255, green: 0x17 / 255, blue: 0x19 / 255)
    static let card = Color(red: 0x1A / 255, green: 0x1F / 255, blue: 0x24 / 255)
    static let line = Color.white.opacity(0.09)
    static let page = Color(red: 0x0B / 255, green: 0x0E / 255, blue: 0x10 / 255)
}

@main
struct MuhenKitApp: App {
    @StateObject private var nav = AppNav()

    /// Mağaza görüntüleri için açılış ekranı: `-mkScreen cad|projects|<modül>` (simülatörde otomatik çekim)
    private func openLaunchScreen() {
        guard let s = UserDefaults.standard.string(forKey: "mkScreen"), !s.isEmpty, nav.path.isEmpty else { return }
        switch s {
        case "home": break
        case "cad": if let m = try? DxfImport.demo() { nav.path = [.cad(m.id)] }
        case "projects": _ = try? DxfImport.demo(); nav.path = [.projects]
        default: if let r = RouteId(rawValue: s) { nav.path = [.module(r)] }
        }
    }

    var body: some Scene {
        WindowGroup {
            NavigationStack(path: $nav.path) {
                HomeView()
                    .navigationDestination(for: AppRoute.self) { r in
                        switch r {
                        case .cad(let id): CadScreen(projectId: id)
                        case .projects: ProjectsView()
                        case .module(let id): ModuleView(route: id)
                        }
                    }
            }
            .environmentObject(nav)
            .task { openLaunchScreen() }
            .preferredColorScheme(.dark)
            .tint(Theme.accent)
            #if os(macOS)
            .frame(minWidth: 900, minHeight: 600)
            #endif
        }
    }
}
