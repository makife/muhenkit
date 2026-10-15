package com.muhenkit.app.cad

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.Typeface
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.math.cos
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sin

/**
 * Yazıların GPU ağı: sistem yazı tipiyle üretilmiş bir glif atlası ve dünya
 * koordinatında glif dörtgenleri. Dörtgenler katmana göre sıralıdır ki katman
 * gizlemek tek bir aralığı atlamak olsun.
 *
 * Köşe düzeni (24 bayt): x, y, u, v (float) · renk (4 × ubyte) · yazı yüksekliği (float)
 */
/** Sistem yazı tipiyle çizilmiş glif atlası. Yeni karakter gelmedikçe yeniden kullanılır. */
class GlyphAtlas internal constructor(
    val bitmap: Bitmap,
    internal val glyphs: Map<Int, Glyph>,
    internal val cap: Float,
) {
    internal class Glyph(
        val u0: Float, val v0: Float, val u1: Float, val v1: Float,
        val x0: Float, val x1: Float, val yTop: Float, val yBot: Float, // kalem/taban çizgisine göre, font pikseli
        val advance: Float,
    )

    fun covers(texts: List<CadText>): Boolean =
        texts.all { t -> t.text.codePoints().allMatch { it < 32 || glyphs.containsKey(it) } }

    companion object {
        private const val FONT_PX = 96f
        private const val PAD = 4
        private const val ATLAS_W = 2048

        // sık kullanılan karakterler baştan eklenir ki yeni yazıda atlas yeniden kurulmasın
        private const val BASIC =
            " !\"#%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]_abcdefghijklmnopqrstuvwxyz{|}~°±²³ØçğıöşüİÇĞÖŞÜ∅Ωµ×÷"

        fun build(texts: List<CadText>): GlyphAtlas? {
            val cps = sortedSetOf<Int>()
            for (t in texts) t.text.codePoints().forEach { if (it >= 32) cps.add(it) }
            BASIC.codePoints().forEach { cps.add(it) }
            var fontPx = FONT_PX
            while (fontPx >= 16f) {
                tryBuild(cps, fontPx)?.let { return it }
                fontPx *= 0.75f // sığmadı: küçült
            }
            return null
        }

        private fun tryBuild(cps: Set<Int>, fontPx: Float): GlyphAtlas? {
            val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                textSize = fontPx
                color = Color.WHITE
                typeface = Typeface.DEFAULT
            }
            val fm = paint.fontMetrics
            val bounds = Rect()
            paint.getTextBounds("H", 0, 1, bounds)
            val cap = bounds.height().toFloat().coerceAtLeast(1f)
            val cellH = (fm.descent - fm.ascent).toInt() + PAD * 2

            // raf yerleşimi
            class Cell(val cp: Int, val s: String, val x: Int, val y: Int, val w: Int, val left: Float, val adv: Float)
            val cells = ArrayList<Cell>(cps.size)
            var cx = 0
            var cy = 0
            for (cp in cps) {
                val s = String(Character.toChars(cp))
                val adv = paint.measureText(s)
                paint.getTextBounds(s, 0, s.length, bounds)
                val left = min(0f, bounds.left.toFloat())
                val right = max(adv, bounds.right.toFloat())
                val w = (right - left).toInt() + PAD * 2 + 1
                if (cx + w > ATLAS_W) {
                    cx = 0
                    cy += cellH
                }
                cells.add(Cell(cp, s, cx, cy, w, left, adv))
                cx += w
            }
            val atlasH = cy + cellH
            if (atlasH > 4096) return null

            val bmp = Bitmap.createBitmap(ATLAS_W, atlasH, Bitmap.Config.ALPHA_8)
            val canvas = Canvas(bmp)
            val glyphs = HashMap<Int, Glyph>(cells.size * 2)
            val baseline = PAD - fm.ascent
            for (c in cells) {
                canvas.drawText(c.s, c.x + PAD - c.left, c.y + baseline, paint)
                glyphs[c.cp] = Glyph(
                    u0 = c.x.toFloat() / ATLAS_W, v0 = c.y.toFloat() / atlasH,
                    u1 = (c.x + c.w).toFloat() / ATLAS_W, v1 = (c.y + cellH).toFloat() / atlasH,
                    x0 = c.left - PAD, x1 = c.left - PAD + c.w,
                    yTop = baseline, yBot = baseline - cellH,
                    advance = c.adv,
                )
            }
            return GlyphAtlas(bmp, glyphs, cap)
        }
    }
}

class TextMesh(
    val atlas: GlyphAtlas,
    val vertexData: ByteBuffer,
    val indexData: ByteBuffer,
    /** katman → (ilk indeks, indeks sayısı) */
    val layerRanges: Map<Int, IntRange>,
    val quadCount: Int,
) {
    companion object {
        const val STRIDE = 24
        private const val LINE_STEP = 1.667f // AutoCAD MTEXT satır aralığı (yazı yüksekliği cinsinden)

        fun build(texts: List<CadText>, atlas: GlyphAtlas, rgbOf: (Int) -> Int): TextMesh {
            val glyphs = atlas.glyphs
            val cap = atlas.cap
            // dörtgenler, katmana göre sıralı
            val order = texts.indices.sortedBy { texts[it].layer }
            var quads = 0
            for (t in texts) quads += t.text.codePointCount(0, t.text.length)
            val vb = ByteBuffer.allocateDirect(quads * 4 * STRIDE).order(ByteOrder.nativeOrder())
            val ib = ByteBuffer.allocateDirect(quads * 6 * 4).order(ByteOrder.nativeOrder())
            val ranges = LinkedHashMap<Int, IntRange>()
            var q = 0
            var curLayer = Int.MIN_VALUE
            var rangeStart = 0

            for (ti in order) {
                val t = texts[ti]
                if (t.layer != curLayer) {
                    if (curLayer != Int.MIN_VALUE) ranges[curLayer] = rangeStart until q * 6
                    curLayer = t.layer
                    rangeStart = q * 6
                }
                val s = t.height / cap // font pikseli → dünya birimi
                val wf = if (t.widthFactor > 0f) t.widthFactor else 1f
                val lines = layoutLines(t, glyphs, s * wf)
                val n = lines.size
                val step = cap * LINE_STEP
                val rot = Math.toRadians(t.rotation.toDouble())
                val cs = cos(rot).toFloat()
                val sn = sin(rot).toFloat()
                val col = rgbOf(t.rgb)
                // ABGR bayt sırası (küçük endian'da RGBA olarak okunur)
                val packed = (0xFF shl 24) or ((col and 0xFF) shl 16) or (col and 0xFF00) or ((col shr 16) and 0xFF)

                for ((li, line) in lines.withIndex()) {
                    var width = 0f
                    line.codePoints().forEach { width += glyphs[it]?.advance ?: 0f }
                    val ax = when (t.halign) { 1 -> -width / 2; 2 -> -width; else -> 0f }
                    val by = when (t.valign) {
                        3 -> -cap - li * step
                        2 -> (cap + (n - 1) * step) / 2 - cap - li * step
                        1 -> (n - 1 - li) * step
                        else -> -li * step
                    }
                    var pen = ax
                    line.codePoints().forEach { cp ->
                        val g = glyphs[cp] ?: return@forEach
                        val qx0 = (pen + g.x0) * s * wf
                        val qx1 = (pen + g.x1) * s * wf
                        val qy0 = (by + g.yBot) * s
                        val qy1 = (by + g.yTop) * s
                        fun put(lx: Float, ly: Float, u: Float, v: Float) {
                            vb.putFloat(t.x + lx * cs - ly * sn)
                            vb.putFloat(t.y + lx * sn + ly * cs)
                            vb.putFloat(u)
                            vb.putFloat(v)
                            vb.putInt(packed)
                            vb.putFloat(t.height)
                        }
                        put(qx0, qy0, g.u0, g.v1)
                        put(qx1, qy0, g.u1, g.v1)
                        put(qx1, qy1, g.u1, g.v0)
                        put(qx0, qy1, g.u0, g.v0)
                        val b = q * 4
                        ib.putInt(b); ib.putInt(b + 1); ib.putInt(b + 2)
                        ib.putInt(b); ib.putInt(b + 2); ib.putInt(b + 3)
                        q++
                        pen += g.advance
                    }
                }
            }
            if (curLayer != Int.MIN_VALUE) ranges[curLayer] = rangeStart until q * 6
            vb.flip()
            ib.flip()
            return TextMesh(atlas, vb, ib, ranges, q)
        }

        /** Satırlara böl; MTEXT kutu genişliği varsa kelime kaydır. */
        private fun layoutLines(t: CadText, glyphs: Map<Int, GlyphAtlas.Glyph>, worldPerPx: Float): List<String> {
            val raw = t.text.split('\n')
            if (t.boxWidth <= 0f) return raw
            val maxPx = t.boxWidth * 1.05f / worldPerPx
            val out = ArrayList<String>()
            for (para in raw) {
                val sb = StringBuilder()
                var w = 0f
                for (word in para.split(' ')) {
                    var ww = 0f
                    word.codePoints().forEach { ww += glyphs[it]?.advance ?: 0f }
                    val sp = glyphs[' '.code]?.advance ?: 0f
                    if (sb.isNotEmpty() && w + sp + ww > maxPx) {
                        out.add(sb.toString())
                        sb.setLength(0)
                        w = 0f
                    }
                    if (sb.isNotEmpty()) {
                        sb.append(' ')
                        w += sp
                    }
                    sb.append(word)
                    w += ww
                }
                out.add(sb.toString())
            }
            return out
        }
    }
}
