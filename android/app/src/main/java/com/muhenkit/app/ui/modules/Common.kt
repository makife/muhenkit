package com.muhenkit.app.ui.modules

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import java.util.Locale

val Ok = Color(0xFF3DFF8E)
val Bad = Color(0xFFFF6B5E)
private val CardBg = Color(0xFF141719)

/** Modül iskeleti: üstte geri + başlık, altında kaydırılabilir içerik */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ModuleScaffold(title: String, subtitle: String, onBack: () -> Unit, content: LazyListScope.() -> Unit) {
    Column(Modifier.fillMaxSize().background(MaterialTheme.colorScheme.background)) {
        TopAppBar(
            title = {
                Column {
                    Text(title, style = MaterialTheme.typography.titleMedium)
                    Text(subtitle, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1)
                }
            },
            navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Outlined.ArrowBack, "Geri") } },
            colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.surface),
        )
        LazyColumn(
            Modifier.fillMaxSize().imePadding(),
            contentPadding = PaddingValues(start = 12.dp, end = 12.dp, top = 12.dp, bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
            content = content,
        )
    }
}

/** Başlıklı kart */
@Composable
fun Section(title: String, modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    Column(
        modifier.fillMaxWidth().background(CardBg, RoundedCornerShape(12.dp)).padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Text(title, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.primary)
        content()
    }
}

/** Metin alanı tabanlı sayı girişi; değer metin olarak tutulur (yazarken biçim bozulmasın) */
@Composable
fun NumField(label: String, value: String, onChange: (String) -> Unit, modifier: Modifier = Modifier, unit: String? = null) {
    OutlinedTextField(
        value, { onChange(it.replace(',', '.')) }, modifier = modifier,
        label = { Text(label, fontSize = 12.sp, maxLines = 1) }, singleLine = true,
        suffix = unit?.let { { Text(it, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant) } },
        isError = value.isNotEmpty() && value.toDoubleOrNull() == null,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
    )
}

/** Satırda yan yana alanlar */
@Composable
fun Fields(vararg items: @Composable (Modifier) -> Unit) {
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        for (it in items) it(Modifier.weight(1f))
    }
}

/** Tek seçimli düğme grubu */
@Composable
fun <T> Choice(label: String?, options: List<T>, selected: T, text: (T) -> String, onSelect: (T) -> Unit) {
    Column {
        if (label != null) Text(label, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            for (o in options) FilterChip(selected = o == selected, onClick = { onSelect(o) }, label = { Text(text(o), fontSize = 13.sp) })
        }
    }
}

/** Ad → değer listesinden seçim (katsayılar, armatürler, iletkenler) */
@Composable
fun NamedChoice(label: String, options: List<Pair<String, Double>>, value: Double, onSelect: (Double) -> Unit) {
    var open by remember { mutableStateOf(false) }
    val cur = options.firstOrNull { it.second == value }?.first ?: fmtv(value)
    Box {
        OutlinedTextField(
            cur, {}, readOnly = true, label = { Text(label, fontSize = 12.sp) }, singleLine = true,
            modifier = Modifier.fillMaxWidth(),
            trailingIcon = { Text("▾", modifier = Modifier.padding(end = 8.dp)) },
        )
        Box(Modifier.matchParentSize().clickable { open = true })
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            for ((n, v) in options) DropdownMenuItem(text = { Text("$n  (${fmtv(v)})") }, onClick = { onSelect(v); open = false })
        }
    }
}

/** Sonuç satırı: etiket — değer birim; [ok] verilirse yeşil/kırmızı */
@Composable
fun ResultRow(label: String, value: String, unit: String = "", ok: Boolean? = null, big: Boolean = false) {
    Row(Modifier.fillMaxWidth().padding(vertical = 2.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(label, modifier = Modifier.weight(1f), color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = if (big) 15.sp else 13.sp)
        Text(
            value + if (unit.isNotEmpty()) " $unit" else "",
            fontWeight = if (big) FontWeight.Bold else FontWeight.Medium,
            fontSize = if (big) 20.sp else 15.sp,
            color = when (ok) { true -> Ok; false -> Bad; null -> MaterialTheme.colorScheme.onSurface },
        )
    }
}

@Composable
fun Note(text: String) = Text(text, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)

/** Basit tablo: başlık + satırlar; [highlight] satırı vurgulanır */
@Composable
fun SimpleTable(headers: List<String>, rows: List<List<String>>, highlight: Int = -1, weights: List<Float>? = null) {
    // sütun başına en az ~70 dp: dar ekranda sığmazsa tablo yana kayar (yazılar ezilmesin)
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        val need = 70.dp * headers.size
        val tableW = if (maxWidth >= need) maxWidth else need
        Box(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState())) {
            TableBody(headers, rows, highlight, weights, Modifier.width(tableW))
        }
    }
}

@Composable
private fun TableBody(headers: List<String>, rows: List<List<String>>, highlight: Int, weights: List<Float>?, modifier: Modifier) {
    Column(modifier.border(1.dp, Color(0x22FFFFFF), RoundedCornerShape(8.dp))) {
        val w = weights ?: List(headers.size) { 1f }
        Row(Modifier.fillMaxWidth().background(Color(0x22FFFFFF)).padding(6.dp)) {
            headers.forEachIndexed { i, h -> Text(h, Modifier.weight(w[i]), fontSize = 11.sp, fontWeight = FontWeight.Bold) }
        }
        rows.forEachIndexed { r, row ->
            Row(
                Modifier.fillMaxWidth().background(if (r == highlight) MaterialTheme.colorScheme.primary.copy(alpha = 0.22f) else Color.Transparent)
                    .padding(horizontal = 6.dp, vertical = 4.dp),
            ) {
                row.forEachIndexed { i, c -> Text(c, Modifier.weight(w.getOrElse(i) { 1f }), fontSize = 12.sp) }
            }
        }
    }
}

fun fmtv(v: Double): String = com.muhenkit.app.calc.fmt(v, 3)

/** Metin girişini sayıya: boş/geçersiz → null */
fun String.num(): Double? = this.replace(',', '.').toDoubleOrNull()

/** Sayıyı alan metnine (nokta ondalıklı, gereksiz sıfırsız) */
fun Double.field(): String {
    var s = "%.6f".format(Locale.ROOT, this)
    if ('.' in s) s = s.trimEnd('0').trimEnd('.')
    return s
}
