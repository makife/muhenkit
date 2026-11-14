package com.muhenkit.app.ui

import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.pdf.PdfDocument
import android.os.Bundle
import android.os.CancellationSignal
import android.os.ParcelFileDescriptor
import android.print.PageRange
import android.print.PrintAttributes
import android.print.PrintDocumentAdapter
import android.print.PrintDocumentInfo
import android.print.PrintManager
import androidx.core.content.FileProvider
import com.muhenkit.app.cad.CadScene
import com.muhenkit.app.cad.DrawingPainter
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt

enum class ExportFormat(val label: String, val ext: String, val mime: String) {
    PDF("PDF", "pdf", "application/pdf"),
    PNG("Resim (PNG)", "png", "image/png"),
}

enum class Paper(val label: String, val wPt: Float, val hPt: Float) {
    A4("A4", 595f, 842f),
    A3("A3", 842f, 1191f),
    A2("A2", 1191f, 1684f),
    A1("A1", 1684f, 2384f),
    A0("A0", 2384f, 3370f),
}

enum class Orientation(val label: String) { AUTO("Otomatik"), LANDSCAPE("Yatay"), PORTRAIT("Dikey") }

data class ExportOptions(
    val format: ExportFormat = ExportFormat.PDF,
    val paper: Paper = Paper.A4,
    val orientation: Orientation = Orientation.AUTO,
    /** true: yalnızca ekranda görünen alan; false: tüm çizim */
    val viewOnly: Boolean = false,
    val monochrome: Boolean = true,
    /** Ölçek paydası (50 → 1/50); 0 = kâğıda sığdır */
    val scale: Int = 0,
    /** Bir çizim biriminin mm karşılığı (cm çizimde 10) */
    val unitMm: Double = 10.0,
    /** Kalınlığı tanımsız çizgilerin kalınlığı (mm) */
    val defaultLw: Float = 0.18f,
    /** Firma antetini sağ alt köşeye al */
    val titleBlock: Boolean = true,
    /** Alt bilgi şeridi (dosya adı, ölçek, tarih) */
    val footer: Boolean = false,
)

/**
 * Sayfa yerleşimi (punto, Y aşağı). Çizim: x' = ox + x·k, y' = oy − y·k; antet aynı biçimde ka/ax/ay ile.
 * [effScale] sığdırmada ortaya çıkan ölçek paydası; [needWmm]/[needHmm] çizimin kâğıttaki boyutu.
 */
class PageLayout(
    val w: Float, val h: Float, val landscape: Boolean,
    val k: Float, val ox: Float, val oy: Float, val area: RectF,
    val hasTitle: Boolean, val ka: Float, val ax: Float, val ay: Float,
    val fits: Boolean, val effScale: Double, val needWmm: Double, val needHmm: Double,
)

/**
 * Çizimi PDF/PNG'ye çevirir; paylaşır, yazdırır (Windows Cad/Exporter.cs ile aynı sayfa düzeni).
 * Pafta TS EN ISO 5457: solda 20 mm cilt payı, diğer kenarlar 10 mm, 0,7 mm çerçeve; antet sağ alt köşede en çok 180 mm (ISO 7200).
 */
object Exporter {
    private const val MM = 72f / 25.4f
    private const val FRAME_LEFT = 20 * MM
    private const val FRAME_SIDE = 10 * MM
    private const val FRAME_LINE = 0.7f * MM
    private const val FOOTER_H = 5 * MM
    private const val TITLE_MAX_W = 180 * MM
    private const val PNG_DPI = 150f

    /** Ölçek paydaları; 0 = kâğıda sığdır */
    val SCALES = listOf(0, 1, 2, 5, 10, 20, 25, 50, 100, 200, 500, 1000)

    /** INSUNITS → bir birimin mm karşılığı; belirtilmemişse cm (mimari altlıkların çoğu) */
    fun unitMm(insunits: Int): Double = when (insunits) { 1 -> 25.4; 2 -> 304.8; 4 -> 1.0; 5 -> 10.0; 6 -> 1000.0; else -> 10.0 }

    /** Tüm çizimin dünya penceresi (%2 pay); [exclude] ≥ 0 ise o nesne (sağ alta alınan antet) hariç */
    fun extentsWindow(s: CadScene, exclude: Int = -1): RectF {
        val r = (if (exclude >= 0) bounds(s) { it != exclude } else null) ?: RectF(s.minX, s.minY, s.maxX, s.maxY)
        val padX = r.width() * 0.02f
        val padY = r.height() * 0.02f
        return RectF(r.left - padX, r.top - padY, r.right + padX, r.bottom + padY)
    }

    /** Sahibi koşula uyan ilkellerin dünya sınırları (left/top = en küçük x/y) */
    fun bounds(s: CadScene, owner: (Int) -> Boolean): RectF? {
        var x0 = Float.MAX_VALUE; var y0 = Float.MAX_VALUE; var x1 = -Float.MAX_VALUE; var y1 = -Float.MAX_VALUE
        fun g(x: Float, y: Float) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y }
        val v = s.vertices
        for (b in s.batches) {
            var i = b.first
            while (i + 1 < b.first + b.count) {
                if (owner(s.ownerOfVertex(i))) { g(v[i * 2], v[i * 2 + 1]); g(v[i * 2 + 2], v[i * 2 + 3]) }
                i += 2
            }
        }
        for (f in s.fills) if (owner(f.owner)) for (q in f.loopFirst.indices) for (i in 0 until f.loopCount[q]) {
            val a = f.loopFirst[q] + i
            g(v[a * 2], v[a * 2 + 1])
        }
        for (t in s.texts) if (owner(t.owner)) { g(t.x, t.y); g(t.x, t.y + t.height) }
        return if (x0 <= x1) RectF(x0, y0, x1, y1) else null
    }

    /** Sağ alta alınacak antetin sınırları; seçenek kapalıysa ya da antet yoksa null */
    fun titleBounds(s: CadScene, o: ExportOptions): RectF? =
        if (o.titleBlock && s.titleBlock >= 0) bounds(s) { it == s.titleBlock } else null

    fun layout(win: RectF, title: RectF?, o: ExportOptions, paper: Paper = o.paper, orientation: Orientation = o.orientation): PageLayout {
        val ww = win.width().coerceAtLeast(1e-6f)
        val wh = win.height().coerceAtLeast(1e-6f)
        val landscape = when (orientation) {
            Orientation.LANDSCAPE -> true
            Orientation.PORTRAIT -> false
            Orientation.AUTO -> ww >= wh
        }
        val w = if (landscape) paper.hPt else paper.wPt
        val h = if (landscape) paper.wPt else paper.hPt
        val fx0 = FRAME_LEFT; val fy0 = FRAME_SIDE; val fx1 = w - FRAME_SIDE
        val iyB = h - FRAME_SIDE - (if (o.footer) FOOTER_H else 0f) // iç alanın altı
        val cands = ArrayList<RectF>()
        var ka = 0f; var ax = 0f; var ay = 0f
        if (title != null && title.width() > 0 && title.height() > 0) {
            ka = min(min(TITLE_MAX_W, fx1 - fx0) / title.width(), (iyB - fy0) * 0.6f / title.height())
            val pw = title.width() * ka
            val ph = title.height() * ka
            ax = fx1 - pw - title.left * ka
            ay = iyB + title.top * ka
            cands.add(RectF(fx0, fy0, fx1 - pw, iyB)) // antetin solu (tam yükseklik)
            cands.add(RectF(fx0, fy0, fx1, iyB - ph)) // antetin üstü (tam genişlik)
        } else cands.add(RectF(fx0, fy0, fx1, iyB))

        fun fit(c: RectF) = min(c.width() / ww, c.height() / wh).coerceAtLeast(0f)
        var best = cands.maxBy { fit(it) }
        var k = fit(best)
        var fits = true
        if (o.scale > 0) {
            k = (o.unitMm / o.scale * MM).toFloat()
            val fitting = cands.filter { fit(it) >= k * 0.999f }
            fits = fitting.isNotEmpty()
            if (fits) best = fitting.maxBy { fit(it) }
        }
        val ox = best.centerX() - (win.left + ww / 2) * k
        val oy = best.centerY() + (win.top + wh / 2) * k
        return PageLayout(w, h, landscape, k, ox, oy, best, ka > 0, ka, ax, ay, fits,
            o.unitMm * MM / k, (ww * k / MM).toDouble(), (wh * k / MM).toDouble())
    }

    /** Seçilen ölçekte çizimin sığdığı en küçük kâğıt (yön sabitse yalnız o yön) */
    fun suggestPaper(win: RectF, title: RectF?, o: ExportOptions): Pair<Paper, Orientation>? {
        val dirs = if (o.orientation == Orientation.AUTO) listOf(Orientation.LANDSCAPE, Orientation.PORTRAIT) else listOf(o.orientation)
        for (p in Paper.entries) for (d in dirs) if (layout(win, title, o, p, d).fits) return p to d
        return null
    }

    private fun drawPage(c: Canvas, L: PageLayout, s: CadScene, hidden: Set<Int>, window: RectF, o: ExportOptions, title: String) {
        c.drawColor(Color.WHITE)
        val tb = if (L.hasTitle) s.titleBlock else -1
        val lw: (Int) -> Float = { if (it >= 0) it / 100f * MM else o.defaultLw * MM }
        val painter = DrawingPainter(s, hidden, o.monochrome, 0f)
        c.save()
        c.clipRect(L.area)
        painter.drawAt(c, L.k, L.ox, L.oy, window, if (tb < 0) null else { w -> w != tb }, null, lw)
        c.restore()
        if (tb >= 0) painter.drawAt(c, L.ka, L.ax, L.ay, null, { w -> w == tb }, if (o.scale > 0) "1/${o.scale}" else null, lw)

        // çerçeve (ISO 5457) ve alt bilgi
        val fx0 = FRAME_LEFT; val fy0 = FRAME_SIDE; val fx1 = L.w - FRAME_SIDE; val fy1 = L.h - FRAME_SIDE
        val frame = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = FRAME_LINE
            color = Color.BLACK
        }
        c.drawRect(fx0, fy0, fx1, fy1, frame)
        if (o.footer) {
            frame.strokeWidth = 0.25f * MM
            c.drawLine(fx0, fy1 - FOOTER_H, fx1, fy1 - FOOTER_H, frame)
            val tp = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                textSize = 7f
                color = Color.BLACK
            }
            val date = SimpleDateFormat("dd.MM.yyyy", Locale("tr")).format(Date())
            c.drawText(title, fx0 + 4, fy1 - 4, tp)
            tp.textAlign = Paint.Align.RIGHT
            val sc = if (o.scale > 0) "Ölçek 1/${o.scale}" else "Ölçeksiz (yaklaşık 1/${Math.round(L.effScale)})"
            c.drawText("$sc · $date", fx1 - 4, fy1 - 4, tp)
        }
    }

    fun render(s: CadScene, hidden: Set<Int>, window: RectF, o: ExportOptions, title: String): ByteArray {
        val L = layout(window, titleBounds(s, o), o)
        return when (o.format) {
            ExportFormat.PDF -> {
                val pdf = PdfDocument()
                val page = pdf.startPage(PdfDocument.PageInfo.Builder(L.w.roundToInt(), L.h.roundToInt(), 1).create())
                drawPage(page.canvas, L, s, hidden, window, o, title)
                pdf.finishPage(page)
                val out = ByteArrayOutputStream()
                pdf.writeTo(out)
                pdf.close()
                out.toByteArray()
            }
            ExportFormat.PNG -> {
                // telefonda bellek sınırı: uzun kenar en çok 6000 piksel
                val k = min(PNG_DPI / 72f, 6000f / max(L.w, L.h))
                val bmp = Bitmap.createBitmap((L.w * k).toInt(), (L.h * k).toInt(), Bitmap.Config.ARGB_8888)
                val c = Canvas(bmp)
                c.scale(k, k)
                drawPage(c, L, s, hidden, window, o, title)
                val out = ByteArrayOutputStream()
                bmp.compress(Bitmap.CompressFormat.PNG, 100, out)
                bmp.recycle()
                out.toByteArray()
            }
        }
    }

    /** Önbelleğe yazıp paylaşım penceresini açar. */
    fun share(context: Context, bytes: ByteArray, fileName: String, mime: String, chooserTitle: String) {
        val dir = File(context.cacheDir, "export").apply { mkdirs() }
        dir.listFiles()?.forEach { it.delete() }
        val f = File(dir, fileName)
        FileOutputStream(f).use { it.write(bytes) }
        val uri = FileProvider.getUriForFile(context, context.packageName + ".files", f)
        val send = Intent(Intent.ACTION_SEND).apply {
            type = mime
            putExtra(Intent.EXTRA_STREAM, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        context.startActivity(Intent.createChooser(send, chooserTitle).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))
    }

    /** Hazır PDF'i Android yazdırma hizmetine verir (yazıcı ya da "PDF olarak kaydet"). */
    fun print(context: Context, pdf: ByteArray, jobName: String) {
        val pm = context.getSystemService(Context.PRINT_SERVICE) as PrintManager
        pm.print(jobName, object : PrintDocumentAdapter() {
            override fun onLayout(old: PrintAttributes?, new: PrintAttributes, cancel: CancellationSignal?, cb: LayoutResultCallback, extras: Bundle?) {
                if (cancel?.isCanceled == true) return cb.onLayoutCancelled()
                cb.onLayoutFinished(
                    PrintDocumentInfo.Builder(jobName).setContentType(PrintDocumentInfo.CONTENT_TYPE_DOCUMENT).setPageCount(1).build(),
                    true,
                )
            }

            override fun onWrite(pages: Array<out PageRange>?, dest: ParcelFileDescriptor, cancel: CancellationSignal?, cb: WriteResultCallback) {
                try {
                    FileOutputStream(dest.fileDescriptor).use { it.write(pdf) }
                    cb.onWriteFinished(arrayOf(PageRange.ALL_PAGES))
                } catch (e: Exception) {
                    cb.onWriteFailed(e.message)
                }
            }
        }, null)
    }
}
