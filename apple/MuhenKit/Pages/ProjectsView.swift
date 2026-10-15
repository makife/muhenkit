import SwiftUI
import UniformTypeIdentifiers

extension UTType {
    static let dxf = UTType(filenameExtension: "dxf") ?? .data
}

/// DXF dosyasını içeri al: projeye kopyalanır (aynı kaynak zaten varsa o proje açılır)
enum DxfImport {
    static func project(from url: URL) throws -> ProjectMeta {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        let data = try Data(contentsOf: url)
        let head = String(decoding: data.prefix(4096), as: UTF8.self)
        if !head.contains("SECTION") {
            throw CadError(message: "\"\(url.lastPathComponent)\" bir DXF dosyası değil. AutoCAD'de \"Farklı Kaydet → DXF\" ile kaydedin.")
        }
        _ = try CadDocument.open(name: url.lastPathComponent, data: data) // açılabildiğini doğrula
        return ProjectStore.create(name: url.deletingPathExtension().lastPathComponent, bytes: data, source: url.path)
    }

    static func blank() throws -> ProjectMeta {
        let data = try Data(contentsOf: Content.url("blank.dxf"))
        return ProjectStore.create(name: "Yeni proje \(ProjectStore.nextBlankNumber())", bytes: data, source: nil)
    }
}

/// Projelerim: kayıtlı çizimler, yeni proje, DXF aç
struct ProjectsView: View {
    @EnvironmentObject private var nav: AppNav
    @State private var projects: [ProjectMeta] = []
    @State private var importing = false
    @State private var error: String?
    @State private var renaming: ProjectMeta?
    @State private var newName = ""

    var body: some View {
        List {
            Section {
                Button { newBlank() } label: { Label("Boş proje", systemImage: "doc.badge.plus") }
                Button { importing = true } label: { Label("DXF aç", systemImage: "folder.badge.plus") }
            }
            Section(projects.isEmpty ? "Henüz proje yok" : "Kayıtlı projeler") {
                ForEach(projects) { p in
                    Button { open(p.id) } label: { row(p) }
                        .buttonStyle(.plain)
                        .contextMenu {
                            Button("Yeniden adlandır") { newName = p.name; renaming = p }
                            Button("Sil", role: .destructive) { ProjectStore.delete(p.id); reload() }
                        }
                }
                .onDelete { idx in
                    for i in idx { ProjectStore.delete(projects[i].id) }
                    reload()
                }
            }
        }
        .navigationTitle("Projelerim")
        .onAppear(perform: reload)
        .fileImporter(isPresented: $importing, allowedContentTypes: [.dxf, .data]) { r in
            switch r {
            case .success(let url):
                do { open(try DxfImport.project(from: url).id) } catch { self.error = error.localizedDescription }
            case .failure(let e): error = e.localizedDescription
            }
        }
        .alert("Hata", isPresented: Binding(get: { error != nil }, set: { if !$0 { error = nil } })) {
            Button("Tamam", role: .cancel) {}
        } message: { Text(error ?? "") }
        .alert("Yeniden adlandır", isPresented: Binding(get: { renaming != nil }, set: { if !$0 { renaming = nil } })) {
            TextField("Ad", text: $newName)
            Button("Kaydet") {
                if let p = renaming, !newName.trimmingCharacters(in: .whitespaces).isEmpty { ProjectStore.rename(p.id, newName) }
                renaming = nil
                reload()
            }
            Button("Vazgeç", role: .cancel) { renaming = nil }
        }
    }

    private func row(_ p: ProjectMeta) -> some View {
        HStack(spacing: 12) {
            Image(systemName: "doc.richtext")
                .font(.title2)
                .foregroundStyle(Theme.gold)
                .frame(width: 44, height: 44)
                .background(Theme.gold.opacity(0.1), in: RoundedRectangle(cornerRadius: 10))
            VStack(alignment: .leading, spacing: 3) {
                Text(p.name).font(.headline)
                Text("\(Self.date(p.modified)) · \(ByteCountFormatter.string(fromByteCount: p.sizeBytes, countStyle: .file))")
                    .font(.caption).foregroundStyle(Theme.muted)
            }
            Spacer()
            if p.dirty { Image(systemName: "circle.fill").font(.system(size: 7)).foregroundStyle(Theme.accent) }
        }
        .contentShape(Rectangle())
    }

    static func date(_ ms: Int64) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "tr_TR")
        f.dateFormat = "d MMM yyyy HH:mm"
        return f.string(from: Date(timeIntervalSince1970: Double(ms) / 1000))
    }

    private func reload() { projects = ProjectStore.list() }

    private func open(_ id: String) { nav.path.append(.cad(id)) }

    private func newBlank() {
        do { open(try DxfImport.blank().id) } catch { self.error = error.localizedDescription }
    }
}
