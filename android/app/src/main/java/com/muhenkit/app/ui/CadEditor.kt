package com.muhenkit.app.ui

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.muhenkit.app.cad.CadDocument
import com.muhenkit.app.cad.CadException
import com.muhenkit.app.cad.CadGlView
import com.muhenkit.app.cad.CadRenderer
import com.muhenkit.app.cad.CadScene
import com.muhenkit.app.cad.CadSymbol
import com.muhenkit.app.cad.SymbolLibrary
import com.muhenkit.app.cad.EditInput
import com.muhenkit.app.cad.GlyphAtlas
import com.muhenkit.app.cad.Overlay
import com.muhenkit.app.cad.AppScope
import com.muhenkit.app.cad.ProjectMeta
import com.muhenkit.app.cad.ProjectStore
import com.muhenkit.app.cad.ViewState
import com.muhenkit.app.cad.TextMesh
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlin.math.abs

enum class CadTool(val label: String, val hint: String) {
    SELECT("Seç", "Seçmek için nesneye dokunun. Tekrar dokunmak seçimi kaldırır."),
    MOVE("Taşı", "Seçimi parmağınızla sürükleyin. Uç, orta ve merkez noktalara yakalar."),
    COPY("Kopyala", "Kopyayı parmağınızla sürükleyip bırakın. Art arda kopyalayabilirsiniz."),
    LINE("Çizgi", "İlk noktaya dokunun ya da basıp sürükleyin. Çizgiler zincir hâlinde devam eder."),
    PLINE("Ç.Çizgi", "Noktalara sırayla dokunun. Bitince Bitir ya da Kapat'a basın."),
    CIRCLE("Daire", "Merkeze dokunun, yarıçap kadar sürükleyip bırakın."),
    RECT("Dikdörtgen", "Bir köşeye basın, karşı köşeye sürükleyip bırakın."),
    OFFSET("Ötele", "Ötelenecek nesneye dokunun."),
    SYMBOL("Sembol", "Yerleştirmek için dokunun ya da basıp sürükleyin."),
    TEXT("Yazı", "Yazının yerleşeceği noktaya dokunun."),
    MEASURE("Ölç", "İlk noktaya dokunun, ikinci noktaya sürükleyip bırakın."),
    ROTATE("Döndür", "Dönme merkezine dokunun."),
    SCALE("Ölçekle", "Ölçekleme merkezine dokunun."),
    MIRROR("Ayna", "Ayna ekseninin ilk noktasına basın, ikinci noktaya sürükleyip bırakın."),
    DIMENSION("Ölçü", "Ölçülecek iki noktaya dokunun (ya da sürükleyin), sonra ölçü çizgisinin yerine dokunun."),
    ;

    /** Seçim olmadan çalışmayan düzenleme araçları */
    val needsSelection: Boolean get() = this == MOVE || this == COPY || this == ROTATE || this == SCALE || this == MIRROR
}

/**
 * DXF düzenleyicisinin durumu ve iş mantığı. Çekirdek çağrıları arka planda,
 * arayüz durumu (Compose) ana iş parçacığında güncellenir.
 */
class CadEditor(private val context: Context, private val scope: CoroutineScope) {
    var glView: CadGlView? = null
        set(v) {
            field = v
            v?.editInput = input
            v?.onViewChanged = { viewChanged() }
        }

    var doc by mutableStateOf<CadDocument?>(null); private set
    var scene by mutableStateOf<CadScene?>(null); private set
    var loading by mutableStateOf(false); private set
    var busy by mutableStateOf(false); private set
    var error by mutableStateOf<String?>(null)
    var message by mutableStateOf<String?>(null)
    var hidden by mutableStateOf(emptySet<Int>()); private set
    var tool by mutableStateOf(CadTool.SELECT); private set
    var selection by mutableStateOf(emptySet<Int>()); private set
    var selectionInfo by mutableStateOf<String?>(null); private set
    var lineStart by mutableStateOf<Pair<Double, Double>?>(null); private set
    /** Çoklu çizginin şimdiye kadarki noktaları */
    var plinePts by mutableStateOf<List<Pair<Double, Double>>>(emptyList()); private set
    var undoCount by mutableStateOf(0); private set
    var redoCount by mutableStateOf(0); private set
    var dirty by mutableStateOf(false); private set
    var activeLayer by mutableStateOf("0")
    /** Orto: çizgi/taşı/kopya yalnızca yatay-dikey */
    var ortho by mutableStateOf(false)
    /** Mıknatıs: uç, orta, merkez ve ekleme noktalarına yakalama */
    var snapOn by mutableStateOf(true)
    /** Açı yakalama: 15°'nin katlarına ±3° içinde yapışma (döndür, çizgi, taşı) */
    var angleSnap by mutableStateOf(true)
    /** Hizalama izleri: geçilen yakalama noktalarından yatay/dikey, referans çizgiye paralel/dik kesikli kılavuzlar */
    var tracking by mutableStateOf(true)
    /** Ölçü: ilk ve ikinci nokta */
    var dimP1 by mutableStateOf<Pair<Double, Double>?>(null); private set
    var dimP2 by mutableStateOf<Pair<Double, Double>?>(null); private set
    /** Döndür/ölçekle merkezi */
    var pivot by mutableStateOf<Pair<Double, Double>?>(null); private set
    /** Ayna: orijinal kalsın mı (AutoCAD varsayılanı: evet) */
    var mirrorKeep by mutableStateOf(true)
    /** Döndür/ölçekle canlı değeri (ipucu çubuğu) */
    var liveText by mutableStateOf<String?>(null); private set
    /** Yerleştirilecek sembol, boyu (çizim birimi) ve açısı */
    var symbol by mutableStateOf<CadSymbol?>(null); private set
    var symbolScale by mutableStateOf(1.0)
    var symbolRot by mutableStateOf(0.0)
    /** Tablolar çizim biriminde üretilir: ölçek sabit 1 (kütüphane ölçeği değişmez) */
    var symbolFixedScale by mutableStateOf<Double?>(null); private set
    val placeScale: Double get() = symbolFixedScale ?: symbolScale
    /** Ölç aracının canlı sonucu */
    var measureText by mutableStateOf<String?>(null); private set
    var textHeight by mutableStateOf(2.5)
    /** Yazı aracında dokunulan nokta: diyalog açık */
    var textAt by mutableStateOf<Pair<Double, Double>?>(null)
    var sourceUri: Uri? = null; private set

    private var atlas: GlyphAtlas? = null
    private var selGeom = FloatArray(0)

    // ───────────────────────── dosya ─────────────────────────

    // ───────────────────────── projeler ─────────────────────────

    /** Açık proje (ProjectStore); her açılan çizim bir projedir */
    var project by mutableStateOf<ProjectMeta?>(null); private set
    /** Son otomatik kayıttan beri yapılan düzenleme */
    private var editsSinceSave = 0
    private var metaChanged = false

    /** Dosyadan aç: proje olarak içeri al. Aynı dosyanın kaydedilmemiş projesi varsa onu aç. */
    fun open(uri: Uri) = load {
        val name = withContext(Dispatchers.IO) { displayName(uri) }
        if (!name.lowercase().endsWith(".dxf")) {
            throw CadException("\"$name\" bir DXF dosyası değil. AutoCAD'de \"Farklı Kaydet → DXF\" ile kaydedin.")
        }
        val existing = withContext(Dispatchers.IO) { ProjectStore.findBySource(context, uri.toString()) }
        if (existing != null && existing.dirtyToSource) {
            message = "Kaydedilmemiş değişikliklerinle Projelerim'den açıldı"
            val bytes = withContext(Dispatchers.IO) { ProjectStore.read(context, existing.id) }
            existing to bytes
        } else {
            val bytes = withContext(Dispatchers.IO) {
                context.contentResolver.openInputStream(uri)?.use { it.readBytes() } ?: throw CadException("Dosya okunamadı.")
            }
            val m = withContext(Dispatchers.IO) {
                if (existing != null) {
                    ProjectStore.writeDrawing(context, existing.id, bytes)
                    existing.copy(modified = System.currentTimeMillis()).also { ProjectStore.saveMeta(context, it) }
                } else ProjectStore.create(context, name.removeSuffix(".dxf").removeSuffix(".DXF"), bytes, uri.toString())
            }
            m to bytes
        }
    }

    /** Projelerim'den aç: kaldığı görünüm ve katmanlarla */
    fun openProject(id: String) = load {
        withContext(Dispatchers.IO) { ProjectStore.meta(context, id) to ProjectStore.read(context, id) }
    }

    /** Boş yeni proje (core/content/blank.dxf: R2018, cm, temel katmanlar) */
    fun newBlank() = load {
        withContext(Dispatchers.IO) {
            val bytes = context.assets.open("blank.dxf").use { it.readBytes() }
            val n = ProjectStore.list(context).count { it.name.startsWith("Yeni proje") } + 1
            ProjectStore.create(context, "Yeni proje $n", bytes, null) to bytes
        }
    }

    /** Örnek proje (core/content/demo.dxf: 2+1 daire, semboller, tablolar, antet); varsa mevcut olanı açar */
    fun openDemo() = load {
        withContext(Dispatchers.IO) {
            val m = ProjectStore.list(context).firstOrNull { it.name == "Örnek proje" }
            if (m != null) m to ProjectStore.read(context, m.id)
            else {
                val bytes = context.assets.open("demo.dxf").use { it.readBytes() }
                ProjectStore.create(context, "Örnek proje", bytes, null) to bytes
            }
        }
    }

    /** Ortak açma: önce açık çizimi kaydet, sonra yükle */
    private fun load(source: suspend () -> Pair<ProjectMeta, ByteArray>) {
        if (loading) return
        loading = true
        error = null
        autosave(final = true)
        doc = null
        scope.launch {
            try {
                ProjectStore.awaitSaves() // önceki projenin son kaydı bitsin
                val (m, bytes) = source()
                val (d, a, mesh) = withContext(Dispatchers.Default) {
                    val d = CadDocument.open(m.name + ".dxf", bytes)
                    if (m.dirtyToSource) d.setDirty(true)
                    val a = GlyphAtlas.build(d.scene.texts)
                    Triple(d, a, a?.let { TextMesh.build(d.scene.texts, it, CadRenderer::adapt) })
                }
                doc = d
                atlas = a
                project = m
                sourceUri = m.sourceUri?.let { Uri.parse(it) }
                editsSinceSave = 0
                metaChanged = false
                ProjectStore.setLast(context, m.id)
                val s = d.scene
                scene = s
                hidden = if (m.view != null) m.hidden.filter { it < s.layers.size }.toSet()
                else s.layers.indices.filter { s.layers[it].offInFile }.toSet()
                activeLayer = if (s.layers.any { it.name == "0" }) "0" else s.layers.firstOrNull()?.name ?: "0"
                textHeight = s.texts.map { it.height.toDouble() }.filter { it > 0 }.sorted().let { if (it.isEmpty()) 10.0 else it[it.size / 2] }
                // semboller örnek projenin biriminde: yazı yüksekliği oranıyla bu çizime uydur
                symbolScale = (textHeight / SymbolLibrary.refTextHeight).takeIf { it.isFinite() && it > 0 } ?: 1.0
                symbolRot = 0.0
                selection = emptySet()
                selGeom = FloatArray(0)
                selectionInfo = null
                lineStart = null
                tool = CadTool.SELECT
                refreshCounts()
                glView?.renderer?.hidden = hidden
                glView?.setOverlay(Overlay.EMPTY)
                // kayıtlı görünüm mutlak koordinatta; bu açılışın kökenine göre çevir
                val initial = m.view?.let { v -> ViewState(v[0] - d.originX, v[1] - d.originY, v[2]) }
                glView?.setScene(s, mesh, initial)
            } catch (e: CadException) {
                error = e.message
            } catch (e: OutOfMemoryError) {
                error = "Çizim bu cihaz için çok büyük."
            } catch (e: Exception) {
                error = "Dosya açılamadı: ${e.message}"
            } finally {
                loading = false
            }
        }
    }

    /** Görünüm değişti (kaydırma/yakınlaştırma): bir sonraki otomatik kayıtta saklanır */
    fun viewChanged() {
        metaChanged = true
    }

    /**
     * Otomatik kayıt: çalışma kopyası + görünüm + katmanlar + önizleme. Yazma uygulama
     * kapsamında yürür, ekran kapansa da biter. [final]: ardından belge kapatılır.
     */
    fun autosave(final: Boolean = false) {
        val d = doc ?: return
        val m = project
        if (m == null) {
            if (final) AppScope.launch { d.close() }
            return
        }
        val edits = editsSinceSave
        val changed = metaChanged
        editsSinceSave = 0
        metaChanged = false
        val s = scene
        val hid = hidden
        val v = glView?.renderer?.view
        val view = v?.let { doubleArrayOf(it.cx + d.originX, it.cy + d.originY, it.zoom) } ?: m.view
        val ctx = context
        if (edits == 0 && !changed && !final) return
        val job = AppScope.launch {
            try {
                if (edits > 0) ProjectStore.writeDrawing(ctx, m.id, d.saveCopy())
                val nm = m.copy(
                    modified = if (edits > 0) System.currentTimeMillis() else m.modified,
                    dirtyToSource = d.dirty, view = view, hidden = hid,
                )
                ProjectStore.saveMeta(ctx, nm)
                if (s != null && (edits > 0 || !ProjectStore.thumb(ctx, m.id).exists())) ProjectStore.writeThumb(ctx, m.id, s, hid)
            } catch (_: Exception) {
            } finally {
                if (final) d.close()
            }
        }
        ProjectStore.track(m.id, job)
    }

    /** Kaynağın üzerine yazar; olmazsa false döner (çağıran "Farklı kaydet"e geçer). */
    suspend fun save(): Boolean {
        val uri = sourceUri ?: return false
        return saveTo(uri)
    }

    suspend fun saveTo(uri: Uri): Boolean {
        val d = doc ?: return false
        return try {
            withContext(Dispatchers.IO) {
                val bytes = d.save()
                val out = runCatching { context.contentResolver.openOutputStream(uri, "wt") }.getOrNull()
                    ?: context.contentResolver.openOutputStream(uri, "w") ?: throw CadException("Yazılamadı")
                out.use { it.write(bytes) }
            }
            sourceUri = uri
            refreshCounts()
            project?.let { m ->
                val nm = m.copy(sourceUri = uri.toString(), dirtyToSource = false)
                project = nm
                withContext(Dispatchers.IO) { ProjectStore.saveMeta(context, nm) }
            }
            editsSinceSave++ // çalışma kopyası da güncellensin
            autosave()
            message = "Kaydedildi"
            true
        } catch (e: Exception) {
            false
        }
    }

    fun suggestedName(): String = (project?.name ?: doc?.name?.removeSuffix(".dxf") ?: "cizim") + ".dxf"

    // ───────────────────────── katmanlar ─────────────────────────

    fun changeHidden(h: Set<Int>) {
        hidden = h
        metaChanged = true
        glView?.setHidden(h)
    }

    // ───────────────────────── araçlar ─────────────────────────

    fun selectTool(t: CadTool) {
        if (t.needsSelection && selection.isEmpty()) {
            message = "Önce ${t.label.lowercase()} için nesne seçin."
            tool = CadTool.SELECT
            return
        }
        glView?.hideLoupe()
        tool = t
        lineStart = null
        plinePts = emptyList()
        offsetTarget = null
        measureText = null
        pivot = null
        liveText = null
        dimP1 = null
        dimP2 = null
        acq.clear()
        acqSeg = null
        lastAcq = null
        guides = null
        trackLabel = null
        if (t == CadTool.OFFSET && selection.isNotEmpty()) {
            selection = emptySet()
            selGeom = FloatArray(0)
            selectionInfo = null
        }
        overlay()
    }

    fun clearSelection() {
        selection = emptySet()
        selGeom = FloatArray(0)
        selectionInfo = null
        if (tool.needsSelection) tool = CadTool.SELECT
        pivot = null
        overlay()
    }

    fun finishLine() {
        if (tool == CadTool.PLINE) return finishPolyline(false)
        lineStart = null
        liveText = null
        overlay()
    }

    fun deleteSelection() {
        val ids = selection
        if (ids.isEmpty()) return
        selection = emptySet()
        selGeom = FloatArray(0)
        selectionInfo = null
        edit { it.delete(ids) }
        if (tool.needsSelection) tool = CadTool.SELECT
        pivot = null
    }

    /** Döndür: merkez seçiliyken açıyla uygula (derece, saat yönünün tersi +). */
    fun rotateBy(deg: Double) {
        val c = pivot ?: return
        val ids = selection
        if (ids.isEmpty() || deg == 0.0) return
        pivot = null
        liveText = null
        edit { it.rotate(ids, c.first, c.second, deg) }
    }

    fun scaleBy(k: Double) {
        val c = pivot ?: return
        val ids = selection
        if (ids.isEmpty() || !(k > 0) || k == 1.0) return
        pivot = null
        liveText = null
        edit { it.scale(ids, c.first, c.second, k) }
    }

    fun cancelPivot() {
        pivot = null
        liveText = null
        overlay()
    }

    /** Tek bir yazı seçiliyse içeriği, değilse null. */
    fun selectedText(): Pair<Int, String>? {
        val id = selection.singleOrNull() ?: return null
        val t = doc?.text(id) ?: return null
        return if (t.isEmpty() && doc?.info(id)?.startsWith("TEXT") != true && doc?.info(id)?.startsWith("MTEXT") != true) null else id to t
    }

    fun setText(id: Int, text: String) = edit { it.setText(id, text) }

    fun applyLayer(layer: String) {
        val ids = selection
        if (ids.isNotEmpty()) edit { it.setLayer(ids, layer) }
    }

    fun applyColor(aci: Int) {
        val ids = selection
        if (ids.isNotEmpty()) edit { it.setColor(ids, aci) }
    }

    fun startSymbol(s: CadSymbol) {
        selectTool(CadTool.SYMBOL)
        symbol = s
        symbolFixedScale = null
    }

    /** Formdan üretilen tablo: yerleştirmek için dokunulacak (sol üst köşe) */
    fun startTable(s: CadSymbol) {
        selectTool(CadTool.SYMBOL)
        symbol = s
        symbolRot = 0.0
        symbolFixedScale = 1.0
    }

    /** Keşif: (görünen ad, adet, kütüphane sembolü mü) — kütüphane sembolleri önce */
    fun counts(): List<Triple<String, Int, Boolean>> {
        val d = doc ?: return emptyList()
        return d.insertCounts().map { (name, n) ->
            val sym = SymbolLibrary.forBlock(name)
            Triple(sym?.name ?: name, n, sym != null)
        }.sortedWith(compareBy({ !it.third }, { it.first }))
    }

    /** Dışa aktarım baytları (arka planda çağırın). */
    fun exportBytes(o: ExportOptions): ByteArray? {
        val s = scene ?: return null
        val window = if (o.viewOnly) glView?.viewWindow() ?: Exporter.extentsWindow(s) else Exporter.extentsWindow(s)
        return Exporter.render(s, hidden, window, o, s.name)
    }

    /** Paylaşmak için güncel DXF (diske kaydetmez). */
    fun dxfBytes(): ByteArray? = doc?.save()

    fun baseName(): String = suggestedName().substringBeforeLast('.')

    fun undo() = edit(reselect = true) { it.undo() }
    fun redo() = edit(reselect = true) { it.redo() }

    fun addText(text: String, height: Double) {
        val p = textAt ?: return
        textAt = null
        if (text.isBlank() || height <= 0) return
        textHeight = height
        val layer = activeLayer
        edit { it.addText(layer, p.first, p.second, height, 0.0, text) }
    }

    /** Çekirdekte düzenle → sahneyi yeniden kur → GPU'ya yükle. Görünüm korunur. */
    private fun edit(reselect: Boolean = false, onDone: (() -> Unit)? = null, op: (CadDocument) -> Unit) {
        val d = doc ?: return
        busy = true
        scope.launch {
            try {
                val sel = selection
                val (s, a, mesh, geom) = withContext(Dispatchers.Default) {
                    op(d)
                    val s = d.rebuild()
                    val a = atlas?.takeIf { it.covers(s.texts) } ?: GlyphAtlas.build(s.texts)
                    val mesh = a?.let { TextMesh.build(s.texts, it, CadRenderer::adapt) }
                    Quad(s, a, mesh, geometryOf(d, sel))
                }
                atlas = a
                scene = s
                selGeom = geom
                if (reselect && geom.isEmpty()) selection = emptySet()
                refreshCounts()
                editsSinceSave++
                glView?.updateScene(s, mesh)
                overlay()
                onDone?.invoke()
            } catch (e: Exception) {
                message = "İşlem yapılamadı: ${e.message}"
            } finally {
                busy = false
            }
        }
    }

    private data class Quad(val s: CadScene, val a: GlyphAtlas?, val mesh: TextMesh?, val geom: FloatArray)

    private fun geometryOf(d: CadDocument, ids: Set<Int>): FloatArray {
        if (ids.isEmpty()) return FloatArray(0)
        val parts = ids.map { d.geometry(it) }
        val out = FloatArray(parts.sumOf { it.size })
        var k = 0
        for (p in parts) {
            p.copyInto(out, k)
            k += p.size
        }
        return out
    }

    private fun refreshCounts() {
        val d = doc ?: return
        undoCount = d.undoCount
        redoCount = d.redoCount
        dirty = d.dirty
    }

    private fun overlay(
        ghost: DoubleArray? = null,
        rubber: FloatArray? = null,
        snap: Triple<Int, Double, Double>? = null,
        cursor: Pair<Double, Double>? = null,
        box: FloatArray? = null,
        boxCrossing: Boolean = false,
    ) {
        val live = cursor != null && tool != CadTool.SELECT
        val g = if (live) guides else null
        val mk = if (live && acq.isNotEmpty()) FloatArray(acq.size * 2) { i -> (if (i % 2 == 0) acq[i / 2].first else acq[i / 2].second).toFloat() } else null
        val o = if (selGeom.isEmpty() && ghost == null && rubber == null && snap == null && cursor == null && box == null) {
            Overlay.EMPTY
        } else {
            Overlay(
                selection = selGeom,
                ghost = ghost,
                rubber = rubber,
                snapType = snap?.first ?: 0,
                snapX = snap?.second?.toFloat() ?: 0f,
                snapY = snap?.third?.toFloat() ?: 0f,
                cursor = cursor != null,
                cursorX = cursor?.first?.toFloat() ?: 0f,
                cursorY = cursor?.second?.toFloat() ?: 0f,
                box = box,
                boxCrossing = boxCrossing,
                guides = g,
                marks = mk,
            )
        }
        glView?.setOverlay(o)
    }

    // ───────────────────────── dokunma ─────────────────────────

    private fun snapPoint(x: Double, y: Double, tol: Double): Triple<Int, Double, Double> =
        (if (snapOn) doc?.snap(x, y, tol)?.takeIf { it.first > 0 } else null) ?: Triple(0, x, y)

    /** Açı yakalama adımı ve toleransı (derece) */
    private val ANGLE_STEP = 15.0
    private val ANGLE_TOL = 3.0


    // ───────────────────────── hizalama izleri ─────────────────────────

    private val acq = ArrayList<Pair<Double, Double>>()
    private var acqSeg: Pair<Pair<Double, Double>, Pair<Double, Double>>? = null
    private var lastAcq: Pair<Double, Double>? = null
    private var guides: FloatArray? = null
    private var trackLabel: String? = null

    private class Guide(val ox: Double, val oy: Double, val dx: Double, val dy: Double, val label: String, val polar: Boolean)

    /** Parmağın geçtiği yakalama noktasını izlemeye al; uç/orta ise o çizgi parçasını referans yap */
    private fun acquire(s: Triple<Int, Double, Double>, tol: Double) {
        if (!tracking || s.first <= 0) return
        val d = doc ?: return
        val q = s.second to s.third
        lastAcq?.let { if (abs(it.first - q.first) + abs(it.second - q.second) < 1e-9) return }
        lastAcq = q
        acq.removeAll { abs(it.first - q.first) + abs(it.second - q.second) < 1e-9 }
        acq.add(q)
        if (acq.size > 5) acq.removeAt(0)
        if (s.first == 1 || s.first == 2) {
            val id = d.pick(q.first, q.second, tol)
            if (id < 0) return
            val g = d.geometry(id)
            var best = Double.MAX_VALUE
            var i = 0
            while (i + 3 < g.size) {
                val ax = g[i].toDouble(); val ay = g[i + 1].toDouble(); val bx = g[i + 2].toDouble(); val by = g[i + 3].toDouble()
                val vx = bx - ax; val vy = by - ay; val l2 = vx * vx + vy * vy
                if (l2 > 1e-18) {
                    val t = (((q.first - ax) * vx + (q.second - ay) * vy) / l2).coerceIn(0.0, 1.0)
                    val ex = ax + vx * t - q.first; val ey = ay + vy * t - q.second
                    val dd = ex * ex + ey * ey
                    if (dd < best) { best = dd; acqSeg = (ax to ay) to (bx to by) }
                }
                i += 4
            }
        }
    }

    /** Orto, 15° kutupsal ve hizalama izleri (yatay/dikey/paralel/dik kılavuzlar, eşit uzunluk) */
    private fun track(p: Triple<Int, Double, Double>, from: Pair<Double, Double>?, tol: Double, polarOk: Boolean): Triple<Int, Double, Double> {
        guides = null
        trackLabel = null
        if (p.first > 0) return p
        if (ortho && from != null) {
            val dx = p.second - from.first
            val dy = p.third - from.second
            return if (abs(dx) >= abs(dy)) Triple(0, p.second, from.second) else Triple(0, from.first, p.third)
        }
        val lines = ArrayList<Guide>()
        var sdx = 0.0; var sdy = 0.0; var slen = 0.0
        acqSeg?.let { (a, b) ->
            sdx = b.first - a.first; sdy = b.second - a.second
            slen = kotlin.math.hypot(sdx, sdy)
            if (slen > 1e-12) { sdx /= slen; sdy /= slen } else slen = 0.0
        }
        if (from != null && polarOk) {
            val dx = p.second - from.first
            val dy = p.third - from.second
            if (angleSnap && dx * dx + dy * dy > 1e-18) {
                val ang = Math.toDegrees(kotlin.math.atan2(dy, dx))
                val ta = Math.round(ang / ANGLE_STEP) * ANGLE_STEP
                if (abs(ang - ta) <= ANGLE_TOL) {
                    val r = Math.toRadians(ta)
                    lines.add(Guide(from.first, from.second, kotlin.math.cos(r), kotlin.math.sin(r), "Kutupsal ${(((ta % 360) + 360) % 360).toInt()}°", true))
                }
            }
            if (tracking && slen > 0) {
                lines.add(Guide(from.first, from.second, sdx, sdy, "Paralel", false))
                lines.add(Guide(from.first, from.second, -sdy, sdx, "Dik", false))
            }
        }
        if (tracking) for (q in acq) {
            if (from != null && abs(from.first - q.first) + abs(from.second - q.second) < 1e-9) continue
            lines.add(Guide(q.first, q.second, 1.0, 0.0, "Yatay hiza", false))
            lines.add(Guide(q.first, q.second, 0.0, 1.0, "Dikey hiza", false))
            if (slen > 0 && abs(sdx) > 1e-6 && abs(sdy) > 1e-6) {
                lines.add(Guide(q.first, q.second, sdx, sdy, "Paralel hiza", false))
                lines.add(Guide(q.first, q.second, -sdy, sdx, "Dik hiza", false))
            }
        }
        if (lines.isEmpty()) return p
        fun dist(g: Guide) = abs((p.second - g.ox) * g.dy - (p.third - g.oy) * g.dx)
        val cands = lines.map { it to dist(it) }.filter { it.second < tol || it.first.polar }
            .sortedBy { if (it.first.polar) it.second * 0.5 else it.second }
        if (cands.isEmpty()) return p
        val g1 = cands[0].first
        for ((g2, d2) in cands.drop(1)) {
            val cr = g1.dx * g2.dy - g1.dy * g2.dx
            if (abs(cr) < 0.05) continue
            if (d2 > tol * 1.5 && !g2.polar) continue
            val t1 = ((g2.ox - g1.ox) * g2.dy - (g2.oy - g1.oy) * g2.dx) / cr
            val ix = g1.ox + g1.dx * t1
            val iy = g1.oy + g1.dy * t1
            if (abs(ix - p.second) + abs(iy - p.third) > tol * 3) continue
            setGuides(listOf(g1, g2), ix, iy, tol)
            trackLabel = "${g1.label} + ${g2.label}"
            return Triple(0, ix, iy)
        }
        var t = (p.second - g1.ox) * g1.dx + (p.third - g1.oy) * g1.dy
        if (from != null && g1.label == "Paralel" && slen > 0 && abs(abs(t) - slen) < tol) {
            t = kotlin.math.sign(t) * slen
            trackLabel = "Paralel · = ${fmt(slen)} (referansla eşit)"
        } else trackLabel = g1.label
        val x = g1.ox + g1.dx * t
        val y = g1.oy + g1.dy * t
        setGuides(listOf(g1), x, y, tol)
        return Triple(0, x, y)
    }

    private fun setGuides(gs: List<Guide>, x: Double, y: Double, tol: Double) {
        val ext = tol * 2
        val out = FloatArray(gs.size * 4)
        gs.forEachIndexed { i, g ->
            val t = (x - g.ox) * g.dx + (y - g.oy) * g.dy
            val sg = if (t >= 0) 1 else -1
            out[i * 4] = g.ox.toFloat(); out[i * 4 + 1] = g.oy.toFloat()
            out[i * 4 + 2] = (x + g.dx * ext * sg).toFloat(); out[i * 4 + 3] = (y + g.dy * ext * sg).toFloat()
        }
        guides = out
    }

    // ───────────────────────── ölçü ─────────────────────────

    private class DimGeom(val snippet: String, val preview: FloatArray, val text: String)

    /**
     * Hizalı ölçü: p1–p2 arası, ölçü çizgisi q'dan geçer. Uzatma çizgileri, eğik tikler, ortalanmış yazı;
     * tek blok olarak eklenir. Blok koordinatları p1'e göredir. (Windows CadEditor.BuildDimension ile aynı)
     */
    private fun buildDimension(p1: Pair<Double, Double>, p2: Pair<Double, Double>, q: Pair<Double, Double>): DimGeom {
        val h = if (textHeight > 0) textHeight else 2.5
        var ux = p2.first - p1.first
        var uy = p2.second - p1.second
        val len = kotlin.math.hypot(ux, uy)
        if (len < 1e-12) { ux = 1.0; uy = 0.0 } else { ux /= len; uy /= len }
        val nx = -uy
        val ny = ux
        val off = (q.first - p1.first) * nx + (q.second - p1.second) * ny
        val sg = if (off >= 0) 1.0 else -1.0
        val gap = h * 0.4; val ext = h * 0.6; val tick = h * 0.5
        val d1 = (p1.first + nx * off) to (p1.second + ny * off)
        val d2 = (p2.first + nx * off) to (p2.second + ny * off)
        val segs = ArrayList<DoubleArray>()
        if (abs(off) > gap) {
            segs.add(doubleArrayOf(p1.first + nx * gap * sg, p1.second + ny * gap * sg, d1.first + nx * ext * sg, d1.second + ny * ext * sg))
            segs.add(doubleArrayOf(p2.first + nx * gap * sg, p2.second + ny * gap * sg, d2.first + nx * ext * sg, d2.second + ny * ext * sg))
        }
        segs.add(doubleArrayOf(d1.first - ux * tick * 0.6, d1.second - uy * tick * 0.6, d2.first + ux * tick * 0.6, d2.second + uy * tick * 0.6))
        val tx = (ux + nx) / Math.sqrt(2.0) * tick
        val ty = (uy + ny) / Math.sqrt(2.0) * tick
        segs.add(doubleArrayOf(d1.first - tx, d1.second - ty, d1.first + tx, d1.second + ty))
        segs.add(doubleArrayOf(d2.first - tx, d2.second - ty, d2.first + tx, d2.second + ty))
        var ang = Math.toDegrees(kotlin.math.atan2(uy, ux))
        if (ang > 90.0001 || ang <= -90) ang += if (ang > 0) -180 else 180
        val ar = Math.toRadians(ang)
        val mx = (d1.first + d2.first) / 2 - kotlin.math.sin(ar) * h * 0.35
        val my = (d1.second + d2.second) / 2 + kotlin.math.cos(ar) * h * 0.35
        val text = fmt(len)
        fun n(v: Double) = java.math.BigDecimal(v).setScale(8, java.math.RoundingMode.HALF_UP).stripTrailingZeros().toPlainString()
        val sb = StringBuilder()
        for (sgm in segs) {
            sb.append("0\nLINE\n100\nAcDbEntity\n8\n0\n62\n0\n100\nAcDbLine\n10\n${n(sgm[0] - p1.first)}\n20\n${n(sgm[1] - p1.second)}\n30\n0.0\n11\n${n(sgm[2] - p1.first)}\n21\n${n(sgm[3] - p1.second)}\n31\n0.0\n")
        }
        val lx = mx - p1.first
        val ly = my - p1.second
        sb.append("0\nTEXT\n100\nAcDbEntity\n8\n0\n62\n0\n100\nAcDbText\n10\n${n(lx)}\n20\n${n(ly)}\n30\n0.0\n40\n${n(h)}\n1\n$text\n50\n${n(ang)}\n72\n1\n11\n${n(lx)}\n21\n${n(ly)}\n31\n0.0\n100\nAcDbText\n73\n0\n")
        val prev = ArrayList<Float>()
        for (sgm in segs) for (v in sgm) prev.add(v.toFloat())
        val tw = text.length * h * 0.3
        prev.addAll(listOf((mx - ux * tw).toFloat(), (my - uy * tw).toFloat(), (mx + ux * tw).toFloat(), (my + uy * tw).toFloat()))
        return DimGeom(sb.toString(), prev.toFloatArray(), text)
    }

    /** Seçimi grupla (tek nesne olur) */
    fun groupSelection() {
        val ids = selection
        if (ids.size < 2) {
            message = "Gruplamak için en az iki nesne seçin."
            return
        }
        var newId = -1
        var err = ""
        edit(onDone = {
            if (newId >= 0) {
                selection = setOf(newId)
                selGeom = doc?.let { geometryOf(it, selection) } ?: FloatArray(0)
                selectionInfo = "1 grup (${ids.size} nesne)"
                overlay()
            } else message = err.ifBlank { "Gruplanamadı." }
        }) {
            newId = it.group(ids)
            if (newId < 0) err = it.lastError()
        }
    }

    /** Seçili grup/sembol/blokları parçalarına ayır */
    fun explodeSelection() {
        val ids = selection
        if (ids.isEmpty()) return
        var parts: List<Int> = emptyList()
        edit(onDone = {
            if (parts.isEmpty()) {
                message = "Seçimde çözülecek grup ya da blok yok."
            } else {
                selection = parts.toSet()
                selGeom = doc?.let { geometryOf(it, selection) } ?: FloatArray(0)
                selectionInfo = "${parts.size} parça"
                overlay()
            }
        }) { parts = it.explode(ids) }
    }

    /** Orto açıksa [p]'yi [from] noktasına göre yatay ya da dikey hizala (yakalama yoksa). */
    private fun orthoFrom(from: Pair<Double, Double>?, p: Triple<Int, Double, Double>): Triple<Int, Double, Double> {
        if (!ortho || from == null || p.first > 0) return p
        val dx = p.second - from.first
        val dy = p.third - from.second
        return if (abs(dx) >= abs(dy)) Triple(0, p.second, from.second) else Triple(0, from.first, p.third)
    }

    private var measureStart: Pair<Double, Double>? = null

    private fun translateM(dx: Double, dy: Double) = doubleArrayOf(1.0, 0.0, 0.0, 1.0, dx, dy)

    /** [c] merkezli, [from] → [to] sürüklemesine karşılık gelen açı (orto: 15° adım). */
    private fun rotationAngle(c: Pair<Double, Double>, from: Triple<Int, Double, Double>, to: Triple<Int, Double, Double>): Double {
        val a0 = kotlin.math.atan2(from.third - c.second, from.second - c.first)
        val a1 = kotlin.math.atan2(to.third - c.second, to.second - c.first)
        var d = Math.toDegrees(a1 - a0)
        while (d > 180) d -= 360
        while (d <= -180) d += 360
        if (ortho) d = Math.round(d / 15.0) * 15.0
        else if (angleSnap) {
            val t = Math.round(d / ANGLE_STEP) * ANGLE_STEP
            if (abs(d - t) <= ANGLE_TOL) d = t.toDouble()
        }
        return d
    }

    private fun scaleFactor(c: Pair<Double, Double>, from: Triple<Int, Double, Double>, to: Triple<Int, Double, Double>, tol: Double): Double {
        val r0 = kotlin.math.hypot(from.second - c.first, from.third - c.second).coerceAtLeast(tol)
        val r1 = kotlin.math.hypot(to.second - c.first, to.third - c.second)
        var k = (r1 / r0).coerceAtLeast(1e-3)
        if (ortho) k = (Math.round(k * 4) / 4.0).coerceAtLeast(0.25) // ortoda 0.25 adım
        return k
    }

    private fun pivotMatrix(c: Pair<Double, Double>, from: Triple<Int, Double, Double>, to: Triple<Int, Double, Double>, tol: Double): DoubleArray {
        val (cx, cy) = c
        return if (tool == CadTool.ROTATE) {
            val deg = rotationAngle(c, from, to)
            liveText = "Açı ${"%.1f".format(deg)}°"
            val r = Math.toRadians(deg)
            val cs = kotlin.math.cos(r)
            val sn = kotlin.math.sin(r)
            doubleArrayOf(cs, sn, -sn, cs, cx - (cs * cx - sn * cy), cy - (sn * cx + cs * cy))
        } else {
            val k = scaleFactor(c, from, to, tol)
            liveText = "Ölçek ×${fmt(k)}"
            doubleArrayOf(k, 0.0, 0.0, k, cx - k * cx, cy - k * cy)
        }
    }

    private fun mirrorM(x1: Double, y1: Double, x2: Double, y2: Double): DoubleArray {
        val phi = kotlin.math.atan2(y2 - y1, x2 - x1)
        val a = kotlin.math.cos(2 * phi)
        val b = kotlin.math.sin(2 * phi)
        return doubleArrayOf(a, b, b, -a, x1 - (a * x1 + b * y1), y1 - (b * x1 - a * y1))
    }

    private fun measure(a: Pair<Double, Double>, b: Triple<Int, Double, Double>) {
        val dx = b.second - a.first
        val dy = b.third - a.second
        val len = kotlin.math.hypot(dx, dy)
        var ang = Math.toDegrees(kotlin.math.atan2(dy, dx))
        if (ang < 0) ang += 360.0
        measureText = "Uzunluk ${fmt(len)} · Açı ${"%.1f".format(ang)}° · ΔX ${fmt(dx)} · ΔY ${fmt(dy)}"
    }

    private fun fmt(v: Double): String = "%.3f".format(v).trimEnd('0').trimEnd('.', ',')

    private var dragBase: Triple<Int, Double, Double>? = null
    /** Bu basış ilk noktayı (çapa) koydu mu: dokunup bırakınca ikinci noktayı bekleriz */
    private var pressSetAnchor = false

    private val drawTools = setOf(CadTool.LINE, CadTool.PLINE, CadTool.CIRCLE, CadTool.RECT)

    private fun seg(a: Pair<Double, Double>, b: Pair<Double, Double>) =
        floatArrayOf(a.first.toFloat(), a.second.toFloat(), b.first.toFloat(), b.second.toFloat())

    /** Çizim aracının önizlemesi (çapa [a] → nokta [b]) ve canlı ölçü yazısı. */
    private fun drawPreview(a: Pair<Double, Double>, b: Pair<Double, Double>): FloatArray {
        val dx = b.first - a.first
        val dy = b.second - a.second
        return when (tool) {
            CadTool.CIRCLE -> {
                val r = kotlin.math.hypot(dx, dy)
                liveText = "Yarıçap ${fmt(r)} · Çap ${fmt(2 * r)}"
                val n = 72
                val out = FloatArray(n * 4 + 4)
                for (i in 0 until n) {
                    val a0 = 2 * Math.PI * i / n
                    val a1 = 2 * Math.PI * (i + 1) / n
                    out[i * 4] = (a.first + r * kotlin.math.cos(a0)).toFloat()
                    out[i * 4 + 1] = (a.second + r * kotlin.math.sin(a0)).toFloat()
                    out[i * 4 + 2] = (a.first + r * kotlin.math.cos(a1)).toFloat()
                    out[i * 4 + 3] = (a.second + r * kotlin.math.sin(a1)).toFloat()
                }
                // yarıçap çizgisi
                seg(a, b).copyInto(out, n * 4)
                out
            }
            CadTool.RECT -> {
                liveText = "${fmt(abs(dx))} × ${fmt(abs(dy))}"
                val c1 = b.first to a.second
                val c3 = a.first to b.second
                seg(a, c1) + seg(c1, b) + seg(b, c3) + seg(c3, a)
            }
            else -> {
                var ang = Math.toDegrees(kotlin.math.atan2(dy, dx))
                if (ang < 0) ang += 360.0
                liveText = "Uzunluk ${fmt(kotlin.math.hypot(dx, dy))} · Açı ${"%.1f".format(ang)}°"
                val prev = if (tool == CadTool.PLINE) plinePts.zipWithNext { p, q -> seg(p, q) }.fold(FloatArray(0)) { x, y -> x + y } else FloatArray(0)
                prev + seg(a, b)
            }
        }
    }

    /** Çizim aracında iki nokta belli: nesneyi ekle. */
    /** Konan nokta çizim bitene kadar hizalama referansı olur */
    private fun addRef(p: Pair<Double, Double>) {
        if (!tracking) return
        acq.removeAll { abs(it.first - p.first) + abs(it.second - p.second) < 1e-9 }
        acq.add(p)
        if (acq.size > 6) acq.removeAt(0)
    }

    private fun commitDraw(a: Pair<Double, Double>, b: Pair<Double, Double>) {
        addRef(a)
        addRef(b)
        val layer = activeLayer
        liveText = null
        when (tool) {
            CadTool.LINE -> {
                lineStart = b
                edit { it.addLine(layer, a.first, a.second, b.first, b.second) }
            }
            CadTool.PLINE -> {
                if (plinePts.isEmpty()) plinePts = listOf(a)
                plinePts = plinePts + b
                lineStart = b
                overlay(rubber = plinePts.zipWithNext { p, q -> seg(p, q) }.fold(FloatArray(0)) { x, y -> x + y }, cursor = b)
            }
            CadTool.CIRCLE -> {
                lineStart = null
                val r = kotlin.math.hypot(b.first - a.first, b.second - a.second)
                edit { it.addCircle(layer, a.first, a.second, r) }
            }
            CadTool.RECT -> {
                lineStart = null
                val pts = listOf(a, b.first to a.second, b, a.first to b.second)
                edit { it.addPolyline(layer, pts, true) }
            }
            else -> {}
        }
    }

    /** Sayısal giriş: çizgi/çoklu çizgi (uzunluk, açı), daire (yarıçap), dikdörtgen (genişlik, yükseklik). */
    fun numericDraw(v1: Double, v2: Double) {
        val a = lineStart ?: return
        when (tool) {
            CadTool.LINE, CadTool.PLINE -> {
                if (v1 <= 0) return
                val r = Math.toRadians(v2)
                commitDraw(a, (a.first + v1 * kotlin.math.cos(r)) to (a.second + v1 * kotlin.math.sin(r)))
                lastAngle = v2
            }
            CadTool.CIRCLE -> if (v1 > 0) commitDraw(a, (a.first + v1) to a.second)
            CadTool.RECT -> if (v1 != 0.0 && v2 != 0.0) commitDraw(a, (a.first + v1) to (a.second + v2))
            else -> {}
        }
    }

    var lastAngle = 0.0; private set

    /** Çoklu çizgiyi bitir: [closed] ise son nokta ilkine bağlanır. */
    fun finishPolyline(closed: Boolean) {
        val pts = plinePts
        plinePts = emptyList()
        lineStart = null
        liveText = null
        if (pts.size < 2 || (closed && pts.size < 3)) {
            overlay()
            return
        }
        val layer = activeLayer
        edit { it.addPolyline(layer, pts, closed) }
    }

    /** Ötele: hedef seçiliyken tarafa dokunulur. */
    var offsetTarget by mutableStateOf<Int?>(null); private set
    var offsetDist by mutableStateOf(1.0)

    private val input = object : EditInput {
        override val capturesDrag: Boolean
            get() = tool != CadTool.SELECT && tool != CadTool.TEXT && tool != CadTool.OFFSET

        override fun onBox(x0: Double, y0: Double, x1: Double, y1: Double, done: Boolean) {
            val d = doc ?: return
            if (busy) return
            if (tool != CadTool.SELECT) selectTool(CadTool.SELECT)
            val crossing = x1 < x0 // sağdan sola: değenler
            if (!done) {
                overlay(box = floatArrayOf(x0.toFloat(), y0.toFloat(), x1.toFloat(), y1.toFloat()), boxCrossing = crossing)
                return
            }
            val ids = d.selectRect(x0, y0, x1, y1, crossing)
            selection = selection + ids.toSet()
            selGeom = geometryOf(d, selection)
            selectionInfo = if (selection.isEmpty()) null else "${selection.size} nesne seçili"
            overlay()
        }

        override fun onTap(x: Double, y: Double, tol: Double) {
            val d = doc ?: return
            if (busy) return
            when (tool) {
                CadTool.SELECT -> {
                    val id = d.pick(x, y, tol)
                    if (id < 0) {
                        if (selection.isNotEmpty()) clearSelection()
                        return
                    }
                    selection = if (id in selection) selection - id else selection + id
                    selGeom = geometryOf(d, selection)
                    selectionInfo = if (selection.isEmpty()) null else describe(d.info(id), id in selection)
                    overlay()
                }
                CadTool.TEXT -> {
                    val p = snapPoint(x, y, tol)
                    textAt = p.second to p.third
                }
                CadTool.OFFSET -> {
                    val target = offsetTarget
                    if (target == null) {
                        val id = d.pick(x, y, tol)
                        if (id < 0) return
                        offsetTarget = id
                        selection = setOf(id)
                        selGeom = geometryOf(d, selection)
                        overlay()
                    } else {
                        val dist = offsetDist
                        offsetTarget = null
                        selection = emptySet()
                        selGeom = FloatArray(0)
                        var result = 0
                        edit(onDone = {
                            if (result < 0) message = "Bu nesne ötelenemiyor. Desteklenenler: çizgi, daire, yay ve düz parçalı çoklu çizgi."
                        }) { result = it.offset(target, dist, x, y) }
                    }
                }
                else -> {}
            }
        }

        override fun onPress(x: Double, y: Double, tol: Double) {
            if (busy) return
            val raw = snapPoint(x, y, tol)
            acquire(raw, tol)
            var p = raw
            if (tool == CadTool.DIMENSION) {
                p = constrain(raw, raw, tol)
                pressSetAnchor = dimP1 == null
                if (pressSetAnchor) dimP1 = p.second to p.third
            }
            if (tool in drawTools) {
                p = orthoFrom(lineStart, p)
                pressSetAnchor = lineStart == null
                if (pressSetAnchor) lineStart = p.second to p.third
            }
            dragBase = p
            if (tool == CadTool.MEASURE && measureStart == null) {
                measureStart = p.second to p.third
                measureText = null
            }
            show(p, tol)
        }

        override fun onDrag(x: Double, y: Double, tol: Double) {
            val b = dragBase ?: return
            if (busy) return
            val raw = snapPoint(x, y, tol)
            acquire(raw, tol)
            show(constrain(raw, b, tol), tol)
        }

        private fun constrain(p: Triple<Int, Double, Double>, b: Triple<Int, Double, Double>, tol: Double): Triple<Int, Double, Double> = when (tool) {
            in drawTools -> track(p, if (tool == CadTool.CIRCLE) null else lineStart, tol, tool != CadTool.RECT)
            CadTool.MOVE, CadTool.COPY, CadTool.MIRROR -> track(p, b.second to b.third, tol, true)
            CadTool.MEASURE -> track(p, measureStart, tol, true)
            CadTool.DIMENSION -> track(p, if (dimP2 == null) dimP1 else null, tol, true)
            CadTool.SYMBOL, CadTool.TEXT -> track(p, null, tol, false)
            else -> { guides = null; trackLabel = null; p }
        }

        private fun withTrack(t: String?): String? = trackLabel?.let { tl -> if (t == null) tl else "$tl · $t" } ?: t

        private fun show(p: Triple<Int, Double, Double>, tol: Double) {
            val snap = p.takeIf { it.first > 0 }
            val cur = p.second to p.third
            val b = dragBase ?: p
            when (tool) {
                CadTool.MOVE, CadTool.COPY -> {
                    liveText = withTrack("ΔX ${fmt(p.second - b.second)} · ΔY ${fmt(p.third - b.third)}")
                    overlay(ghost = translateM(p.second - b.second, p.third - b.third), snap = snap, cursor = cur)
                }
                CadTool.DIMENSION -> {
                    val a1 = dimP1
                    val a2 = dimP2
                    if (a1 != null && a2 != null) {
                        val dg = buildDimension(a1, a2, cur)
                        liveText = withTrack("Ölçü ${dg.text}")
                        overlay(rubber = dg.preview, snap = snap, cursor = cur)
                    } else if (a1 != null) {
                        liveText = withTrack("Ölçü ${fmt(kotlin.math.hypot(cur.first - a1.first, cur.second - a1.second))}")
                        overlay(rubber = seg(a1, cur), snap = snap, cursor = cur)
                    } else overlay(snap = snap, cursor = cur)
                }
                CadTool.SYMBOL -> {
                    val sym = symbol ?: return
                    overlay(rubber = sym.segments(p.second, p.third, placeScale, symbolRot), snap = snap, cursor = cur)
                }
                in drawTools -> {
                    val a = lineStart
                    val rb = a?.let { drawPreview(it, cur) }
                    liveText = withTrack(if (a != null) liveText else null)
                    overlay(rubber = rb, snap = snap, cursor = cur)
                }
                CadTool.ROTATE, CadTool.SCALE -> {
                    val c = pivot
                    val m = c?.let { pivotMatrix(it, b, p, tol) }
                    overlay(ghost = m, snap = snap, cursor = cur)
                }
                CadTool.MIRROR -> {
                    val axis = floatArrayOf(b.second.toFloat(), b.third.toFloat(), p.second.toFloat(), p.third.toFloat())
                    val m = if (abs(p.second - b.second) + abs(p.third - b.third) > tol * 0.05) mirrorM(b.second, b.third, p.second, p.third) else null
                    overlay(ghost = m, rubber = axis, snap = snap, cursor = cur)
                }
                CadTool.MEASURE -> {
                    val s = measureStart
                    if (s != null && (s.first != p.second || s.second != p.third)) measure(s, p)
                    liveText = trackLabel
                    val rubber = s?.let { floatArrayOf(it.first.toFloat(), it.second.toFloat(), p.second.toFloat(), p.third.toFloat()) }
                    overlay(rubber = rubber, snap = snap, cursor = cur)
                }
                else -> {}
            }
        }

        override fun onRelease(x: Double, y: Double, tol: Double) {
            val b = dragBase ?: return
            dragBase = null
            if (busy) return
            val p = constrain(snapPoint(x, y, tol), b, tol)
            val cur = p.second to p.third
            when (tool) {
                CadTool.DIMENSION -> {
                    val a1 = dimP1 ?: return
                    val a2 = dimP2
                    if (a2 == null) {
                        val moved = abs(a1.first - p.second) + abs(a1.second - p.third) > tol * 0.05
                        // sürüklendi ya da ikinci kez dokunuldu: ikinci nokta
                        if (moved || !pressSetAnchor) dimP2 = cur
                        liveText = null
                        overlay(rubber = dimP2?.let { seg(a1, it) }, cursor = cur)
                        return
                    }
                    val dg = buildDimension(a1, a2, cur)
                    addRef(a1)
                    addRef(a2)
                    dimP1 = null
                    dimP2 = null
                    liveText = null
                    val layer = activeLayer
                    edit { it.insertDimension(layer, a1.first, a1.second, dg.snippet) }
                }
                CadTool.SYMBOL -> {
                    val sym = symbol ?: return
                    val layer = activeLayer
                    val sc = placeScale
                    val rot = symbolRot
                    edit { it.insertSymbol(sym, layer, p.second, p.third, sc, rot) }
                }
                in drawTools -> {
                    val a = lineStart ?: return
                    val moved = abs(a.first - p.second) + abs(a.second - p.third) > tol * 0.05
                    if (!moved) {
                        // yalnızca dokunuldu: ilk nokta kondu (ya da çapanın üstüne dokunuldu), sonrakini bekle
                        overlay(rubber = if (tool == CadTool.PLINE && plinePts.size > 1) drawPreview(a, a) else null, cursor = a)
                        liveText = null
                        return
                    }
                    commitDraw(a, cur)
                }
                CadTool.ROTATE, CadTool.SCALE -> {
                    val c = pivot
                    if (c == null) {
                        // ilk dokunuş: merkez
                        pivot = cur
                        liveText = null
                        overlay(cursor = pivot)
                        return
                    }
                    if (abs(p.second - b.second) + abs(p.third - b.third) < tol * 0.05) {
                        overlay(cursor = c)
                        return
                    }
                    if (tool == CadTool.ROTATE) rotateBy(rotationAngle(c, b, p)) else scaleBy(scaleFactor(c, b, p, tol))
                }
                CadTool.MIRROR -> {
                    if (abs(p.second - b.second) + abs(p.third - b.third) < tol * 0.05) {
                        overlay()
                        return
                    }
                    val ids = selection
                    val keep = mirrorKeep
                    edit { it.mirror(ids, b.second, b.third, p.second, p.third, keep) }
                }
                CadTool.MEASURE -> {
                    val s = measureStart ?: return
                    if (abs(s.first - p.second) + abs(s.second - p.third) < tol * 0.05) {
                        overlay(cursor = s) // yalnızca dokunuldu: ilk nokta kondu, ikincisini bekle
                        return
                    }
                    measure(s, p)
                    measureStart = null
                    overlay(rubber = floatArrayOf(s.first.toFloat(), s.second.toFloat(), p.second.toFloat(), p.third.toFloat()))
                }
                CadTool.MOVE, CadTool.COPY -> {
                    val dx = p.second - b.second
                    val dy = p.third - b.third
                    if (abs(dx) + abs(dy) < tol * 0.05) {
                        overlay()
                        return
                    }
                    val ids = selection
                    if (tool == CadTool.MOVE) edit { it.move(ids, dx, dy) } else edit { it.copy(ids, dx, dy) }
                }
                else -> {}
            }
        }

        override fun onCancel() {
            dragBase = null
            if (pressSetAnchor && tool in drawTools && plinePts.isEmpty()) lineStart = null
            pressSetAnchor = false
            overlay()
        }
    }

    private fun describe(info: String, added: Boolean): String {
        val parts = info.split('|')
        val type = TYPE_NAMES[parts.getOrElse(0) { "" }] ?: parts.getOrElse(0) { "" }
        val head = if (selection.size > 1) "${selection.size} nesne seçili" else "1 nesne seçili"
        return if (added) "$head · $type · ${parts.getOrElse(1) { "" }}" else head
    }

    private fun displayName(uri: Uri): String {
        context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { c ->
            if (c.moveToFirst()) c.getString(0)?.let { return it }
        }
        return uri.lastPathSegment?.substringAfterLast('/') ?: "cizim.dxf"
    }

    /** Ekrandan çıkış: son kez kaydet ve kapat */
    fun dispose() {
        autosave(final = true)
        doc = null
    }

    companion object {
        private val TYPE_NAMES = mapOf(
            "LINE" to "Çizgi", "LWPOLYLINE" to "Çoklu çizgi", "POLYLINE" to "Çoklu çizgi", "CIRCLE" to "Daire", "ARC" to "Yay",
            "ELLIPSE" to "Elips", "TEXT" to "Yazı", "MTEXT" to "Çok satırlı yazı", "INSERT" to "Blok", "HATCH" to "Tarama",
            "SOLID" to "Dolgu", "SPLINE" to "Eğri", "DIMENSION" to "Ölçü", "POINT" to "Nokta",
        )
    }
}
