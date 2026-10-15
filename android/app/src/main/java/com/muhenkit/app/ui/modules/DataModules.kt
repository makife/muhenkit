package com.muhenkit.app.ui.modules

import android.content.Intent
import android.graphics.BitmapFactory
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.Remove
import androidx.compose.material.icons.outlined.Share
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.muhenkit.app.calc.Calc
import com.muhenkit.app.calc.fmt
import org.json.JSONArray
import org.json.JSONObject

// ───────────────────────── Sayaç panosu ─────────────────────────

@Composable
fun MeterPanelModule(onBack: () -> Unit) {
    var need by remember { mutableStateOf("14") }
    var three by remember { mutableStateOf(false) }
    var choice by remember { mutableIntStateOf(0) }
    val r = Calc.run("meter_panel", mapOf("need" to need, "three" to three))
    val n = r.i("options")
    val sel = choice.coerceIn(0, maxOf(0, n - 1))
    ModuleScaffold("Sayaç Panosu", "Dolap tipi sayaç panosu ve enerji odası asgari ölçüleri (Çizelge 1)", onBack) {
        item {
            Section("Girdi") {
                NumField("Gereken sayaç adedi", need, { need = it }, Modifier.fillMaxWidth())
                Choice(null, listOf(false, true), three, { if (it) "Üç fazlı sayaç" else "Tek fazlı sayaç" }) { three = it }
            }
        }
        if (n == 0) item { Section("Sonuç") { Note("Bu adet için tablo dışında: birden fazla pano gerekir.") } }
        else {
            item {
                Section("Seçenekler") {
                    Choice(null, (0 until n).toList(), sel, { "${r.i("option.$it.floors")} katlı · ${r["option.$it.per_row"]}/kat" }) { choice = it }
                }
            }
            item {
                val p = "option.$sel."
                Section("Pano ve enerji odası") {
                    ResultRow("Bir kattaki sayaç", r[p + "per_row"], "adet")
                    ResultRow("Kat sayısı", r[p + "floors"])
                    ResultRow("Toplam sayaç kapasitesi", r[p + "total"], "adet", big = true)
                    ResultRow("Pano eni", fmt(r.d(p + "width"), 1), "cm", big = true)
                    ResultRow("Pano yüksekliği", fmt(r.d(p + "h"), 0), "cm")
                    ResultRow("Enerji odası en az eni (Şekil A)", fmt(r.d(p + "room_a"), 1), "cm")
                    ResultRow("Enerji odası en az derinliği (Şekil B)", fmt(r.d(p + "room_b"), 1), "cm")
                    ResultRow("Enerji odası yüksekliği", fmt(r.d(p + "room_h"), 0), "cm")
                }
            }
        }
    }
}

// ───────────────────────── Aydınlık düzeyleri ─────────────────────────

@Composable
fun LuxTableModule(onBack: () -> Unit) {
    val rows = remember { Calc.table("lux").drop(1) }
    var q by remember { mutableStateOf("") }
    val filtered = rows.filter { q.isBlank() || it[1].contains(q, true) || it[0].contains(q, true) }
    ModuleScaffold("Aydınlık Düzeyleri", "Mekan türüne göre en az aydınlık düzeyi (genel / özel lüks)", onBack) {
        item { OutlinedTextField(q, { q = it }, label = { Text("Mekan ara (ör. mutfak, sınıf)") }, singleLine = true, modifier = Modifier.fillMaxWidth()) }
        for ((cat, items) in filtered.groupBy { it[0] }) {
            item {
                Section(cat) {
                    SimpleTable(listOf("Mekan", "Genel lx", "Özel lx"), items.map { listOf(it[1], it[2].ifEmpty { "—" }, it[3].ifEmpty { "—" }) },
                        weights = listOf(2.6f, 1f, 1f))
                }
            }
        }
    }
}

// ───────────────────────── Malzeme keşfi ─────────────────────────

@Composable
fun MaterialsModule(onBack: () -> Unit) {
    val context = LocalContext.current
    val rows = remember { Calc.table("prices").drop(1) }
    val qty = remember { mutableStateMapOf<String, Int>() }
    var markup by remember { mutableStateOf("1") }
    val m = markup.num() ?: 1.0
    val chosen = rows.filter { (qty[it[2]] ?: 0) > 0 }
    val total = chosen.sumOf { it[3].toDouble() * (qty[it[2]] ?: 0) * m }
    ModuleScaffold("Malzeme Keşfi", "Sigorta, kaçak akım rölesi, kompakt şalter (2018 liste fiyatları)", onBack) {
        item {
            Section("Toplam") {
                NumField("Güncelleme çarpanı (2018 → bugün)", markup, { markup = it }, Modifier.fillMaxWidth())
                ResultRow("Seçilen kalem", "${chosen.size}")
                ResultRow("Toplam", fmt(total, 2), "TL", big = true)
                if (chosen.isNotEmpty()) OutlinedButton(onClick = {
                    val body = buildString {
                        append("Malzeme keşfi\n")
                        for (c in chosen) append("${c[0]} ${c[1]} (${c[2]}): ${qty[c[2]]} adet × ${fmt(c[3].toDouble() * m, 2)} TL\n")
                        append("TOPLAM: ${fmt(total, 2)} TL")
                    }
                    context.startActivity(Intent.createChooser(Intent(Intent.ACTION_SEND).apply {
                        type = "text/plain"; putExtra(Intent.EXTRA_TEXT, body)
                    }, "Keşfi paylaş"))
                }) { Icon(Icons.Outlined.Share, null); Spacer(Modifier.width(6.dp)); Text("Paylaş") }
                Note("Fiyatlar Schneider Electric 26 Mayıs 2018 liste fiyatlarıdır; çarpanla güncelleyin.")
            }
        }
        for ((group, items) in rows.groupBy { it[0] }) {
            item {
                Section(group) {
                    for (it in items) {
                        val ref = it[2]
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Column(Modifier.weight(1f)) {
                                Text(it[1], fontSize = 13.sp)
                                Text("$ref · ${fmt(it[3].toDouble() * m, 2)} TL", fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            IconButton(onClick = { qty[ref] = maxOf(0, (qty[ref] ?: 0) - 1) }) { Icon(Icons.Outlined.Remove, "Azalt") }
                            Text("${qty[ref] ?: 0}", fontWeight = FontWeight.Bold, modifier = Modifier.width(28.dp))
                            IconButton(onClick = { qty[ref] = (qty[ref] ?: 0) + 1 }) { Icon(Icons.Outlined.Add, "Artır") }
                        }
                    }
                }
            }
        }
    }
}

// ───────────────────────── rehberler (ortak içerik: core/content/guides.json) ─────────────────────────

private fun guides(context: android.content.Context): JSONObject =
    JSONObject(context.assets.open("guides.json").bufferedReader().use { it.readText() })

private fun JSONArray.strings() = (0 until length()).map { getString(it) }
private fun JSONArray.pairs() = (0 until length()).map { getJSONArray(it).let { a -> a.getString(0) to a.get(1).toString() } }

/** Ortak içerikten görsel (core/content/img) */
@Composable
fun AssetImage(path: String) {
    val context = LocalContext.current
    val bmp = remember(path) { context.assets.open(path).use { BitmapFactory.decodeStream(it) }?.asImageBitmap() }
    if (bmp != null) Image(bmp, null, Modifier.fillMaxWidth().background(Color.White, RoundedCornerShape(8.dp)), contentScale = ContentScale.FillWidth)
}

@Composable
fun WeakCurrentModule(onBack: () -> Unit) {
    val context = LocalContext.current
    val g = remember { guides(context).getJSONObject("weak") }
    ModuleScaffold(g.getString("title"), g.getString("subtitle"), onBack) {
        item {
            Section("Bina fiber altyapısı (yeni uygulama)") {
                g.getJSONArray("fiberSteps").strings().forEachIndexed { i, s -> Text("${i + 1}. $s") }
                Note(g.getString("fiberSource"))
            }
        }
        item { Section("Daire içi dağılım") { g.getJSONArray("flatNotes").strings().forEach { Text("• $it") } } }
        items(g.getJSONArray("images").pairs()) { (title, path) -> Section(title) { AssetImage(path) } }
    }
}

@Composable
fun SmmModule(onBack: () -> Unit) {
    val context = LocalContext.current
    val g = remember { guides(context).getJSONObject("smm") }
    val steps = remember { g.getJSONArray("steps").pairs() }
    val docs = remember { g.getJSONArray("docs").strings() }
    val prefs = remember { context.getSharedPreferences("smm", android.content.Context.MODE_PRIVATE) }
    // ilerleme telefonda saklanır
    val done = remember { mutableStateMapOf<String, Boolean>().apply { prefs.all.forEach { (k, v) -> if (v == true) put(k, true) } } }
    fun toggle(k: String) {
        val v = !(done[k] ?: false)
        done[k] = v
        prefs.edit().putBoolean(k, v).apply()
    }
    val total = steps.sumOf { it.second.toDouble() }
    val progress = (steps.indices.count { done["s$it"] == true } + docs.indices.count { done["d$it"] == true }).toFloat() / (steps.size + docs.size)
    ModuleScaffold(g.getString("title"), g.getString("subtitle"), onBack) {
        item {
            Section("İlerleme") {
                LinearProgressIndicator(progress = { progress }, modifier = Modifier.fillMaxWidth())
                ResultRow("Tamamlanan", "%${(progress * 100).toInt()}")
            }
        }
        item {
            Section("Adımlar ve maliyet") {
                steps.forEachIndexed { i, (s, cost) ->
                    Row(Modifier.fillMaxWidth().clickable { toggle("s$i") }, verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(done["s$i"] == true, { toggle("s$i") })
                        Text(s, Modifier.weight(1f), fontSize = 13.sp)
                        Text("${fmt(cost.toDouble(), 0)} TL", fontSize = 13.sp)
                    }
                }
                ResultRow("Toplam başlangıç maliyeti", fmt(total, 0), "TL", big = true)
                Note(g.getString("costNote"))
            }
        }
        item {
            Section("Gerekli evraklar") {
                docs.forEachIndexed { i, s ->
                    Row(Modifier.fillMaxWidth().clickable { toggle("d$i") }, verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(done["d$i"] == true, { toggle("d$i") })
                        Text(s, fontSize = 13.sp)
                    }
                }
                Text("Şirket / ortak / ücretli için ek", fontWeight = FontWeight.Bold, fontSize = 13.sp)
                g.getJSONArray("companyExtra").strings().forEach { Text("• $it", fontSize = 13.sp) }
            }
        }
        item { Section("SMM'ler ne iş yapar, nereden kazanır?") { g.getJSONArray("jobs").pairs().forEach { (a, b) -> Text(a, fontWeight = FontWeight.Medium); Note(b) } } }
        item { Section("Formlar") { g.getJSONArray("forms").pairs().forEach { (a, b) -> Text(a, fontWeight = FontWeight.Medium); Note(b) } } }
    }
}
