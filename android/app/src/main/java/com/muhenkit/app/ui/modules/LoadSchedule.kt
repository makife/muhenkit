package com.muhenkit.app.ui.modules

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.muhenkit.app.calc.Calc
import com.muhenkit.app.calc.fmt

/** Linye: tür L aydınlatma, P priz, O özel, M motor; faz R/S/T ya da RST (üç faz) */
data class Linye(val name: String, val type: String, val count: String, val unitW: String, val length: String, val phase: String)

private val TYPE_NAMES = mapOf("L" to "Aydınlatma", "P" to "Priz", "O" to "Özel", "M" to "Motor")

@Composable
fun LoadScheduleModule(onBack: () -> Unit) {
    var three by remember { mutableStateOf(true) }
    var demandMode by remember { mutableIntStateOf(0) }
    var demandFactor by remember { mutableStateOf("0.6") }
    var kolonL by remember { mutableStateOf("15") }
    var limit by remember { mutableStateOf("1.5") }
    var cos by remember { mutableStateOf("0.8") }
    val ls = remember {
        mutableStateListOf(
            Linye("L1", "L", "6", "100", "15", "R"), Linye("L2", "L", "6", "100", "15", "S"),
            Linye("P1", "P", "5", "300", "15", "T"), Linye("P2", "P", "5", "300", "15", "R"),
            Linye("P3", "O", "1", "2000", "10", "S"),
        )
    }
    fun input(balance: Boolean): Map<String, Any?> {
        val m = linkedMapOf<String, Any?>("three" to three, "demand_mode" to demandMode, "demand_factor" to demandFactor,
            "kolon_l" to kolonL, "limit" to limit, "cos" to cos, "n" to ls.size, "balance" to balance)
        ls.forEachIndexed { i, l ->
            m["linye.$i.name"] = l.name; m["linye.$i.type"] = l.type; m["linye.$i.count"] = l.count
            m["linye.$i.unit_w"] = l.unitW; m["linye.$i.length"] = l.length; m["linye.$i.phase"] = if (three) l.phase else "R"
        }
        return m
    }
    val r = Calc.run("load_schedule", input(false))

    fun add(t: String) {
        val prefix = when (t) { "L" -> "L"; "M" -> "M"; else -> "P" }
        val n = ls.count { it.name.startsWith(prefix) } + 1
        ls.add(when (t) {
            "L" -> Linye("$prefix$n", t, "6", "100", "15", "R")
            "P" -> Linye("$prefix$n", t, "5", "300", "15", "R")
            "O" -> Linye("$prefix$n", t, "1", "2000", "10", "R")
            else -> Linye("$prefix$n", t, "1", "4000", "10", if (three) "RST" else "R")
        })
    }

    fun balance() {
        val b = Calc.run("load_schedule", input(true))
        for (i in ls.indices) ls[i] = ls[i].copy(phase = b["linye.$i.phase"].ifEmpty { ls[i].phase })
    }

    ModuleScaffold("Yükleme Cetveli", "Linyeler, faz dağılımı, sigorta/kablo seçimi, gerilim düşümü", onBack) {
        item {
            Section("Pano") {
                Choice(null, listOf(true, false), three, { if (it) "Trifaze pano" else "Monofaze pano" }) { three = it }
                Choice("Talep gücü", listOf(0, 1), demandMode, { if (it == 0) "Konut %60/%40" else "Sabit katsayı" }) { demandMode = it }
                if (demandMode == 1) NumField("Talep faktörü", demandFactor, { demandFactor = it }, Modifier.fillMaxWidth())
                Fields({ NumField("Kolon uzunluğu", kolonL, { kolonL = it }, it, "m") }, { NumField("%e sınırı", limit, { limit = it }, it, "%") },
                    { NumField("cos φ (kolon)", cos, { cos = it }, it) })
            }
        }
        itemsIndexed(ls, key = { i, l -> "$i${l.name}" }) { i, l ->
            LinyeCard(i, l, three, r, onChange = { ls[i] = it }, onDelete = { ls.removeAt(i) })
        }
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                for (t in listOf("L", "P", "O", "M")) OutlinedButton(onClick = { add(t) }, modifier = Modifier.weight(1f), contentPadding = PaddingValues(4.dp)) {
                    Text("+ ${TYPE_NAMES[t]}", fontSize = 12.sp)
                }
            }
        }
        if (three) item {
            Button(onClick = { balance() }, modifier = Modifier.fillMaxWidth()) { Text("Fazları dengele") }
        }
        item {
            Section("Sonuç") {
                ResultRow("Kurulu güç", fmt(r.d("installed"), 0), "W")
                ResultRow("Talep gücü", fmt(r.d("demand"), 0), "W", big = true)
                ResultRow("Pano akımı", fmt(r.d("board_current")), "A")
                ResultRow("Ana sigorta", r["main_fuse"], "A", big = true)
                ResultRow("Kolon kablosu", r["kolon.name"].ifEmpty { "sınırı sağlayan yok" }, ok = r["kolon.name"].isNotEmpty())
                ResultRow("Kolon %e", fmt(r.d("kolon_drop"), 3), "%")
                val worst = r.i("worst")
                ResultRow("Toplam %e (kolon + ${if (worst >= 0) r["linye.$worst.name"] else "-"})", fmt(r.d("total_drop"), 3), "%",
                    ok = r.d("total_drop") <= (limit.num() ?: 1.5) * 2)
                if (three) {
                    ResultRow("Faz yükleri R / S / T", "${fmt(r.d("phase_r"), 0)} / ${fmt(r.d("phase_s"), 0)} / ${fmt(r.d("phase_t"), 0)}", "W")
                    ResultRow("Dengesizlik", fmt(r.d("unbalance"), 1), "%", ok = r.d("unbalance") <= 20)
                }
                ResultRow("Kaçak akım rölesi", r["kakr"])
            }
        }
    }
}

@Composable
private fun LinyeCard(i: Int, l: Linye, three: Boolean, r: com.muhenkit.app.calc.CalcResult, onChange: (Linye) -> Unit, onDelete: () -> Unit) {
    Section("${l.name} · ${TYPE_NAMES[l.type]}") {
        Fields(
            { NumField(if (l.type == "L") "Sorti" else "Adet", l.count, { v -> onChange(l.copy(count = v)) }, it) },
            { NumField("Birim güç", l.unitW, { v -> onChange(l.copy(unitW = v)) }, it, "W") },
            { NumField("Uzunluk", l.length, { v -> onChange(l.copy(length = v)) }, it, "m") },
        )
        Row(verticalAlignment = Alignment.CenterVertically) {
            if (three) {
                val phases = if (l.type == "M") listOf("RST") else listOf("R", "S", "T", "RST")
                Box(Modifier.weight(1f)) { Choice("Faz", phases, l.phase, { it }) { onChange(l.copy(phase = it)) } }
            } else Spacer(Modifier.weight(1f))
            IconButton(onClick = onDelete) { Icon(Icons.Outlined.Delete, "Sil") }
        }
        val p = "linye.$i."
        Text(
            "${fmt(r.d(p + "power"), 0)} W · ${fmt(r.d(p + "current"))} A · ${r[p + "fuse"]} A sigorta · ${r[p + "cable"]} · %e ${fmt(r.d(p + "drop"), 3)}",
            fontSize = 13.sp, fontWeight = FontWeight.Medium, color = MaterialTheme.colorScheme.primary,
        )
    }
}
