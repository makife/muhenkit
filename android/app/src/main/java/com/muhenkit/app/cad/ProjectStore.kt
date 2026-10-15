package com.muhenkit.app.cad

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.RectF
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream

/**
 * "Projelerim": açılan her DXF uygulamanın kendi alanına kopyalanır, düzenlemeler bu kopyaya
 * otomatik kaydedilir. Kullanıcının orijinal dosyası yalnızca "Kaydet" ile değişir.
 *
 *   files/projects/<id>/drawing.dxf   çalışma kopyası
 *   files/projects/<id>/meta.json     ad, kaynak, tarih, görünüm, gizli katmanlar
 *   files/projects/<id>/thumb.png     önizleme
 */
data class ProjectMeta(
    val id: String,
    val name: String,
    val sourceUri: String?,
    val created: Long,
    val modified: Long,
    /** Orijinal dosyaya kaydedilmemiş değişiklik var */
    val dirtyToSource: Boolean = false,
    /** Görünüm (mutlak dünya koordinatı): merkez x, y ve yakınlaştırma */
    val view: DoubleArray? = null,
    val hidden: Set<Int> = emptySet(),
    val sizeBytes: Long = 0,
)

/** Ekrandan çıkınca da biten işler (otomatik kayıt) için uygulama ömürlü kapsam */
object AppScope : CoroutineScope by CoroutineScope(SupervisorJob() + Dispatchers.Default)

object ProjectStore {
    private fun root(c: Context) = File(c.filesDir, "projects").apply { mkdirs() }
    private fun dir(c: Context, id: String) = File(root(c), id)
    fun drawing(c: Context, id: String) = File(dir(c, id), "drawing.dxf")
    fun thumb(c: Context, id: String) = File(dir(c, id), "thumb.png")

    private fun prefs(c: Context) = c.getSharedPreferences("projects", Context.MODE_PRIVATE)

    /** Süren otomatik kayıtlar: proje yeniden açılmadan önce beklenir (son düzenleme kaybolmasın) */
    private val saving = java.util.concurrent.ConcurrentHashMap<String, kotlinx.coroutines.Job>()
    fun track(id: String, job: kotlinx.coroutines.Job) {
        saving[id] = job
        job.invokeOnCompletion { saving.remove(id, job) }
    }
    suspend fun awaitSaves(id: String? = null) {
        if (id != null) saving[id]?.join() else saving.values.toList().forEach { it.join() }
    }

    fun lastProject(c: Context): String? = prefs(c).getString("last", null)?.takeIf { dir(c, it).exists() }
    fun setLast(c: Context, id: String) = prefs(c).edit().putString("last", id).apply()

    @Synchronized
    fun list(c: Context): List<ProjectMeta> =
        root(c).listFiles()?.mapNotNull { f -> runCatching { meta(c, f.name) }.getOrNull() }?.sortedByDescending { it.modified } ?: emptyList()

    @Synchronized
    fun meta(c: Context, id: String): ProjectMeta {
        val j = JSONObject(File(dir(c, id), "meta.json").readText())
        val v = j.optJSONArray("view")
        val h = j.optJSONArray("hidden")
        return ProjectMeta(
            id = id, name = j.getString("name"), sourceUri = j.optString("source").ifEmpty { null },
            created = j.optLong("created"), modified = j.optLong("modified"), dirtyToSource = j.optBoolean("dirty"),
            view = v?.let { a -> DoubleArray(a.length()) { a.getDouble(it) } },
            hidden = h?.let { a -> (0 until a.length()).map { a.getInt(it) }.toSet() } ?: emptySet(),
            sizeBytes = drawing(c, id).length(),
        )
    }

    @Synchronized
    fun saveMeta(c: Context, m: ProjectMeta) {
        val j = JSONObject()
            .put("name", m.name).put("source", m.sourceUri ?: "").put("created", m.created).put("modified", m.modified)
            .put("dirty", m.dirtyToSource)
        m.view?.let { v -> j.put("view", JSONArray().apply { v.forEach { put(it) } }) }
        j.put("hidden", JSONArray().apply { m.hidden.forEach { put(it) } })
        atomicWrite(File(dir(c, m.id), "meta.json"), j.toString().toByteArray())
    }

    /** Yarım kalmış yazım çalışma kopyasını bozmasın: önce geçici dosya, sonra yer değiştir */
    private fun atomicWrite(f: File, bytes: ByteArray) {
        f.parentFile?.mkdirs()
        val tmp = File(f.parentFile, f.name + ".tmp")
        FileOutputStream(tmp).use { it.write(bytes); it.fd.sync() }
        if (!tmp.renameTo(f)) {
            f.delete()
            tmp.renameTo(f)
        }
    }

    @Synchronized
    fun create(c: Context, name: String, bytes: ByteArray, sourceUri: String?): ProjectMeta {
        val now = System.currentTimeMillis()
        var id = now.toString()
        while (dir(c, id).exists()) id += "_"
        atomicWrite(drawing(c, id), bytes)
        val m = ProjectMeta(id, name, sourceUri, now, now, sizeBytes = bytes.size.toLong())
        saveMeta(c, m)
        return m
    }

    @Synchronized
    fun writeDrawing(c: Context, id: String, bytes: ByteArray) = atomicWrite(drawing(c, id), bytes)

    fun read(c: Context, id: String): ByteArray = drawing(c, id).readBytes()

    fun findBySource(c: Context, uri: String): ProjectMeta? = list(c).firstOrNull { it.sourceUri == uri }

    @Synchronized
    fun rename(c: Context, id: String, name: String) {
        saveMeta(c, meta(c, id).copy(name = name))
    }

    @Synchronized
    fun delete(c: Context, id: String) {
        dir(c, id).deleteRecursively()
        if (prefs(c).getString("last", null) == id) prefs(c).edit().remove("last").apply()
    }

    /** Önizleme resmi (tüm çizim, koyu zemin) */
    fun writeThumb(c: Context, id: String, scene: CadScene, hidden: Set<Int>) {
        val w = 480
        val h = 300
        val bmp = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bmp)
        canvas.drawColor(Color.WHITE)
        val pad = (scene.maxX - scene.minX).coerceAtLeast(1f) * 0.03f
        val win = RectF(scene.minX - pad, scene.minY - pad, scene.maxX + pad, scene.maxY + pad)
        DrawingPainter(scene, hidden, monochrome = false, lineWidth = 0.8f).draw(canvas, win, RectF(4f, 4f, w - 4f, h - 4f))
        val f = thumb(c, id)
        val tmp = File(f.parentFile, "thumb.tmp")
        FileOutputStream(tmp).use { bmp.compress(Bitmap.CompressFormat.PNG, 90, it) }
        tmp.renameTo(f)
        bmp.recycle()
    }
}
