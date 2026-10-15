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

/// Android CadScreen ile aynı palet
enum Bar {
    static let color = Color(hex: 0x141719, alpha: 0.90)
    static let primary = Color(hex: 0xF2B266)
    static let onPrimary = Color(hex: 0x1A1205)
    static let onSurface = Color(hex: 0xEDEADF)
    static let onSurfaceVariant = Color(hex: 0x8DA897)
    static let error = Color(hex: 0xFF6B5E)
}

/// DXF çizim ekranı (Android CadScreen düzeni): tam ekran tuval, gizlenebilir üst çubuk, ipucu çubuğu,
/// dar ekranda alt sekmeli araçlar, geniş ekranda sol ray + açılır panel
struct CadScreen: View {
    let projectId: String?
    @EnvironmentObject private var nav: AppNav
    @StateObject private var model = CadModel()
    @State private var showLayers = false
    @State private var showSymbols = false
    @State private var showProps = false
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
    @State private var numberAsk: CadTool?
    @State private var numberValue = ""

    var body: some View {
        EditorBody(model: model, editor: model.editor, canvasSize: $canvasSize, command: $command,
                   showSymbols: $showSymbols, table: $table, showProps: $showProps, showLayers: $showLayers,
                   onEditText: beginEditText, onNumber: askNumber, menu: menu, onSave: saveDxf, onHome: { nav.home() })
            #if os(iOS)
            .toolbar(.hidden, for: .navigationBar)
            #else
            .navigationBarBackButtonHidden(true)
            #endif
            .task {
                guard model.editor.doc == nil else { return }
                if let id = projectId ?? ProjectStore.lastProject { await model.load(id) }
            }
            .onDisappear { model.persist() }
            .sheet(isPresented: $showLayers) { LayersSheet(model: model, editor: model.editor) }
            .sheet(isPresented: $showProps) { PropsSheet(editor: model.editor) }
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
            .alert(numberTitle, isPresented: Binding(get: { numberAsk != nil }, set: { if !$0 { numberAsk = nil } })) {
                TextField("Değer", text: $numberValue)
                    #if os(iOS)
                    .keyboardType(.numbersAndPunctuation)
                    #endif
                Button("Tamam") {
                    let v = numberValue.trimmingCharacters(in: .whitespaces)
                    if !v.isEmpty && !model.editor.command(v) { model.editor.message = "Anlaşılmayan değer: \(v)" }
                    numberAsk = nil
                }
                Button("Vazgeç", role: .cancel) { numberAsk = nil }
            } message: { Text(numberHint) }
            .onChange(of: model.editor.textRequest) { p in if p != nil { textSize = Calc.fmt(model.editor.textHeight, 2) } }
    }

    private var numberTitle: String {
        switch numberAsk {
        case .offset: return "Öteleme mesafesi"
        case .symbol: return "Sembol ölçeği"
        case .rotate: return "Döndürme açısı (°)"
        case .scale: return "Ölçek çarpanı"
        case .circle: return "Yarıçap"
        case .rect: return "Genişlik, yükseklik"
        case .dimension: return "Ölçü çizgisi uzaklığı"
        default: return "Değer"
        }
    }

    private var numberHint: String {
        switch numberAsk {
        case .line, .pline: return "Uzunluk (imleç yönünde), uzunluk<açı ya da dx,dy"
        case .rect: return "ör. 400,300"
        default: return ""
        }
    }

    private func askNumber() {
        numberValue = ""
        numberAsk = model.editor.tool
    }

    private func beginEditText() {
        if let t = model.editor.selectedText() {
            textValue = t.text
            editingText = t
        }
    }

    private func saveDxf() {
        if let d = model.editor.doc {
            model.persist()
            save(d.save(), .dxf, model.project?.name ?? "cizim")
        }
    }

    /// Üst çubuk menüsü
    private var menu: CadMenuActions {
        CadMenuActions(
            open: { importing = true },
            newBlank: {
                do { let m = try DxfImport.blank(); model.persist(); Task { await model.load(m.id) } } catch { model.error = error.localizedDescription }
            },
            saveAs: saveDxf,
            counts: { showCounts = true },
            export: { showExport = true }
        )
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

struct CadMenuActions {
    let open: () -> Void
    let newBlank: () -> Void
    let saveAs: () -> Void
    let counts: () -> Void
    let export: () -> Void
}

/// Araç kategorileri: telefonda alt sekmeler, geniş ekranda sol ray
enum ToolCat: CaseIterable {
    case draw, edit, insert, settings
    var label: String { ["Çiz", "Düzenle", "Ekle", "Ayarlar"][index] }
    var icon: String { ["pencil.tip", "pencil", "plus.square", "slider.horizontal.3"][index] }
    private var index: Int { ToolCat.allCases.firstIndex(of: self)! }
}

private struct ToolItem: Identifiable {
    let id = UUID()
    let icon: String
    let label: String
    let active: Bool
    var enabled = true
    let action: () -> Void
}

/// Tuval + çubuklar (düzenleyiciyi gözler)
private struct EditorBody: View {
    @ObservedObject var model: CadModel
    @ObservedObject var editor: CadEditor
    @Binding var canvasSize: CGSize
    @Binding var command: String
    @Binding var showSymbols: Bool
    @Binding var table: TableKind?
    @Binding var showProps: Bool
    @Binding var showLayers: Bool
    let onEditText: () -> Void
    let onNumber: () -> Void
    let menu: CadMenuActions
    let onSave: () -> Void
    let onHome: () -> Void
    @State private var topHidden = false
    @State private var cat = ToolCat.draw
    @State private var flyout: ToolCat?
    @State private var toast: String?
    @State private var showTables = false

    private static let railW: CGFloat = 64
    private static let sideW: CGFloat = 92

    var body: some View {
        GeometryReader { geo in
            let wide = geo.size.width >= 600
            ZStack(alignment: .top) {
                if let s = editor.scene {
                    CadCanvas(scene: s, view: $model.view, hidden: model.hidden, revision: editor.revision, overlay: editor.overlay,
                              editor: editor, onKey: key)
                        .ignoresSafeArea()
                        .onAppear { canvasSize = geo.size; fitIfNeeded(s) }
                        .onChange(of: geo.size) { canvasSize = $0; fitIfNeeded(s) }
                } else {
                    CadColors.background.ignoresSafeArea()
                    if model.loading {
                        VStack(spacing: 12) { ProgressView(); Text("Çizim okunuyor…").foregroundStyle(Bar.onSurface) }
                            .frame(maxWidth: .infinity, maxHeight: .infinity)
                    } else {
                        EmptyCad(model: model, onOpen: menu.open).frame(maxWidth: .infinity, maxHeight: .infinity)
                    }
                }

                VStack(spacing: 0) {
                    if !topHidden { topBar } else { Spacer().frame(height: 0) }
                    if editor.scene != nil {
                        if wide {
                            HStack(alignment: .top, spacing: 0) {
                                sideTools
                                VStack(alignment: .leading, spacing: 0) {
                                    hintBar.frame(maxWidth: 560, alignment: .leading)
                                    Spacer()
                                    toastView
                                }
                                Spacer(minLength: 0)
                            }
                        } else {
                            Spacer()
                            toastView
                            hintBar
                            bottomTools
                        }
                    } else {
                        Spacer()
                    }
                }

                if topHidden {
                    HStack {
                        Spacer()
                        Button { withAnimation { topHidden = false } } label: {
                            Image(systemName: "chevron.down").font(.system(size: 16, weight: .semibold)).foregroundStyle(Bar.onSurface)
                                .frame(width: 40, height: 40).background(Bar.color, in: Circle())
                        }
                        .buttonStyle(.plain)
                        .padding(8)
                    }
                }
            }
        }
        .background(CadColors.background.ignoresSafeArea())
        .onAppear { editor.onFit = { if let s = editor.scene { model.view = CadCanvas.fit(s, in: canvasSize) } } }
        .onChange(of: editor.message) { m in
            guard let m else { return }
            toast = m
            editor.message = nil
            DispatchQueue.main.asyncAfter(deadline: .now() + 3.5) { if toast == m { toast = nil } }
        }
        .confirmationDialog("Tablo ekle", isPresented: $showTables, titleVisibility: .visible) {
            ForEach(TableKind.allCases) { k in Button(k.title) { table = k } }
        }
    }

    private func fitIfNeeded(_ s: CadScene) {
        guard model.needsFit, canvasSize.width > 0 else { return }
        model.needsFit = false
        model.view = CadCanvas.fit(s, in: canvasSize)
    }

    // ───────────────────────── üst çubuk ─────────────────────────

    private var topBar: some View {
        HStack(spacing: 2) {
            iconButton("house", "Ana sayfa", action: onHome)
            VStack(alignment: .leading, spacing: 1) {
                Text((editor.scene?.name ?? model.project?.name ?? "DXF Çizim") + (editor.dirty ? " •" : ""))
                    .font(.system(size: 16, weight: .medium)).foregroundStyle(Bar.onSurface).lineLimit(1)
                if let s = editor.scene {
                    Text("\(s.entityCount) nesne · \(s.layers.count) katman · \(s.parseMs) ms")
                        .font(.system(size: 12)).foregroundStyle(Bar.onSurfaceVariant).lineLimit(1)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            if editor.busy { ProgressView().controlSize(.small).padding(.trailing, 4) }
            if let s = editor.scene {
                iconButton("arrow.up.left.and.arrow.down.right", "Sığdır") { withAnimation(.easeOut(duration: 0.25)) { model.view = CadCanvas.fit(s, in: canvasSize) } }
                iconButton("square.3.layers.3d", "Katmanlar") { showLayers = true }
                iconButton("square.and.arrow.down", "Kaydet", enabled: editor.dirty || editor.undoCount > 0, action: onSave)
            }
            iconButton("chevron.up", "Üst çubuğu gizle") { withAnimation { topHidden = true } }
            Menu {
                Button { menu.open() } label: { Label("DXF aç", systemImage: "folder") }
                Button { menu.newBlank() } label: { Label("Yeni boş proje", systemImage: "doc.badge.plus") }
                if editor.scene != nil {
                    Button { menu.saveAs() } label: { Label("Farklı kaydet", systemImage: "square.and.arrow.down.on.square") }
                    Button { menu.counts() } label: { Label("Keşif listesi", systemImage: "checklist") }
                    Button { menu.export() } label: { Label("PDF / resim / yazdır", systemImage: "doc.richtext") }
                }
            } label: {
                Image(systemName: "ellipsis").font(.system(size: 18)).foregroundStyle(Bar.onSurface).frame(width: 44, height: 44)
            }
            .menuStyle(.borderlessButton)
            .fixedSize()
        }
        .padding(.leading, 4).padding(.trailing, 4).padding(.vertical, 4)
        .background(Bar.color.ignoresSafeArea(edges: .top))
        .transition(.move(edge: .top).combined(with: .opacity))
    }

    private func iconButton(_ icon: String, _ label: String, enabled: Bool = true, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: icon).font(.system(size: 18)).frame(width: 44, height: 44)
                .foregroundStyle(enabled ? Bar.onSurface : Bar.onSurface.opacity(0.3))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
        .help(label)
        .accessibilityLabel(label)
    }

    // ───────────────────────── ipucu çubuğu ─────────────────────────

    private var drawTool: Bool { [.line, .pline, .circle, .rect].contains(editor.tool) }
    private var pivotTool: Bool { editor.tool == .rotate || editor.tool == .scale }
    private var drawStarted: Bool { editor.tool == .pline ? editor.plineCount > 0 : editor.hasAnchor }

    private var hintText: String {
        let ed = editor
        #if os(iOS)
        let tapWord = "dokunun"
        #else
        let tapWord = "tıklayın"
        #endif
        if !command.isEmpty { return "Komut: \(command)" }
        if pivotTool && ed.hasPivot {
            return ed.live ?? (ed.tool == .rotate ? "Bir noktadan sürükleyerek döndürün ya da açı seçin." : "Bir noktadan sürükleyerek ölçekleyin ya da çarpan seçin.")
        }
        if ed.tool == .mirror && !ed.selection.isEmpty { return ed.hint }
        if ed.tool == .select, let info = ed.selectionInfo { return info }
        if drawTool && drawStarted {
            return ed.live ?? (ed.tool == .circle ? "Yarıçap kadar sürükleyin ya da yarıçapı girin." :
                ed.tool == .rect ? "Karşı köşeye \(tapWord) ya da ölçüyü girin." : "Sonraki noktaya \(tapWord) ya da uzunluk girin.")
        }
        if ed.tool == .symbol && ed.placingTable { return "\(ed.symbol?.name ?? "Tablo"): sol üst köşesini koyacağın yere \(tapWord)." }
        if ed.tool == .symbol { return ed.live ?? "\(ed.symbol?.name ?? "Sembol"): yerleştirmek için \(tapWord). Ölçek ×\(CadEditor.fmt(ed.symbolScale))" }
        if ed.tool == .offset {
            return ed.hasOffsetTarget ? "Hangi tarafa? O tarafa \(tapWord)." : "Mesafe \(CadEditor.fmt(ed.offsetDist)) · Ötelenecek nesneye \(tapWord)."
        }
        if ed.tool == .dimension {
            return ed.live ?? (ed.dimStage == 0 ? "Ölçülecek ilk noktaya \(tapWord) (uçlara yakalar)." :
                ed.dimStage == 1 ? "İkinci noktaya \(tapWord) ya da sürükleyin." : "Ölçü çizgisinin yerine \(tapWord).")
        }
        if let l = ed.live, [.measure, .move, .copy, .text].contains(ed.tool) { return l }
        #if os(iOS)
        if ed.tool == .select && ed.selection.isEmpty { return "Seçmek için nesneye dokunun. Pencere seçimi: uzun basıp sürükleyin." }
        #else
        if ed.tool == .select && ed.selection.isEmpty { return "Seçmek için nesneye tıklayın. Pencere seçimi: boşlukta sürükleyin (soldan sağa içindekiler, sağdan sola değenler)." }
        #endif
        return ed.hint
    }

    /// Yalnız yol gösteren yazı: 4 sn sonra küçülüp ⓘ olur
    private var genericHint: Bool {
        command.isEmpty && editor.live == nil && editor.selection.isEmpty
            && ![.symbol, .offset, .mirror].contains(editor.tool)
            && !(drawTool && drawStarted) && !(pivotTool && editor.hasPivot) && editor.tool != .dimension
    }

    @State private var hintCollapsed = false

    @ViewBuilder
    private var hintBar: some View {
        let text = hintText
        if genericHint && hintCollapsed {
            Button { hintCollapsed = false } label: {
                Image(systemName: "info.circle").font(.system(size: 18)).foregroundStyle(Bar.onSurfaceVariant)
                    .frame(width: 36, height: 36).background(Bar.color, in: Circle())
            }
            .buttonStyle(.plain)
            .padding(.horizontal, 12).padding(.vertical, 6)
            .frame(maxWidth: .infinity, alignment: .leading)
        } else {
            VStack(alignment: .leading, spacing: 0) {
                Text(text).font(.system(size: 12)).foregroundStyle(.white).padding(.vertical, 10).padding(.trailing, 8)
                    .fixedSize(horizontal: false, vertical: true)
                let buttons = hintButtons
                if !buttons.isEmpty {
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: 4) { ForEach(buttons) { b in hintButton(b) } }.padding(.bottom, 6)
                    }
                }
            }
            .padding(.leading, 12).padding(.trailing, 4)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Bar.color, in: RoundedRectangle(cornerRadius: 10))
            .padding(.horizontal, 12).padding(.vertical, 6)
            .task(id: text) {
                hintCollapsed = false
                guard genericHint else { return }
                try? await Task.sleep(nanoseconds: 4_000_000_000)
                if !Task.isCancelled && genericHint { withAnimation { hintCollapsed = true } }
            }
        }
    }

    private struct HintBtn: Identifiable {
        let id = UUID()
        let title: String
        var chip = false
        var on = false
        let action: () -> Void
    }

    private var hintButtons: [HintBtn] {
        let ed = editor
        var b: [HintBtn] = []
        if drawTool && drawStarted { b.append(HintBtn(title: "Değer", action: onNumber)) }
        if (ed.tool == .line || ed.tool == .pline) && drawStarted { b.append(HintBtn(title: "Bitir") { ed.enter() }) }
        if ed.tool == .pline && ed.plineCount >= 3 { b.append(HintBtn(title: "Kapat") { ed.finishPolyline(closed: true) }) }
        if ed.tool == .offset { b.append(HintBtn(title: "Mesafe", action: onNumber)) }
        if ed.tool == .symbol {
            for deg in [0.0, 90.0, 180.0, 270.0] {
                b.append(HintBtn(title: "\(Int(deg))°", chip: true, on: ed.symbolRot == deg) { ed.setSymbolRot(deg) })
            }
            if !ed.placingTable {
                b.append(HintBtn(title: "Ölçek", action: onNumber))
                b.append(HintBtn(title: "Değiştir") { showSymbols = true })
            }
        }
        if pivotTool && ed.hasPivot {
            if ed.tool == .rotate {
                b.append(HintBtn(title: "90°", chip: true) { ed.rotateBy(90) })
                b.append(HintBtn(title: "−90°", chip: true) { ed.rotateBy(-90) })
                b.append(HintBtn(title: "180°", chip: true) { ed.rotateBy(180) })
            } else {
                b.append(HintBtn(title: "×0.5", chip: true) { ed.scaleBy(0.5) })
                b.append(HintBtn(title: "×2", chip: true) { ed.scaleBy(2) })
            }
            b.append(HintBtn(title: "Değer", action: onNumber))
            b.append(HintBtn(title: "İptal") { ed.cancelPivot() })
        }
        if ed.tool == .mirror { b.append(HintBtn(title: ed.mirrorKeep ? "Orijinal: koru" : "Orijinal: sil") { ed.mirrorKeep.toggle() }) }
        if ed.tool == .select && ed.selectedText() != nil { b.append(HintBtn(title: "Düzenle", action: onEditText)) }
        if ed.tool == .select && !ed.selection.isEmpty { b.append(HintBtn(title: "Seçimi kaldır") { ed.clearSelection() }) }
        if ed.tool != .select { b.append(HintBtn(title: "Vazgeç") { ed.escape() }) }
        return b
    }

    private func hintButton(_ b: HintBtn) -> some View {
        Button(action: b.action) {
            Text(b.title).font(.system(size: 13, weight: b.chip ? .regular : .medium))
                .foregroundStyle(b.on ? Bar.onPrimary : Bar.primary)
                .padding(.horizontal, b.chip ? 8 : 10).padding(.vertical, 6)
                .background(b.on ? Bar.primary : Color.clear, in: RoundedRectangle(cornerRadius: 8))
                .overlay(RoundedRectangle(cornerRadius: 8).stroke(b.chip ? Bar.primary.opacity(0.5) : Color.clear, lineWidth: 1))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    @ViewBuilder
    private var toastView: some View {
        if let t = toast {
            Text(t).font(.system(size: 13)).foregroundStyle(Bar.onSurface)
                .padding(.horizontal, 14).padding(.vertical, 10)
                .background(Color(hex: 0x2A2F33), in: RoundedRectangle(cornerRadius: 8))
                .padding(.horizontal, 16).padding(.bottom, 4)
                .frame(maxWidth: .infinity)
                .transition(.opacity)
        }
    }

    // ───────────────────────── araçlar ─────────────────────────

    private func tools(_ c: ToolCat) -> [ToolItem] {
        let ed = editor
        let sel = !ed.selection.isEmpty
        let free = !ed.busy
        switch c {
        case .draw:
            return [
                ToolItem(icon: "line.diagonal", label: "Çizgi", active: ed.tool == .line) { ed.selectTool(.line) },
                ToolItem(icon: "point.topleft.down.curvedto.point.bottomright.up", label: "Ç.Çizgi", active: ed.tool == .pline) { ed.selectTool(.pline) },
                ToolItem(icon: "circle", label: "Daire", active: ed.tool == .circle) { ed.selectTool(.circle) },
                ToolItem(icon: "square", label: "Dikdörtgen", active: ed.tool == .rect) { ed.selectTool(.rect) },
                ToolItem(icon: "textformat", label: "Yazı", active: ed.tool == .text) { ed.selectTool(.text) },
                ToolItem(icon: "arrow.left.and.right", label: "Ölçü", active: ed.tool == .dimension) { ed.selectTool(.dimension) },
                ToolItem(icon: "ruler", label: "Ölç", active: ed.tool == .measure) { ed.selectTool(.measure) },
            ]
        case .edit:
            return [
                ToolItem(icon: "hand.tap", label: "Seç", active: ed.tool == .select) { ed.selectTool(.select) },
                ToolItem(icon: "arrow.up.and.down.and.arrow.left.and.right", label: "Taşı", active: ed.tool == .move, enabled: sel) { ed.selectTool(.move) },
                ToolItem(icon: "doc.on.doc", label: "Kopyala", active: ed.tool == .copy, enabled: sel) { ed.selectTool(.copy) },
                ToolItem(icon: "rotate.right", label: "Döndür", active: ed.tool == .rotate, enabled: sel) { ed.selectTool(.rotate) },
                ToolItem(icon: "arrow.up.left.and.arrow.down.right", label: "Ölçekle", active: ed.tool == .scale, enabled: sel) { ed.selectTool(.scale) },
                ToolItem(icon: "arrow.left.and.right.righttriangle.left.righttriangle.right", label: "Ayna", active: ed.tool == .mirror, enabled: sel) { ed.selectTool(.mirror) },
                ToolItem(icon: "square.on.square.dashed", label: "Ötele", active: ed.tool == .offset) { ed.selectTool(.offset) },
                ToolItem(icon: "paintpalette", label: "Özellik", active: false, enabled: sel && free) { showProps = true },
                ToolItem(icon: "rectangle.3.group", label: "Grupla", active: false, enabled: ed.selection.count >= 2 && free) { ed.groupSelection() },
                ToolItem(icon: "square.split.2x2", label: "Çöz", active: false, enabled: sel && free) { ed.explodeSelection() },
                ToolItem(icon: "trash", label: "Sil", active: false, enabled: sel && free) { ed.deleteSelection() },
            ]
        case .insert:
            return [
                ToolItem(icon: "bolt.circle", label: "Sembol", active: ed.tool == .symbol && !ed.placingTable) { showSymbols = true },
                ToolItem(icon: "tablecells", label: "Tablo", active: ed.tool == .symbol && ed.placingTable) { showTables = true },
                ToolItem(icon: "textformat", label: "Yazı", active: ed.tool == .text) { ed.selectTool(.text) },
            ]
        case .settings:
            return [
                ToolItem(icon: "scope", label: "Mıknatıs", active: ed.snapOn) { ed.snapOn.toggle() },
                ToolItem(icon: "safari", label: "Açı 15°", active: ed.angleSnap) { ed.angleSnap.toggle() },
                ToolItem(icon: "plus", label: "Orto", active: ed.ortho) { ed.ortho.toggle() },
                ToolItem(icon: "line.3.horizontal", label: "İz", active: ed.tracking) { ed.tracking.toggle() },
            ]
        }
    }

    private func toolButton(_ t: ToolItem, wide: Bool = false, after: (() -> Void)? = nil) -> some View {
        let fg = !t.enabled ? Bar.onSurface.opacity(0.3) : t.active ? Bar.onPrimary : Bar.onSurface
        return Button {
            t.action()
            after?()
        } label: {
            VStack(spacing: 2) {
                Image(systemName: t.icon).font(.system(size: 19)).frame(height: 24)
                Text(t.label).font(.system(size: 11)).lineLimit(1)
            }
            .foregroundStyle(fg)
            .padding(.horizontal, 8).padding(.vertical, 6)
            .frame(minWidth: wide ? Self.sideW - 8 : 56)
            .background(t.active ? Bar.primary : Color.clear, in: RoundedRectangle(cornerRadius: 10))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(!t.enabled)
    }

    /// Telefon (dik): üstte seçili kategorinin araçları, altta kategoriler + geri al/yinele
    private var bottomTools: some View {
        VStack(spacing: 0) {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 2) { ForEach(tools(cat)) { toolButton($0) } }.padding(.horizontal, 4).padding(.vertical, 4)
            }
            Divider().overlay(Color.white.opacity(0.13))
            HStack(spacing: 0) {
                ForEach(ToolCat.allCases, id: \.self) { c in
                    let on = c == cat
                    Button { cat = c } label: {
                        HStack(spacing: 4) {
                            Image(systemName: c.icon).font(.system(size: 15))
                            Text(c.label).font(.system(size: 12, weight: on ? .bold : .regular)).lineLimit(1)
                        }
                        .foregroundStyle(on ? Bar.primary : Bar.onSurfaceVariant)
                        .frame(maxWidth: .infinity).padding(.vertical, 8)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
                undoRedo
            }
            .padding(.horizontal, 4).padding(.vertical, 2)
        }
        .background(Bar.color.ignoresSafeArea(edges: .bottom))
    }

    private var undoRedo: some View {
        Group {
            iconButton("arrow.uturn.backward", "Geri al", enabled: editor.undoCount > 0 && !editor.busy) { editor.undo() }
            iconButton("arrow.uturn.forward", "Yinele", enabled: editor.redoCount > 0 && !editor.busy) { editor.redo() }
        }
    }

    /// Geniş ekran: solda 4 ana düğmeli ray; birine dokununca o kategorinin araçları sağa açılır
    private var sideTools: some View {
        HStack(alignment: .top, spacing: 0) {
            ScrollView(.vertical, showsIndicators: false) {
                VStack(spacing: 6) {
                    ForEach(ToolCat.allCases, id: \.self) { c in
                        let on = c == flyout
                        let holds = c != .settings && tools(c).contains { $0.active }
                        Button { withAnimation(.easeOut(duration: 0.2)) { flyout = on ? nil : c } } label: {
                            VStack(spacing: 2) {
                                Image(systemName: c.icon).font(.system(size: 19))
                                Text(c.label).font(.system(size: 10)).lineLimit(1)
                                Circle().fill(holds ? Bar.primary : Color.clear).frame(width: 5, height: 5).padding(.top, 1)
                            }
                            .foregroundStyle(on || holds ? Bar.primary : Bar.onSurface)
                            .frame(width: Self.railW - 8).padding(.vertical, 8)
                            .background(on ? Bar.primary.opacity(0.22) : Color.clear, in: RoundedRectangle(cornerRadius: 12))
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                    Divider().frame(width: 40).overlay(Color.white.opacity(0.2)).padding(.vertical, 6)
                    VStack(spacing: 0) { undoRedo }
                }
                .padding(.vertical, 6)
            }
            .frame(width: Self.railW)
            .frame(maxHeight: .infinity)
            .background(Bar.color.ignoresSafeArea(edges: [.bottom, .leading]))
            if let f = flyout {
                ScrollView(.vertical, showsIndicators: false) {
                    VStack(spacing: 4) {
                        Text(f.label).font(.system(size: 11, weight: .bold)).foregroundStyle(Bar.primary).padding(.bottom, 4)
                        ForEach(tools(f)) { t in
                            toolButton(t, wide: true) { if f != .settings { withAnimation(.easeOut(duration: 0.2)) { flyout = nil } } }
                        }
                    }
                    .padding(.vertical, 6)
                }
                .frame(width: Self.sideW)
                .frame(maxHeight: .infinity)
                .background(Color(hex: 0x1A1E21, alpha: 0.95).ignoresSafeArea(edges: .bottom))
                .transition(.move(edge: .leading).combined(with: .opacity))
            }
        }
    }

    // ───────────────────────── klavye (macOS) ─────────────────────────

    /// AutoCAD gibi: harfler komut satırına, Enter/boşluk çalıştırır
    private func key(_ k: String, _ cmd: Bool) -> Bool {
        if cmd {
            switch k.lowercased() {
            case "z": editor.undo(); return true
            case "y": editor.redo(); return true
            case "a": editor.selectAll(); return true
            case "s": onSave(); return true
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
}

/// Seçimin katmanı ve rengi (Android PropsSheet)
private struct PropsSheet: View {
    @ObservedObject var editor: CadEditor
    @Environment(\.dismiss) private var dismiss
    private static let colors: [(String, Int, Color)] = [
        ("Katman rengi", 256, .gray), ("Kırmızı", 1, .red), ("Sarı", 2, .yellow), ("Yeşil", 3, .green), ("Camgöbeği", 4, .cyan),
        ("Mavi", 5, .blue), ("Mor", 6, Color(hex: 0xFF00FF)), ("Beyaz/siyah", 7, .white),
    ]

    var body: some View {
        NavigationStack {
            List {
                Section("\(editor.selection.count) nesne seçili") {
                    if let s = editor.scene {
                        Menu {
                            ForEach(s.layers.map(\.name), id: \.self) { n in Button(n) { editor.applyLayer(n); dismiss() } }
                        } label: { Label("Katmanı değiştir", systemImage: "square.3.layers.3d") }
                    }
                }
                Section("Renk") {
                    ForEach(Self.colors, id: \.1) { c in
                        Button { editor.applyColor(c.1); dismiss() } label: {
                            HStack {
                                Circle().fill(c.2).frame(width: 16, height: 16).overlay(Circle().stroke(Color.white.opacity(0.3)))
                                Text(c.0)
                            }
                        }
                    }
                }
            }
            .navigationTitle("Özellikler")
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Kapat") { dismiss() } } }
        }
        #if os(macOS)
        .frame(minWidth: 360, minHeight: 440)
        #endif
    }
}

private struct EmptyCad: View {
    @ObservedObject var model: CadModel
    let onOpen: () -> Void

    var body: some View {
        VStack(spacing: 16) {
            Image(systemName: "pencil.and.ruler").font(.system(size: 54)).foregroundStyle(Bar.primary)
            Text(model.error ?? "Bir DXF dosyası açın.\nDWG dosyalarını AutoCAD'de \"Farklı Kaydet → DXF\" ile kaydedin.")
                .multilineTextAlignment(.center)
                .foregroundStyle(model.error != nil ? Bar.error : Bar.onSurfaceVariant)
            HStack {
                Button(action: onOpen) { Label("DXF aç", systemImage: "folder") }.buttonStyle(.borderedProminent)
                Button {
                    do { let m = try DxfImport.blank(); Task { await model.load(m.id) } } catch { model.error = error.localizedDescription }
                } label: { Label("Boş proje", systemImage: "doc.badge.plus") }.buttonStyle(.bordered)
            }
        }
        .padding(32)
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
