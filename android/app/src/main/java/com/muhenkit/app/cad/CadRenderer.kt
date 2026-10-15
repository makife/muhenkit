package com.muhenkit.app.cad

import android.opengl.GLES30.*
import android.opengl.GLSurfaceView
import java.nio.ByteBuffer
import java.nio.ByteOrder
import javax.microedition.khronos.egl.EGLConfig
import javax.microedition.khronos.opengles.GL10

/** Ekrandaki görünüm: [cx],[cy] ekran ortasındaki dünya noktası, [zoom] piksel/birim. */
data class ViewState(val cx: Double, val cy: Double, val zoom: Double)

/**
 * Düzenleyici katmanı (göreli dünya koordinatları).
 * [selection]: seçili nesnelerin (x1,y1,x2,y2) parçaları · [ghostDx],[ghostDy]: taşıma/kopya önizlemesi
 * [rubber]: çizgi önizlemesi (x1,y1,x2,y2) · [snapType]: 0 yok, 1 uç, 2 orta, 3 merkez, 4 ekleme
 */
class Overlay(
    val selection: FloatArray = FloatArray(0),
    /** Önizleme dönüşümü (a,b,c,d,e,f): x' = a·x + c·y + e, y' = b·x + d·y + f */
    val ghost: DoubleArray? = null,
    val rubber: FloatArray? = null,
    val snapType: Int = 0,
    val snapX: Float = 0f,
    val snapY: Float = 0f,
    val cursor: Boolean = false,
    val cursorX: Float = 0f,
    val cursorY: Float = 0f,
    /** [rubber]: (x1,y1,x2,y2) parçaları — çizgi, daire, dikdörtgen, çoklu çizgi önizlemesi */
    /** Pencere seçimi kutusu (x0,y0,x1,y1); [boxCrossing]: sağdan sola (değenler) */
    val box: FloatArray? = null,
    val boxCrossing: Boolean = false,
    /** Hizalama izleri: kesikli kılavuzlar (x1,y1,x2,y2) */
    val guides: FloatArray? = null,
    /** İzlenen (yakalanmış) noktalar (x,y): küçük artılar */
    val marks: FloatArray? = null,
) {
    companion object {
        val EMPTY = Overlay()
    }
}

/**
 * GPU çizici. Tüm sahne tek seferde VBO'lara yüklenir; her karede yalnızca
 * dönüşüm matrisi değişir. Bu yüzden kaydırma/yakınlaştırma çizim boyutundan bağımsızdır.
 */
class CadRenderer : GLSurfaceView.Renderer {
    @Volatile var view = ViewState(0.0, 0.0, 1.0)
    @Volatile var hidden: Set<Int> = emptySet()
    @Volatile var overlay: Overlay = Overlay.EMPTY
    @Volatile private var pending: Pair<CadScene, TextMesh?>? = null
    @Volatile private var clearPending = false

    var width = 1; private set
    var height = 1; private set

    private val background = floatArrayOf(0.043f, 0.055f, 0.063f) // #0B0E10

    // GPU kaynakları
    private var lineProg = 0
    private var textProg = 0
    private var vbo = 0
    private var textVbo = 0
    private var textIbo = 0
    private var atlasTex = 0
    private var ovVbo = 0
    private var ovSelUploaded: FloatArray? = null
    private val ovTmp = FloatArray(64)
    private var uploadedAtlas: GlyphAtlas? = null
    private var scene: CadScene? = null
    private var mesh: TextMesh? = null

    private var uLineXf = 0
    private var uLineColor = 0
    private var uLineLin = 0
    private var uTextXf = 0
    private var uTextPx = 0

    fun setScene(s: CadScene, m: TextMesh?) {
        pending = s to m
    }

    fun clear() {
        clearPending = true
    }

    override fun onSurfaceCreated(gl: GL10?, config: EGLConfig?) {
        lineProg = program(LINE_VS, LINE_FS)
        uLineXf = glGetUniformLocation(lineProg, "u_xf")
        uLineColor = glGetUniformLocation(lineProg, "u_color")
        uLineLin = glGetUniformLocation(lineProg, "u_lin")
        textProg = program(TEXT_VS, TEXT_FS)
        uTextXf = glGetUniformLocation(textProg, "u_xf")
        uTextPx = glGetUniformLocation(textProg, "u_px")
        ovVbo = 0
        ovSelUploaded = null
        // bağlam yeniden kurulduysa sahneyi tekrar yükle
        scene?.let { s -> pending = s to mesh }
        vbo = 0; textVbo = 0; textIbo = 0; atlasTex = 0
        uploadedAtlas = null
    }

    override fun onSurfaceChanged(gl: GL10?, w: Int, h: Int) {
        width = w
        height = h
        glViewport(0, 0, w, h)
    }

    override fun onDrawFrame(gl: GL10?) {
        if (clearPending) {
            clearPending = false
            release(withAtlas = true)
            scene = null
            mesh = null
        }
        pending?.let {
            pending = null
            upload(it.first, it.second)
        }
        glClearColor(background[0], background[1], background[2], 1f)
        glClearStencil(0)
        glClear(GL_COLOR_BUFFER_BIT or GL_STENCIL_BUFFER_BIT)
        val s = scene ?: return
        val v = view
        val hid = hidden

        // dünya → NDC: ndc = p * (sx, sy) + (tx, ty)
        val sx = (2 * v.zoom / width)
        val sy = (2 * v.zoom / height)
        val xf = floatArrayOf(sx.toFloat(), sy.toFloat(), (-v.cx * sx).toFloat(), (-v.cy * sy).toFloat())

        glEnable(GL_BLEND)
        glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA)

        glUseProgram(lineProg)
        glUniform4fv(uLineXf, 1, xf, 0)
        glUniform4f(uLineLin, 1f, 0f, 0f, 1f)
        glBindBuffer(GL_ARRAY_BUFFER, vbo)
        glEnableVertexAttribArray(0)
        glVertexAttribPointer(0, 2, GL_FLOAT, false, 8, 0)

        // dolgular: şablon tamponunda çift-tek, sonra aynı üçgenlerle boya ve sıfırla
        glEnable(GL_STENCIL_TEST)
        for (f in s.fills) {
            if (f.layer in hid) continue
            glColorMask(false, false, false, false)
            glStencilFunc(GL_ALWAYS, 0, 0xFF)
            glStencilOp(GL_KEEP, GL_KEEP, GL_INVERT)
            for (k in f.loopFirst.indices) glDrawArrays(GL_TRIANGLE_FAN, f.loopFirst[k], f.loopCount[k])
            glColorMask(true, true, true, true)
            glStencilFunc(GL_NOTEQUAL, 0, 0xFF)
            glStencilOp(GL_ZERO, GL_ZERO, GL_ZERO)
            setColor(f.rgb, f.alpha)
            for (k in f.loopFirst.indices) glDrawArrays(GL_TRIANGLE_FAN, f.loopFirst[k], f.loopCount[k])
        }
        glDisable(GL_STENCIL_TEST)

        // çizgiler
        for (b in s.batches) {
            if (b.layer in hid) continue
            setColor(b.rgb, 1f)
            glDrawArrays(GL_LINES, b.first, b.count)
        }

        // yazılar
        val m = mesh
        if (m != null && atlasTex != 0) {
            glUseProgram(textProg)
            glUniform4fv(uTextXf, 1, xf, 0)
            glUniform1f(uTextPx, v.zoom.toFloat())
            glBindBuffer(GL_ARRAY_BUFFER, textVbo)
            glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, textIbo)
            glEnableVertexAttribArray(0)
            glEnableVertexAttribArray(1)
            glEnableVertexAttribArray(2)
            glEnableVertexAttribArray(3)
            glVertexAttribPointer(0, 2, GL_FLOAT, false, TextMesh.STRIDE, 0)
            glVertexAttribPointer(1, 2, GL_FLOAT, false, TextMesh.STRIDE, 8)
            glVertexAttribPointer(2, 4, GL_UNSIGNED_BYTE, true, TextMesh.STRIDE, 16)
            glVertexAttribPointer(3, 1, GL_FLOAT, false, TextMesh.STRIDE, 20)
            glActiveTexture(GL_TEXTURE0)
            glBindTexture(GL_TEXTURE_2D, atlasTex)
            for ((layer, r) in m.layerRanges) {
                if (layer in hid || r.isEmpty()) continue
                glDrawElements(GL_TRIANGLES, r.last - r.first + 1, GL_UNSIGNED_INT, r.first * 4)
            }
            glDisableVertexAttribArray(1)
            glDisableVertexAttribArray(2)
            glDisableVertexAttribArray(3)
            glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, 0)
        }

        drawOverlay(xf, v)
    }

    /** Seçim vurgusu, önizlemeler ve yakalama işareti. Kalınlıklar ekranda sabittir. */
    private fun drawOverlay(xf: FloatArray, v: ViewState) {
        val o = overlay
        if (o === Overlay.EMPTY) return
        if (ovVbo == 0) {
            val ids = IntArray(1)
            glGenBuffers(1, ids, 0)
            ovVbo = ids[0]
        }
        glUseProgram(lineProg)
        glBindBuffer(GL_ARRAY_BUFFER, ovVbo)
        glEnableVertexAttribArray(0)
        val px = 1.0 / v.zoom // bir pikselin dünya karşılığı
        val nx = (2.0 / width).toFloat() // bir pikselin NDC karşılığı
        val ny = (2.0 / height).toFloat()

        val sel = o.selection
        if (sel.isNotEmpty()) {
            if (sel !== ovSelUploaded) {
                val bb = ByteBuffer.allocateDirect(sel.size * 4).order(ByteOrder.nativeOrder())
                bb.asFloatBuffer().put(sel)
                glBufferData(GL_ARRAY_BUFFER, bb.capacity(), bb, GL_DYNAMIC_DRAW)
                ovSelUploaded = sel
            }
            glVertexAttribPointer(0, 2, GL_FLOAT, false, 8, 0)
            val n = sel.size / 2
            // kalın görünüm: 1 piksel kaydırılmış kopyalar
            glUniform4f(uLineColor, 0.22f, 0.78f, 1f, 1f)
            for ((dx, dy) in OFFSETS) {
                glUniform4f(uLineXf, xf[0], xf[1], xf[2] + dx * nx, xf[3] + dy * ny)
                glDrawArrays(GL_LINES, 0, n)
            }
            o.ghost?.let { g ->
                // NDC = S·(G·p) + T  →  doğrusal kısım S·L, öteleme S·t + T
                glUniform4f(uLineColor, 1f, 0.71f, 0.15f, 1f)
                setMatrix(
                    (xf[0] * g[0]).toFloat(), (xf[1] * g[1]).toFloat(), (xf[0] * g[2]).toFloat(), (xf[1] * g[3]).toFloat(),
                    (xf[0] * g[4] + xf[2]).toFloat(), (xf[1] * g[5] + xf[3]).toFloat(),
                )
                glDrawArrays(GL_LINES, 0, n)
            }
            glUniform4fv(uLineXf, 1, xf, 0)
            glUniform4f(uLineLin, 1f, 0f, 0f, 1f)
        }

        // küçük geçici çizgiler: lastik bant, yakalama işareti, artı imleci
        var k = 0
        fun seg(x1: Double, y1: Double, x2: Double, y2: Double) {
            ovTmp[k++] = x1.toFloat(); ovTmp[k++] = y1.toFloat(); ovTmp[k++] = x2.toFloat(); ovTmp[k++] = y2.toFloat()
        }
        fun flush(r: Float, g: Float, b: Float) {
            if (k == 0) return
            val bb = ByteBuffer.allocateDirect(k * 4).order(ByteOrder.nativeOrder())
            bb.asFloatBuffer().put(ovTmp, 0, k)
            glBufferData(GL_ARRAY_BUFFER, bb.capacity(), bb, GL_STREAM_DRAW)
            ovSelUploaded = null
            glVertexAttribPointer(0, 2, GL_FLOAT, false, 8, 0)
            glUniform4f(uLineColor, r, g, b, 1f)
            for ((dx, dy) in OFFSETS) {
                glUniform4f(uLineXf, xf[0], xf[1], xf[2] + dx * nx, xf[3] + dy * ny)
                glDrawArrays(GL_LINES, 0, k / 2)
            }
            glUniform4fv(uLineXf, 1, xf, 0)
            k = 0
        }

        o.box?.let { b ->
            val x0 = b[0].toDouble(); val y0 = b[1].toDouble(); val x1 = b[2].toDouble(); val y1 = b[3].toDouble()
            // yarı saydam dolgu: AutoCAD gibi pencere mavi, kesişen yeşil
            ovTmp[0] = x0.toFloat(); ovTmp[1] = y0.toFloat(); ovTmp[2] = x1.toFloat(); ovTmp[3] = y0.toFloat()
            ovTmp[4] = x1.toFloat(); ovTmp[5] = y1.toFloat(); ovTmp[6] = x0.toFloat(); ovTmp[7] = y1.toFloat()
            val bb = ByteBuffer.allocateDirect(32).order(ByteOrder.nativeOrder())
            bb.asFloatBuffer().put(ovTmp, 0, 8)
            glBufferData(GL_ARRAY_BUFFER, 32, bb, GL_STREAM_DRAW)
            ovSelUploaded = null
            glVertexAttribPointer(0, 2, GL_FLOAT, false, 8, 0)
            if (o.boxCrossing) glUniform4f(uLineColor, 0.24f, 1f, 0.56f, 0.12f) else glUniform4f(uLineColor, 0.22f, 0.6f, 1f, 0.14f)
            glDrawArrays(GL_TRIANGLE_FAN, 0, 4)
            seg(x0, y0, x1, y0); seg(x1, y0, x1, y1); seg(x1, y1, x0, y1); seg(x0, y1, x0, y0)
            if (o.boxCrossing) flush(0.24f, 1f, 0.56f) else flush(0.22f, 0.6f, 1f)
        }
        if (o.cursor) {
            val cx = o.cursorX.toDouble()
            val cy = o.cursorY.toDouble()
            val r = 26 * px
            seg(cx - r, cy, cx - 6 * px, cy); seg(cx + 6 * px, cy, cx + r, cy)
            seg(cx, cy - r, cx, cy - 6 * px); seg(cx, cy + 6 * px, cx, cy + r)
            flush(0.93f, 0.92f, 0.87f)
        }
        o.rubber?.takeIf { it.size >= 4 }?.let { r ->
            val bb = ByteBuffer.allocateDirect(r.size * 4).order(ByteOrder.nativeOrder())
            bb.asFloatBuffer().put(r)
            glBufferData(GL_ARRAY_BUFFER, bb.capacity(), bb, GL_STREAM_DRAW)
            ovSelUploaded = null
            glVertexAttribPointer(0, 2, GL_FLOAT, false, 8, 0)
            glUniform4f(uLineColor, 1f, 0.71f, 0.15f, 1f)
            for ((dx, dy) in OFFSETS) {
                glUniform4f(uLineXf, xf[0], xf[1], xf[2] + dx * nx, xf[3] + dy * ny)
                glDrawArrays(GL_LINES, 0, r.size / 2)
            }
            glUniform4fv(uLineXf, 1, xf, 0)
        }
        o.guides?.takeIf { it.size >= 4 }?.let { g ->
            // kesikli: 7 px çizgi, 5 px boşluk (ekran ölçüsünde)
            val dash = 7 * px
            val gap = 5 * px
            val out = ArrayList<Float>()
            var i = 0
            while (i + 3 < g.size) {
                val x1 = g[i].toDouble(); val y1 = g[i + 1].toDouble(); val x2 = g[i + 2].toDouble(); val y2 = g[i + 3].toDouble()
                val len = Math.hypot(x2 - x1, y2 - y1)
                if (len > 0) {
                    val ux = (x2 - x1) / len
                    val uy = (y2 - y1) / len
                    var t = 0.0
                    var n = 0
                    while (t < len && n < 2000) {
                        val e = minOf(t + dash, len)
                        out.add((x1 + ux * t).toFloat()); out.add((y1 + uy * t).toFloat())
                        out.add((x1 + ux * e).toFloat()); out.add((y1 + uy * e).toFloat())
                        t = e + gap
                        n++
                    }
                }
                i += 4
            }
            o.marks?.let { m ->
                var j = 0
                val r = 6 * px
                while (j + 1 < m.size) {
                    val x = m[j].toDouble(); val y = m[j + 1].toDouble()
                    out.add((x - r).toFloat()); out.add(y.toFloat()); out.add((x + r).toFloat()); out.add(y.toFloat())
                    out.add(x.toFloat()); out.add((y - r).toFloat()); out.add(x.toFloat()); out.add((y + r).toFloat())
                    j += 2
                }
            }
            if (out.isNotEmpty()) {
                val arr = out.toFloatArray()
                val bb = ByteBuffer.allocateDirect(arr.size * 4).order(ByteOrder.nativeOrder())
                bb.asFloatBuffer().put(arr)
                glBufferData(GL_ARRAY_BUFFER, bb.capacity(), bb, GL_STREAM_DRAW)
                ovSelUploaded = null
                glVertexAttribPointer(0, 2, GL_FLOAT, false, 8, 0)
                glUniform4f(uLineColor, 0.49f, 0.91f, 0.69f, 1f)
                for ((dx, dy) in OFFSETS) {
                    glUniform4f(uLineXf, xf[0], xf[1], xf[2] + dx * nx, xf[3] + dy * ny)
                    glDrawArrays(GL_LINES, 0, arr.size / 2)
                }
                glUniform4fv(uLineXf, 1, xf, 0)
            }
        }
        if (o.snapType > 0) {
            val x = o.snapX.toDouble()
            val y = o.snapY.toDouble()
            val r = 8 * px
            when (o.snapType) {
                2 -> { seg(x - r, y - r, x + r, y - r); seg(x + r, y - r, x, y + r); seg(x, y + r, x - r, y - r) } // üçgen: orta
                3 -> { // daire: merkez
                    for (i in 0 until 12) {
                        val a0 = i * Math.PI / 6
                        val a1 = (i + 1) * Math.PI / 6
                        seg(x + r * Math.cos(a0), y + r * Math.sin(a0), x + r * Math.cos(a1), y + r * Math.sin(a1))
                    }
                }
                4 -> { seg(x - r, y - r, x + r, y + r); seg(x - r, y + r, x + r, y - r) } // çarpı: ekleme noktası
                else -> { seg(x - r, y - r, x + r, y - r); seg(x + r, y - r, x + r, y + r); seg(x + r, y + r, x - r, y + r); seg(x - r, y + r, x - r, y - r) } // kare: uç
            }
            flush(0.24f, 1f, 0.56f)
        }
    }

    /** Çizgi gölgelendiricisine tam 2x3 dönüşüm ver (u_xf etkisiz kalır). */
    private fun setMatrix(a: Float, b: Float, c: Float, d: Float, e: Float, f: Float) {
        glUniform4f(uLineXf, 1f, 1f, e, f)
        glUniform4f(uLineLin, a, b, c, d)
    }

    private fun setColor(rgb: Int, a: Float) {
        val c = adapt(rgb)
        glUniform4f(uLineColor, ((c shr 16) and 0xFF) / 255f, ((c shr 8) and 0xFF) / 255f, (c and 0xFF) / 255f, a)
    }

    private fun upload(s: CadScene, m: TextMesh?) {
        release()
        val ids = IntArray(3)
        glGenBuffers(3, ids, 0)
        vbo = ids[0]; textVbo = ids[1]; textIbo = ids[2]

        val vb = ByteBuffer.allocateDirect(s.vertices.size * 4).order(ByteOrder.nativeOrder())
        vb.asFloatBuffer().put(s.vertices)
        glBindBuffer(GL_ARRAY_BUFFER, vbo)
        glBufferData(GL_ARRAY_BUFFER, vb.capacity(), vb, GL_STATIC_DRAW)

        if (m != null && m.quadCount > 0) {
            glBindBuffer(GL_ARRAY_BUFFER, textVbo)
            glBufferData(GL_ARRAY_BUFFER, m.vertexData.remaining(), m.vertexData, GL_STATIC_DRAW)
            glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, textIbo)
            glBufferData(GL_ELEMENT_ARRAY_BUFFER, m.indexData.remaining(), m.indexData, GL_STATIC_DRAW)
            glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, 0)

            if (m.atlas !== uploadedAtlas || atlasTex == 0) uploadAtlas(m.atlas)
        }
        scene = s
        mesh = m
    }

    private fun uploadAtlas(a: GlyphAtlas) {
        if (atlasTex != 0) glDeleteTextures(1, intArrayOf(atlasTex), 0)
        val t = IntArray(1)
        glGenTextures(1, t, 0)
        atlasTex = t[0]
        glBindTexture(GL_TEXTURE_2D, atlasTex)
        glPixelStorei(GL_UNPACK_ALIGNMENT, 1)
        // R8: her sürücüde mipmap üretilebilir ve süzülebilir (GL_ALPHA'nın aksine)
        val bmp = a.bitmap
        val px = ByteBuffer.allocateDirect(bmp.rowBytes * bmp.height).order(ByteOrder.nativeOrder())
        bmp.copyPixelsToBuffer(px)
        px.rewind()
        glTexImage2D(GL_TEXTURE_2D, 0, GL_R8, bmp.width, bmp.height, 0, GL_RED, GL_UNSIGNED_BYTE, px)
        glGenerateMipmap(GL_TEXTURE_2D)
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR)
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR)
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE)
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE)
        uploadedAtlas = a
    }

    /** Sahne tamponlarını bırakır; atlas dokusu yalnızca [withAtlas] ise silinir (düzenlemede yeniden kullanılır). */
    private fun release(withAtlas: Boolean = false) {
        val bufs = intArrayOf(vbo, textVbo, textIbo).filter { it != 0 }.toIntArray()
        if (bufs.isNotEmpty()) glDeleteBuffers(bufs.size, bufs, 0)
        vbo = 0; textVbo = 0; textIbo = 0
        if (withAtlas) {
            if (atlasTex != 0) glDeleteTextures(1, intArrayOf(atlasTex), 0)
            atlasTex = 0
            uploadedAtlas = null
        }
    }

    companion object {
        private val OFFSETS = arrayOf(0f to 0f, 1f to 0f, 0f to 1f)

        /** Koyu zeminde siyah çizgiler görünmez: beyaza çevir. */
        fun adapt(rgb: Int): Int {
            val c = rgb and 0xFFFFFF
            val r = (c shr 16) and 0xFF
            val g = (c shr 8) and 0xFF
            val b = c and 0xFF
            return if (r * 299 + g * 587 + b * 114 < 40_000) 0xFFFFFF else c
        }

        private fun shader(type: Int, src: String): Int {
            val s = glCreateShader(type)
            glShaderSource(s, src)
            glCompileShader(s)
            val ok = IntArray(1)
            glGetShaderiv(s, GL_COMPILE_STATUS, ok, 0)
            if (ok[0] == 0) throw IllegalStateException("Gölgelendirici derlenemedi: " + glGetShaderInfoLog(s))
            return s
        }

        private fun program(vs: String, fs: String): Int {
            val p = glCreateProgram()
            glAttachShader(p, shader(GL_VERTEX_SHADER, vs))
            glAttachShader(p, shader(GL_FRAGMENT_SHADER, fs))
            glLinkProgram(p)
            return p
        }

        private const val LINE_VS = """#version 300 es
layout(location = 0) in vec2 a_pos;
uniform vec4 u_xf;   // ölçek (xy) ve öteleme (zw)
uniform vec4 u_lin;  // ön dönüşüm: p' = (a·x + c·y, b·x + d·y); normalde birim
void main() {
  vec2 p = vec2(u_lin.x * a_pos.x + u_lin.z * a_pos.y, u_lin.y * a_pos.x + u_lin.w * a_pos.y);
  gl_Position = vec4(p * u_xf.xy + u_xf.zw, 0.0, 1.0);
}
"""
        private const val LINE_FS = """#version 300 es
precision mediump float;
uniform vec4 u_color;
out vec4 o;
void main() { o = u_color; }
"""
        // Ekranda 2 pikselden küçük yazılar kırpma alanının dışına atılır (okunamaz, yalnızca kirlilik).
        private const val TEXT_VS = """#version 300 es
layout(location = 0) in vec2 a_pos;
layout(location = 1) in vec2 a_uv;
layout(location = 2) in vec4 a_color;
layout(location = 3) in float a_h;
uniform vec4 u_xf;
uniform float u_px;
out vec2 v_uv;
out vec4 v_color;
void main() {
  v_uv = a_uv;
  v_color = a_color;
  gl_Position = a_h * u_px < 2.0 ? vec4(2.0, 2.0, 2.0, 1.0) : vec4(a_pos * u_xf.xy + u_xf.zw, 0.0, 1.0);
}
"""
        private const val TEXT_FS = """#version 300 es
precision mediump float;
uniform sampler2D u_tex;
in vec2 v_uv;
in vec4 v_color;
out vec4 o;
void main() { o = vec4(v_color.rgb, v_color.a * texture(u_tex, v_uv).r); }
"""
    }
}
