package com.muhenkit.app.ui

import android.content.Context
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.TableChart
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.snapshots.SnapshotStateList
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.muhenkit.app.cad.CadSymbol
import com.muhenkit.app.cad.ProjectTables
import com.muhenkit.app.cad.ProjectTables.Load
import com.muhenkit.app.cad.ProjectTables.Room

enum class TableKind(val title: String, val hint: String) {
    LIGHTING("Aydınlatma hesap tablosu", "Mahal bazında: ölçüler, lüks, verim, armatür seçimi ve sayısı"),
    POWER("Toplam güç tablosu", "Bölüm/daire bazında kurulu güç, eşzamanlılık ve talep gücü"),
    TITLE("Firma anteti", "Projeyi yapan firma ve proje bilgileri (bilgiler hatırlanır)"),
}

/** Tablo türü seçimi */
@Composable
fun TableChooser(onPick: (TableKind) -> Unit) {
    Column(Modifier.padding(horizontal = 16.dp).padding(bottom = 24.dp)) {
        Text("Tablo ekle", style = MaterialTheme.typography.titleMedium)
        Text("Formu doldur, tablo çizilsin; sonra çizimde yerine dokun.", style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        Spacer(Modifier.height(8.dp))
        for (k in TableKind.entries) {
            Row(
                Modifier.fillMaxWidth().padding(vertical = 4.dp).border(1.dp, Color(0x33FFFFFF), RoundedCornerShape(10.dp))
                    .clickable { onPick(k) }.padding(12.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Icon(Icons.Outlined.TableChart, null, tint = MaterialTheme.colorScheme.primary)
                Spacer(Modifier.width(12.dp))
                Column {
                    Text(k.title, fontWeight = FontWeight.Medium)
                    Text(k.hint, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
    }
}

/** Tam ekran form iskeleti: üstte kapat / "Çizime ekle" */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun FormDialog(title: String, onDismiss: () -> Unit, onDone: () -> Unit, content: @Composable ColumnScope.() -> Unit) {
    Dialog(onDismissRequest = onDismiss, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
            Column(Modifier.fillMaxSize().statusBarsPadding().navigationBarsPadding().imePadding()) {
                TopAppBar(
                    title = { Text(title) },
                    navigationIcon = { IconButton(onClick = onDismiss) { Icon(Icons.Outlined.Close, "Kapat") } },
                    actions = { Button(onClick = onDone, modifier = Modifier.padding(end = 8.dp)) { Text("Çizime ekle") } },
                    colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.surface),
                )
                content()
            }
        }
    }
}

@Composable
private fun Field(label: String, value: String, modifier: Modifier = Modifier, number: Boolean = false, placeholder: String? = null, onChange: (String) -> Unit) {
    OutlinedTextField(
        value, onChange, label = { Text(label, fontSize = 12.sp) }, singleLine = true, modifier = modifier,
        placeholder = placeholder?.let { { Text(it, color = MaterialTheme.colorScheme.onSurfaceVariant) } },
        keyboardOptions = if (number) KeyboardOptions(keyboardType = KeyboardType.Decimal) else KeyboardOptions.Default,
    )
}

// ───────────────────────── aydınlatma ─────────────────────────

@Composable
fun LightingForm(textHeight: Double, onDismiss: () -> Unit, onDone: (CadSymbol) -> Unit) {
    val rooms = remember { mutableStateListOf(Room(name = "SALON")) }
    var note by remember { mutableStateOf("Not: Tasarruflu ampül kullanılması tercih edilerek, armatür seçimi yapılmıştır.") }
    FormDialog("Aydınlatma hesap tablosu", onDismiss, onDone = { onDone(ProjectTables.lightingTable(textHeight, rooms.toList(), note)) }) {
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(12.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            itemsIndexed(rooms) { i, r -> RoomCard(i, r, rooms) }
            item {
                OutlinedButton(onClick = { rooms.add(Room(H = rooms.lastOrNull()?.H ?: "3", refl = rooms.lastOrNull()?.refl ?: "80-50-30")) }, modifier = Modifier.fillMaxWidth()) {
                    Icon(Icons.Outlined.Add, null); Spacer(Modifier.width(6.dp)); Text("Mahal ekle")
                }
            }
            item { Field("Tablo altı not", note, Modifier.fillMaxWidth()) { note = it } }
            item {
                Text(
                    "k ve Z boş bırakılırsa hesaplanır: k = (a·b)/(h·(a+b)), h = H − çalışma düzlemi; Z = (E·a·b·1,25)/(Ø·η). " +
                        "η boşsa 80-50-30 için tipik değer önerilir — cetvelden gireceğin değer geçerlidir.",
                    fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
private fun RoomCard(i: Int, r: Room, rooms: SnapshotStateList<Room>) {
    fun set(n: Room) { rooms[i] = n }
    val k = ProjectTables.d(r.k) ?: ProjectTables.roomIndex(r)
    val eta = ProjectTables.d(r.eta) ?: k?.let { ProjectTables.suggestEta(it) }
    val z = ProjectTables.d(r.zCalc) ?: ProjectTables.lampCount(r.copy(eta = eta?.let { ProjectTables.f(it) } ?: ""), k)
    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("Mahal ${i + 1}", fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                if (rooms.size > 1) IconButton(onClick = { rooms.removeAt(i) }) { Icon(Icons.Outlined.Delete, "Sil") }
            }
            Field("Mahal ismi", r.name, Modifier.fillMaxWidth()) { set(r.copy(name = it)) }
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Field("En a (m)", r.a, Modifier.weight(1f), true) { set(r.copy(a = it)) }
                Field("Boy b (m)", r.b, Modifier.weight(1f), true) { set(r.copy(b = it)) }
                Field("Yükseklik H", r.H, Modifier.weight(1f), true) { set(r.copy(H = it)) }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Field("Çalışma düz. (m)", r.work, Modifier.weight(1f), true) { set(r.copy(work = it)) }
                Field("Aydınlık E (lx)", r.lux, Modifier.weight(1f), true) { set(r.copy(lux = it)) }
                Field("Yansıtma", r.refl, Modifier.weight(1f)) { set(r.copy(refl = it)) }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Field("Mahal endeksi k", r.k, Modifier.weight(1f), true, k?.let { ProjectTables.f(it) }) { set(r.copy(k = it)) }
                Field("Verim η", r.eta, Modifier.weight(1f), true, eta?.let { ProjectTables.f(it) }) { set(r.copy(eta = it)) }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Field("Armatür gücü (W)", r.power, Modifier.weight(1f), true) { set(r.copy(power = it)) }
                Field("Işık akısı Ø (lm)", r.flux, Modifier.weight(1f), true) { set(r.copy(flux = it)) }
                Field("Armatür tipi", r.type, Modifier.weight(1f)) { set(r.copy(type = it)) }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Field("Hesaplanan Z", r.zCalc, Modifier.weight(1f), true, z?.let { ProjectTables.f(it) }) { set(r.copy(zCalc = it)) }
                Field("Kullanılacak sayı", r.zUsed, Modifier.weight(1f), false,
                    z?.let { kotlin.math.ceil(it - 1e-9).toInt().toString() }) { set(r.copy(zUsed = it)) }
            }
        }
    }
}

// ───────────────────────── toplam güç ─────────────────────────

@Composable
fun PowerForm(textHeight: Double, onDismiss: () -> Unit, onDone: (CadSymbol) -> Unit) {
    val loads = remember { mutableStateListOf(Load("1. KAT DAİRE", "", "60"), Load("2. KAT DAİRE", "", "60")) }
    var title by remember { mutableStateOf("TOPLAM GÜÇ TABLOSU") }
    FormDialog("Toplam güç tablosu", onDismiss, onDone = { onDone(ProjectTables.powerTable(textHeight, title, loads.toList())) }) {
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            item { Field("Tablo başlığı", title, Modifier.fillMaxWidth()) { title = it } }
            itemsIndexed(loads) { i, l ->
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
                    Field("Bölüm", l.name, Modifier.weight(1.6f)) { loads[i] = l.copy(name = it) }
                    Field("Kurulu (W)", l.installed, Modifier.weight(1f), true) { loads[i] = l.copy(installed = it) }
                    Field("Eş zam. %", l.demandPct, Modifier.weight(0.8f), true) { loads[i] = l.copy(demandPct = it) }
                    IconButton(onClick = { loads.removeAt(i) }) { Icon(Icons.Outlined.Delete, "Sil") }
                }
            }
            item {
                OutlinedButton(onClick = { loads.add(Load(demandPct = loads.lastOrNull()?.demandPct ?: "100")) }, modifier = Modifier.fillMaxWidth()) {
                    Icon(Icons.Outlined.Add, null); Spacer(Modifier.width(6.dp)); Text("Satır ekle")
                }
            }
            item {
                val inst = loads.sumOf { ProjectTables.d(it.installed) ?: 0.0 }
                val dem = loads.sumOf { (ProjectTables.d(it.installed) ?: 0.0) * (ProjectTables.d(it.demandPct) ?: 100.0) / 100 }
                Text("Toplam kurulu güç: ${ProjectTables.f(inst, 0)} W   ·   Toplam talep güç: ${ProjectTables.f(dem, 0)} W",
                    fontWeight = FontWeight.Medium, color = MaterialTheme.colorScheme.primary)
            }
        }
    }
}

// ───────────────────────── antet ─────────────────────────

private fun prefs(c: Context) = c.getSharedPreferences("antet", Context.MODE_PRIVATE)

@Composable
fun TitleForm(textHeight: Double, onDismiss: () -> Unit, onDone: (CadSymbol) -> Unit) {
    val context = LocalContext.current
    val values = remember {
        val p = prefs(context)
        mutableStateMapOf<String, String>().apply {
            for ((k, lv) in ProjectTables.TITLE_FIELDS) put(k, p.getString(k, null) ?: lv.second)
        }
    }
    FormDialog("Firma anteti", onDismiss, onDone = {
        // bir sonraki projede yeniden yazmamak için sakla
        prefs(context).edit().apply { values.forEach { (k, v) -> putString(k, v) } }.apply()
        onDone(ProjectTables.titleBlock(textHeight, values.toMap()))
    }) {
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            item {
                Text("Bilgiler telefonda saklanır; sonraki projede hazır gelir.", fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            itemsIndexed(ProjectTables.TITLE_FIELDS) { _, (k, lv) ->
                val num = k in setOf("kurulu", "talep", "talepFaktor", "alan", "kat", "bagimsiz")
                Field(lv.first, values[k].orEmpty(), Modifier.fillMaxWidth(), num) { values[k] = it }
            }
        }
    }
}
