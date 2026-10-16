import SwiftUI
import UniformTypeIdentifiers
import ImageIO

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

    /// Örnek proje (core/content/demo.dxf): 2+1 daire, semboller, tablolar, antet
    static func demo() throws -> ProjectMeta {
        if let m = ProjectStore.list().first(where: { $0.name == "Örnek proje" }) { return m }
        let data = try Data(contentsOf: Content.url("demo.dxf"))
        return ProjectStore.create(name: "Örnek proje", bytes: data, source: nil)
    }

    static func blank() throws -> ProjectMeta {
        let data = try Data(contentsOf: Content.url("blank.dxf"))
        return ProjectStore.create(name: "Yeni proje \(ProjectStore.nextBlankNumber())", bytes: data, source: nil)
    }
}

/// Projelerim: kayıtlı çizimler (önizlemeli kartlar, Android ile aynı), yeni proje, DXF aç
struct ProjectsView: View {
    @EnvironmentObject private var nav: AppNav
    @State private var projects: [ProjectMeta] = []
    @State private var thumbs: [String: CGImage] = [:]
    @State private var importing = false
    @State private var error: String?
    @State private var renaming: ProjectMeta?
    @State private var deleting: ProjectMeta?
    @State private var newName = ""

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                HStack(spacing: 8) {
                    action("DXF aç", "folder.badge.plus", filled: true) { importing = true }
                    action("Boş proje", "doc.badge.plus") { newBlank() }
                    action("Örnek", "sparkles") {
                        do { open(try DxfImport.demo().id) } catch { self.error = error.localizedDescription }
                    }
                }
                if projects.isEmpty {
                    Text("Henüz proje yok. Açtığın her DXF burada saklanır ve kaldığın yerden devam edersin.")
                        .foregroundStyle(Theme.muted)
                        .padding(8)
                }
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 170), spacing: 10)], spacing: 10) {
                    ForEach(projects) { p in card(p) }
                }
            }
            .padding(12)
        }
        .background(Theme.page)
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
        .alert("Projeyi sil", isPresented: Binding(get: { deleting != nil }, set: { if !$0 { deleting = nil } })) {
            Button("Sil", role: .destructive) {
                if let p = deleting { ProjectStore.delete(p.id) }
                deleting = nil
                reload()
            }
            Button("İptal", role: .cancel) { deleting = nil }
        } message: {
            Text(deleteMessage)
        }
    }

    private var deleteMessage: String {
        guard let p = deleting else { return "" }
        return "\"\(p.name)\" Projelerim'den silinsin mi?"
            + (p.source != nil ? " Orijinal DXF dosyan silinmez." : " Bu proje bir dosyaya kaydedilmedi; tamamen silinir.")
    }

    private func action(_ title: String, _ icon: String, filled: Bool = false, _ run: @escaping () -> Void) -> some View {
        Button(action: run) {
            Label(title, systemImage: icon)
                .font(.subheadline.weight(.semibold))
                .lineLimit(1)
                .minimumScaleFactor(0.75)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 11)
                .foregroundStyle(filled ? Bar.onPrimary : Theme.accent)
                .background(filled ? Theme.accent : Color.clear, in: Capsule())
                .overlay(Capsule().stroke(Theme.accent.opacity(filled ? 0 : 0.6), lineWidth: 1))
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
    }

    private func card(_ p: ProjectMeta) -> some View {
        VStack(spacing: 0) {
            ZStack {
                Color.white
                if let img = thumbs[p.id] {
                    Image(decorative: img, scale: 1).resizable().scaledToFit()
                } else {
                    Image(systemName: "pencil.and.ruler").font(.system(size: 34)).foregroundStyle(Bar.onSurfaceVariant)
                }
            }
            .aspectRatio(1.6, contentMode: .fit)
            HStack(spacing: 4) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(p.name).font(.subheadline.bold()).lineLimit(1).truncationMode(.tail)
                    Text(Self.date(p.modified)).font(.caption2).foregroundStyle(Theme.muted)
                    if p.dirty {
                        Text("Dosyaya kaydedilmedi").font(.caption2).foregroundStyle(Theme.accent)
                    } else if p.source == nil {
                        Text("Yalnız uygulamada").font(.caption2).foregroundStyle(Theme.muted)
                    }
                }
                Spacer(minLength: 0)
                Menu {
                    Button { open(p.id) } label: { Label("Aç", systemImage: "pencil") }
                    Button { newName = p.name; renaming = p } label: { Label("Yeniden adlandır", systemImage: "character.cursor.ibeam") }
                    Button(role: .destructive) { deleting = p } label: { Label("Sil", systemImage: "trash") }
                } label: {
                    Image(systemName: "ellipsis")
                        .rotationEffect(.degrees(90))
                        .frame(width: 36, height: 36)
                        .contentShape(Rectangle())
                }
                .menuStyle(.borderlessButton)
                .fixedSize()
            }
            .padding(.leading, 10)
            .padding(.vertical, 6)
            .padding(.trailing, 2)
        }
        .background(Theme.surface)
        .clipShape(RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(Color.white.opacity(0.13), lineWidth: 1))
        .contentShape(RoundedRectangle(cornerRadius: 14))
        .onTapGesture { open(p.id) }
        .contextMenu {
            Button("Yeniden adlandır") { newName = p.name; renaming = p }
            Button("Sil", role: .destructive) { deleting = p }
        }
    }

    static func date(_ ms: Int64) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "tr_TR")
        f.dateFormat = "dd.MM.yyyy HH:mm"
        return f.string(from: Date(timeIntervalSince1970: Double(ms) / 1000))
    }

    private func reload() {
        let list = ProjectStore.list()
        projects = list
        // önizlemeler arka planda yüklenir; önizlemesi olmayan (eski sürümden kalan) projelerinki bir kez üretilir
        Task.detached(priority: .utility) {
            for p in list {
                let url = ProjectStore.thumb(p.id)
                if !FileManager.default.fileExists(atPath: url.path),
                   let data = try? ProjectStore.read(p.id),
                   let d = try? CadDocument.open(name: p.name, data: data), let s = d.scene,
                   let png = Exporter.thumbnail(s, hidden: Set(p.hidden ?? [])) {
                    ProjectStore.writeThumb(p.id, png)
                }
                guard let src = CGImageSourceCreateWithURL(url as CFURL, [kCGImageSourceShouldCache: false] as CFDictionary),
                      let img = CGImageSourceCreateImageAtIndex(src, 0, nil) else { continue }
                await MainActor.run { thumbs[p.id] = img }
            }
        }
    }

    private func open(_ id: String) { nav.path.append(.cad(id)) }

    private func newBlank() {
        do { open(try DxfImport.blank().id) } catch { self.error = error.localizedDescription }
    }
}
