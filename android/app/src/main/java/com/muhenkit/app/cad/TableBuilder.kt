package com.muhenkit.app.cad

/**
 * Izgara tablo üretici: satır/sütun ölçüleri ve hücre yazılarından DXF blok içeriği
 * (çizgiler + TEXT) ve önizleme parçaları üretir. Sonuç [CadSymbol] olarak yerleştirilir.
 *
 * Birimler çizim birimidir; [textHeight] hedef çizimin yazı yüksekliğidir. Ekleme noktası
 * tablonun sol üst köşesidir.
 */
class TableBuilder(private val textHeight: Double) {
    class Cell(
        val text: String,
        val col: Int,
        val row: Int,
        val colSpan: Int = 1,
        val rowSpan: Int = 1,
        /** 0 sol, 1 orta, 2 sağ */
        val align: Int = 1,
        val scale: Double = 1.0,
        val color: Int = 7,
    )

    private val cols = ArrayList<Double>()
    private val rows = ArrayList<Double>()
    private val cells = ArrayList<Cell>()
    /** Çizilmeyecek iç çizgiler (birleşik hücreler) hücre üzerinden hesaplanır */

    fun columns(vararg widthsInTextHeights: Double) = apply { widthsInTextHeights.forEach { cols.add(it * textHeight) } }
    fun row(heightInTextHeights: Double = 2.2): Int {
        rows.add(heightInTextHeights * textHeight)
        return rows.size - 1
    }
    fun cell(c: Cell) = apply { cells.add(c) }
    fun text(row: Int, col: Int, text: String, align: Int = 1, colSpan: Int = 1, scale: Double = 1.0, color: Int = 7) =
        apply { cells.add(Cell(text, col, row, colSpan, 1, align, scale, color)) }

    val width get() = cols.sum()
    val height get() = rows.sum()

    private fun x(c: Int) = cols.take(c).sum()
    private fun y(r: Int) = -rows.take(r).sum() // aşağı doğru

    /** Hücre birleşimlerine göre çizilecek kenar parçaları (x1,y1,x2,y2) */
    private fun edges(): List<DoubleArray> {
        val nR = rows.size
        val nC = cols.size
        // her ızgara hücresinin hangi birleşik hücreye ait olduğu
        val owner = Array(nR) { IntArray(nC) { -1 } }
        cells.forEachIndexed { i, c ->
            for (r in c.row until minOf(nR, c.row + c.rowSpan)) for (k in c.col until minOf(nC, c.col + c.colSpan)) owner[r][k] = i
        }
        fun same(r1: Int, c1: Int, r2: Int, c2: Int): Boolean {
            if (r1 !in 0 until nR || r2 !in 0 until nR || c1 !in 0 until nC || c2 !in 0 until nC) return false
            val a = owner[r1][c1]
            return a >= 0 && a == owner[r2][c2]
        }
        val out = ArrayList<DoubleArray>()
        // yatay kenarlar
        for (r in 0..nR) for (c in 0 until nC) {
            if (r in 1 until nR && same(r - 1, c, r, c)) continue
            out.add(doubleArrayOf(x(c), y(r), x(c + 1), y(r)))
        }
        // dikey kenarlar
        for (c in 0..nC) for (r in 0 until nR) {
            if (c in 1 until nC && same(r, c - 1, r, c)) continue
            out.add(doubleArrayOf(x(c), y(r), x(c), y(r + 1)))
        }
        return out
    }

    private fun num(v: Double): String {
        val s = "%.6f".format(java.util.Locale.ROOT, v)
        return if ('.' in s) s.trimEnd('0').trimEnd('.') else s
    }

    /** Yazının sığması için gereken yükseklik: hücre genişliğine göre küçültülür */
    private fun fitHeight(c: Cell): Double {
        val w = (x(c.col + c.colSpan) - x(c.col)) - textHeight * 0.6
        var h = textHeight * c.scale
        // AutoCAD STANDARD (txt.shx) karakter genişliği ≈ 0,85·yükseklik: sığmazsa küçült
        val est = c.text.length * h * 0.85
        if (est > w && w > 0) h *= w / est
        return h
    }

    fun build(id: String, name: String): CadSymbol {
        val sb = StringBuilder()
        fun t(code: Int, v: Any) {
            sb.append(code).append('\n').append(v).append('\n')
        }
        val segs = ArrayList<Float>()
        val w = width
        val h = height
        // ekleme noktası sol üst: (0,0); önizleme merkezli olmasın diye olduğu gibi
        for (e in edges()) {
            t(0, "LINE"); t(100, "AcDbEntity"); t(8, "0"); t(62, 7); t(100, "AcDbLine")
            t(10, num(e[0])); t(20, num(e[1])); t(30, 0); t(11, num(e[2])); t(21, num(e[3])); t(31, 0)
            segs.add(e[0].toFloat()); segs.add(e[1].toFloat()); segs.add(e[2].toFloat()); segs.add(e[3].toFloat())
        }
        val texts = ArrayList<CadSymbol.SymText>()
        for (c in cells) {
            if (c.text.isBlank()) continue
            val x0 = x(c.col)
            val x1 = x(c.col + c.colSpan)
            val yTop = y(c.row)
            val yBot = y(c.row + c.rowSpan)
            val th = fitHeight(c)
            val ax = when (c.align) { 0 -> x0 + textHeight * 0.3; 2 -> x1 - textHeight * 0.3; else -> (x0 + x1) / 2 }
            val ay = (yTop + yBot) / 2
            // 72: yatay hiza (0 sol, 1 orta, 2 sağ), 73: 2 orta → hizalama noktası 11/21
            t(0, "TEXT"); t(100, "AcDbEntity"); t(8, "0"); t(62, c.color); t(100, "AcDbText")
            t(10, num(ax)); t(20, num(ay)); t(30, 0); t(40, num(th)); t(1, c.text.replace('\n', ' '))
            t(7, "STANDARD"); t(72, c.align); t(11, num(ax)); t(21, num(ay)); t(31, 0); t(100, "AcDbText"); t(73, 2)
            texts.add(CadSymbol.SymText(0xFFFFFF, ax.toFloat(), ay.toFloat(), th.toFloat(), 0f, c.align, 2, c.text))
        }
        return CadSymbol(
            id = id, name = name, category = "Tablo", width = w, height = h, snippetText = sb.toString(),
            lines = listOf(0xFFFFFF to segs.toFloatArray()), fills = emptyList(), texts = texts,
        )
    }
}
