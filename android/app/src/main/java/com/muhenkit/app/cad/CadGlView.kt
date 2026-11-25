package com.muhenkit.app.cad

import android.animation.ValueAnimator
import android.annotation.SuppressLint
import android.content.Context
import android.os.Build
import android.view.HapticFeedbackConstants
import android.widget.Magnifier
import android.opengl.GLSurfaceView
import android.view.GestureDetector
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import android.view.animation.DecelerateInterpolator
import android.widget.OverScroller
import javax.microedition.khronos.egl.EGL10
import javax.microedition.khronos.egl.EGLConfig
import javax.microedition.khronos.egl.EGLDisplay
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow

/** Düzenleme araçlarının dokunma girdisi (göreli dünya koordinatları, [tol]: dokunma toleransı). */
interface EditInput {
    /** true: tek parmak sürüklemesi kaydırma yerine araca gider (çizgi, taşı, kopyala). */
    val capturesDrag: Boolean
    fun onTap(x: Double, y: Double, tol: Double) {}
    fun onPress(x: Double, y: Double, tol: Double) {}
    fun onDrag(x: Double, y: Double, tol: Double) {}
    fun onRelease(x: Double, y: Double, tol: Double) {}
    fun onCancel() {}
    /** Uzun basıp sürükleyerek pencere seçimi (yalnızca [capturesDrag] false iken). */
    fun onBox(x0: Double, y0: Double, x1: Double, y1: Double, done: Boolean) {}
}

/**
 * DXF çizim yüzeyi. Yalnızca görünüm değişince çizer (pil dostu); dokunma
 * hareketleri doğrudan görünüm dönüşümünü günceller.
 *
 * Görünüm alanları (AutoCAD gibi): tek görünüm ya da 2×2 dört görünüm. Dört bölme aynı çizimi kendi
 * kaydırma/yakınlaştırmasıyla gösterir; dokunulan bölme etkin olur ve hareket o bölmede sürer. Dört görünüm
 * [contentInsets] (üst çubuk, alt araçlar, sol ray) dışında kalan alana yerleşir; tek görünüm tüm yüzeyi kaplar.
 */
@SuppressLint("ViewConstructor")
class CadGlView(context: Context) : GLSurfaceView(context) {
    val renderer = CadRenderer()
    private var scene: CadScene? = null
    private val scroller = OverScroller(context)
    private var zoomAnim: ValueAnimator? = null

    /** Görünüm değişince çağrılır (ör. ölçek göstergesi için). */
    var onViewChanged: ((ViewState) -> Unit)? = null

    var editInput: EditInput? = null

    /** Dokunma toleransı: 16 dp'nin dünya karşılığı. */
    private fun tol(): Double = 16 * resources.displayMetrics.density / renderer.view.zoom

    // ───────────────────────── görünüm alanları ─────────────────────────

    /** Dört görünüm açık mı */
    var quad = false; private set
    /** Etkin bölme (0 sol üst, 1 sağ üst, 2 sol alt, 3 sağ alt) */
    val active: Int get() = renderer.active
    /** Bölme oranları (0,15…0,85) */
    var splitX = 0.5; private set
    var splitY = 0.5; private set
    /** Dört görünümün yerleşeceği alanı daraltan kenar boşlukları (piksel): sol, üst, sağ, alt */
    private var insets = IntArray(4)
    private val hasView = BooleanArray(4)
    /** Düzen değişti (dört/tek, etkin bölme, oran, boyut): arayüz bölme adlarını/çerçeveyi yeniden yerleştirir */
    var onLayoutChanged: (() -> Unit)? = null

    fun setContentInsets(left: Int, top: Int, right: Int, bottom: Int) {
        val n = intArrayOf(left, top, right, bottom)
        if (n.contentEquals(insets)) return
        insets = n
        pushLayout()
    }

    fun setQuad(on: Boolean) {
        if (quad == on) return
        quad = on
        pushLayout()
        fitMissing()
    }

    fun setActive(i: Int) {
        val n = i.coerceIn(0, 3)
        if (renderer.active == n) return
        renderer.active = n
        pushLayout()
    }

    fun setSplit(fx: Double, fy: Double) {
        splitX = fx.coerceIn(0.15, 0.85)
        splitY = fy.coerceIn(0.15, 0.85)
        pushLayout()
    }

    /** Bölmenin yüzeydeki dikdörtgeni (piksel, sol üst köken); tek görünümde etkin bölme tüm yüzey */
    fun paneRect(i: Int): android.graphics.Rect {
        if (!quad) return android.graphics.Rect(0, 0, width, height)
        val l = insets[0]; val t = insets[1]
        val r = (width - insets[2]).coerceAtLeast(l + 2); val b = (height - insets[3]).coerceAtLeast(t + 2)
        val mx = l + ((r - l) * splitX).toInt()
        val my = t + ((b - t) * splitY).toInt()
        val g = 1 // bölme çizgisi: 2 piksel
        return when (i) {
            0 -> android.graphics.Rect(l, t, mx - g, my - g)
            1 -> android.graphics.Rect(mx + g, t, r, my - g)
            2 -> android.graphics.Rect(l, my + g, mx - g, b)
            else -> android.graphics.Rect(mx + g, my + g, r, b)
        }
    }

    /** Dört görünümün tamamının kapladığı alan (bölme tutamağı için) */
    fun quadArea(): android.graphics.Rect =
        android.graphics.Rect(insets[0], insets[1], width - insets[2], height - insets[3])

    /** Bölme görünümleri (hiç ayarlanmamış bölme null) */
    fun paneViews(): List<ViewState?> = (0 until 4).map { if (hasView[it]) renderer.views[it] else null }

    fun setPaneView(i: Int, v: ViewState) {
        if (v.zoom <= 0 || !v.zoom.isFinite()) return
        renderer.views[i] = v
        hasView[i] = true
        requestRender()
    }

    private fun pushLayout() {
        renderer.panes = if (!quad) emptyArray()
        else Array(4) { i -> paneRect(i).let { r -> intArrayOf(i, r.left, r.top, r.width(), r.height()) } }
        requestRender()
        onLayoutChanged?.invoke()
    }

    /** Hiç görünümü olmayan görünür bölmeleri sığdır */
    private fun fitMissing() {
        val cur = renderer.active
        for (i in 0 until 4) if (!hasView[i] && (quad || i == cur)) fitPane(i)
    }

    private fun pw(): Int = paneRect(renderer.active).width().coerceAtLeast(1)
    private fun ph(): Int = paneRect(renderer.active).height().coerceAtLeast(1)

    /** Yüzey koordinatını etkin bölmenin yerel koordinatına çevir */
    private fun lx(x: Float) = x - paneRect(renderer.active).left
    private fun ly(y: Float) = y - paneRect(renderer.active).top

    /** Dokunulan noktadaki bölme (dört görünümde); yoksa etkin bölme */
    private fun paneAt(x: Float, y: Float): Int {
        if (!quad) return renderer.active
        for (i in 0 until 4) if (paneRect(i).contains(x.toInt(), y.toInt())) return i
        return renderer.active
    }

    init {
        setEGLContextClientVersion(3)
        setEGLConfigChooser(MsaaConfigChooser())
        preserveEGLContextOnPause = true
        setRenderer(renderer)
        renderMode = RENDERMODE_WHEN_DIRTY
    }

    /** [initial]: projeden geri yüklenen görünüm; yoksa tüm çizim sığdırılır */
    fun setScene(s: CadScene, mesh: TextMesh?, initial: ViewState? = null) {
        scene = s
        renderer.setScene(s, mesh)
        hasView.fill(false)
        if (initial != null && initial.zoom > 0 && initial.zoom.isFinite()) setView(initial)
        else fit()
        fitMissing()
    }

    /** Boş çizimde sığdırılacak bir şey yok: 10 m × 7 m'lik (cm) bir pencere göster */
    private fun emptyWindow(): Boolean {
        val s = scene ?: return false
        return s.entityCount == 0
    }

    /** Düzenlemeden sonra: görünüm korunur. */
    fun updateScene(s: CadScene, mesh: TextMesh?) {
        scene = s
        renderer.setScene(s, mesh)
        requestRender()
    }

    /** Ekranda görünen dünya penceresi (göreli koordinat) */
    fun viewWindow(): android.graphics.RectF {
        val v = renderer.view
        val hw = pw() / 2.0 / v.zoom
        val hh = ph() / 2.0 / v.zoom
        return android.graphics.RectF((v.cx - hw).toFloat(), (v.cy - hh).toFloat(), (v.cx + hw).toFloat(), (v.cy + hh).toFloat())
    }

    fun setOverlay(o: Overlay) {
        renderer.overlay = o
        requestRender()
    }

    fun setHidden(h: Set<Int>) {
        renderer.hidden = h
        requestRender()
    }

    /** Tüm çizimi etkin bölmeye sığdır. */
    fun fit(animated: Boolean = false) {
        if (width == 0 || height == 0) {
            post { fit(animated) }
            return
        }
        val target = fitView(renderer.active) ?: return
        if (animated) animateTo(target) else setView(target)
    }

    private fun fitView(i: Int): ViewState? {
        val s = scene ?: return null
        val r = paneRect(i)
        if (r.width() <= 0 || r.height() <= 0) return null
        val empty = emptyWindow()
        val w = if (empty) 1000.0 else max(s.maxX - s.minX, 1e-6f).toDouble()
        val h = if (empty) 700.0 else max(s.maxY - s.minY, 1e-6f).toDouble()
        val z = min(r.width() / w, r.height() / h) * 0.92
        return if (empty) ViewState(500.0, 350.0, z) else ViewState((s.minX + s.maxX) / 2.0, (s.minY + s.maxY) / 2.0, z)
    }

    private fun fitPane(i: Int) {
        val v = fitView(i) ?: return
        setPaneView(i, v)
        onViewChanged?.invoke(v)
    }

    /** Nokta ekranın dışındaysa ortala (yakınlaştırma aynı kalır) */
    fun reveal(x: Double, y: Double) {
        val w = viewWindow()
        if (x >= w.left && x <= w.right && y >= w.top && y <= w.bottom) return
        animateTo(ViewState(x, y, renderer.view.zoom))
    }

    private fun setView(v: ViewState) {
        renderer.view = v
        hasView[renderer.active] = true
        requestRender()
        onViewChanged?.invoke(v)
    }

    /** [px],[py]: etkin bölmenin yerel pikseli */
    private fun toWorld(px: Float, py: Float, v: ViewState = renderer.view): Pair<Double, Double> =
        (v.cx + (px - pw() / 2.0) / v.zoom) to (v.cy - (py - ph() / 2.0) / v.zoom)

    /** (px,py) ekran noktası sabit kalacak şekilde yakınlaştır. */
    private fun zoomAt(px: Float, py: Float, factor: Double) {
        val v = renderer.view
        val nz = (v.zoom * factor).coerceIn(minZoom(), 1e7)
        val (wx, wy) = toWorld(px, py, v)
        setView(ViewState(wx - (px - pw() / 2.0) / nz, wy + (py - ph() / 2.0) / nz, nz))
    }

    private fun panBy(dx: Float, dy: Float) {
        val v = renderer.view
        setView(ViewState(v.cx - dx / v.zoom, v.cy + dy / v.zoom, v.zoom))
    }

    private fun minZoom(): Double {
        val s = scene ?: return 1e-9
        val ext = max(s.maxX - s.minX, s.maxY - s.minY).toDouble().coerceAtLeast(1e-6)
        return min(pw(), ph()) / ext * 0.02
    }

    private fun animateTo(target: ViewState) {
        zoomAnim?.cancel()
        val from = renderer.view
        zoomAnim = ValueAnimator.ofFloat(0f, 1f).apply {
            duration = 260
            interpolator = DecelerateInterpolator(1.6f)
            addUpdateListener {
                val t = (it.animatedValue as Float).toDouble()
                // yakınlaştırmayı logaritmik ara değerle: göz için doğrusal hissettirir
                val z = from.zoom * (target.zoom / from.zoom).pow(t)
                setView(ViewState(from.cx + (target.cx - from.cx) * t, from.cy + (target.cy - from.cy) * t, z))
            }
            start()
        }
    }

    // ───────────────────────── dokunma ─────────────────────────

    private var lastFocusX = 0f
    private var lastFocusY = 0f

    private val scaleDetector = ScaleGestureDetector(context, object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
        override fun onScaleBegin(d: ScaleGestureDetector): Boolean {
            lastFocusX = d.focusX
            lastFocusY = d.focusY
            return true
        }

        override fun onScale(d: ScaleGestureDetector): Boolean {
            // iki parmakla aynı anda kaydır + yakınlaştır
            panBy(d.focusX - lastFocusX, d.focusY - lastFocusY)
            lastFocusX = d.focusX
            lastFocusY = d.focusY
            zoomAt(lx(d.focusX), ly(d.focusY), d.scaleFactor.toDouble())
            return true
        }
    }).apply { isQuickScaleEnabled = true }

    private val gestureDetector = GestureDetector(context, object : GestureDetector.SimpleOnGestureListener() {
        override fun onDown(e: MotionEvent): Boolean {
            scroller.forceFinished(true)
            zoomAnim?.cancel()
            return true
        }

        override fun onScroll(e1: MotionEvent?, e2: MotionEvent, dx: Float, dy: Float): Boolean {
            if (scaleDetector.isInProgress || e2.pointerCount > 1) return true
            panBy(-dx, -dy)
            return true
        }

        override fun onFling(e1: MotionEvent?, e2: MotionEvent, vx: Float, vy: Float): Boolean {
            if (e2.pointerCount > 1) return true
            scroller.fling(0, 0, vx.toInt(), vy.toInt(), Int.MIN_VALUE, Int.MAX_VALUE, Int.MIN_VALUE, Int.MAX_VALUE)
            lastFlingX = 0
            lastFlingY = 0
            postOnAnimation(flingStep)
            return true
        }

        override fun onSingleTapUp(e: MotionEvent): Boolean {
            val (x, y) = toWorld(lx(e.x), ly(e.y))
            editInput?.onTap(x, y, tol())
            return true
        }

        override fun onLongPress(e: MotionEvent) {
            if (editInput == null || scaleDetector.isInProgress) return
            boxStart = toWorld(lx(e.x), ly(e.y))
            performHapticFeedback(HapticFeedbackConstants.LONG_PRESS)
            val (x, y) = boxStart!!
            editInput?.onBox(x, y, x, y, false)
        }

        override fun onDoubleTap(e: MotionEvent): Boolean {
            val v = renderer.view
            val nz = (v.zoom * 2.5).coerceAtMost(1e7)
            val ex = lx(e.x); val ey = ly(e.y)
            val (wx, wy) = toWorld(ex, ey)
            animateTo(ViewState(wx - (ex - pw() / 2.0) / nz, wy + (ey - ph() / 2.0) / nz, nz))
            return true
        }
    })

    private var lastFlingX = 0
    private var lastFlingY = 0
    private val flingStep = object : Runnable {
        override fun run() {
            if (!scroller.computeScrollOffset()) return
            val x = scroller.currX
            val y = scroller.currY
            panBy((x - lastFlingX).toFloat(), (y - lastFlingY).toFloat())
            lastFlingX = x
            lastFlingY = y
            postOnAnimation(this)
        }
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        // dört görünümde dokunulan bölme etkin olur; hareket bitene kadar o bölmede kalır
        if (e.actionMasked == MotionEvent.ACTION_DOWN && quad) {
            val p = paneAt(e.x, e.y)
            if (p != renderer.active) {
                zoomAnim?.cancel()
                scroller.forceFinished(true)
                setActive(p)
            }
        }
        scaleDetector.onTouchEvent(e)
        val input = editInput
        val start = boxStart
        if (start != null) {
            // pencere seçimi sürüyor
            val (x, y) = toWorld(lx(e.x), ly(e.y))
            when (e.actionMasked) {
                MotionEvent.ACTION_MOVE -> input?.onBox(start.first, start.second, x, y, false)
                MotionEvent.ACTION_UP -> {
                    boxStart = null
                    input?.onBox(start.first, start.second, x, y, true)
                }
                MotionEvent.ACTION_CANCEL, MotionEvent.ACTION_POINTER_DOWN -> {
                    boxStart = null
                    input?.onCancel()
                }
            }
            if (e.actionMasked != MotionEvent.ACTION_POINTER_DOWN) return true
        }
        if (input == null || !input.capturesDrag) {
            // araç değiştiyse açık kalmış büyüteci kapat
            if (e.actionMasked == MotionEvent.ACTION_DOWN) hideLoupe()
            dragging = false
            gestureDetector.onTouchEvent(e)
            return true
        }
        // araç kipi: tek parmak araca, iki parmak kaydır/yakınlaştır
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                scroller.forceFinished(true)
                zoomAnim?.cancel()
                dragging = true
                val (x, y) = toWorld(lx(e.x), ly(e.y))
                input.onPress(x, y, tol())
                showLoupe(e)
            }
            MotionEvent.ACTION_POINTER_DOWN -> {
                hideLoupe()
                if (dragging) {
                    dragging = false
                    input.onCancel()
                }
            }
            MotionEvent.ACTION_MOVE -> if (dragging && e.pointerCount == 1) {
                val (x, y) = toWorld(lx(e.x), ly(e.y))
                input.onDrag(x, y, tol())
                showLoupe(e)
            }
            MotionEvent.ACTION_UP -> {
                hideLoupe()
                if (dragging) {
                    dragging = false
                    val (x, y) = toWorld(lx(e.x), ly(e.y))
                    input.onRelease(x, y, tol())
                }
            }
            MotionEvent.ACTION_CANCEL -> {
                hideLoupe()
                if (dragging) {
                    dragging = false
                    input.onCancel()
                }
            }
        }
        return true
    }

    private var dragging = false
    private var boxStart: Pair<Double, Double>? = null

    /** Parmağın altını parmağın üstünde büyütür (Android 10+; SurfaceView içeriğini kopyalar). */
    private val loupe: Magnifier? by lazy {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) return@lazy null
        val d = resources.displayMetrics.density
        Magnifier.Builder(this)
            .setSize((120 * d).toInt(), (120 * d).toInt())
            .setCornerRadius(60 * d)
            .setInitialZoom(2.5f)
            .setDefaultSourceToMagnifierOffset(0, (-110 * d).toInt())
            .build()
    }

    /**
     * Doğrudan göster: ertelenmiş (post) gösterim parmak kalktıktan sonra çalışıp büyüteci
     * ekranda asılı bırakıyordu. Konum hemen okunur (MotionEvent sonradan geri dönüştürülür).
     */
    private fun showLoupe(e: MotionEvent) {
        val m = loupe ?: return
        if (!dragging) return
        m.show(e.x, e.y)
    }

    fun hideLoupe() {
        loupe?.dismiss()
    }

    override fun onDetachedFromWindow() {
        hideLoupe()
        super.onDetachedFromWindow()
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        pushLayout()
        if (oldw == 0 && oldh == 0) fit() else requestRender()
        fitMissing()
    }

    /** 4× MSAA + 8 bit şablon; desteklenmezse MSAA'sız. */
    private class MsaaConfigChooser : EGLConfigChooser {
        override fun chooseConfig(egl: EGL10, display: EGLDisplay): EGLConfig {
            val base = intArrayOf(
                EGL10.EGL_RED_SIZE, 8, EGL10.EGL_GREEN_SIZE, 8, EGL10.EGL_BLUE_SIZE, 8, EGL10.EGL_ALPHA_SIZE, 8,
                EGL10.EGL_STENCIL_SIZE, 8, EGL10.EGL_RENDERABLE_TYPE, 0x40, // EGL_OPENGL_ES3_BIT_KHR
            )
            val msaa = base + intArrayOf(EGL10.EGL_SAMPLE_BUFFERS, 1, EGL10.EGL_SAMPLES, 4, EGL10.EGL_NONE)
            val plain = base + intArrayOf(EGL10.EGL_NONE)
            for (attrs in listOf(msaa, plain)) {
                val configs = arrayOfNulls<EGLConfig>(1)
                val n = IntArray(1)
                if (egl.eglChooseConfig(display, attrs, configs, 1, n) && n[0] > 0) return configs[0]!!
            }
            throw IllegalStateException("Uygun EGL yapılandırması yok")
        }
    }
}
