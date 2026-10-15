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

enum class ExportFormat(val label: String, val ext: String, val mime: String) {
    PDF("PDF", "pdf", "application/pdf"),
    PNG("Resim (PNG)", "png", "image/png"),
}

enum class Paper(val label: String, val wPt: Float, val hPt: Float) {
    A4("A4", 595f, 842f),
    A3("A3", 842f, 1191f),
}

enum class Orientation(val label: String) { AUTO("Otomatik"), LANDSCAPE("Yatay"), PORTRAIT("Dikey") }

data class ExportOptions(
    val format: ExportFormat = ExportFormat.PDF,
    val paper: Paper = Paper.A4,
    val orientation: Orientation = Orientation.AUTO,
    /** true: yalnızca ekranda görünen alan; false: tüm çizim */
    val viewOnly: Boolean = false,
    val monochrome: Boolean = true,
)

/** Çizimi PDF/PNG'ye çevirir; paylaşır, yazdırır. */
object Exporter {
    private const val MARGIN = 28f // ≈10 mm
    private const val FOOTER = 14f
    private const val PNG_DPI = 150f

    /** Tüm çizimin dünya penceresi (%2 pay) */
    fun extentsWindow(s: CadScene): RectF {
        val padX = (s.maxX - s.minX) * 0.02f
        val padY = (s.maxY - s.minY) * 0.02f
        return RectF(s.minX - padX, s.minY - padY, s.maxX + padX, s.maxY + padY)
    }

    private fun pageSize(o: ExportOptions, window: RectF): Pair<Float, Float> {
        val landscape = when (o.orientation) {
            Orientation.LANDSCAPE -> true
            Orientation.PORTRAIT -> false
            Orientation.AUTO -> window.width() >= window.height()
        }
        return if (landscape) o.paper.hPt to o.paper.wPt else o.paper.wPt to o.paper.hPt
    }

    private fun drawPage(c: Canvas, w: Float, h: Float, s: CadScene, hidden: Set<Int>, window: RectF, o: ExportOptions, title: String, lineWidth: Float) {
        c.drawColor(Color.WHITE)
        val target = RectF(MARGIN, MARGIN, w - MARGIN, h - MARGIN - FOOTER)
        DrawingPainter(s, hidden, o.monochrome, lineWidth).draw(c, window, target)
        // çerçeve ve alt bilgi
        val frame = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = 0.6f
            color = Color.BLACK
        }
        c.drawRect(MARGIN, MARGIN, w - MARGIN, h - MARGIN, frame)
        c.drawLine(MARGIN, h - MARGIN - FOOTER, w - MARGIN, h - MARGIN - FOOTER, frame)
        val tp = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            textSize = 7f
            color = Color.BLACK
        }
        val date = SimpleDateFormat("dd.MM.yyyy", Locale("tr")).format(Date())
        c.drawText(title, MARGIN + 4, h - MARGIN - 4, tp)
        tp.textAlign = Paint.Align.RIGHT
        c.drawText("$date · MühenKit", w - MARGIN - 4, h - MARGIN - 4, tp)
    }

    fun render(s: CadScene, hidden: Set<Int>, window: RectF, o: ExportOptions, title: String): ByteArray {
        val (w, h) = pageSize(o, window)
        return when (o.format) {
            ExportFormat.PDF -> {
                val pdf = PdfDocument()
                val page = pdf.startPage(PdfDocument.PageInfo.Builder(w.toInt(), h.toInt(), 1).create())
                drawPage(page.canvas, w, h, s, hidden, window, o, title, 0.4f)
                pdf.finishPage(page)
                val out = ByteArrayOutputStream()
                pdf.writeTo(out)
                pdf.close()
                out.toByteArray()
            }
            ExportFormat.PNG -> {
                val k = PNG_DPI / 72f
                val bmp = Bitmap.createBitmap((w * k).toInt(), (h * k).toInt(), Bitmap.Config.ARGB_8888)
                val c = Canvas(bmp)
                c.scale(k, k)
                drawPage(c, w, h, s, hidden, window, o, title, 0.5f)
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
