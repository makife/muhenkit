package com.muhenkit.app.cad

/** mkcad C++ çekirdeğine JNI bağlantısı (app/src/main/cpp/jni_bridge.cpp). */
internal object MkNative {
    init {
        System.loadLibrary("muhenkit")
    }

    external fun open(data: ByteArray): Long
    external fun lastError(): ByteArray
    external fun close(doc: Long)
    external fun buildScene(doc: Long): Int
    external fun info(doc: Long): ByteArray
    external fun extents(doc: Long): FloatArray
    external fun vertices(doc: Long): FloatArray
    external fun batches(doc: Long): IntArray
    external fun polys(doc: Long): IntArray
    external fun textFloats(doc: Long): FloatArray
    external fun textInts(doc: Long): IntArray
    external fun textPool(doc: Long): ByteArray
    external fun layerNames(doc: Long): ByteArray
    external fun layerInts(doc: Long): IntArray

    // düzenleme
    external fun pick(doc: Long, x: Double, y: Double, tol: Double): Int
    external fun selectRect(doc: Long, x0: Double, y0: Double, x1: Double, y1: Double, crossing: Boolean): IntArray
    external fun snap(doc: Long, x: Double, y: Double, tol: Double): DoubleArray
    external fun geometry(doc: Long, id: Int): FloatArray
    external fun entityInfo(doc: Long, id: Int): ByteArray
    external fun move(doc: Long, ids: IntArray, dx: Double, dy: Double): Int
    external fun copy(doc: Long, ids: IntArray, dx: Double, dy: Double): Int
    external fun remove(doc: Long, ids: IntArray): Int
    external fun addLine(doc: Long, layer: ByteArray, x1: Double, y1: Double, x2: Double, y2: Double): Int
    external fun addText(doc: Long, layer: ByteArray, x: Double, y: Double, h: Double, rot: Double, text: ByteArray): Int
    external fun undo(doc: Long): Int
    external fun redo(doc: Long): Int
    external fun undoCount(doc: Long): Int
    external fun redoCount(doc: Long): Int
    external fun isDirty(doc: Long): Int
    external fun save(doc: Long): ByteArray

    // dönüşüm ve özellikler
    external fun rotate(doc: Long, ids: IntArray, cx: Double, cy: Double, deg: Double): Int
    external fun scale(doc: Long, ids: IntArray, cx: Double, cy: Double, k: Double): Int
    external fun mirror(doc: Long, ids: IntArray, x1: Double, y1: Double, x2: Double, y2: Double, keep: Boolean): Int
    external fun entityText(doc: Long, id: Int): ByteArray
    external fun setText(doc: Long, id: Int, text: ByteArray): Int
    external fun setLayer(doc: Long, ids: IntArray, layer: ByteArray): Int
    external fun setColor(doc: Long, ids: IntArray, aci: Int): Int

    // çizim
    external fun addCircle(doc: Long, layer: ByteArray, cx: Double, cy: Double, r: Double): Int
    external fun addPolyline(doc: Long, layer: ByteArray, xy: DoubleArray, closed: Boolean): Int
    external fun offset(doc: Long, id: Int, d: Double, px: Double, py: Double): Int

    // semboller
    external fun defineBlock(doc: Long, name: ByteArray, snippet: ByteArray): Int
    external fun insertBlock(doc: Long, layer: ByteArray, name: ByteArray, x: Double, y: Double, s: Double, rot: Double): Int
    external fun insertCounts(doc: Long): ByteArray

    // grupla / çöz
    external fun group(doc: Long, ids: IntArray): Int
    external fun explode(doc: Long, ids: IntArray): IntArray

    // proje otomatik kaydı
    external fun saveCopy(doc: Long): ByteArray
    external fun setDirty(doc: Long, dirty: Boolean)
}
