package com.muhenkit.app.ui.modules

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.muhenkit.app.calc.Calc
import com.muhenkit.app.calc.fmt

// ───────────────────────── Topraklama ─────────────────────────

@Composable
fun GroundingModule(onBack: () -> Unit) {
    var rho by remember { mutableStateOf("100") }
    var useStrip by remember { mutableStateOf(true) }
    var stripL by remember { mutableStateOf("106") }
    var stripD by remember { mutableDoubleStateOf(0.008) }
    var stripH by remember { mutableStateOf("1") }
    var useRod by remember { mutableStateOf(true) }
    var rodL by remember { mutableStateOf("1.75") }
    var rodD by remember { mutableStateOf("0.02") }
    var rodN by remember { mutableStateOf("8") }
    var useFound by remember { mutableStateOf(true) }
    var fa by remember { mutableStateOf("17") }
    var fb by remember { mutableStateOf("28") }
    var useMesh by remember { mutableStateOf(false) }
    var meshL by remember { mutableStateOf("170") }
    var ia by remember { mutableStateOf("25") }
    var ul by remember { mutableStateOf("50") }
    val wr = remember { mutableStateListOf("", "", "", "") }
    var season by remember { mutableStateOf("2") }
    var ika by remember { mutableStateOf("10") }
    var t by remember { mutableStateOf("1") }
    val conductors = remember { Calc.named("conductors") }
    val input = mutableMapOf<String, Any?>("rho" to rho, "use_strip" to useStrip, "strip_l" to stripL, "strip_d" to stripD, "strip_h" to stripH,
        "use_rod" to useRod, "rod_l" to rodL, "rod_d" to rodD, "rod_n" to rodN, "use_found" to useFound, "fa" to fa, "fb" to fb,
        "use_mesh" to useMesh, "mesh_l" to meshL, "ia" to ia, "ul" to ul, "season" to season, "ika" to ika, "t" to t)
    wr.forEachIndexed { i, v -> input["w_r.$i"] = v }
    val r = Calc.run("grounding", input)
    ModuleScaffold("Topraklama", "Şerit, çubuk, temel ve gözlü topraklama · Wenner · Onderdonk", onBack) {
        item {
            Section("Toprak") {
                NumField("Özgül direnç ρ", rho, { rho = it }, Modifier.fillMaxWidth(), "Ωm")
                Note("Ölçüm yaptıysan aşağıdaki Wenner bölümünden ortalama ρ'yu bulup buraya yaz.")
            }
        }
        item {
            Section("Topraklayıcılar") {
                Toggle("Gömülü şerit / iletken", useStrip) { useStrip = it }
                if (useStrip) {
                    Fields({ NumField("Uzunluk", stripL, { stripL = it }, it, "m") }, { NumField("Derinlik", stripH, { stripH = it }, it, "m") })
                    NamedChoice("İletken (eşdeğer çap)", conductors, stripD) { stripD = it }
                }
                Toggle("Çubuk topraklayıcı", useRod) { useRod = it }
                if (useRod) Fields({ NumField("Boy", rodL, { rodL = it }, it, "m") }, { NumField("Çap", rodD, { rodD = it }, it, "m") }, { NumField("Adet", rodN, { rodN = it }, it) })
                Toggle("Temel topraklayıcı", useFound) { useFound = it }
                Fields({ NumField("Temel eni", fa, { fa = it }, it, "m") }, { NumField("Temel boyu", fb, { fb = it }, it, "m") })
                Toggle("Gözlü (ağ) topraklama", useMesh) { useMesh = it }
                if (useMesh) NumField("Toplam ağ iletkeni", meshL, { meshL = it }, Modifier.fillMaxWidth(), "m")
            }
        }
        item {
            Section("Sonuç") {
                if (useStrip) ResultRow("Şerit R", fmt(r.d("r_strip")), "Ω")
                if (useRod) ResultRow("Çubuklar R", fmt(r.d("r_rod")), "Ω")
                if (useFound) ResultRow("Temel R", fmt(r.d("r_found")), "Ω")
                if (useMesh) ResultRow("Gözlü R", fmt(r.d("r_mesh")), "Ω")
                ResultRow("Paralel eşdeğer R", fmt(r.d("r_total"), 3), "Ω", ok = r.b("ok"), big = true)
                Fields({ NumField("Ia (koruma akımı)", ia, { ia = it }, it, "A") }, { NumField("UL (dokunma)", ul, { ul = it }, it, "V") })
                ResultRow("İzin verilen en büyük R = UL / Ia", fmt(r.d("r_max"), 3), "Ω")
                ResultRow("Temel alanına göre en az çubuk", r["min_rods"], "adet")
            }
        }
        item {
            Section("Wenner ölçümü (ρ = 2π·a·R)") {
                val aList = listOf("1", "2", "4", "6")
                for (i in 0 until 4) Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("a = ${aList[i]} m", modifier = Modifier.width(72.dp))
                    NumField("Ölçülen R", wr[i], { wr[i] = it }, Modifier.weight(1f), "Ω")
                    Text("  ρ = ${fmt(r.d("w_rho.$i"), 1)}", modifier = Modifier.width(96.dp))
                }
                NumField("Mevsim katsayısı", season, { season = it }, Modifier.fillMaxWidth())
                ResultRow("Hesaba esas ortalama ρ", fmt(r.d("rho_avg"), 1), "Ωm", big = true)
            }
        }
        item {
            Section("Onderdonk: kısa devreye göre iletken kesiti") {
                Fields({ NumField("Kısa devre akımı", ika, { ika = it }, it, "kA") }, { NumField("Süre", t, { t = it }, it, "s") })
                ResultRow("Kaynak (Cadweld)", fmt(r.d("onderdonk_cadweld"), 1), "mm²")
                ResultRow("Lehim / bindirme kaynağı", fmt(r.d("onderdonk_lehim"), 1), "mm²")
                ResultRow("Cıvatalı / pres", fmt(r.d("onderdonk_civata"), 1), "mm²")
            }
        }
    }
}

@Composable
private fun Toggle(label: String, on: Boolean, onChange: (Boolean) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(label, modifier = Modifier.weight(1f))
        Switch(on, onChange)
    }
}

// ───────────────────────── Paratoner ─────────────────────────

@Composable
fun LightningModule(onBack: () -> Unit) {
    var l by remember { mutableStateOf("26") }
    var w by remember { mutableStateOf("21") }
    var h by remember { mutableStateOf("30") }
    var nk by remember { mutableStateOf("20") }
    var c1 by remember { mutableDoubleStateOf(0.25) }
    var c2 by remember { mutableDoubleStateOf(1.0) }
    var c3 by remember { mutableDoubleStateOf(0.5) }
    var c4 by remember { mutableDoubleStateOf(1.0) }
    var c5 by remember { mutableDoubleStateOf(5.0) }
    var mast by remember { mutableStateOf("6") }
    var dt by remember { mutableIntStateOf(60) }
    var level by remember { mutableIntStateOf(0) }
    val r = Calc.run("lightning", mapOf("l" to l, "w" to w, "h" to h, "nk" to nk, "c1" to c1, "c2" to c2, "c3" to c3, "c4" to c4, "c5" to c5,
        "mast" to mast, "delta_t" to dt, "level" to level))
    ModuleScaffold("Paratoner", "Yıldırım risk analizi (Nd/Nc), koruma seviyesi, aktif paratoner yarıçapı", onBack) {
        item {
            Section("Yapı") {
                Fields({ NumField("Boy L", l, { l = it }, it, "m") }, { NumField("En W", w, { w = it }, it, "m") }, { NumField("Yükseklik H", h, { h = it }, it, "m") })
                NumField("Yıllık yıldırımlı gün Nk", nk, { nk = it }, Modifier.fillMaxWidth(), "gün")
                Note("Nk: aşağıdaki yıldırımlı gün haritasından.")
                NamedChoice("C1 · çevre", Calc.named("lp_c1"), c1) { c1 = it }
                NamedChoice("C2 · yapı türü", Calc.named("lp_c2"), c2) { c2 = it }
                NamedChoice("C3 · içerik", Calc.named("lp_c3"), c3) { c3 = it }
                NamedChoice("C4 · kullanım", Calc.named("lp_c4"), c4) { c4 = it }
                NamedChoice("C5 · sonuç", Calc.named("lp_c5"), c5) { c5 = it }
            }
        }
        item { Section("Yıldırımlı gün haritası (Nk)") { AssetImage("img/yildirim_haritasi.jpg") } }
        item {
            Section("Risk") {
                ResultRow("Eşdeğer toplama alanı Ae", fmt(r.d("ae"), 0), "m²")
                ResultRow("Yer yıldırım yoğunluğu Ng", fmt(r.d("ng"), 3), "/km²·yıl")
                ResultRow("Beklenen düşme Nd", "%.3e".format(r.d("nd")))
                ResultRow("Kabul edilebilir Nc", "%.3e".format(r.d("nc")))
                if (r.i("level") == 0) ResultRow("Sonuç", "Koruma zorunlu değil", ok = true, big = true)
                else ResultRow("Gerekli koruma verimi E", "${fmt(r.d("e") * 100, 1)} % → Seviye ${r["level"]}", ok = false, big = true)
            }
        }
        item {
            Section("Aktif paratoner (ESE)") {
                Choice("Koruma seviyesi", listOf(0, 1, 2, 3, 4), level, { if (it == 0) "Otomatik (${r["level_used"]})" else "Seviye $it" }) { level = it }
                Fields({ NumField("Direk yüksekliği", mast, { mast = it }, it, "m") })
                Choice("ΔT (µs)", listOf(15, 25, 45, 60), dt, { "$it" }) { dt = it }
                ResultRow("Yuvarlanan küre D", fmt(r.d("d"), 0), "m")
                ResultRow("Koruma yarıçapı Rp", fmt(r.d("rp"), 1), "m", big = true)
                ResultRow("Yapının yarı köşegeni", fmt(r.d("half_diag"), 1), "m")
                ResultRow("Tek paratoner yeterli mi", if (r.b("covers")) "Evet" else "Hayır", ok = r.b("covers"))
            }
        }
    }
}
