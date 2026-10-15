package com.muhenkit.app.ui

import android.net.Uri
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import kotlin.math.abs
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.PathMeasure
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.draw.shadow
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.*
import com.muhenkit.app.R
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.draw.scale
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.Image
import androidx.compose.foundation.Canvas
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.muhenkit.app.ui.modules.*

/** Uygulama ekranları */
enum class Route(val title: String, val subtitle: String, val icon: ImageVector) {
    CAD("DXF Çizim", "Son projene devam et · sembol, tablo, PDF", Icons.Outlined.Architecture),
    PROJECTS("Projelerim", "Kayıtlı çizimler, yeni proje, DXF aç", Icons.Outlined.FolderSpecial),
    LIGHTING("Aydınlatma", "Verim metodu ile armatür sayısı ve yerleşim", Icons.Outlined.Lightbulb),
    LOAD("Yükleme Cetveli", "Linyeler, faz dağılımı, sigorta/kablo, %e", Icons.Outlined.TableRows),
    DROP("Gerilim Düşümü", "Bir/üç faz %e, kesit önerisi, ısınma", Icons.Outlined.TrendingDown),
    DIVERSITY("Eşzamanlılık", "Daire %60/%40 ve bina katsayısı", Icons.Outlined.Apartment),
    GROUNDING("Topraklama", "Şerit, çubuk, temel, gözlü · Wenner", Icons.Outlined.Bolt),
    LIGHTNING("Paratoner", "Risk analizi ve aktif paratoner yarıçapı", Icons.Outlined.Thunderstorm),
    MOTOR("Motor & TMŞ", "Termik röle, sigorta ve kesit", Icons.Outlined.Settings),
    CABLE("Kablo Seçimi", "NYY tablosuna göre kesit", Icons.Outlined.Cable),
    METER("Sayaç Panosu", "Pano ölçüleri ve enerji odası", Icons.Outlined.GridView),
    LUX("Aydınlık Düzeyleri", "Mekan türüne göre lüks tablosu", Icons.Outlined.WbSunny),
    MATERIALS("Malzeme Keşfi", "Sigorta, KAKR, kompakt şalter fiyatları", Icons.Outlined.ShoppingCart),
    WEAK("Zayıf Akım / Fiber", "Bina fiber altyapısı ve daire içi dağılım", Icons.Outlined.Router),
    SMM("SMM Rehberi", "SMM olma adımları, evraklar, iş kolları", Icons.Outlined.Badge),
}

/** Kök: ana sayfa ↔ modüller; DXF "birlikte aç" ile gelirse doğrudan çizime */
@Composable
fun AppRoot(openUri: Uri?) {
    var route by rememberSaveable { mutableStateOf<Route?>(null) }
    var cadRequest by remember { mutableStateOf<CadRequest?>(null) }
    LaunchedEffect(openUri) {
        if (openUri != null) {
            cadRequest = CadRequest.OpenUri(openUri)
            route = Route.CAD
        }
    }
    val back = { route = null }
    BackHandler(enabled = route != null) { back() }
    when (route) {
        null -> HomeScreen {
            if (it == Route.CAD) cadRequest = CadRequest.ResumeLast
            route = it
        }
        Route.PROJECTS -> ProjectsScreen(onBack = back, onOpen = {
            cadRequest = it
            route = Route.CAD
        })
        Route.CAD -> CadScreen(cadRequest, onExit = back)
        Route.LIGHTING -> LightingModule(back)
        Route.LOAD -> LoadScheduleModule(back)
        Route.DROP -> VoltageDropModule(back)
        Route.DIVERSITY -> DiversityModule(back)
        Route.GROUNDING -> GroundingModule(back)
        Route.LIGHTNING -> LightningModule(back)
        Route.MOTOR -> MotorModule(back)
        Route.CABLE -> CableModule(back)
        Route.METER -> MeterPanelModule(back)
        Route.LUX -> LuxTableModule(back)
        Route.MATERIALS -> MaterialsModule(back)
        Route.WEAK -> WeakCurrentModule(back)
        Route.SMM -> SmmModule(back)
    }
}

/** MühenKit logosu: simgeyle aynı katmanlar (res/drawable/ic_launcher_*) */
@Composable
fun AppLogo(size: androidx.compose.ui.unit.Dp) {
    Box(Modifier.size(size).clip(RoundedCornerShape(size * 0.24f))) {
        Image(painterResource(R.drawable.ic_launcher_background), null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
        // uyarlanır simge 108 birim, içerik ortadaki 72'lik alanda: logo için biraz büyüt
        Image(painterResource(R.drawable.ic_launcher_foreground), null, Modifier.fillMaxSize().scale(1.35f), contentScale = ContentScale.Fit)
    }
}

// ───────────────────────── Ana sayfa: mavi devre kartı ─────────────────────────
// Eski uygulamanın PCB ana sayfası (MCU + çipler + bakır yollar + akım darbeleri), mavi lehim maskesiyle.

private object Pcb {
    val board = Color(0xFF082152)
    val board2 = Color(0xFF0C2E6E)
    val maskDot = Color(0xFF3D7BD9)
    val copper = Color(0xFFC08A3E)
    val copperHi = Color(0xFFF2B266)
    val gold = Color(0xFFE4C06A)
    val silk = Color(0xFFEFF3FB)
    val silkDim = Color(0xFF9FB6DC)
    val chip = Color(0xFF0E141C)
    val chip2 = Color(0xFF1A222D)
    val chipEdge = Color(0xFF2C3A4E)
    val led = listOf(Color(0xFF3DFF8E), Color(0xFFFFB627), Color(0xFF5CC8FF), Color(0xFFFF6B5E))
}

/** Kartın sabit desenleri: lehim maskesi noktaları, silik bakır yollar, vialar */
@Composable
private fun PcbBoard(modifier: Modifier) {
    Canvas(modifier) {
        drawRect(Brush.verticalGradient(listOf(Pcb.board2, Pcb.board)))
        val step = 18.dp.toPx()
        var y = step / 2
        while (y < size.height) {
            var x = step / 2
            while (x < size.width) {
                drawCircle(Pcb.maskDot.copy(alpha = 0.16f), 1.1.dp.toPx(), Offset(x, y))
                x += step
            }
            y += step
        }
        // silik arka plan yolları: 45° kırılımlı yatay hatlar ve uçlarında vialar
        val rnd = java.util.Random(11)
        val w = 1.6.dp.toPx()
        repeat(14) {
            val y0 = rnd.nextFloat() * size.height
            val x0 = rnd.nextFloat() * size.width * 0.6f
            val len = size.width * (0.2f + rnd.nextFloat() * 0.4f)
            val bend = 14.dp.toPx() * if (rnd.nextBoolean()) 1 else -1
            val p = Path().apply {
                moveTo(x0, y0); lineTo(x0 + len * 0.4f, y0); lineTo(x0 + len * 0.4f + abs(bend), y0 + bend); lineTo(x0 + len, y0 + bend)
            }
            drawPath(p, Pcb.copper.copy(alpha = 0.10f), style = Stroke(w))
            for (pt in listOf(Offset(x0, y0), Offset(x0 + len, y0 + bend))) {
                drawCircle(Pcb.copper.copy(alpha = 0.18f), 3.5.dp.toPx(), pt)
                drawCircle(Pcb.board, 1.4.dp.toPx(), pt)
            }
        }
    }
}

/** IC bacakları: üst/alt sıra ve yanlar */
private fun DrawScope.pins(r: Rect, top: Int, bottom: Int, sides: Int) {
    val pin = Brush.verticalGradient(listOf(Color(0xFFE6E8EA), Color(0xFF8C9298)))
    val pw = 5.dp.toPx()
    val ph = 8.dp.toPx()
    fun row(n: Int, y: Float) {
        val span = r.width - 40.dp.toPx()
        for (i in 0 until n) {
            val x = r.left + 20.dp.toPx() + span * (i + 0.5f) / n
            drawRoundRect(pin, Offset(x - pw / 2, y - ph / 2), Size(pw, ph), CornerRadius(1.dp.toPx()))
        }
    }
    row(top, r.top + 4.dp.toPx())
    row(bottom, r.bottom - 4.dp.toPx())
    val span = r.height - 30.dp.toPx()
    for (i in 0 until sides) {
        val y = r.top + 15.dp.toPx() + span * (i + 0.5f) / sides
        drawRoundRect(pin, Offset(r.left, y - pw / 2), Size(ph, pw), CornerRadius(1.dp.toPx()))
        drawRoundRect(pin, Offset(r.right - ph, y - pw / 2), Size(ph, pw), CornerRadius(1.dp.toPx()))
    }
}

/** MCU'dan her çipe bakır yol (45° kırılımlı) */
private fun tracePaths(mcu: Rect, chips: List<Rect>, unit: Float): List<Path> {
    val n = chips.size
    return chips.mapIndexed { i, r ->
        val pinX = mcu.left + 30 * unit + (mcu.width - 60 * unit) * (if (n == 1) 0.5f else i.toFloat() / (n - 1))
        val busY = r.top - 16 * unit - (i % 4) * 5 * unit
        val firstDrop = mcu.bottom + 18 * unit + (i % 6) * 5 * unit
        val endX = r.center.x + ((i % 3) - 1) * 10 * unit
        val trunkX = r.left - 13 * unit + (i % 3) * 4 * unit
        val dx = trunkX - pinX
        val diag = minOf(abs(dx), 14 * unit)
        val sg = if (dx < 0) -1f else 1f
        Path().apply {
            moveTo(pinX, mcu.bottom)
            lineTo(pinX, firstDrop)
            lineTo(pinX + diag * sg, firstDrop + diag)
            lineTo(trunkX - diag * sg, firstDrop + diag)
            lineTo(trunkX, firstDrop + diag * 2)
            lineTo(trunkX, busY - 8 * unit)
            lineTo(trunkX + 8 * unit, busY)
            lineTo(endX - 8 * unit, busY)
            lineTo(endX, busY + 8 * unit)
            lineTo(endX, r.top)
        }
    }
}

@Composable
private fun HomeScreen(onOpen: (Route) -> Unit) {
    var query by remember { mutableStateOf("") }
    var pressed by remember { mutableStateOf<Int?>(null) }
    val list = Route.entries.filter { query.isBlank() || "${it.title} ${it.subtitle}".contains(query, ignoreCase = true) }
    // akım darbeleri: 4 saniyelik döngü
    val flow by rememberInfiniteTransition(label = "akım").animateFloat(
        0f, 1f, infiniteRepeatable(tween(4000, easing = LinearEasing)), label = "akım",
    )
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val density = LocalDensity.current
        val wDp = maxWidth
        val hDp = maxHeight
        val cols = when { wDp > 1150.dp -> 4; wDp > 820.dp -> 3; else -> 2 }
        val side = 14.dp
        val gap = if (cols == 2) 22.dp else 34.dp
        val chipW = (minOf(wDp, 1240.dp) - side * 2 - gap * (cols - 1)) / cols
        val chipH = if (cols == 2) 128.dp else 148.dp
        val rowGap = 46.dp
        val left = (wDp - (chipW * cols + gap * (cols - 1))) / 2
        val mcuW = minOf(wDp - 28.dp, 560.dp)
        val mcuTop = 12.dp
        val mcuH = 168.dp
        val gridTop = mcuTop + mcuH + 64.dp
        val rows = (list.size + cols - 1) / cols
        val totalH = gridTop + (chipH + rowGap) * rows + 60.dp
        val scroll = rememberScrollState()
        with(density) {
            val mcu = Rect(((wDp - mcuW) / 2).toPx(), mcuTop.toPx(), ((wDp + mcuW) / 2).toPx(), (mcuTop + mcuH).toPx())
            val rects = list.indices.map { i ->
                val x = left + (chipW + gap) * (i % cols)
                val y = gridTop + (chipH + rowGap) * (i / cols)
                Rect(x.toPx(), y.toPx(), (x + chipW).toPx(), (y + chipH).toPx())
            }
            val paths = remember(rects, mcu) { tracePaths(mcu, rects, 1.dp.toPx()) }
            Box(Modifier.fillMaxSize()) {
                PcbBoard(Modifier.fillMaxSize())
                Box(Modifier.fillMaxSize().verticalScroll(scroll).statusBarsPadding().navigationBarsPadding()) {
                    Box(Modifier.fillMaxWidth().height(maxOf(totalH, hDp))) {
                        // yollar, vialar ve akım darbeleri
                        Canvas(Modifier.fillMaxSize()) {
                            val measures = paths.map { p -> PathMeasure().apply { setPath(p, false) } }
                            paths.forEachIndexed { i, p ->
                                val hot = i == pressed
                                if (hot) drawPath(p, Pcb.copperHi.copy(alpha = 0.22f), style = Stroke(9.dp.toPx(), cap = StrokeCap.Round))
                                drawPath(p, if (hot) Pcb.copperHi else Pcb.copper.copy(alpha = 0.6f),
                                    style = Stroke((if (hot) 3.4f else 2.4f).dp.toPx(), join = StrokeJoin.Round, cap = StrokeCap.Round))
                                val m = measures[i]
                                val start = m.getPosition(0f)
                                drawCircle(Pcb.gold, 4.5.dp.toPx(), start)
                                drawCircle(Pcb.board, 1.8.dp.toPx(), start)
                                val end = m.getPosition(m.length)
                                drawRect(Pcb.gold, Offset(end.x - 4.5.dp.toPx(), end.y - 2.5.dp.toPx()), Size(9.dp.toPx(), 5.dp.toPx()))
                                for (k in 0 until 2) {
                                    val phase = (flow + i * 0.137f + k * 0.5f) % 1f
                                    val pos = m.getPosition(m.length * phase)
                                    val c = if (hot) Color(0xFFFFB627) else Pcb.copperHi
                                    drawCircle(c.copy(alpha = 0.18f), (if (hot) 9f else 7f).dp.toPx(), pos)
                                    drawCircle(c.copy(alpha = 0.35f), (if (hot) 5f else 4f).dp.toPx(), pos)
                                    drawCircle(Color.White.copy(alpha = 0.92f), (if (hot) 2.6f else 1.9f).dp.toPx(), pos)
                                }
                            }
                            pins(mcu, 0, list.size.coerceAtMost(22), 6)
                            rects.forEach { pins(it, 4, 4, 0) }
                        }
                        // MCU: başlık + arama
                        Box(Modifier.offset((mcu.left / density.density).dp, mcuTop).size(mcuW, mcuH)) {
                            McuChip(query) { query = it }
                        }
                        // modül çipleri
                        list.forEachIndexed { i, r ->
                            Box(
                                Modifier.offset(left + (chipW + gap) * (i % cols), gridTop + (chipH + rowGap) * (i / cols)).size(chipW, chipH),
                            ) {
                                ModuleChip(i, r, lit = pressed == i, onPress = { pressed = if (it) i else null }) { onOpen(r) }
                            }
                        }
                        if (list.isEmpty()) {
                            Text("EŞLEŞEN MODÜL YOK", color = Pcb.silkDim, fontSize = 13.sp, letterSpacing = 2.sp,
                                modifier = Modifier.align(Alignment.TopCenter).padding(top = gridTop))
                        }
                        Text("MÜHENKİT REV 2.0 · ELEKTRİK · ${list.size} MODÜL", color = Pcb.silkDim.copy(alpha = 0.7f), fontSize = 10.sp,
                            letterSpacing = 1.5.sp, modifier = Modifier.align(Alignment.BottomCenter).padding(bottom = 18.dp))
                    }
                }
            }
        }
    }
}

@Composable
private fun McuChip(query: String, onQuery: (String) -> Unit) {
    Column(
        Modifier.fillMaxSize().padding(horizontal = 10.dp, vertical = 8.dp)
            .shadow(16.dp, RoundedCornerShape(6.dp))
            .background(Brush.linearGradient(listOf(Pcb.chip2, Pcb.chip)), RoundedCornerShape(6.dp))
            .border(1.5.dp, Pcb.chipEdge, RoundedCornerShape(6.dp)).padding(horizontal = 16.dp, vertical = 12.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            AppLogo(34.dp)
            Spacer(Modifier.width(10.dp))
            Text("MÜHENKİT", color = Pcb.silk, fontSize = 22.sp, fontWeight = FontWeight.ExtraBold, letterSpacing = 4.sp, modifier = Modifier.weight(1f))
            Box(Modifier.size(8.dp).clip(CircleShape).background(Pcb.led[0]))
            Spacer(Modifier.width(6.dp))
            Text("PWR", color = Pcb.silkDim, fontSize = 9.sp)
        }
        Spacer(Modifier.height(4.dp))
        Text("Elektrik proje ve hesap kiti — bir çipe dokun", color = Pcb.silkDim, fontSize = 11.sp, maxLines = 1)
        Spacer(Modifier.weight(1f))
        Row(
            Modifier.fillMaxWidth().height(40.dp).clip(RoundedCornerShape(8.dp)).background(Color(0xFF0A1018))
                .border(1.dp, Pcb.chipEdge, RoundedCornerShape(8.dp)).padding(horizontal = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(Icons.Outlined.Search, null, tint = Pcb.silkDim, modifier = Modifier.size(18.dp))
            Spacer(Modifier.width(8.dp))
            Box(Modifier.weight(1f)) {
                if (query.isEmpty()) Text("Modül ara… (ör. topraklama)", color = Pcb.silkDim.copy(alpha = 0.6f), fontSize = 13.sp)
                androidx.compose.foundation.text.BasicTextField(
                    query, onQuery, singleLine = true,
                    textStyle = androidx.compose.ui.text.TextStyle(color = Pcb.silk, fontSize = 13.sp),
                    cursorBrush = androidx.compose.ui.graphics.SolidColor(Pcb.copperHi),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            if (query.isNotEmpty()) Icon(Icons.Outlined.Close, "Temizle", tint = Pcb.silkDim, modifier = Modifier.size(18.dp).clickable { onQuery("") })
        }
    }
}

@Composable
private fun ModuleChip(i: Int, r: Route, lit: Boolean, onPress: (Boolean) -> Unit, onClick: () -> Unit) {
    val scale by animateFloatAsState(if (lit) 0.97f else 1f, label = "çip")
    // çizim ve projeler öne çıksın: beyaz gövde, siyah yazı
    val white = r == Route.CAD || r == Route.PROJECTS
    val bg = when {
        white && lit -> listOf(Color(0xFFFFF4E2), Color(0xFFE9DCC6))
        white -> listOf(Color.White, Color(0xFFE4EAF3))
        lit -> listOf(Color(0xFF243142), Color(0xFF151C26))
        else -> listOf(Pcb.chip2, Pcb.chip)
    }
    val titleC = if (white) Color(0xFF0B0F14) else Pcb.silk
    val subC = if (white) Color(0xFF2B3542) else Pcb.silkDim
    val iconC = when { lit -> Pcb.copperHi; white -> Color(0xFF0A2C7A); else -> Pcb.gold }
    Column(
        Modifier.fillMaxSize().scale(scale).padding(vertical = 7.dp, horizontal = 2.dp)
            .shadow(10.dp, RoundedCornerShape(5.dp))
            .background(Brush.linearGradient(bg), RoundedCornerShape(5.dp))
            .border(1.3.dp, if (lit) Pcb.copperHi.copy(alpha = 0.85f) else if (white) Color(0xFFB8C4D6) else Pcb.chipEdge, RoundedCornerShape(5.dp))
            .pointerInput(r) {
                detectTapGestures(
                    onPress = {
                        onPress(true)
                        tryAwaitRelease()
                        onPress(false)
                    },
                    onTap = { onClick() },
                )
            }
            .padding(start = 12.dp, end = 10.dp, top = 8.dp, bottom = 8.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(r.icon, null, tint = iconC, modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(8.dp))
            Text("U${i + 1}", color = subC, fontSize = 10.sp)
            Spacer(Modifier.weight(1f))
            val led = Pcb.led[i % Pcb.led.size]
            Box(Modifier.size(7.dp).clip(CircleShape).background(if (lit) led else led.copy(alpha = 0.25f)))
        }
        Spacer(Modifier.height(6.dp))
        Text(r.title.uppercase(java.util.Locale("tr")), color = titleC, fontSize = 13.sp, fontWeight = FontWeight.ExtraBold,
            letterSpacing = 1.sp, maxLines = 1, overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis)
        Spacer(Modifier.height(3.dp))
        Text(r.subtitle, color = subC, fontSize = 11.sp, maxLines = 2, lineHeight = 14.sp,
            overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
        Text("MK-${"%02d".format(i + 1)}-${r.name.take(4)}", color = if (white) Color(0xFF6A7689) else Color(0xFF5C6B82), fontSize = 9.sp, letterSpacing = 1.5.sp)
    }
}
