package com.muhenkit.app.cad

/**
 * Açık, düzenlenebilir DXF belgesi. Belge çekirdekte (C++) açık kalır; her
 * düzenleme doğrudan çekirdeğe gider, ardından [rebuild] ile sahne yenilenir.
 *
 * Koordinatlar dışarıya göreli verilir (dünya − köken). Köken belge açılırken
 * sabitlenir ki düzenlemeden sonra görünüm kaymasın.
 *
 * Tüm yöntemler eşzamanlıdır (@Synchronized): çekirdek tek iş parçacıklıdır.
 */
class CadDocument private constructor(
    private var handle: Long,
    val name: String,
    val originX: Double,
    val originY: Double,
) {
    lateinit var scene: CadScene
        private set

    /** Kapalı belgeye yerel çağrı çökme demektir: istisnaya çevir */
    private fun h(): Long {
        if (handle == 0L) throw CadException("Çizim kapatıldı.")
        return handle
    }

    val isOpen: Boolean @Synchronized get() = handle != 0L

    companion object {
        /** DXF baytlarını açar ve sahneyi kurar. Ağır iş: arka planda çağırın. */
        fun open(name: String, data: ByteArray): CadDocument {
            val t0 = System.nanoTime()
            val h = MkNative.open(data)
            if (h == 0L) throw CadException(String(MkNative.lastError(), Charsets.UTF_8).ifEmpty { "DXF okunamadı." })
            if (MkNative.buildScene(h) != 0) {
                val err = String(MkNative.lastError(), Charsets.UTF_8)
                MkNative.close(h)
                throw CadException(err)
            }
            val ext = MkNative.extents(h)
            val doc = CadDocument(h, name, (ext[0].toDouble() + ext[2]) / 2, (ext[1].toDouble() + ext[3]) / 2)
            doc.scene = CadScene.extract(name, h, doc.originX, doc.originY, t0)
            return doc
        }
    }

    @Synchronized
    fun rebuild(): CadScene {
        check(handle != 0L)
        val t0 = System.nanoTime()
        MkNative.buildScene(h())
        scene = CadScene.extract(name, handle, originX, originY, t0)
        return scene
    }

    @Synchronized
    fun pick(x: Double, y: Double, tol: Double): Int = MkNative.pick(h(), x + originX, y + originY, tol)

    /** Pencere seçimi (göreli koordinatlar). [crossing]: değenler de seçilir. */
    @Synchronized
    fun selectRect(x0: Double, y0: Double, x1: Double, y1: Double, crossing: Boolean): IntArray =
        MkNative.selectRect(h(), x0 + originX, y0 + originY, x1 + originX, y1 + originY, crossing)

    /** (tür, x, y) göreli; tür 0: yakalama yok. 1 uç, 2 orta, 3 merkez, 4 ekleme noktası */
    @Synchronized
    fun snap(x: Double, y: Double, tol: Double): Triple<Int, Double, Double> {
        val r = MkNative.snap(h(), x + originX, y + originY, tol)
        return Triple(r[0].toInt(), r[1] - originX, r[2] - originY)
    }

    /** Seçim vurgusu: göreli (x1,y1,x2,y2) dörtlüleri. */
    @Synchronized
    fun geometry(id: Int): FloatArray {
        val g = MkNative.geometry(h(), id)
        for (i in g.indices step 2) {
            g[i] = (g[i] - originX).toFloat()
            g[i + 1] = (g[i + 1] - originY).toFloat()
        }
        return g
    }

    /** "TÜR|katman|handle[|blok]" */
    @Synchronized
    fun info(id: Int): String = String(MkNative.entityInfo(h(), id), Charsets.UTF_8)

    @Synchronized
    fun move(ids: Collection<Int>, dx: Double, dy: Double) = MkNative.move(h(), ids.toIntArray(), dx, dy)

    /** Kopyalar; yeni kimlikler ardışıktır, ilki döner (−1: kopyalanamadı). */
    @Synchronized
    fun copy(ids: Collection<Int>, dx: Double, dy: Double) = MkNative.copy(h(), ids.toIntArray(), dx, dy)

    @Synchronized
    fun delete(ids: Collection<Int>) = MkNative.remove(h(), ids.toIntArray())

    @Synchronized
    fun addLine(layer: String, x1: Double, y1: Double, x2: Double, y2: Double) =
        MkNative.addLine(h(), layer.toByteArray(), x1 + originX, y1 + originY, x2 + originX, y2 + originY)

    @Synchronized
    fun addText(layer: String, x: Double, y: Double, height: Double, rotation: Double, text: String) =
        MkNative.addText(h(), layer.toByteArray(), x + originX, y + originY, height, rotation, text.toByteArray())

    @Synchronized
    fun rotate(ids: Collection<Int>, cx: Double, cy: Double, deg: Double) =
        MkNative.rotate(h(), ids.toIntArray(), cx + originX, cy + originY, deg)

    @Synchronized
    fun scale(ids: Collection<Int>, cx: Double, cy: Double, k: Double) =
        MkNative.scale(h(), ids.toIntArray(), cx + originX, cy + originY, k)

    /** [keep]: orijinal kalır, aynalı kopya eklenir. */
    @Synchronized
    fun mirror(ids: Collection<Int>, x1: Double, y1: Double, x2: Double, y2: Double, keep: Boolean) =
        MkNative.mirror(h(), ids.toIntArray(), x1 + originX, y1 + originY, x2 + originX, y2 + originY, keep)

    /** Yazı içeriği (biçim kodları temizlenmiş); yazı değilse "". */
    @Synchronized
    fun text(id: Int): String = String(MkNative.entityText(h(), id), Charsets.UTF_8)

    @Synchronized
    fun setText(id: Int, text: String) = MkNative.setText(h(), id, text.toByteArray()) == 1

    @Synchronized
    fun setLayer(ids: Collection<Int>, layer: String) = MkNative.setLayer(h(), ids.toIntArray(), layer.toByteArray())

    /** [aci]: 1–255, 256 = katman rengi, 0 = bloktan */
    @Synchronized
    fun setColor(ids: Collection<Int>, aci: Int) = MkNative.setColor(h(), ids.toIntArray(), aci)

    @Synchronized
    fun addCircle(layer: String, cx: Double, cy: Double, r: Double) =
        MkNative.addCircle(h(), layer.toByteArray(), cx + originX, cy + originY, r)

    /** [pts]: göreli (x,y) çiftleri */
    @Synchronized
    fun addPolyline(layer: String, pts: List<Pair<Double, Double>>, closed: Boolean): Int {
        val xy = DoubleArray(pts.size * 2)
        pts.forEachIndexed { i, p ->
            xy[i * 2] = p.first + originX
            xy[i * 2 + 1] = p.second + originY
        }
        return MkNative.addPolyline(h(), layer.toByteArray(), xy, closed)
    }

    /** Ötele: (px,py) tarafında [d] mesafede kopya; desteklenmiyorsa −1 */
    @Synchronized
    fun offset(id: Int, d: Double, px: Double, py: Double) = MkNative.offset(h(), id, d, px + originX, py + originY)

    /** Sembolü yerleştir; blok tanımı yoksa önce eklenir. Yeni kimlik veya −1. */
    @Synchronized
    fun insertSymbol(sym: CadSymbol, layer: String, x: Double, y: Double, scale: Double, rot: Double): Int {
        if (MkNative.defineBlock(h(), sym.blockName.toByteArray(), sym.dxfSnippet().toByteArray()) != 1) {
            throw CadException(String(MkNative.lastError(), Charsets.UTF_8))
        }
        return MkNative.insertBlock(h(), layer.toByteArray(), sym.blockName.toByteArray(), x + originX, y + originY, scale, rot)
    }

    /** Ölçü: tek blok (MK_OLCU_n) olarak (x,y) göreli noktasına yerleştirilir. Yeni kimlik veya −1. */
    @Synchronized
    fun insertDimension(layer: String, x: Double, y: Double, snippet: String): Int {
        val name = "MK_OLCU_" + java.lang.Long.toHexString(System.nanoTime())
        if (MkNative.defineBlock(h(), name.toByteArray(), snippet.toByteArray()) != 1) {
            throw CadException(String(MkNative.lastError(), Charsets.UTF_8))
        }
        return MkNative.insertBlock(h(), layer.toByteArray(), name.toByteArray(), x + originX, y + originY, 1.0, 0.0)
    }

    /** Blok adı → yerleştirme sayısı */
    @Synchronized
    fun insertCounts(): List<Pair<String, Int>> =
        String(MkNative.insertCounts(h()), Charsets.UTF_8).lines().filter { '\t' in it }.map {
            val (n, c) = it.split('\t', limit = 2)
            n to (c.toIntOrNull() ?: 0)
        }

    /** Seçimi tek bloğa çevirir; yeni yerleştirmenin kimliği (−1: hata, [lastError]) */
    @Synchronized
    fun group(ids: Collection<Int>): Int = MkNative.group(h(), ids.toIntArray())

    /** Blok yerleştirmelerini çözer; yeni nesnelerin kimlikleri */
    @Synchronized
    fun explode(ids: Collection<Int>): List<Int> {
        val r = MkNative.explode(h(), ids.toIntArray())
        return if (r[0] < 0) emptyList() else (r[0] until r[0] + r[1]).toList()
    }

    fun lastError(): String = String(MkNative.lastError(), Charsets.UTF_8)

    @Synchronized fun undo() = MkNative.undo(h()) == 1
    @Synchronized fun redo() = MkNative.redo(h()) == 1
    val undoCount: Int @Synchronized get() = MkNative.undoCount(h())
    val redoCount: Int @Synchronized get() = MkNative.redoCount(h())
    val dirty: Boolean @Synchronized get() = MkNative.isDirty(h()) == 1

    @Synchronized
    fun save(): ByteArray = MkNative.save(h())

    /** Proje otomatik kaydı: "kaynak dosyaya kaydedilmedi" işaretine dokunmaz */
    @Synchronized
    fun saveCopy(): ByteArray = MkNative.saveCopy(h())

    @Synchronized
    fun setDirty(d: Boolean) = MkNative.setDirty(h(), d)

    @Synchronized
    fun close() {
        if (handle != 0L) MkNative.close(handle)
        handle = 0L
    }
}
