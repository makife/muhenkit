package com.muhenkit.app.cad

import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.Typeface
import kotlin.math.min

/**
 * Çizimi bir Android Canvas'a vektör olarak çizer: PDF sayfası (PdfDocument) ve PNG (Bitmap)
 * aynı kodu kullanır. Beyaz kâğıt varsayılır.
 */
class DrawingPainter(
    private val scene: CadScene,
    private val hidden: Set<Int>,
    private val monochrome: Boolean,
    /** [draw] için çizgi kalınlığı (Canvas birimi) */
    private val lineWidth: Float,
) {
    private fun color(rgb: Int): Int {
        if (monochrome) return Color.BLACK
        val c = rgb and 0xFFFFFF
        val r = (c shr 16) and 0xFF
        val g = (c shr 8) and 0xFF
        val b = c and 0xFF
        // beyaz kâğıtta beyaz/çok açık renkler görünmez: siyah (AutoCAD çizim davranışı)
        return if (r * 299 + g * 587 + b * 114 > 200_000) Color.BLACK else (0xFF000000.toInt() or c)
    }

    /**
     * Pencereyi hedefe sığdırarak çizer (önizleme).
     * @param window çizilecek dünya penceresi (göreli koordinat): x0, y0, x1, y1
     * @param target sayfadaki hedef dikdörtgen (Canvas birimi; PDF'te punto)
     */
    fun draw(canvas: Canvas, window: RectF, target: RectF) {
        val ww = window.width().coerceAtLeast(1e-6f)
        val wh = window.height().coerceAtLeast(1e-6f)
        val k = min(target.width() / ww, target.height() / wh)
        // dünya → sayfa: x' = ox + x·k, y' = oy − y·k (Y yukarı)
        val ox = target.centerX() - (window.left + ww / 2) * k
        val oy = target.centerY() + (window.top + wh / 2) * k
        canvas.save()
        canvas.clipRect(target)
        drawAt(canvas, k, ox, oy, null, null, null) { lineWidth }
        canvas.restore()
    }

    /**
     * Sahneyi x' = ox + x·k, y' = oy − y·k dönüşümüyle çizer.
     * @param cull bu dünya penceresinden çok uzaktaki yazıları atla
     * @param owner ilkelin sahip nesnesine göre süzgeç (antet ayrı çizilirken)
     * @param scaleText antetteki "1/50" gibi ölçek yazılarının yerine yazılacak değer
     * @param lw DXF kalınlığından (1/100 mm, -3 tanımsız) sayfa birimine çizgi kalınlığı
     */
    fun drawAt(
        canvas: Canvas, k: Float, ox: Float, oy: Float, cull: RectF?, owner: ((Int) -> Boolean)?, scaleText: String?,
        lw: (Int) -> Float,
    ) {
        canvas.save()
        canvas.translate(ox, oy)
        canvas.scale(k, -k)

        val v = scene.vertices
        // dolgular
        val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
        for (f in scene.fills) {
            if (f.layer in hidden || (owner != null && !owner(f.owner))) continue
            val path = Path().apply { fillType = Path.FillType.EVEN_ODD }
            for (j in f.loopFirst.indices) {
                val first = f.loopFirst[j]
                val n = f.loopCount[j]
                path.moveTo(v[first * 2], v[first * 2 + 1])
                for (q in 1 until n) path.lineTo(v[(first + q) * 2], v[(first + q) * 2 + 1])
                path.close()
            }
            fillPaint.color = color(f.rgb)
            fillPaint.alpha = (f.alpha * 255).toInt().coerceIn(0, 255)
            canvas.drawPath(path, fillPaint)
        }

        // çizgiler: kalınlık sayfa biriminde sabit
        val linePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeCap = Paint.Cap.ROUND
        }
        for (b in scene.batches) {
            if (b.layer in hidden) continue
            linePaint.color = color(b.rgb)
            linePaint.strokeWidth = lw(b.lweight) / k
            if (owner != null) {
                // süzgeçli: yalnız uyan parçaları topla
                val seg = FloatArray(b.count * 2)
                var n = 0
                var i = b.first
                while (i + 1 < b.first + b.count) {
                    if (owner(scene.ownerOfVertex(i))) {
                        seg[n++] = v[i * 2]; seg[n++] = v[i * 2 + 1]; seg[n++] = v[i * 2 + 2]; seg[n++] = v[i * 2 + 3]
                    }
                    i += 2
                }
                if (n > 0) canvas.drawLines(seg, 0, n, linePaint)
                continue
            }
            // drawLines tek çağrıda en çok ~16 bin parça: büyük partileri böl
            var o = b.first
            val end = b.first + b.count
            while (o < end) {
                val n = min(end - o, 32_768) and 1.inv()
                canvas.drawLines(v, o * 2, n * 2, linePaint)
                o += n
                if (n == 0) break
            }
        }
        canvas.restore()

        // yazılar (sayfa koordinatında, okunur yönde)
        val tp = Paint(Paint.ANTI_ALIAS_FLAG).apply { typeface = Typeface.DEFAULT }
        tp.textSize = 100f
        val bounds = Rect()
        tp.getTextBounds("H", 0, 1, bounds)
        val capRatio = bounds.height() / 100f
        for (t0 in scene.texts) {
            if (t0.layer in hidden || (owner != null && !owner(t0.owner))) continue
            if (cull != null) {
                val m = t0.height * 60
                if (t0.x < cull.left - m || t0.x > cull.right + m || t0.y < cull.top - m || t0.y > cull.bottom + m) continue
            }
            val t = if (scaleText != null && SCALE_TEXT.matches(t0.text)) t0.copyText(scaleText) else t0
            val capPx = t.height * k
            if (capPx < 0.3f) continue
            tp.color = color(t.rgb)
            tp.textSize = capPx / capRatio
            tp.textScaleX = if (t.widthFactor > 0) t.widthFactor else 1f
            tp.textAlign = when (t.halign) { 1 -> Paint.Align.CENTER; 2 -> Paint.Align.RIGHT; else -> Paint.Align.LEFT }
            val lines = wrap(t, tp, k)
            val n = lines.size
            val step = capPx * 1.667f
            canvas.save()
            canvas.translate(ox + t.x * k, oy - t.y * k)
            canvas.rotate(-t.rotation)
            for ((i, line) in lines.withIndex()) {
                // taban çizgisi (aşağı +): TextMesh ile aynı dikey hizalama
                val by = when (t.valign) {
                    3 -> capPx + i * step
                    2 -> -((capPx + (n - 1) * step) / 2 - capPx) + i * step
                    1 -> -(n - 1 - i) * step
                    else -> i * step
                }
                canvas.drawText(line, 0f, by, tp)
            }
            canvas.restore()
        }
    }

    private fun CadText.copyText(s: String) =
        CadText(x, y, height, rotation, widthFactor, boxWidth, layer, rgb, halign, valign, s, owner)

    private fun wrap(t: CadText, p: Paint, k: Float): List<String> {
        val raw = t.text.split('\n')
        if (t.boxWidth <= 0f) return raw
        val max = t.boxWidth * k * 1.05f
        val out = ArrayList<String>()
        for (para in raw) {
            val sb = StringBuilder()
            for (word in para.split(' ')) {
                val cand = if (sb.isEmpty()) word else "$sb $word"
                if (sb.isNotEmpty() && p.measureText(cand) > max) {
                    out.add(sb.toString())
                    sb.setLength(0)
                    sb.append(word)
                } else {
                    sb.setLength(0)
                    sb.append(cand)
                }
            }
            out.add(sb.toString())
        }
        return out
    }

    private companion object {
        val SCALE_TEXT = Regex("""^\s*1\s*[/:]\s*\d+([.,]\d+)?\s*$""")
    }
}
