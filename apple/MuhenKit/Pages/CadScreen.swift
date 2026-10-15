import SwiftUI
import UniformTypeIdentifiers

/// Dışa verilecek dosya (DXF / PDF / PNG)
struct ExportFile: FileDocument {
    static var readableContentTypes: [UTType] { [.dxf, .pdf, .png] }
    var data: Data

    init(data: Data) { self.data = data }
    init(configuration: ReadConfiguration) throws { data = configuration.file.regularFileContents ?? Data() }
    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper { FileWrapper(regularFileWithContents: data) }
}

/// Açık çizimin durumu
@MainActor
final class CadModel: ObservableObject {
    @Published var doc: CadDocument?
    @Published var project: ProjectMeta?
    @Published var view = ViewState(cx: 0, cy: 0, zoom: 1)
    @Published var hidden: Set<Int> = []
    @Published var revision = 0
    @Published var error: String?
    @Published var loading = false
    var needsFit = false

    func load(_ id: String) async {
        loading = true
        defer { loading = false }
        do {
            let meta = try ProjectStore.meta(id)
            let data = try ProjectStore.read(id)
            let d = try await Task.detached(priority: .userInitiated) { try CadDocument.open(name: meta.name, data: data) }.value
            d.setDirty(meta.dirty)
            doc = d
            project = meta
            ProjectStore.lastProject = id
            let s = d.scene!
            hidden = []
            if let v = meta.view, v.count == 3, let h = meta.hidden {
                view = ViewState(cx: v[0] - d.originX, cy: v[1] - d.originY, zoom: v[2])
                hidden = Set(h.filter { $0 < s.layers.count })
            } else {
                for (i, l) in s.layers.enumerated() where l.offInFile { hidden.insert(i) }
                needsFit = true
            }
            revision += 1
        } catch {
            self.error = error.localizedDescription
        }
    }

    /// Görünüm ve katman durumunu projeye yaz; çizim değiştiyse çalışma kopyasını da
    func persist() {
        guard let d = doc, var m = project else { return }
        if d.undoCount > 0 || d.dirty {
            ProjectStore.writeDrawing(m.id, d.saveCopy())
            m.modified = ProjectStore.now
            m.dirty = d.dirty
        }
        m.view = [view.cx + d.originX, view.cy + d.originY, view.zoom]
        m.hidden = Array(hidden).sorted()
        ProjectStore.saveMeta(m)
        project = m
    }
}

/// DXF çizim ekranı
struct CadScreen: View {
    let projectId: String?
    @EnvironmentObject private var nav: AppNav
    @StateObject private var model = CadModel()
    @State private var showLayers = false
    @State private var importing = false
    @State private var exporting = false
    @State private var exportFile = ExportFile(data: Data())
    @State private var canvasSize: CGSize = .zero

    var body: some View {
        Group {
            if let d = model.doc, let s = d.scene {
                GeometryReader { geo in
                    CadCanvas(scene: s, view: $model.view, hidden: model.hidden, revision: model.revision)
                        .onAppear { canvasSize = geo.size; fitIfNeeded(s) }
                        .onChange(of: geo.size) { canvasSize = $0; fitIfNeeded(s) }
                        .overlay(alignment: .bottomLeading) { status(s) }
                }
                .ignoresSafeArea(edges: .bottom)
            } else if model.loading {
                ProgressView("Çizim açılıyor…")
            } else {
                empty
            }
        }
        .background(CadColors.background)
        .navigationTitle(model.project?.name ?? "DXF Çizim")
        #if os(iOS)
        .navigationBarTitleDisplayMode(.inline)
        #endif
        .toolbar { toolbar }
        .task {
            guard model.doc == nil else { return }
            if let id = projectId ?? ProjectStore.lastProject { await model.load(id) }
        }
        .onDisappear { model.persist() }
        .sheet(isPresented: $showLayers) { layersSheet }
        .fileImporter(isPresented: $importing, allowedContentTypes: [.dxf, .data]) { r in
            if case .success(let url) = r {
                do {
                    let m = try DxfImport.project(from: url)
                    model.persist()
                    Task { await model.load(m.id) }
                } catch { model.error = error.localizedDescription }
            }
        }
        .fileExporter(isPresented: $exporting, document: exportFile, contentType: .dxf, defaultFilename: model.project?.name ?? "cizim") { _ in }
        .alert("Hata", isPresented: Binding(get: { model.error != nil }, set: { if !$0 { model.error = nil } })) {
            Button("Tamam", role: .cancel) {}
        } message: { Text(model.error ?? "") }
    }

    private func fitIfNeeded(_ s: CadScene) {
        guard model.needsFit, canvasSize.width > 0 else { return }
        model.needsFit = false
        model.view = CadCanvas.fit(s, in: canvasSize)
    }

    @ToolbarContentBuilder
    private var toolbar: some ToolbarContent {
        ToolbarItemGroup(placement: .primaryAction) {
            if let s = model.doc?.scene {
                Button { model.view = CadCanvas.fit(s, in: canvasSize) } label: { Label("Sığdır", systemImage: "arrow.up.left.and.arrow.down.right") }
                Button { showLayers = true } label: { Label("Katmanlar", systemImage: "square.3.layers.3d") }
                Menu {
                    Button { importing = true } label: { Label("DXF aç", systemImage: "folder") }
                    Button {
                        if let d = model.doc { exportFile = ExportFile(data: d.save()); exporting = true }
                    } label: { Label("DXF olarak kaydet", systemImage: "square.and.arrow.down") }
                } label: { Label("Dosya", systemImage: "doc") }
            }
        }
    }

    private func status(_ s: CadScene) -> some View {
        Text("\(s.entityCount) nesne · \(s.layers.count) katman · \(s.version)")
            .font(.caption2.monospaced())
            .foregroundStyle(Theme.muted)
            .padding(.horizontal, 10).padding(.vertical, 6)
            .background(.ultraThinMaterial, in: Capsule())
            .padding(12)
    }

    private var empty: some View {
        VStack(spacing: 16) {
            Image(systemName: "pencil.and.ruler").font(.system(size: 54)).foregroundStyle(Theme.gold)
            Text("Açık çizim yok").font(.title2.weight(.semibold))
            Text("Bir DXF dosyası aç ya da boş projeyle başla.").foregroundStyle(Theme.muted)
            HStack {
                Button { importing = true } label: { Label("DXF aç", systemImage: "folder") }.buttonStyle(.borderedProminent)
                Button {
                    do { let m = try DxfImport.blank(); Task { await model.load(m.id) } } catch { model.error = error.localizedDescription }
                } label: { Label("Boş proje", systemImage: "doc.badge.plus") }.buttonStyle(.bordered)
            }
        }
        .padding()
    }

    private var layersSheet: some View {
        NavigationStack {
            List {
                if let s = model.doc?.scene {
                    ForEach(Array(s.layers.enumerated()), id: \.offset) { i, l in
                        Toggle(isOn: Binding(get: { !model.hidden.contains(i) }, set: { on in
                            if on { model.hidden.remove(i) } else { model.hidden.insert(i) }
                        })) {
                            HStack {
                                Circle().fill(CadColors.adapt(l.rgb)).frame(width: 12, height: 12)
                                Text(l.name)
                                Spacer()
                                Text("\(l.count)").foregroundStyle(.secondary).font(.caption)
                            }
                        }
                    }
                }
            }
            .navigationTitle("Katmanlar")
            .toolbar {
                ToolbarItem(placement: .confirmationAction) { Button("Tamam") { showLayers = false } }
            }
        }
        #if os(macOS)
        .frame(minWidth: 380, minHeight: 460)
        #endif
    }
}
