import SwiftUI
import Combine
import UniformTypeIdentifiers

/// Dışa verilecek dosya (DXF / PDF / PNG)
struct ExportFile: FileDocument {
    static var readableContentTypes: [UTType] { [.dxf, .pdf, .png] }
    var data: Data

    init(data: Data) { self.data = data }
    init(configuration: ReadConfiguration) throws { data = configuration.file.regularFileContents ?? Data() }
    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper { FileWrapper(regularFileWithContents: data) }
}

/// Açık çizimin durumu: proje, görünüm, katmanlar; düzenleme CadEditor'da
final class CadModel: ObservableObject {
    let editor = CadEditor()
    @Published var project: ProjectMeta?
    @Published var view = ViewState(cx: 0, cy: 0, zoom: 1)
    @Published var hidden: Set<Int> = []
    @Published var error: String?
    @Published var loading = false
    var needsFit = false
    private var saveTask: DispatchWorkItem?
    private var forward: AnyCancellable?

    init() {
        // düzenleyici değişince ekran (araç çubuğu, uyarılar) da yenilensin
        forward = editor.objectWillChange.sink { [weak self] _ in self?.objectWillChange.send() }
        // düzenlemeden 3 sn sonra çalışma kopyasına otomatik kayıt
        editor.onEdited = { [weak self] in
            guard let self else { return }
            self.saveTask?.cancel()
            let w = DispatchWorkItem { [weak self] in self?.persist() }
            self.saveTask = w
            DispatchQueue.main.asyncAfter(deadline: .now() + 3, execute: w)
        }
    }

    @MainActor
    func load(_ id: String) async {
        loading = true
        defer { loading = false }
        do {
            let meta = try ProjectStore.meta(id)
            let data = try ProjectStore.read(id)
            let d = try await Task.detached(priority: .userInitiated) { try CadDocument.open(name: meta.name, data: data) }.value
            d.setDirty(meta.dirty)
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
            editor.attach(d)
        } catch {
            self.error = error.localizedDescription
        }
    }

    /// Görünüm ve katman durumunu projeye yaz; çizim değiştiyse çalışma kopyasını da
    func persist() {
        guard let d = editor.doc, var m = project else { return }
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

/// DXF çizim ekranı: tuval + araç çubuğu + durum
struct CadScreen: View {
    let projectId: String?
    @StateObject private var model = CadModel()
    @State private var showLayers = false
    @State private var showSymbols = false
    @State private var table: TableKind?
    @State private var showExport = false
    @State private var exportOptions = ExportOptions()
    @State private var importing = false
    @State private var exporting = false
    @State private var exportFile = ExportFile(data: Data())
    @State private var exportType = UTType.dxf
    @State private var exportName = "cizim"
    @State private var canvasSize: CGSize = .zero
    @State private var textValue = ""
    @State private var textSize = ""
    @State private var editingText: (id: Int, text: String)?
    @State private var showCounts = false
    @State private var command = ""

    var body: some View {
        EditorBody(model: model, editor: model.editor, canvasSize: $canvasSize, command: $command,
                   showSymbols: $showSymbols, table: $table, onEditText: beginEditText)
            .navigationTitle(model.project?.name ?? "DXF Çizim")
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar { toolbar }
            .task {
                guard model.editor.doc == nil else { return }
                if let id = projectId ?? ProjectStore.lastProject { await model.load(id) }
            }
            .onDisappear { model.persist() }
            .sheet(isPresented: $showLayers) { LayersSheet(model: model, editor: model.editor) }
            .sheet(isPresented: $showSymbols) { SymbolPicker { model.editor.startSymbol($0) } }
            .sheet(item: $table) { k in
                switch k {
                case .lighting: LightingTableForm(textHeight: model.editor.textHeight) { model.editor.startTable($0) }
                case .power: PowerTableForm(textHeight: model.editor.textHeight) { model.editor.startTable($0) }
                case .title: TitleBlockForm(textHeight: model.editor.textHeight) { model.editor.startTable($0) }
                }
            }
            .sheet(isPresented: $showExport) { ExportSheet(options: $exportOptions, onGo: runExport) }
            .sheet(isPresented: $showCounts) { CountsSheet(items: model.editor.counts()) }
            .fileImporter(isPresented: $importing, allowedContentTypes: [.dxf, .data]) { r in
                if case .success(let url) = r {
                    do {
                        let m = try DxfImport.project(from: url)
                        model.persist()
                        Task { await model.load(m.id) }
                    } catch { model.error = error.localizedDescription }
                }
            }
            .fileExporter(isPresented: $exporting, document: exportFile, contentType: exportType, defaultFilename: exportName) { _ in }
            .alert("Hata", isPresented: Binding(get: { model.error != nil }, set: { if !$0 { model.error = nil } })) {
                Button("Tamam", role: .cancel) {}
            } message: { Text(model.error ?? "") }
            .alert("Yazı ekle", isPresented: Binding(get: { model.editor.textRequest != nil }, set: { if !$0 { model.editor.textRequest = nil } })) {
                TextField("Yazı", text: $textValue)
                TextField("Yükseklik", text: $textSize)
                Button("Ekle") {
                    if let p = model.editor.textRequest {
                        model.editor.addText(p.x, p.y, textValue, height: Calc.num(textSize) ?? model.editor.textHeight)
                    }
                    model.editor.textRequest = nil
                    textValue = ""
                }
                Button("Vazgeç", role: .cancel) { model.editor.textRequest = nil }
            }
            .alert("Yazıyı düzenle", isPresented: Binding(get: { editingText != nil }, set: { if !$0 { editingText = nil } })) {
                TextField("Yazı", text: $textValue)
                Button("Kaydet") {
                    if let e = editingText { model.editor.setText(e.id, textValue) }
                    editingText = nil
                }
                Button("Vazgeç", role: .cancel) { editingText = nil }
            }
            .onChange(of: model.editor.textRequest) { p in if p != nil { textSize = Calc.fmt(model.editor.textHeight, 2) } }
    }

    private func beginEditText() {
        if let t = model.editor.selectedText() {
            textValue = t.text
            editingText = t
        }
    }

    @ToolbarContentBuilder
    private var toolbar: some ToolbarContent {
        ToolbarItemGroup(placement: .primaryAction) {
            if let s = model.editor.scene {
                Button { model.editor.undo() } label: { Label("Geri al", systemImage: "arrow.uturn.backward") }
                    .disabled(model.editor.undoCount == 0)
                Button { model.editor.redo() } label: { Label("Yinele", systemImage: "arrow.uturn.forward") }
                    .disabled(model.editor.redoCount == 0)
                Button { model.view = CadCanvas.fit(s, in: canvasSize) } label: { Label("Sığdır", systemImage: "arrow.up.left.and.arrow.down.right") }
                Button { showLayers = true } label: { Label("Katmanlar", systemImage: "square.3.layers.3d") }
                Menu {
                    Button { importing = true } label: { Label("DXF aç", systemImage: "folder") }
                    Button {
                        if let d = model.editor.doc { save(d.save(), .dxf, model.project?.name ?? "cizim") }
                    } label: { Label("DXF olarak kaydet", systemImage: "square.and.arrow.down") }
                    Button { showExport = true } label: { Label("PDF / resim", systemImage: "printer") }
                    Divider()
                    Button { showCounts = true } label: { Label("Keşif (adetler)", systemImage: "number") }
                } label: { Label("Dosya", systemImage: "doc") }
            }
        }
    }

    private func save(_ data: Data, _ type: UTType, _ name: String) {
        exportFile = ExportFile(data: data)
        exportType = type
        exportName = name
        exporting = true
    }

    private func runExport() {
        guard let s = model.editor.scene else { return }
        let o = exportOptions
        let v = model.view
        let size = canvasSize
        let win: CGRect
        if o.viewOnly {
            let hw = size.width / 2 / v.zoom, hh = size.height / 2 / v.zoom
            win = CGRect(x: v.cx - hw, y: v.cy - hh, width: hw * 2, height: hh * 2)
        } else {
            win = Exporter.extents(s)
        }
        let name = model.project?.name ?? "cizim"
        let data = Exporter.render(s, hidden: model.hidden, window: win, o, title: name)
        // sayfa kapanırken ikinci sayfa açılamaz: bir sonraki döngüde
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { save(data, o.format == .pdf ? .pdf : .png, name) }
    }
}

/// Tuval + araçlar (düzenleyiciyi gözler)
private struct EditorBody: View {
    @ObservedObject var model: CadModel
    @ObservedObject var editor: CadEditor
    @Binding var canvasSize: CGSize
    @Binding var command: String
    @Binding var showSymbols: Bool
    @Binding var table: TableKind?
    let onEditText: () -> Void
    @FocusState private var commandFocused: Bool

    var body: some View {
        Group {
            if let s = editor.scene {
                VStack(spacing: 0) {
                    GeometryReader { geo in
                        CadCanvas(scene: s, view: $model.view, hidden: model.hidden, revision: editor.revision, overlay: editor.overlay,
                                  editor: editor, onKey: key)
                            .onAppear { canvasSize = geo.size; fitIfNeeded(s) }
                            .onChange(of: geo.size) { canvasSize = $0; fitIfNeeded(s) }
                            .overlay(alignment: .top) { statusBar }
                            .overlay(alignment: .bottomTrailing) { actionButtons.padding(10) }
                    }
                    toolStrip
                }
            } else if model.loading {
                ProgressView("Çizim açılıyor…").frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                EmptyCad(model: model).frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .background(CadColors.background)
        .onAppear { editor.onFit = { if let s = editor.scene { model.view = CadCanvas.fit(s, in: canvasSize) } } }
        .alert("Bilgi", isPresented: Binding(get: { editor.message != nil }, set: { if !$0 { editor.message = nil } })) {
            Button("Tamam", role: .cancel) {}
        } message: { Text(editor.message ?? "") }
    }

    private func fitIfNeeded(_ s: CadScene) {
        guard model.needsFit, canvasSize.width > 0 else { return }
        model.needsFit = false
        model.view = CadCanvas.fit(s, in: canvasSize)
    }

    /// macOS klavye: AutoCAD gibi harfler komut satırına, Enter çalıştırır
    private func key(_ k: String, _ cmd: Bool) -> Bool {
        if cmd {
            switch k.lowercased() {
            case "z": editor.undo(); return true
            case "y": editor.redo(); return true
            case "a": editor.selectAll(); return true
            default: return false
            }
        }
        switch k {
        case "esc":
            if !command.isEmpty { command = "" } else { editor.escape() }
            return true
        case "enter":
            if command.isEmpty { editor.enter() } else { run() }
            return true
        case "delete":
            if !command.isEmpty { command.removeLast() } else { editor.deleteSelection() }
            return true
        default:
            guard k.count == 1, let ch = k.first, !ch.isNewline else { return false }
            if ch == " " { if command.isEmpty { editor.enter() } else { run() }; return true }
            command.append(ch)
            return true
        }
    }

    private func run() {
        let c = command
        command = ""
        if !editor.command(c) { editor.message = "Anlaşılmayan komut: \(c)" }
    }

    private var statusBar: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 6) {
                Text(editor.tool.label).font(.caption.weight(.bold)).foregroundStyle(Theme.page)
                    .padding(.horizontal, 8).padding(.vertical, 3).background(Theme.accent, in: Capsule())
                Text(editor.live ?? editor.selectionInfo ?? editor.hint).font(.caption).lineLimit(2)
                Spacer(minLength: 0)
                if editor.busy { ProgressView().controlSize(.small) }
            }
            HStack(spacing: 6) {
                toggle("YAKALA", $editor.snapOn)
                toggle("ORTO", $editor.ortho)
                toggle("İZ", $editor.tracking)
                toggle("AÇI 15°", $editor.angleSnap)
                Spacer(minLength: 0)
                #if os(macOS)
                HStack(spacing: 4) {
                    Image(systemName: "terminal").font(.caption)
                    TextField("Komut (L, PL, C, M, CO, RO…) ya da sayı", text: $command)
                        .textFieldStyle(.plain).font(.caption.monospaced()).frame(width: 260)
                        .focused($commandFocused)
                        .onSubmit { if command.isEmpty { editor.enter() } else { run() } }
                }
                .padding(.horizontal, 8).padding(.vertical, 4)
                .background(Color.white.opacity(0.07), in: RoundedRectangle(cornerRadius: 6))
                #endif
            }
        }
        .padding(.horizontal, 10).padding(.vertical, 7)
        .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 10))
        .padding(8)
    }

    private func toggle(_ t: String, _ b: Binding<Bool>) -> some View {
        Button { b.wrappedValue.toggle() } label: {
            Text(t).font(.system(size: 10.5, weight: .semibold))
                .padding(.horizontal, 7).padding(.vertical, 3)
                .foregroundStyle(b.wrappedValue ? Theme.page : MK.text)
                .background(b.wrappedValue ? Theme.accent.opacity(0.9) : Color.white.opacity(0.08), in: Capsule())
        }
        .buttonStyle(.plain)
    }

    /// Bağlama göre: Bitir, Kapat, Vazgeç, Sil, Grupla, Çöz, Döndür (sembol)
    private var actionButtons: some View {
        HStack(spacing: 8) {
            if editor.canFinish {
                pill("Bitir", "checkmark") { editor.enter() }
                if editor.tool == .pline { pill("Kapat", "lasso") { editor.finishPolyline(closed: true) } }
            }
            if editor.tool == .symbol { pill("90°", "rotate.right") { editor.rotateSymbol() } }
            if !editor.selection.isEmpty && editor.tool == .select {
                if editor.selectedText() != nil { pill("Düzenle", "character.cursor.ibeam") { onEditText() } }
                if editor.selection.count >= 2 { pill("Grupla", "rectangle.3.group") { editor.groupSelection() } }
                pill("Çöz", "square.split.2x2") { editor.explodeSelection() }
                pill("Sil", "trash", danger: true) { editor.deleteSelection() }
            }
            if editor.tool != .select || !editor.selection.isEmpty { pill("Vazgeç", "xmark") { editor.escape() } }
        }
    }

    private func pill(_ t: String, _ icon: String, danger: Bool = false, _ a: @escaping () -> Void) -> some View {
        Button(action: a) {
            Label(t, systemImage: icon).font(.subheadline.weight(.semibold))
                .padding(.horizontal, 12).padding(.vertical, 8)
                .foregroundStyle(danger ? MK.bad : MK.text)
                .background(.ultraThinMaterial, in: Capsule())
                .overlay(Capsule().stroke(Color.white.opacity(0.12)))
        }
        .buttonStyle(.plain)
    }

    private static let editTools: [CadTool] = [.select, .move, .copy, .rotate, .scale, .mirror, .offset]
    private static let drawTools: [CadTool] = [.line, .pline, .circle, .rect, .text, .dimension, .measure]

    private var toolStrip: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 4) {
                ForEach(Self.drawTools) { toolButton($0) }
                divider
                Button { showSymbols = true } label: { stripLabel("Sembol", CadTool.symbol.symbol, on: editor.tool == .symbol) }.buttonStyle(.plain)
                Menu {
                    ForEach(TableKind.allCases) { k in Button(k.title) { table = k } }
                } label: { stripLabel("Tablo", "tablecells", on: false) }
                .menuStyle(.borderlessButton).fixedSize()
                divider
                ForEach(Self.editTools) { toolButton($0) }
            }
            .padding(.horizontal, 8).padding(.vertical, 6)
        }
        .background(MK.card)
        .overlay(alignment: .top) { Divider() }
    }

    private var divider: some View { Rectangle().fill(MK.edge).frame(width: 1, height: 34).padding(.horizontal, 4) }

    private func toolButton(_ t: CadTool) -> some View {
        Button { editor.selectTool(t) } label: { stripLabel(t.label, t.symbol, on: editor.tool == t) }
            .buttonStyle(.plain)
            .disabled(editor.busy)
    }

    private func stripLabel(_ title: String, _ icon: String, on: Bool) -> some View {
        VStack(spacing: 3) {
            Image(systemName: icon).font(.system(size: 17))
            Text(title).font(.system(size: 10))
        }
        .frame(width: 62, height: 46)
        .foregroundStyle(on ? Theme.page : MK.text)
        .background(on ? Theme.accent : Color.clear, in: RoundedRectangle(cornerRadius: 8))
        .contentShape(Rectangle())
    }
}

private struct EmptyCad: View {
    @ObservedObject var model: CadModel
    @State private var importing = false

    var body: some View {
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
        .fileImporter(isPresented: $importing, allowedContentTypes: [.dxf, .data]) { r in
            if case .success(let url) = r {
                do { let m = try DxfImport.project(from: url); Task { await model.load(m.id) } } catch { model.error = error.localizedDescription }
            }
        }
    }
}

private struct LayersSheet: View {
    @ObservedObject var model: CadModel
    @ObservedObject var editor: CadEditor
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            List {
                if let s = editor.scene {
                    Section("Yeni nesneler bu katmana") {
                        Picker("Etkin katman", selection: $editor.activeLayer) {
                            ForEach(s.layers.map(\.name), id: \.self) { Text($0).tag($0) }
                        }
                        if !editor.selection.isEmpty {
                            Button("Seçimi etkin katmana taşı") { editor.applyLayer(editor.activeLayer) }
                        }
                    }
                    Section("Görünürlük") {
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
            }
            .navigationTitle("Katmanlar")
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Tamam") { dismiss() } } }
        }
        #if os(macOS)
        .frame(minWidth: 380, minHeight: 460)
        #endif
    }
}

private struct CountsSheet: View {
    let items: [(name: String, count: Int, library: Bool)]
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            List {
                if items.isEmpty { Text("Çizimde blok/sembol yerleştirmesi yok.").foregroundStyle(.secondary) }
                ForEach(items.indices, id: \.self) { i in
                    HStack {
                        Text(items[i].name).opacity(items[i].library ? 1 : 0.65)
                        Spacer()
                        Text("\(items[i].count) ad.").fontWeight(.semibold).foregroundStyle(Theme.accent)
                    }
                }
            }
            .navigationTitle("Keşif — sembol adetleri")
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Kapat") { dismiss() } } }
        }
        #if os(macOS)
        .frame(minWidth: 420, minHeight: 460)
        #endif
    }
}
