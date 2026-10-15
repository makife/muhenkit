package com.muhenkit.app.cad

import kotlin.math.ceil

/** Proje tabloları: aydınlatma hesabı, toplam güç ve firma anteti. Hepsi [TableBuilder] ile çizilir. */
object ProjectTables {
    // ───────────────────────── aydınlatma hesap tablosu ─────────────────────────

    /** Bir mahal. Boş bırakılan hesaplanabilir alanlar otomatik doldurulur. */
    data class Room(
        val name: String = "",
        val a: String = "", // en (m)
        val b: String = "", // boy (m)
        val H: String = "3", // mahal yüksekliği (m)
        val work: String = "0.85", // çalışma düzlemi (m): h = H − çalışma düzlemi
        val lux: String = "",
        val refl: String = "80-50-30",
        val k: String = "", // boşsa hesaplanır
        val eta: String = "", // boşsa önerilir
        val power: String = "",
        val flux: String = "",
        val type: String = "",
        val zCalc: String = "", // boşsa hesaplanır
        val zUsed: String = "", // boşsa yukarı yuvarlanır
    )

    fun d(s: String): Double? = s.trim().replace(',', '.').toDoubleOrNull()
    /** Ondalık virgüllü sayı; yalnızca ondalık kısmın sondaki sıfırları atılır (9500 → "9500", 1,50 → "1,5") */
    fun f(v: Double, digits: Int = 2): String {
        var s = "%.${digits}f".format(java.util.Locale.ROOT, v)
        if ('.' in s) s = s.trimEnd('0').trimEnd('.')
        return s.replace('.', ',')
    }

    /** Mahal endeksi k = (a·b) / (h·(a+b)), h = H − çalışma düzlemi */
    fun roomIndex(r: Room): Double? {
        val a = d(r.a) ?: return null
        val b = d(r.b) ?: return null
        val h = (d(r.H) ?: return null) - (d(r.work) ?: 0.0)
        if (h <= 0 || a + b <= 0) return null
        return a * b / (h * (a + b))
    }

    /**
     * Verim önerisi: doğrudan ışıklı armatür, 80-50-30 yansıtma için tipik verim tablosundan
     * doğrusal ara değer. Kullanıcı kendi cetvelinden girerse o geçerli.
     */
    private val ETA = listOf(0.6 to 0.27, 0.8 to 0.32, 1.0 to 0.36, 1.25 to 0.41, 1.5 to 0.45, 2.0 to 0.51, 2.5 to 0.55, 3.0 to 0.58, 4.0 to 0.62, 5.0 to 0.65)
    fun suggestEta(k: Double): Double {
        if (k <= ETA.first().first) return ETA.first().second
        if (k >= ETA.last().first) return ETA.last().second
        for (i in 1 until ETA.size) {
            val (k0, e0) = ETA[i - 1]
            val (k1, e1) = ETA[i]
            if (k <= k1) return e0 + (e1 - e0) * (k - k0) / (k1 - k0)
        }
        return ETA.last().second
    }

    /** Hesaplanan sayı Z = (E · a · b · 1,25) / (Φ · η) — 1,25: kirlenme (bakım) katsayısı */
    fun lampCount(r: Room, k: Double?): Double? {
        val e = d(r.lux) ?: return null
        val a = d(r.a) ?: return null
        val b = d(r.b) ?: return null
        val phi = d(r.flux) ?: return null
        val eta = d(r.eta) ?: k?.let { suggestEta(it) } ?: return null
        if (phi <= 0 || eta <= 0) return null
        return e * a * b * 1.25 / (phi * eta)
    }

    /** Tablo için tüm değerleri tamamlanmış satır */
    fun resolved(r: Room): List<String> {
        val k = d(r.k) ?: roomIndex(r)
        val eta = d(r.eta) ?: k?.let { suggestEta(it) }
        val z = d(r.zCalc) ?: lampCount(r.copy(eta = eta?.let { f(it) } ?: r.eta), k)
        val used = r.zUsed.ifBlank { z?.let { ceil(it - 1e-9).toInt().toString() } ?: "" }
        val hTxt = listOf(r.H, r.work).filter { it.isNotBlank() }.joinToString(" - ")
        return listOf(
            r.name, r.a, r.b, r.H, hTxt, r.lux, r.refl,
            k?.let { f(it) } ?: "", eta?.let { f(it) } ?: "", r.power, r.flux, r.type, z?.let { f(it) } ?: "", used,
        )
    }

    private val LIGHT_ROWS = listOf(
        "MAHAL İSMİ",
        "Mahal eni ( a ) ( m )",
        "Mahal boyu ( b ) ( m )",
        "Mahal yüksekliği ( H ) ( m )",
        "Armatür yüksekliği ( h ) ( m )",
        "İstenilen aydınlık şiddeti ( E ) Lüx",
        "Tavan-duvar-yer yansıtma faktörleri",
        "Mahal endeksi ( k )  k = (a x b) / h x (a + b)",
        "Mahal verimi (cetvelden) ( η )",
        "Seçilen armatür gücü ( W )",
        "Seçilen armatür ışık akısı ( Ø )",
        "Seçilen armatür tipi",
        "Hesaplanan armatür sayısı  Z = (E x a x b x 1,25) / Ø x η",
        "Kullanılacak armatür sayısı",
    )

    fun lightingTable(th: Double, rooms: List<Room>, note: String): CadSymbol {
        val tb = TableBuilder(th)
        tb.columns(38.0, *DoubleArray(rooms.size) { 9.0 })
        val values = rooms.map { resolved(it) }
        LIGHT_ROWS.forEachIndexed { i, label ->
            val r = tb.row(if (i == 0) 2.6 else 2.2)
            tb.text(r, 0, label, align = 0, scale = if (i == 0) 1.1 else 1.0)
            values.forEachIndexed { c, v -> tb.text(r, c + 1, v[i], scale = if (i == 0) 1.1 else 1.0) }
        }
        if (note.isNotBlank()) {
            val r = tb.row(2.2)
            tb.text(r, 0, note, align = 0, colSpan = rooms.size + 1, scale = 0.85)
        }
        return tb.build("TBL_AYD_${System.currentTimeMillis() % 1_000_000}", "Aydınlatma hesap tablosu")
    }

    // ───────────────────────── toplam güç tablosu ─────────────────────────

    data class Load(val name: String = "", val installed: String = "", val demandPct: String = "")

    fun powerTable(th: Double, title: String, loads: List<Load>): CadSymbol {
        val tb = TableBuilder(th)
        tb.columns(4.0, 18.0, 11.0, 9.0, 11.0)
        val r0 = tb.row(2.8)
        tb.text(r0, 0, title.ifBlank { "TOPLAM GÜÇ TABLOSU" }, colSpan = 5, scale = 1.25)
        val r1 = tb.row(2.6)
        listOf("No", "Bölüm / Daire", "Kurulu güç (W)", "Eş zam. (%)", "Talep güç (W)").forEachIndexed { i, s -> tb.text(r1, i, s, scale = 0.9) }
        var sumI = 0.0
        var sumD = 0.0
        loads.filter { it.name.isNotBlank() || it.installed.isNotBlank() }.forEachIndexed { i, l ->
            val inst = d(l.installed) ?: 0.0
            val pct = d(l.demandPct) ?: 100.0
            val dem = inst * pct / 100.0
            sumI += inst
            sumD += dem
            val r = tb.row()
            tb.text(r, 0, (i + 1).toString())
            tb.text(r, 1, l.name, align = 0)
            tb.text(r, 2, f(inst, 0))
            tb.text(r, 3, f(pct, 0))
            tb.text(r, 4, f(dem, 0))
        }
        val rt = tb.row(2.6)
        tb.text(rt, 0, "TOPLAM", colSpan = 2, scale = 1.1)
        tb.text(rt, 2, f(sumI, 0), scale = 1.1)
        tb.text(rt, 3, if (sumI > 0) f(sumD / sumI * 100, 0) else "")
        tb.text(rt, 4, f(sumD, 0), scale = 1.1)
        val rs = tb.row(2.4)
        tb.text(rs, 0, "TOPLAM KURULU GÜÇ = ${f(sumI, 0)} W     TOPLAM TALEP GÜÇ = ${f(sumD, 0)} W", colSpan = 5, align = 0)
        return tb.build("TBL_GUC_${System.currentTimeMillis() % 1_000_000}", "Toplam güç tablosu")
    }

    // ───────────────────────── firma anteti ─────────────────────────

    /** Antet alanları: anahtar → (etiket, varsayılan). Sıra formdaki sıradır. */
    val TITLE_FIELDS = listOf(
        "firma" to ("Firma adı" to ""),
        "firmaAlt" to ("Firma alt yazısı (ör. elektrik – otomasyon)" to ""),
        "proje" to ("Proje adı" to "ELEKTRİK İÇ TESİSAT PROJESİ"),
        "sorumlu" to ("Proje sorumlusu adı soyadı" to ""),
        "smm" to ("S.M.M. no" to ""),
        "et" to ("E.T. (oda sicil) no" to ""),
        "yaptiran" to ("Projeyi yaptıran adı soyadı" to ""),
        "yaptiranAdres" to ("Projeyi yaptıran adresi" to ""),
        "vergi" to ("Vergi dairesi / no" to ""),
        "yapiSahibi" to ("Yapı sahibi" to ""),
        "il" to ("İl" to ""),
        "ilce" to ("İlçe" to ""),
        "adres" to ("Yapı adresi" to ""),
        "pafta" to ("Pafta" to ""),
        "ada" to ("Ada" to ""),
        "parsel" to ("Parsel" to ""),
        "sinif" to ("Yapı sınıfı" to ""),
        "kat" to ("Kat sayısı" to ""),
        "alan" to ("Toplam alan (m²)" to ""),
        "bagimsiz" to ("Bağımsız bölüm sayısı" to ""),
        "amac" to ("Kullanım amacı" to "KONUT"),
        "kurulu" to ("Toplam kurulu güç (W)" to ""),
        "talepFaktor" to ("Talep faktörü (%)" to ""),
        "talep" to ("Toplam talep güç (W)" to ""),
        "cizen" to ("Çizen" to ""),
        "tarih" to ("Çizim tarihi" to ""),
        "olcek" to ("Ölçek" to "1/50"),
        "projeNo" to ("Proje no / pafta" to ""),
    )

    fun titleBlock(th: Double, v: Map<String, String>): CadSymbol {
        fun g(k: String) = v[k].orEmpty()
        val tb = TableBuilder(th)
        tb.columns(10.0, 10.0, 10.0, 10.0, 10.0, 10.0) // 60 yazı yüksekliği genişlik
        fun pair(label: String, value: String, c: Int, span: Int, r: Int) {
            tb.text(r, c, label, align = 0, scale = 0.75)
            tb.text(r, c + 1, value, align = 0, colSpan = span)
        }
        var r = tb.row(5.5)
        tb.text(r, 0, g("firma"), colSpan = 6, scale = 2.4)
        if (g("firmaAlt").isNotBlank()) {
            r = tb.row(2.4); tb.text(r, 0, g("firmaAlt"), colSpan = 6, scale = 1.1)
        }
        r = tb.row(3.6); tb.text(r, 0, g("proje"), colSpan = 6, scale = 1.6)
        r = tb.row(2.2)
        tb.text(r, 0, "PROJE SORUMLUSU", colSpan = 2, scale = 0.85)
        tb.text(r, 2, "S.M.M. NO", scale = 0.85); tb.text(r, 3, "E.T. NO", scale = 0.85); tb.text(r, 4, "İMZA", colSpan = 2, scale = 0.85)
        r = tb.row(3.0)
        tb.text(r, 0, g("sorumlu"), colSpan = 2); tb.text(r, 2, g("smm")); tb.text(r, 3, g("et")); tb.text(r, 4, "", colSpan = 2)
        r = tb.row(2.2); tb.text(r, 0, "PROJEYİ YAPTIRAN", colSpan = 6, scale = 0.85)
        r = tb.row(); pair("ADI SOYADI", g("yaptiran"), 0, 5, r)
        r = tb.row(); pair("ADRESİ", g("yaptiranAdres"), 0, 5, r)
        r = tb.row(); pair("VERGİ D./NO", g("vergi"), 0, 2, r); pair("YAPI SAHİBİ", g("yapiSahibi"), 3, 2, r)
        r = tb.row(2.2); tb.text(r, 0, "ARSANIN ÖZELLİKLERİ", colSpan = 6, scale = 0.85)
        r = tb.row(2.0)
        listOf("İL", "İLÇE", "ADRES", "PAFTA", "ADA", "PARSEL").forEachIndexed { i, s -> tb.text(r, i, s, scale = 0.75) }
        r = tb.row()
        listOf("il", "ilce", "adres", "pafta", "ada", "parsel").forEachIndexed { i, k -> tb.text(r, i, g(k)) }
        r = tb.row(2.2); tb.text(r, 0, "YAPININ ÖZELLİKLERİ", colSpan = 6, scale = 0.85)
        r = tb.row(2.0)
        listOf("SINIFI", "KAT SAYISI", "TOP. ALAN (m²)", "BAĞ. BÖL. SAY.", "KULLANIM AMACI", "TALEP FAK. (%)").forEachIndexed { i, s -> tb.text(r, i, s, scale = 0.75) }
        r = tb.row()
        listOf("sinif", "kat", "alan", "bagimsiz", "amac", "talepFaktor").forEachIndexed { i, k -> tb.text(r, i, g(k)) }
        r = tb.row(2.6)
        tb.text(r, 0, "TOPLAM KURULU GÜÇ = ${g("kurulu")} W", colSpan = 3, align = 0)
        tb.text(r, 3, "TOPLAM TALEP GÜÇ = ${g("talep")} W", colSpan = 3, align = 0)
        r = tb.row(2.0)
        listOf("ÇİZEN", "", "ÇİZİM TARİHİ", "", "ÖLÇEK", "PROJE NO").forEachIndexed { i, s -> if (s.isNotEmpty()) tb.text(r, i, s, scale = 0.75) }
        r = tb.row()
        tb.text(r, 0, g("cizen"), colSpan = 2); tb.text(r, 2, g("tarih"), colSpan = 2); tb.text(r, 4, g("olcek")); tb.text(r, 5, g("projeNo"))
        return tb.build("TBL_ANTET_${System.currentTimeMillis() % 1_000_000}", "Firma anteti")
    }
}
