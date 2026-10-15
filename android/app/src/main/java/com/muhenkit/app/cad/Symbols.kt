package com.muhenkit.app.cad

import android.content.Context
import kotlin.math.cos
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sin

/**
 * Sembol ve hazır detay kütüphanesi. İçerik örnek projenin lejantından çıkarılmıştır
 * (araçlar: muhenkit_native/tools/symbols) ve assets/symbols.dat dosyasında durur.
 *
 * Kayıt biçimi:
 *   @SYM \t kimlik \t ad \t kategori \t genişlik \t yükseklik
 *   L \t RRGGBB x1 y1 x2 y2 …          önizleme çizgileri
 *   F \t RRGGBB \t alfa x y x y …      önizleme dolguları
 *   T \t RRGGBB \t x y h açı yh dh \t yazı
 *   S \t kod␟değer␞kod␟değer␞…         DXF blok içeriği (tek satır)
 *   @END
 * Koordinatlar ekleme noktasına (sembol merkezi) göredir; birim örnek projeninkidir.
 */
class CadSymbol(
    val id: String,
    val name: String,
    val category: String,
    val width: Double,
    val height: Double,
    private val snippetText: String,
    val lines: List<Pair<Int, FloatArray>>,
    val fills: List<Triple<Int, Float, FloatArray>>,
    val texts: List<SymText>,
) {
    class SymText(val rgb: Int, val x: Float, val y: Float, val h: Float, val rot: Float, val halign: Int, val valign: Int, val text: String)

    val blockName get() = "MK_$id"

    fun dxfSnippet(): String = snippetText

    /** Önizleme sınırları (x0, y0, x1, y1) */
    val bounds: FloatArray by lazy {
        var x0 = Float.MAX_VALUE
        var y0 = Float.MAX_VALUE
        var x1 = -Float.MAX_VALUE
        var y1 = -Float.MAX_VALUE
        fun add(a: FloatArray) {
            var i = 0
            while (i + 1 < a.size) {
                x0 = min(x0, a[i]); x1 = max(x1, a[i]); y0 = min(y0, a[i + 1]); y1 = max(y1, a[i + 1])
                i += 2
            }
        }
        lines.forEach { add(it.second) }
        fills.forEach { add(it.third) }
        texts.forEach { add(floatArrayOf(it.x, it.y, it.x, it.y + it.h)) }
        if (x0 > x1) floatArrayOf(-1f, -1f, 1f, 1f) else floatArrayOf(x0, y0, x1, y1)
    }

    /** Yerleştirme önizlemesi: (x1,y1,x2,y2) parçaları, [s] ölçek, [rotDeg] dönüş, (x,y) konum. */
    fun segments(x: Double = 0.0, y: Double = 0.0, s: Double = 1.0, rotDeg: Double = 0.0): FloatArray {
        val r = Math.toRadians(rotDeg)
        val cs = cos(r) * s
        val sn = sin(r) * s
        var n = 0
        lines.forEach { n += it.second.size }
        val out = FloatArray(n)
        var k = 0
        for ((_, a) in lines) {
            var i = 0
            while (i + 1 < a.size) {
                out[k++] = (x + a[i] * cs - a[i + 1] * sn).toFloat()
                out[k++] = (y + a[i] * sn + a[i + 1] * cs).toFloat()
                i += 2
            }
        }
        return out
    }
}

object SymbolLibrary {
    @Volatile private var loaded: List<CadSymbol> = emptyList()
    /** Örnek projenin ortanca yazı yüksekliği: hedef çizime ölçek referansı */
    var refTextHeight = 10.0; private set

    val all: List<CadSymbol> get() = loaded
    val categories: List<String> get() = loaded.map { it.category }.distinct()

    private var byBlock: Map<String, CadSymbol> = emptyMap()
    fun forBlock(name: String): CadSymbol? = byBlock[name]

    /** Uygulama açılışında bir kez (arka planda) çağrılır. */
    @Synchronized
    fun load(context: Context) {
        if (loaded.isNotEmpty()) return
        val out = ArrayList<CadSymbol>()
        context.assets.open("symbols.dat").bufferedReader(Charsets.UTF_8).useLines { seq ->
            var head: List<String>? = null
            var lines = ArrayList<Pair<Int, FloatArray>>()
            var fills = ArrayList<Triple<Int, Float, FloatArray>>()
            var texts = ArrayList<CadSymbol.SymText>()
            var snippet = ""
            for (line in seq) {
                when {
                    line.startsWith("#ref_h ") -> refTextHeight = line.substring(7).trim().toDoubleOrNull() ?: 10.0
                    line.startsWith("@SYM\t") -> {
                        head = line.split('\t')
                        lines = ArrayList(); fills = ArrayList(); texts = ArrayList(); snippet = ""
                    }
                    line.startsWith("L\t") -> {
                        val p = line.substring(2).split(' ')
                        lines.add(p[0].toInt(16) to floats(p, 1))
                    }
                    line.startsWith("F\t") -> {
                        val t = line.split('\t')
                        val p = t[2].split(' ')
                        fills.add(Triple(t[1].toInt(16), p[0].toFloat(), floats(p, 1)))
                    }
                    line.startsWith("T\t") -> {
                        val t = line.split('\t')
                        val p = t[2].split(' ')
                        texts.add(CadSymbol.SymText(t[1].toInt(16), p[0].toFloat(), p[1].toFloat(), p[2].toFloat(), p[3].toFloat(),
                            p[4].toInt(), p[5].toInt(), t.getOrElse(3) { "" }))
                    }
                    line.startsWith("S\t") -> snippet = line.substring(2).replace('\u001f', '\n').replace('\u001e', '\n')
                    line == "@END" -> {
                        val h = head
                        if (h != null && h.size >= 6) {
                            out.add(CadSymbol(h[1], h[2], h[3], h[4].toDouble(), h[5].toDouble(), snippet, lines, fills, texts))
                        }
                        head = null
                    }
                }
            }
        }
        byBlock = out.associateBy { it.blockName }
        loaded = out
    }

    private fun floats(p: List<String>, from: Int): FloatArray {
        val a = FloatArray(p.size - from)
        for (i in from until p.size) a[i - from] = p[i].toFloatOrNull() ?: 0f
        return a
    }
}
