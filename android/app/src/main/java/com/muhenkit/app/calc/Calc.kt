package com.muhenkit.app.calc

import java.util.Locale

internal object CalcNative {
    init {
        System.loadLibrary("muhenkit")
    }

    external fun calc(module: ByteArray, input: ByteArray): ByteArray
    external fun table(name: ByteArray): ByteArray
}

/** Hesap sonucu: "anahtar=değer" satırları */
class CalcResult(private val kv: Map<String, String>) {
    operator fun get(k: String): String = kv[k].orEmpty()
    fun d(k: String): Double = kv[k]?.toDoubleOrNull() ?: Double.NaN
    fun i(k: String): Int = kv[k]?.toDoubleOrNull()?.toInt() ?: 0
    fun b(k: String): Boolean = kv[k] == "1"
    val error: String? get() = kv["error"]
}

/**
 * C++ hesap çekirdeği (core/mkcalc). Android, Windows, iOS ve macOS aynı formülleri çalıştırır;
 * eski uygulamanın formülleriyle rastgele girdilerde birebir karşılaştırılmıştır (tools/check.py).
 */
object Calc {
    fun run(module: String, input: Map<String, Any?>): CalcResult {
        val text = input.entries.joinToString("\n") { (k, v) ->
            val s = when (v) {
                null -> ""
                is Boolean -> if (v) "1" else "0"
                is Double -> if (v.isFinite()) "%.10g".format(Locale.ROOT, v) else ""
                else -> v.toString().replace(',', '.')
            }
            "$k=$s"
        }
        val out = String(CalcNative.calc(module.toByteArray(), text.toByteArray()), Charsets.UTF_8)
        val kv = LinkedHashMap<String, String>()
        for (line in out.lineSequence()) {
            val i = line.indexOf('=')
            if (i > 0) kv[line.substring(0, i)] = line.substring(i + 1)
        }
        return CalcResult(kv)
    }

    /** Sekme ayrılmış tablo: ilk satır başlıklar */
    fun table(name: String): List<List<String>> =
        String(CalcNative.table(name.toByteArray()), Charsets.UTF_8).lines().filter { it.isNotEmpty() }.map { it.split('\t') }

    /** Ad → değer tablosu (soiling, luminaires, conductors, lp_c1…) */
    fun named(name: String): List<Pair<String, Double>> = table(name).drop(1).map { it[0] to (it.getOrNull(1)?.toDoubleOrNull() ?: 0.0) }
}

/** Türkçe sayı biçimi: ondalık virgül, yalnız ondalığın sondaki sıfırları atılır */
fun fmt(v: Double, digits: Int = 2): String {
    if (!v.isFinite()) return "—"
    var s = "%.${digits}f".format(Locale.ROOT, v)
    if ('.' in s) s = s.trimEnd('0').trimEnd('.')
    return s.replace('.', ',')
}
