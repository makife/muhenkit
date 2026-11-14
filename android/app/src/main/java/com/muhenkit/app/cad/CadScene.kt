package com.muhenkit.app.cad

class CadException(message: String) : Exception(message)

/** [lweight]: çizgi kalınlığı 1/100 mm, -3 tanımsız (çıktıda varsayılan kalınlık) */
class CadLayer(val name: String, val rgb: Int, val offInFile: Boolean, val count: Int, val lweight: Int = -3)

/** Aynı katman, renk ve kalınlıktaki çizgi parçaları: [first] köşe indeksi, [count] köşe sayısı. */
class LineBatch(val layer: Int, val rgb: Int, val first: Int, val count: Int, val lweight: Int = -3)

/** Çift-tek kuralıyla birlikte doldurulan halkalar. [owner]: üst düzey nesne kimliği (pick ile aynı). */
class FillGroup(val layer: Int, val rgb: Int, val alpha: Float, val loopFirst: IntArray, val loopCount: IntArray, val owner: Int = -1)

class CadText(
    val x: Float, val y: Float, val height: Float, val rotation: Float, val widthFactor: Float, val boxWidth: Float,
    val layer: Int, val rgb: Int, val halign: Int, val valign: Int, val text: String, val owner: Int = -1,
)

/**
 * GPU'ya yüklenmeye hazır çizim. Koordinatlar [originX],[originY] çıkarılmış hâldedir:
 * büyük (ör. UTM) koordinatlarda float hassasiyeti kaybolmasın diye.
 */
class CadScene(
    val name: String,
    val version: String,
    val entityCount: Int,
    val layers: List<CadLayer>,
    val vertices: FloatArray,
    val batches: List<LineBatch>,
    val fills: List<FillGroup>,
    val texts: List<CadText>,
    val originX: Double,
    val originY: Double,
    val minX: Float, val minY: Float, val maxX: Float, val maxY: Float,
    val parseMs: Long,
    /** Çizgi parçası başına sahip nesne: köşe i'nin parçası segOwners[i / 2] */
    val segOwners: IntArray = IntArray(0),
    /** Çizim birimi (INSUNITS): 4 mm, 5 cm, 6 m, 1 inç, 2 ayak; 0/-1 belirtilmemiş */
    val units: Int = -1,
    /** Son eklenen firma anteti (MK_TBL_ANTET_ bloğu) nesne kimliği; yoksa -1 */
    val titleBlock: Int = -1,
) {
    fun ownerOfVertex(i: Int): Int = if (i / 2 < segOwners.size) segOwners[i / 2] else -1

    companion object {
        /** Çekirdekteki sahne tamponlarını kopyalar; koordinatlardan ([ox],[oy]) çıkarılır. */
        internal fun extract(name: String, doc: Long, ox: Double, oy: Double, t0: Long): CadScene {
            val info = String(MkNative.info(doc), Charsets.UTF_8).split('|')
            val ext = MkNative.extents(doc)

            val v = MkNative.vertices(doc)
            for (i in v.indices step 2) {
                v[i] = (v[i] - ox).toFloat()
                v[i + 1] = (v[i + 1] - oy).toFloat()
            }

            val names = String(MkNative.layerNames(doc), Charsets.UTF_8).split('\u0000')
            val li = MkNative.layerInts(doc)
            val layers = names.indices.map { CadLayer(names[it], li[it * 4], li[it * 4 + 1] and 1 != 0, li[it * 4 + 2], li[it * 4 + 3]) }

            val b = MkNative.batches(doc)
            val batches = (0 until b.size / 5).map { LineBatch(b[it * 5], b[it * 5 + 1], b[it * 5 + 2] / 2, b[it * 5 + 3] / 2, b[it * 5 + 4]) }
            val polyOwners = MkNative.owners(doc, 1)
            val textOwners = MkNative.owners(doc, 2)

            // aynı gruptaki halkaları birleştir
            val p = MkNative.polys(doc)
            val groups = LinkedHashMap<Int, MutableList<Int>>()
            for (k in 0 until p.size / 6) groups.getOrPut(p[k * 6 + 2]) { mutableListOf() }.add(k)
            val fills = groups.values.map { ks ->
                val k0 = ks[0]
                FillGroup(
                    layer = p[k0 * 6], rgb = p[k0 * 6 + 1], alpha = p[k0 * 6 + 5] / 1000f,
                    loopFirst = IntArray(ks.size) { p[ks[it] * 6 + 4] / 2 },
                    loopCount = IntArray(ks.size) { p[ks[it] * 6 + 3] },
                    owner = polyOwners.getOrElse(k0) { -1 },
                )
            }

            val tf = MkNative.textFloats(doc)
            val ti = MkNative.textInts(doc)
            val pool = MkNative.textPool(doc)
            val texts = (0 until ti.size / 6).map {
                val f = it * 6
                CadText(
                    x = (tf[f] - ox).toFloat(), y = (tf[f + 1] - oy).toFloat(), height = tf[f + 2], rotation = tf[f + 3],
                    widthFactor = tf[f + 4], boxWidth = tf[f + 5],
                    layer = ti[f], rgb = ti[f + 1], halign = ti[f + 2], valign = ti[f + 3],
                    text = String(pool, ti[f + 4], ti[f + 5], Charsets.UTF_8),
                    owner = textOwners.getOrElse(it) { -1 },
                )
            }

            return CadScene(
                name = name,
                version = info.getOrElse(0) { "" },
                entityCount = info.getOrNull(2)?.toIntOrNull() ?: 0,
                layers = layers,
                vertices = v,
                batches = batches,
                fills = fills,
                texts = texts,
                originX = ox, originY = oy,
                minX = (ext[0] - ox).toFloat(), minY = (ext[1] - oy).toFloat(),
                maxX = (ext[2] - ox).toFloat(), maxY = (ext[3] - oy).toFloat(),
                parseMs = (System.nanoTime() - t0) / 1_000_000,
                segOwners = MkNative.owners(doc, 0),
                units = MkNative.insunits(doc),
                titleBlock = MkNative.findInsert(doc, "MK_TBL_ANTET_".toByteArray()),
            )
        }
    }
}
