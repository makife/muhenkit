package com.muhenkit.app.ui.modules

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.dp
import com.muhenkit.app.calc.Calc
import com.muhenkit.app.calc.fmt

// ───────────────────────── Aydınlatma (verim metodu) ─────────────────────────

@Composable
fun LightingModule(onBack: () -> Unit) {
    var a by remember { mutableStateOf("6") }
    var b by remember { mutableStateOf("4") }
    var hRoom by remember { mutableStateOf("3") }
    var work by remember { mutableStateOf("0.85") }
    var susp by remember { mutableStateOf("0") }
    var lux by remember { mutableStateOf("300") }
    var combo by remember { mutableIntStateOf(0) }
    var soil by remember { mutableDoubleStateOf(1.25) }
    var flux by remember { mutableStateOf("3600") }
    val combos = remember { Calc.table("combos").drop(1).map { it[0] } }
    val soiling = remember { Calc.named("soiling") }
    val lums = remember { Calc.named("luminaires") }
    val r = Calc.run("lighting", mapOf("a" to a, "b" to b, "H" to hRoom, "work" to work, "susp" to susp, "lux" to lux,
        "combo" to combo, "soiling" to soil, "flux" to flux))
    ModuleScaffold("Aydınlatma", "Verim metodu: armatür sayısı, oda indeksi ve yerleşim", onBack) {
        item {
            Section("Mahal") {
                Fields({ NumField("En a", a, { a = it }, it, "m") }, { NumField("Boy b", b, { b = it }, it, "m") }, { NumField("Yükseklik H", hRoom, { hRoom = it }, it, "m") })
                Fields({ NumField("Çalışma düzlemi", work, { work = it }, it, "m") }, { NumField("Sarkıt", susp, { susp = it }, it, "m") }, { NumField("Aydınlık E", lux, { lux = it }, it, "lx") })
                Choice("Tavan-duvar-yer yansıtma", combos.indices.toList(), combo, { combos[it] }) { combo = it }
                NamedChoice("Kirlenme (bakım) faktörü", soiling, soil) { soil = it }
            }
        }
        item {
            Section("Armatür") {
                NamedChoice("Hazır armatür", lums, flux.num() ?: 0.0) { flux = it.field() }
                NumField("Armatür ışık akısı Ø", flux, { flux = it }, Modifier.fillMaxWidth(), "lm")
            }
        }
        item {
            Section("Sonuç") {
                ResultRow("Hesap yüksekliği h = H − düzlem − sarkıt", fmt(r.d("h")), "m")
                ResultRow("Oda indeksi k = a·b / h·(a+b)", fmt(r.d("k")))
                ResultRow("Verim η (tablodan)", fmt(r.d("eta"), 3))
                ResultRow("Gerekli toplam akı Φ = E·A·d / η", fmt(r.d("total_flux"), 0), "lm")
                ResultRow("Hesaplanan armatür", fmt(r.d("exact_count")))
                ResultRow("Kullanılacak armatür", "${r.i("count")}  (${r.i("cols")} × ${r.i("rows")})", big = true)
                ResultRow("Elde edilen aydınlık", fmt(r.d("achieved_lux"), 0), "lx", ok = r.d("achieved_lux") >= (lux.num() ?: 0.0))
            }
        }
        item {
            Section("Yerleşim") {
                val aa = a.num() ?: 1.0
                val bb = b.num() ?: 1.0
                val cols = r.i("cols").coerceAtLeast(1)
                val rows = r.i("rows").coerceAtLeast(1)
                val primary = MaterialTheme.colorScheme.primary
                Canvas(Modifier.fillMaxWidth().height(220.dp)) {
                    val k = minOf(size.width / aa.toFloat(), size.height / bb.toFloat()) * 0.9f
                    val w = aa.toFloat() * k
                    val h = bb.toFloat() * k
                    val ox = (size.width - w) / 2
                    val oy = (size.height - h) / 2
                    drawRect(Color(0xFFEDEADF), Offset(ox, oy), Size(w, h), style = Stroke(2f))
                    // armatürler: her hücrenin ortasında (eşit aralık; kenarlara yarım aralık)
                    for (c in 0 until cols) for (rr in 0 until rows) {
                        val cx = ox + w * (c + 0.5f) / cols
                        val cy = oy + h * (rr + 0.5f) / rows
                        drawCircle(primary, radius = minOf(w / cols, h / rows) * 0.18f, center = Offset(cx, cy), style = Stroke(3f))
                        drawLine(primary, Offset(cx - 6, cy - 6), Offset(cx + 6, cy + 6), 2f)
                        drawLine(primary, Offset(cx - 6, cy + 6), Offset(cx + 6, cy - 6), 2f)
                    }
                }
                Note("Armatürler aralarında eşit, duvara yarım aralık olacak şekilde yerleştirilir: ${fmt((a.num() ?: 0.0) / cols)} m × ${fmt((b.num() ?: 0.0) / rows)} m.")
            }
        }
        item { Section("Armatür yüksekliği") { AssetImage("img/armatur_yukseklik.jpg") } }
    }
}

// ───────────────────────── Gerilim düşümü ─────────────────────────

private val SECTIONS = listOf(1.5, 2.5, 4.0, 6.0, 10.0, 16.0, 25.0, 35.0, 50.0, 70.0, 95.0, 120.0, 150.0, 185.0, 240.0, 300.0)

@Composable
fun VoltageDropModule(onBack: () -> Unit) {
    var three by remember { mutableStateOf(true) }
    var cu by remember { mutableStateOf(true) }
    var p by remember { mutableStateOf("11008") }
    var l by remember { mutableStateOf("30") }
    var s by remember { mutableDoubleStateOf(10.0) }
    var limit by remember { mutableStateOf("1.5") }
    var cos by remember { mutableStateOf("0.8") }
    val r = Calc.run("voltage_drop", mapOf("three" to three, "k" to if (cu) 56 else 35, "p" to p, "l" to l, "s" to s, "limit" to limit, "cos" to cos))
    ModuleScaffold("Gerilim Düşümü", "Bir / üç faz %e, kesit önerisi ve ısınma kontrolü", onBack) {
        item {
            Section("Hat") {
                Choice(null, listOf(true, false), three, { if (it) "Üç faz 380 V" else "Bir faz 220 V" }) { three = it }
                Choice(null, listOf(true, false), cu, { if (it) "Bakır (k=56)" else "Alüminyum (k=35)" }) { cu = it }
                Fields({ NumField("Güç P", p, { p = it }, it, "W") }, { NumField("Uzunluk L", l, { l = it }, it, "m") })
                Fields({ NumField("%e sınırı", limit, { limit = it }, it, "%") }, { NumField("cos φ", cos, { cos = it }, it) })
                Choice("Kesit (mm²)", SECTIONS, s, { fmt(it, 1) }) { s = it }
            }
        }
        item {
            Section("Sonuç") {
                ResultRow("Gerilim düşümü %e", fmt(r.d("e"), 3), "%", ok = r.b("drop_ok"), big = true)
                ResultRow("ΔU", fmt(r.d("dv")), "V")
                ResultRow("Akım", fmt(r.d("i")), "A")
                ResultRow("Sigorta", r["fuse"], "A")
                ResultRow("Isınma (kablo ≥ sigorta)", if (r["selected.name"].isEmpty()) "tabloda yok" else "${r["selected.name"]}: ${fmt(r.d("selected.ampacity"), 0)} A",
                    ok = if (r["selected.name"].isEmpty()) null else r.b("heat_ok"))
                ResultRow("Sınır için en az kesit", fmt(r.d("s_min")), "mm²")
                ResultRow("Önerilen kablo", r["suggested.name"], big = true)
            }
        }
    }
}

// ───────────────────────── Eşzamanlılık ─────────────────────────

@Composable
fun DiversityModule(onBack: () -> Unit) {
    var flatW by remember { mutableStateOf("16000") }
    var flats by remember { mutableStateOf("12") }
    var common by remember { mutableStateOf("6000") }
    var cf by remember { mutableStateOf("1") }
    var cos by remember { mutableStateOf("0.8") }
    val r = Calc.run("diversity", mapOf("flat_w" to flatW, "flats" to flats, "common" to common, "common_factor" to cf, "cos" to cos))
    val table = remember { Calc.table("diversity").drop(1) }
    ModuleScaffold("Eşzamanlılık", "Daire %60/%40 kuralı ve bina eşzamanlılık katsayısı", onBack) {
        item {
            Section("Bina") {
                Fields({ NumField("Daire kurulu gücü", flatW, { flatW = it }, it, "W") }, { NumField("Daire sayısı", flats, { flats = it }, it) })
                Fields({ NumField("Ortak alan gücü", common, { common = it }, it, "W") }, { NumField("Ortak alan faktörü", cf, { cf = it }, it) })
                NumField("cos φ", cos, { cos = it }, Modifier.fillMaxWidth())
                Note("Ortak alan: merdiven, asansör, hidrofor vb.")
            }
        }
        item {
            Section("Sonuç") {
                ResultRow("Daire talep gücü (8 kW'a kadar %60, üstü %40)", fmt(r.d("per_flat"), 0), "W")
                ResultRow("Bina eşzamanlılık katsayısı", "%${r["factor"]}")
                ResultRow("Dairelerin talep gücü", fmt(r.d("flats_demand"), 0), "W")
                ResultRow("Ortak alan talep gücü", fmt(r.d("common_demand"), 0), "W")
                ResultRow("Toplam talep gücü", fmt(r.d("total"), 0), "W", big = true)
                ResultRow("Akım", fmt(r.d("i")), "A")
                ResultRow("Ana sigorta", r["fuse"], "A", big = true)
            }
        }
        item {
            Section("Bina eşzamanlılık tablosu") {
                Note("Konutlarda bir dairenin eşzamanlı yükü: kurulu gücün 8 kW'a kadar olan bölümü %60, kalan bölümü %40.")
                Note("Binanın eşzamanlı yükü için daire sayısına göre katsayı:")
                SimpleTable(listOf("Daire sayısı", "Katsayı"), table.map { listOf(if (it[1] == "99999") "${it[0]} ve üstü" else "${it[0]} – ${it[1]}", "%${it[2]}") }, r.i("row"))
            }
        }
    }
}

// ───────────────────────── Kablo seçimi ─────────────────────────

@Composable
fun CableModule(onBack: () -> Unit) {
    var amps by remember { mutableStateOf("45") }
    var mode by remember { mutableIntStateOf(0) }
    var factor by remember { mutableStateOf("1") }
    val r = Calc.run("cable", mapOf("amps" to amps, "mode" to mode, "factor" to factor))
    val cables = remember { Calc.table("cables").drop(1) }
    ModuleScaffold("Kablo Seçimi", "NYY akım taşıma tablolarına göre sigortaya uygun kesit", onBack) {
        item {
            Section("Girdi") {
                NumField("Yük akımı", amps, { amps = it }, Modifier.fillMaxWidth(), "A")
                Choice("Döşeme", listOf(0, 1, 2), mode, { listOf("Hesap (ATK)", "Toprakta", "Havada")[it] }) { mode = it }
                NumField("Düzeltme katsayısı (sıcaklık, gruplama)", factor, { factor = it }, Modifier.fillMaxWidth())
            }
        }
        item {
            Section("Sonuç") {
                ResultRow("Sigorta", r["fuse"], "A")
                ResultRow("Seçilen kablo", r["cable.name"].ifEmpty { "tabloda yok" }, big = true, ok = r["cable.name"].isNotEmpty())
                if (r["cable.name"].isNotEmpty()) {
                    ResultRow("Faz kesiti", fmt(r.d("cable.section"), 1), "mm²")
                    ResultRow("R / X", "${fmt(r.d("cable.r"), 4)} / ${fmt(r.d("cable.x"), 3)}", "Ω/km")
                }
            }
        }
        item {
            Section("Hesap tablosu (ATK)") {
                SimpleTable(listOf("Kablo", "Hesap A", "Toprak A", "Hava A", "R Ω/km"),
                    cables.map { listOf(it[0], it[2], it[3], it[4], it[5]) }, r.i("index"), listOf(1.4f, 1f, 1f, 1f, 1f))
            }
        }
        item {
            Section("NYY (YVV) 0,6/1 kV kablo kataloğu · TS 212") {
                val nyy = remember { Calc.table("nyy").drop(1) }
                SimpleTable(listOf("Kesit mm²", "R Ω/km", "Toprak A", "Hava A", "kg/km"),
                    nyy.map { listOf(it[0], it[1], it[2], it[3], it[4]) }, weights = listOf(1.4f, 1f, 1f, 1f, 1f))
            }
        }
    }
}

// ───────────────────────── Motor & TMŞ ─────────────────────────

@Composable
fun MotorModule(onBack: () -> Unit) {
    var kw by remember { mutableStateOf("7.5") }
    var cos by remember { mutableStateOf("0.85") }
    var eff by remember { mutableStateOf("0.88") }
    val r = Calc.run("motor", mapOf("kw" to kw, "cos" to cos, "eff" to eff))
    val table = remember { Calc.table("motor").drop(1) }
    ModuleScaffold("Motor & TMŞ", "Motor gücüne göre akım, termik röle, sigorta ve kesit (Tablo 1.1)", onBack) {
        item {
            Section("Motor") {
                NumField("Motor gücü", kw, { kw = it }, Modifier.fillMaxWidth(), "kW")
                Fields({ NumField("cos φ", cos, { cos = it }, it) }, { NumField("Verim η", eff, { eff = it }, it) })
            }
        }
        item {
            Section("Sonuç (tablodan ${r["kw"]} kW / ${r["hp"]} HP)") {
                ResultRow("Hesaplanan akım", fmt(r.d("i_calc")), "A")
                ResultRow("Tablo akımı", fmt(r.d("amps")), "A")
                ResultRow("Termik röle ayar aralığı", r["termik"], "A", big = true)
                ResultRow("Buşonlu sigorta (normal)", r["buson_normal"], "A")
                ResultRow("Buşonlu sigorta (gecikmeli)", r["buson_gecikmeli"], "A")
                ResultRow("NH sigorta", r["nh"], "A")
                ResultRow("İletken kesiti", fmt(r.d("kesit"), 1), "mm²", big = true)
            }
        }
        item {
            Section("Tablo 1.1 · Motor gücüne göre termik röle ve sigorta seçimi") {
                SimpleTable(
                    listOf("kW", "HP", "Akım A", "mm²", "Termik A", "Buşon N", "Buşon G", "NH A"),
                    table.map { listOf(it[0], it[1], it[2], it[3], it[4], it[5], it[6], it[7]) },
                    r.i("row"), listOf(0.8f, 0.8f, 0.9f, 0.7f, 1.3f, 1f, 1f, 1.1f),
                )
                Note("Buşon N: buşonlu normal · Buşon G: buşonlu gecikmeli · NH: bıçaklı sigorta")
            }
        }
    }
}
