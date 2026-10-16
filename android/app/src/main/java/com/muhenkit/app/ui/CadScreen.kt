package com.muhenkit.app.ui

import android.net.Uri
import androidx.activity.compose.BackHandler
import androidx.activity.compose.LocalActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.Redo
import androidx.compose.material.icons.automirrored.outlined.Undo
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.animation.*
import androidx.compose.foundation.verticalScroll
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import com.muhenkit.app.cad.CadGlView
import com.muhenkit.app.cad.CadRenderer
import com.muhenkit.app.cad.CadScene
import com.muhenkit.app.cad.CadSymbol
import com.muhenkit.app.cad.SymbolLibrary
import android.content.Intent
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.nativeCanvas
import kotlinx.coroutines.launch

private val BarColor = Color(0xE6141719)

/** Çizim ekranı neyle açılsın */
sealed interface CadRequest {
    data class OpenUri(val uri: Uri) : CadRequest
    data class OpenProject(val id: String) : CadRequest
    data object NewBlank : CadRequest
    data object Demo : CadRequest
    /** Ana sayfadan "DXF Çizim": son projeye devam */
    data object ResumeLast : CadRequest
}

/** Ekran işlemi (eskiden kaydedilmemiş değişiklik sorusuyla korunuyordu; artık otomatik kayıt var) */
private sealed interface Pending {
    data object Exit : Pending
    data object OpenFile : Pending
    data class OpenUri(val uri: Uri) : Pending
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CadScreen(request: CadRequest?, onExit: () -> Unit = {}) {
    val context = LocalContext.current
    val activity = LocalActivity.current
    val scope = rememberCoroutineScope()
    val ed = remember { CadEditor(context.applicationContext, scope) }
    DisposableEffect(Unit) { onDispose { ed.dispose() } }
    var showLayers by remember { mutableStateOf(false) }
    var showProps by remember { mutableStateOf(false) }
    var showSymbols by remember { mutableStateOf(false) }
    var showCounts by remember { mutableStateOf(false) }
    var showExport by remember { mutableStateOf(false) }
    var toolCat by remember { mutableStateOf(ToolCat.EDIT) }
    var topBarHeight by remember { mutableStateOf(64.dp) }
    var flyout by remember { mutableStateOf<ToolCat?>(null) }
    // üst çubuk gizli mi (tercih saklanır)
    val uiPrefs = remember { context.getSharedPreferences("ui", android.content.Context.MODE_PRIVATE) }
    var topHidden by remember { mutableStateOf(uiPrefs.getBoolean("topHidden", false)) }
    fun setTopHidden(h: Boolean) {
        topHidden = h
        uiPrefs.edit().putBoolean("topHidden", h).apply()
    }
    var showTables by remember { mutableStateOf(false) }
    var tableForm by remember { mutableStateOf<TableKind?>(null) }
    var exporting by remember { mutableStateOf(false) }
    var pendingBytes by remember { mutableStateOf<ByteArray?>(null) }
    var numberAsk by remember { mutableStateOf<CadTool?>(null) }
    var textEdit by remember { mutableStateOf<Pair<Int, String>?>(null) }
    var pending by remember { mutableStateOf<Pending?>(null) }
    val snackbar = remember { SnackbarHostState() }

    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri -> uri?.let(ed::open) }
    // dışa aktarımı dosyaya kaydet (biçime göre iki başlatıcı)
    fun writeTo(uri: android.net.Uri?) {
        val b = pendingBytes ?: return
        pendingBytes = null
        if (uri == null) return
        scope.launch {
            try {
                kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                    // bazı sağlayıcılar (Drive vb.) "wt" kipini desteklemiyor: "w" ile yeniden dene
                    val out = runCatching { context.contentResolver.openOutputStream(uri, "wt") }.getOrNull()
                        ?: context.contentResolver.openOutputStream(uri, "w")
                        ?: throw java.io.IOException("Dosya açılamadı")
                    out.use { it.write(b) }
                }
                ed.message = "Dışa aktarıldı"
            } catch (e: Exception) {
                ed.message = "Kaydedilemedi: ${e.message}"
            }
        }
    }
    val savePdf = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/pdf")) { writeTo(it) }
    val savePng = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("image/png")) { writeTo(it) }

    /** Dışa aktar: [action] 0 paylaş, 1 kaydet, 2 yazdır */
    fun export(o: ExportOptions, action: Int) {
        if (exporting) return
        exporting = true
        scope.launch {
            try {
                val bytes = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.Default) { ed.exportBytes(o) } ?: return@launch
                val file = "${ed.baseName()}.${o.format.ext}"
                when (action) {
                    0 -> Exporter.share(context, bytes, file, o.format.mime, "Çizimi paylaş")
                    1 -> {
                        pendingBytes = bytes
                        if (o.format == ExportFormat.PDF) savePdf.launch(file) else savePng.launch(file)
                    }
                    else -> Exporter.print(context, bytes, ed.baseName())
                }
                showExport = false
            } catch (e: OutOfMemoryError) {
                ed.message = "Bellek yetmedi: A4 ya da PDF deneyin."
            } catch (e: Exception) {
                ed.message = "Dışa aktarılamadı: ${e.message}"
            } finally {
                exporting = false
            }
        }
    }

    fun shareDxf() = scope.launch {
        val b = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.Default) { ed.dxfBytes() } ?: return@launch
        Exporter.share(context, b, ed.suggestedName(), "application/dxf", "DXF dosyasını paylaş")
    }

    val saveAs = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/dxf")) { uri ->
        if (uri != null) scope.launch { if (!ed.saveTo(uri)) ed.message = "Kaydedilemedi." }
    }

    fun save(then: (() -> Unit)? = null) = scope.launch {
        if (ed.save()) then?.invoke() else saveAs.launch(ed.suggestedName())
    }

    fun run(p: Pending) {
        when (p) {
            Pending.Exit -> onExit()
            Pending.OpenFile -> picker.launch(arrayOf("*/*"))
            is Pending.OpenUri -> ed.open(p.uri)
        }
    }

    // projeler otomatik kaydedildiği için soru sormadan geç (açık çizim önce kaydedilir)
    fun guarded(p: Pending) = run(p)

    LaunchedEffect(request) {
        when (request) {
            is CadRequest.OpenUri -> ed.open(request.uri)
            is CadRequest.OpenProject -> ed.openProject(request.id)
            CadRequest.NewBlank -> ed.newBlank()
            CadRequest.Demo -> ed.openDemo()
            CadRequest.ResumeLast -> com.muhenkit.app.cad.ProjectStore.lastProject(context)?.let { ed.openProject(it) }
            null -> {}
        }
    }
    // değişiklik varsa 45 saniyede bir otomatik kayıt
    LaunchedEffect(Unit) {
        while (true) {
            kotlinx.coroutines.delay(45_000)
            ed.autosave()
        }
    }
    LaunchedEffect(ed.message) {
        ed.message?.let {
            snackbar.showSnackbar(it)
            ed.message = null
        }
    }
    // geri: üst katman (AppRoot) ana sayfaya döner; çıkışta otomatik kayıt yapılır

    // Surface dışındaki yazı ve simgeler varsayılan olarak siyah çıkar: koyu zemin için açık renk ver
    CompositionLocalProvider(LocalContentColor provides MaterialTheme.colorScheme.onSurface) {
    BoxWithConstraints(Modifier.fillMaxSize().background(Color(0xFF0B0E10))) {
        // 600 dp ve üstü (yatay telefon, tablet): sol araç çubuğu; dar ekran: alt sekmeler
        val wide = maxWidth >= 600.dp
        AndroidView(
            factory = { ctx -> CadGlView(ctx).also { ed.glView = it } },
            modifier = Modifier.fillMaxSize(),
            onRelease = { it.onPause() },
        )

        AnimatedVisibility(
            visible = !topHidden,
            enter = slideInVertically { -it } + fadeIn(),
            exit = slideOutVertically { -it } + fadeOut(),
            modifier = Modifier.align(Alignment.TopCenter),
        ) {
            TopBar(ed, onFit = { ed.glView?.fit(animated = true) }, onLayers = { showLayers = true }, onHeight = { topBarHeight = it },
                onOpen = { guarded(Pending.OpenFile) }, onSave = { save() }, onSaveAs = { saveAs.launch(ed.suggestedName()) },
                onCounts = { showCounts = true }, onExport = { showExport = true }, onShareDxf = { shareDxf() },
                onHome = onExit, onNewBlank = { ed.newBlank() },
                onHide = { setTopHidden(true) }, modifier = Modifier)
        }
        // gizliyken köşede geri getirme düğmesi
        if (topHidden) {
            Box(
                Modifier.align(Alignment.TopEnd).statusBarsPadding()
                    .windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.End))
                    .padding(8.dp).size(40.dp).clip(CircleShape).background(BarColor).clickable { setTopHidden(false) },
                contentAlignment = Alignment.Center,
            ) { Icon(Icons.Outlined.ExpandMore, "Üst çubuğu göster") }
        }

        val scene = ed.scene
        when {
            ed.loading -> Column(Modifier.align(Alignment.Center), horizontalAlignment = Alignment.CenterHorizontally) {
                CircularProgressIndicator()
                Spacer(Modifier.height(12.dp))
                Text("Çizim okunuyor…")
            }
            scene == null || ed.error != null -> EmptyState(ed.error, hasScene = scene != null,
                onOpen = { guarded(Pending.OpenFile) }, onNewBlank = { ed.newBlank() }, modifier = Modifier.align(Alignment.Center))
        }

        if (scene != null) {
            val onProps = { showProps = true }
            val onSyms = { showSymbols = true }
            val onTbls = { showTables = true }
            if (wide) {
                SideTools(ed, flyout, { flyout = it }, onProps, onSyms, onTbls,
                    Modifier.align(Alignment.TopStart)
                        .then(if (topHidden) Modifier.statusBarsPadding() else Modifier.padding(top = topBarHeight))
                        .windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Start)))
                // ipucu üstte: alttaki araç paneliyle (Sil vb.) çakışmasın; panel açılınca sağa kayar
                val hintStart by animateDpAsState(RAIL_W + if (flyout != null) SIDE_W else 0.dp, label = "ipucu")
                Box(
                    Modifier.align(Alignment.TopStart)
                        .then(if (topHidden) Modifier.statusBarsPadding() else Modifier.padding(top = topBarHeight))
                        .windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Horizontal))
                        .padding(start = hintStart, end = if (topHidden) 56.dp else 0.dp),
                ) {
                    HintBar(ed, onNumber = { numberAsk = ed.tool }, onEditText = { textEdit = ed.selectedText() }, onSymbols = onSyms,
                        modifier = Modifier.widthIn(max = 560.dp))
                }
                SnackbarHost(
                    snackbar,
                    Modifier.align(Alignment.BottomCenter).windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Horizontal + WindowInsetsSides.Bottom))
                        .padding(start = RAIL_W),
                )
            } else {
                Column(Modifier.align(Alignment.BottomCenter).fillMaxWidth()) {
                    SnackbarHost(snackbar, Modifier.align(Alignment.CenterHorizontally))
                    HintBar(ed, onNumber = { numberAsk = ed.tool }, onEditText = { textEdit = ed.selectedText() }, onSymbols = onSyms)
                    BottomTools(ed, toolCat, { toolCat = it }, onProps, onSyms, onTbls)
                }
            }
        } else {
            SnackbarHost(snackbar, Modifier.align(Alignment.BottomCenter).navigationBarsPadding())
        }
    }
    }

    val s = ed.scene
    if (showLayers && s != null) {
        ModalBottomSheet(onDismissRequest = { showLayers = false }) {
            LayerSheet(s, ed.hidden, ed.activeLayer, onActive = { ed.activeLayer = it }, onChange = ed::changeHidden)
        }
    }

    if (showSymbols && s != null) {
        ModalBottomSheet(onDismissRequest = { showSymbols = false }) {
            SymbolSheet(ed.symbol) {
                ed.startSymbol(it)
                showSymbols = false
            }
        }
    }

    if (showCounts && s != null) {
        val rows = remember(ed.undoCount, ed.redoCount) { ed.counts() }
        ModalBottomSheet(onDismissRequest = { showCounts = false }) {
            CountSheet(ed.suggestedName(), rows) { text ->
                context.startActivity(Intent.createChooser(Intent(Intent.ACTION_SEND).apply {
                    type = "text/plain"
                    putExtra(Intent.EXTRA_SUBJECT, "Keşif listesi — ${ed.suggestedName()}")
                    putExtra(Intent.EXTRA_TEXT, text)
                }, "Keşif listesini paylaş"))
            }
        }
    }

    if (showTables && s != null) {
        ModalBottomSheet(onDismissRequest = { showTables = false }) {
            TableChooser {
                showTables = false
                tableForm = it
            }
        }
    }
    tableForm?.let { kind ->
        val done: (CadSymbol) -> Unit = {
            tableForm = null
            ed.startTable(it)
        }
        when (kind) {
            TableKind.LIGHTING -> LightingForm(ed.textHeight, onDismiss = { tableForm = null }, onDone = done)
            TableKind.POWER -> PowerForm(ed.textHeight, onDismiss = { tableForm = null }, onDone = done)
            TableKind.TITLE -> TitleForm(ed.textHeight, onDismiss = { tableForm = null }, onDone = done)
        }
    }

    if (showExport && s != null) {
        ModalBottomSheet(onDismissRequest = { if (!exporting) showExport = false }) {
            ExportSheet(exporting, onGo = ::export)
        }
    }

    if (showProps && s != null) {
        ModalBottomSheet(onDismissRequest = { showProps = false }) {
            PropsSheet(s, ed.selection.size, onLayer = {
                ed.applyLayer(it)
                showProps = false
            }, onColor = {
                ed.applyColor(it)
                showProps = false
            })
        }
    }

    numberAsk?.let { t ->
        val rotate = t == CadTool.ROTATE
        when (t) {
            CadTool.SYMBOL -> NumberDialog(
                title = "Sembol ölçeği", label = "Çarpan (1 = örnek projedeki boy)",
                initial = trimNum(ed.symbolScale), onDismiss = { numberAsk = null },
                onOk = { v -> numberAsk = null; if (v > 0) ed.symbolScale = v },
            )
            CadTool.LINE, CadTool.PLINE -> NumberDialog2(
                title = "Uzunluk ve açı", label1 = "Uzunluk", init1 = "", label2 = "Açı (derece, 0 = sağa, 90 = yukarı)",
                init2 = trimNum(ed.lastAngle), onDismiss = { numberAsk = null },
                onOk = { a, b -> numberAsk = null; ed.numericDraw(a, b) },
            )
            CadTool.RECT -> NumberDialog2(
                title = "Dikdörtgen ölçüsü", label1 = "Genişlik (− sola)", init1 = "", label2 = "Yükseklik (− aşağı)", init2 = "",
                onDismiss = { numberAsk = null }, onOk = { a, b -> numberAsk = null; ed.numericDraw(a, b) },
            )
            CadTool.CIRCLE -> NumberDialog(
                title = "Daire", label = "Yarıçap", initial = "", onDismiss = { numberAsk = null },
                onOk = { v -> numberAsk = null; ed.numericDraw(v, 0.0) },
            )
            CadTool.OFFSET -> NumberDialog(
                title = "Öteleme mesafesi", label = "Mesafe", initial = trimNum(ed.offsetDist), onDismiss = { numberAsk = null },
                onOk = { v -> numberAsk = null; if (v > 0) ed.offsetDist = v },
            )
            else -> NumberDialog(
            title = if (rotate) "Döndürme açısı" else "Ölçek faktörü",
            label = if (rotate) "Derece (saat yönünün tersi +)" else "Çarpan (ör. 0.5 veya 2)",
            initial = if (rotate) "90" else "2",
            onDismiss = { numberAsk = null },
            onOk = { v ->
                numberAsk = null
                if (rotate) ed.rotateBy(v) else ed.scaleBy(v)
            },
        )
        }
    }

    textEdit?.let { (id, cur) ->
        EditTextDialog(cur, onDismiss = { textEdit = null }, onOk = {
            textEdit = null
            ed.setText(id, it)
        })
    }

    ed.textAt?.let { TextDialog(ed.textHeight, onDismiss = { ed.textAt = null }, onOk = ed::addText) }

    pending?.let { p ->
        AlertDialog(
            onDismissRequest = { pending = null },
            title = { Text("Kaydedilmemiş değişiklikler") },
            text = { Text("\"${ed.suggestedName()}\" dosyasındaki değişiklikler kaydedilsin mi?") },
            confirmButton = {
                TextButton(onClick = {
                    pending = null
                    save { run(p) }
                }) { Text("Kaydet") }
            },
            dismissButton = {
                Row {
                    TextButton(onClick = { pending = null }) { Text("İptal") }
                    TextButton(onClick = {
                        pending = null
                        run(p)
                    }) { Text("Kaydetme", color = MaterialTheme.colorScheme.error) }
                }
            },
        )
    }
}

@Composable
private fun TopBar(
    ed: CadEditor, onFit: () -> Unit, onLayers: () -> Unit, onOpen: () -> Unit, onSave: () -> Unit, onSaveAs: () -> Unit,
    onCounts: () -> Unit, onExport: () -> Unit, onShareDxf: () -> Unit, modifier: Modifier, onHeight: (androidx.compose.ui.unit.Dp) -> Unit = {},
    onHide: () -> Unit = {},
    onHome: () -> Unit = {},
    onNewBlank: () -> Unit = {},
) {
    val scene = ed.scene
    var menu by remember { mutableStateOf(false) }
    val density = androidx.compose.ui.platform.LocalDensity.current
    Surface(
        color = BarColor, contentColor = MaterialTheme.colorScheme.onSurface,
        modifier = modifier.fillMaxWidth().onGloballyPositioned { onHeight(with(density) { it.size.height.toDp() }) },
    ) {
        Row(
            Modifier.statusBarsPadding().windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Horizontal))
                .padding(start = 16.dp, end = 4.dp, top = 4.dp, bottom = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = onHome, modifier = Modifier.offset(x = (-12).dp)) { Icon(Icons.Outlined.Home, "Ana sayfa") }
            Column(Modifier.weight(1f).offset(x = (-8).dp)) {
                Text(
                    (scene?.name ?: "DXF Çizim") + if (ed.dirty) " •" else "",
                    style = MaterialTheme.typography.titleMedium,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                scene?.let { s ->
                    Text(
                        "${s.entityCount} nesne · ${s.layers.size} katman · ${s.parseMs} ms",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
            if (ed.busy) CircularProgressIndicator(Modifier.size(18.dp).padding(end = 4.dp), strokeWidth = 2.dp)
            if (scene != null) {
                IconButton(onClick = onFit) { Icon(Icons.Outlined.ZoomOutMap, "Sığdır") }
                IconButton(onClick = onLayers) { Icon(Icons.Outlined.Layers, "Katmanlar") }
                IconButton(onClick = onSave, enabled = ed.dirty) { Icon(Icons.Outlined.Save, "Kaydet") }
            }
            IconButton(onClick = onHide) { Icon(Icons.Outlined.ExpandLess, "Üst çubuğu gizle") }
            Box {
                IconButton(onClick = { menu = true }) { Icon(Icons.Outlined.MoreVert, "Menü") }
                DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                    DropdownMenuItem(text = { Text("DXF aç") }, leadingIcon = { Icon(Icons.Outlined.FolderOpen, null) },
                        onClick = { menu = false; onOpen() })
                    DropdownMenuItem(text = { Text("Yeni boş proje") }, leadingIcon = { Icon(Icons.Outlined.NoteAdd, null) },
                        onClick = { menu = false; onNewBlank() })
                    if (scene != null) {
                        DropdownMenuItem(text = { Text("Farklı kaydet") }, leadingIcon = { Icon(Icons.Outlined.SaveAs, null) },
                            onClick = { menu = false; onSaveAs() })
                        DropdownMenuItem(text = { Text("Keşif listesi") }, leadingIcon = { Icon(Icons.Outlined.Checklist, null) },
                            onClick = { menu = false; onCounts() })
                        DropdownMenuItem(text = { Text("PDF / resim / yazdır") }, leadingIcon = { Icon(Icons.Outlined.PictureAsPdf, null) },
                            onClick = { menu = false; onExport() })
                        DropdownMenuItem(text = { Text("DXF'i paylaş") }, leadingIcon = { Icon(Icons.Outlined.Share, null) },
                            onClick = { menu = false; onShareDxf() })
                    }
                }
            }
        }
    }
}

@Composable
private fun EmptyState(error: String?, hasScene: Boolean, onOpen: () -> Unit, onNewBlank: () -> Unit, modifier: Modifier) {
    Column(modifier.padding(32.dp), horizontalAlignment = Alignment.CenterHorizontally) {
        if (!hasScene) {
            Icon(Icons.Outlined.Architecture, null, Modifier.size(56.dp), tint = MaterialTheme.colorScheme.primary)
            Spacer(Modifier.height(12.dp))
        }
        Text(
            error ?: "Bir DXF dosyası açın.\nDWG dosyalarını AutoCAD'de \"Farklı Kaydet → DXF\" ile kaydedin.",
            textAlign = TextAlign.Center,
            color = if (error != null) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = if (hasScene) Modifier.background(BarColor, RoundedCornerShape(8.dp)).padding(12.dp) else Modifier,
        )
        Spacer(Modifier.height(16.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = onOpen) {
                Icon(Icons.Outlined.FolderOpen, null)
                Spacer(Modifier.width(8.dp))
                Text("DXF aç")
            }
            OutlinedButton(onClick = onNewBlank) {
                Icon(Icons.Outlined.NoteAdd, null)
                Spacer(Modifier.width(8.dp))
                Text("Boş proje")
            }
        }
    }
}

/** Etkin aracın ipucu ya da seçim bilgisi, bağlama göre düğmelerle. */
@Composable
private fun HintBar(ed: CadEditor, onNumber: () -> Unit, onEditText: () -> Unit, onSymbols: () -> Unit, modifier: Modifier = Modifier.fillMaxWidth()) {
    val pivotTool = ed.tool == CadTool.ROTATE || ed.tool == CadTool.SCALE
    val drawTool = ed.tool == CadTool.LINE || ed.tool == CadTool.PLINE || ed.tool == CadTool.CIRCLE || ed.tool == CadTool.RECT
    val text = when {
        pivotTool && ed.pivot != null -> ed.liveText
            ?: if (ed.tool == CadTool.ROTATE) "Bir noktadan sürükleyerek döndürün ya da açı seçin." else "Bir noktadan sürükleyerek ölçekleyin ya da çarpan seçin."
        ed.tool == CadTool.MIRROR && ed.selection.isNotEmpty() -> ed.tool.hint
        ed.tool == CadTool.SELECT && ed.selectionInfo != null -> ed.selectionInfo!!
        drawTool && ed.lineStart != null -> ed.liveText ?: when (ed.tool) {
            CadTool.CIRCLE -> "Yarıçap kadar sürükleyin ya da yarıçapı girin."
            CadTool.RECT -> "Karşı köşeye dokunun ya da ölçüyü girin."
            else -> "Sonraki noktaya dokunun ya da uzunluk girin."
        }
        ed.tool == CadTool.SYMBOL && ed.symbolFixedScale != null -> "${ed.symbol?.name ?: "Tablo"}: sol üst köşesini koyacağın yere dokun."
        ed.tool == CadTool.SYMBOL -> "${ed.symbol?.name ?: "Sembol"}: yerleştirmek için dokunun. Ölçek ×${trimNum(ed.symbolScale)}"
        ed.tool == CadTool.OFFSET -> if (ed.offsetTarget == null) "Mesafe ${trimNum(ed.offsetDist)} · Ötelenecek nesneye dokunun." else "Hangi tarafa? O tarafa dokunun."
        ed.tool == CadTool.MEASURE && ed.measureText != null -> ed.measureText!!
        ed.tool == CadTool.DIMENSION -> ed.liveText ?: when {
            ed.dimP1 == null -> "Ölçülecek ilk noktaya dokunun (uçlara yakalar)."
            ed.dimP2 == null -> "İkinci noktaya dokunun ya da sürükleyin."
            else -> "Ölçü çizgisinin yerine dokunun."
        }
        (ed.tool == CadTool.MOVE || ed.tool == CadTool.COPY || ed.tool == CadTool.TEXT || ed.tool == CadTool.SYMBOL) && ed.liveText != null -> ed.liveText!!
        ed.tool == CadTool.SELECT && ed.selection.isEmpty() -> "Seçmek için nesneye dokunun. Pencere seçimi: uzun basıp sürükleyin."
        else -> ed.tool.hint
    }
    // yalnız yol gösteren (canlı değer ya da düğme içermeyen) yazılar kendiliğinden küçülür
    val selectHint = ed.tool == CadTool.SELECT && ed.selection.isEmpty()
    val generic = (text == ed.tool.hint || selectHint || (pivotTool && ed.pivot == null)) &&
        ed.tool != CadTool.SYMBOL && ed.tool != CadTool.OFFSET && ed.tool != CadTool.MIRROR
    var collapsed by remember(text) { mutableStateOf(false) }
    LaunchedEffect(text, collapsed) {
        if (generic && !collapsed) {
            kotlinx.coroutines.delay(4000)
            collapsed = true
        }
    }
    if (generic && collapsed) {
        Box(modifier.padding(horizontal = 12.dp, vertical = 6.dp)) {
            Box(
                Modifier.size(36.dp).clip(CircleShape).background(BarColor).clickable { collapsed = false },
                contentAlignment = Alignment.Center,
            ) { Icon(Icons.Outlined.Info, "İpucunu göster", tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(20.dp)) }
        }
        return
    }
    Column(
        modifier.padding(horizontal = 12.dp, vertical = 6.dp)
            .background(BarColor, RoundedCornerShape(10.dp)).padding(start = 12.dp, end = 4.dp),
    ) {
        Text(text, style = MaterialTheme.typography.bodySmall, color = Color.White, modifier = Modifier.padding(top = 10.dp, bottom = 10.dp, end = 8.dp))
        // bağlam düğmeleri: dar ekranda yazıyı ezmesin diye ayrı, kaydırılabilir satırda
        Row(Modifier.horizontalScroll(rememberScrollState()), verticalAlignment = Alignment.CenterVertically) {
        if (drawTool && ed.lineStart != null) TextButton(onClick = onNumber) { Text("Değer") }
        if ((ed.tool == CadTool.LINE || ed.tool == CadTool.PLINE) && ed.lineStart != null) TextButton(onClick = ed::finishLine) { Text("Bitir") }
        if (ed.tool == CadTool.PLINE && ed.plinePts.size >= 3) TextButton(onClick = { ed.finishPolyline(true) }) { Text("Kapat") }
        if (ed.tool == CadTool.OFFSET) TextButton(onClick = onNumber) { Text("Mesafe") }
        if (ed.tool == CadTool.SYMBOL) {
            for (deg in listOf(0.0, 90.0, 180.0, 270.0)) {
                val on = ed.symbolRot == deg
                Text(
                    "${deg.toInt()}°",
                    fontSize = 13.sp,
                    color = if (on) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.primary,
                    modifier = Modifier.padding(horizontal = 2.dp)
                        .background(if (on) MaterialTheme.colorScheme.primary else Color.Transparent, RoundedCornerShape(8.dp))
                        .border(1.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.5f), RoundedCornerShape(8.dp))
                        .clickable { ed.symbolRot = deg }.padding(horizontal = 8.dp, vertical = 6.dp),
                )
            }
            if (ed.symbolFixedScale == null) {
                TextButton(onClick = onNumber) { Text("Ölçek") }
                TextButton(onClick = onSymbols) { Text("Değiştir") }
            }
        }
        if (pivotTool && ed.pivot != null) {
            if (ed.tool == CadTool.ROTATE) {
                ChipButton("90°") { ed.rotateBy(90.0) }
                ChipButton("−90°") { ed.rotateBy(-90.0) }
                ChipButton("180°") { ed.rotateBy(180.0) }
            } else {
                ChipButton("×0.5") { ed.scaleBy(0.5) }
                ChipButton("×2") { ed.scaleBy(2.0) }
            }
            TextButton(onClick = onNumber) { Text("Değer") }
            TextButton(onClick = ed::cancelPivot) { Text("İptal") }
        }
        if (ed.tool == CadTool.MIRROR) {
            TextButton(onClick = { ed.mirrorKeep = !ed.mirrorKeep }) { Text(if (ed.mirrorKeep) "Orijinal: koru" else "Orijinal: sil") }
        }
        if (ed.tool == CadTool.SELECT && ed.selection.size == 1 && ed.selectionInfo?.contains("azı") == true) {
            TextButton(onClick = onEditText) { Text("Düzenle") }
        }
        if (ed.selection.isNotEmpty() && ed.tool == CadTool.SELECT) {
            TextButton(onClick = ed::clearSelection) { Text("Seçimi kaldır") }
        }
        }
    }
}

/** Araç kategorileri: telefonda alt sekmeler, geniş ekranda sol çubuk */
enum class ToolCat(val label: String, val icon: ImageVector) {
    DRAW("Çiz", Icons.Outlined.Draw),
    EDIT("Düzenle", Icons.Outlined.Edit),
    INSERT("Ekle", Icons.Outlined.AddBox),
    SETTINGS("Ayarlar", Icons.Outlined.Tune),
}

private class ToolItem(val icon: ImageVector, val label: String, val active: Boolean, val enabled: Boolean = true, val onClick: () -> Unit)

private fun toolsOf(cat: ToolCat, ed: CadEditor, onProps: () -> Unit, onSymbols: () -> Unit, onTables: () -> Unit): List<ToolItem> {
    val sel = ed.selection.isNotEmpty()
    val free = !ed.busy
    return when (cat) {
        ToolCat.DRAW -> listOf(
            ToolItem(Icons.Outlined.Timeline, "Çizgi", ed.tool == CadTool.LINE) { ed.selectTool(CadTool.LINE) },
            ToolItem(Icons.Outlined.Polyline, "Ç.Çizgi", ed.tool == CadTool.PLINE) { ed.selectTool(CadTool.PLINE) },
            ToolItem(Icons.Outlined.Circle, "Daire", ed.tool == CadTool.CIRCLE) { ed.selectTool(CadTool.CIRCLE) },
            ToolItem(Icons.Outlined.CropSquare, "Dikdörtgen", ed.tool == CadTool.RECT) { ed.selectTool(CadTool.RECT) },
            ToolItem(Icons.Outlined.TextFields, "Yazı", ed.tool == CadTool.TEXT) { ed.selectTool(CadTool.TEXT) },
            ToolItem(Icons.Outlined.SquareFoot, "Ölçü", ed.tool == CadTool.DIMENSION) { ed.selectTool(CadTool.DIMENSION) },
            ToolItem(Icons.Outlined.Straighten, "Ölç", ed.tool == CadTool.MEASURE) { ed.selectTool(CadTool.MEASURE) },
        )
        ToolCat.EDIT -> listOf(
            ToolItem(Icons.Outlined.TouchApp, "Seç", ed.tool == CadTool.SELECT) { ed.selectTool(CadTool.SELECT) },
            ToolItem(Icons.Outlined.OpenWith, "Taşı", ed.tool == CadTool.MOVE, sel) { ed.selectTool(CadTool.MOVE) },
            ToolItem(Icons.Outlined.ContentCopy, "Kopyala", ed.tool == CadTool.COPY, sel) { ed.selectTool(CadTool.COPY) },
            ToolItem(Icons.Outlined.RotateRight, "Döndür", ed.tool == CadTool.ROTATE, sel) { ed.selectTool(CadTool.ROTATE) },
            ToolItem(Icons.Outlined.OpenInFull, "Ölçekle", ed.tool == CadTool.SCALE, sel) { ed.selectTool(CadTool.SCALE) },
            ToolItem(Icons.Outlined.Flip, "Ayna", ed.tool == CadTool.MIRROR, sel) { ed.selectTool(CadTool.MIRROR) },
            ToolItem(Icons.Outlined.ControlPointDuplicate, "Ötele", ed.tool == CadTool.OFFSET) { ed.selectTool(CadTool.OFFSET) },
            ToolItem(Icons.Outlined.Palette, "Özellik", false, sel && free) { onProps() },
            ToolItem(Icons.Outlined.Workspaces, "Grupla", false, ed.selection.size >= 2 && free) { ed.groupSelection() },
            ToolItem(Icons.Outlined.CallSplit, "Çöz", false, sel && free) { ed.explodeSelection() },
            ToolItem(Icons.Outlined.Delete, "Sil", false, sel && free) { ed.deleteSelection() },
        )
        ToolCat.INSERT -> listOf(
            ToolItem(Icons.Outlined.ElectricalServices, "Sembol", ed.tool == CadTool.SYMBOL && ed.symbolFixedScale == null) { onSymbols() },
            ToolItem(Icons.Outlined.TableChart, "Tablo", ed.tool == CadTool.SYMBOL && ed.symbolFixedScale != null) { onTables() },
            ToolItem(Icons.Outlined.TextFields, "Yazı", ed.tool == CadTool.TEXT) { ed.selectTool(CadTool.TEXT) },
        )
        ToolCat.SETTINGS -> listOf(
            ToolItem(Icons.Outlined.Adjust, "Mıknatıs", ed.snapOn) { ed.snapOn = !ed.snapOn },
            ToolItem(Icons.Outlined.Explore, "Açı 15°", ed.angleSnap) { ed.angleSnap = !ed.angleSnap },
            ToolItem(Icons.Outlined.Add, "Orto", ed.ortho) { ed.ortho = !ed.ortho },
            ToolItem(Icons.Outlined.LinearScale, "İz", ed.tracking) { ed.tracking = !ed.tracking },
        )
    }
}

@Composable
private fun ToolButton(t: ToolItem, wide: Boolean = false) = ToolButton(t.icon, t.label, t.active, t.enabled, t.onClick, wide)

/** Telefon (dik): üstte seçili kategorinin araçları, altta kategoriler + geri al/yinele */
@Composable
private fun BottomTools(ed: CadEditor, cat: ToolCat, onCat: (ToolCat) -> Unit, onProps: () -> Unit, onSymbols: () -> Unit, onTables: () -> Unit) {
    Surface(color = BarColor, contentColor = MaterialTheme.colorScheme.onSurface) {
        Column(Modifier.fillMaxWidth().navigationBarsPadding()) {
            Row(
                Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 4.dp, vertical = 4.dp),
                horizontalArrangement = Arrangement.spacedBy(2.dp),
            ) {
                for (t in toolsOf(cat, ed, onProps, onSymbols, onTables)) ToolButton(t)
            }
            HorizontalDivider(color = Color(0x22FFFFFF))
            Row(Modifier.fillMaxWidth().padding(horizontal = 4.dp, vertical = 2.dp), verticalAlignment = Alignment.CenterVertically) {
                for (c in ToolCat.entries) {
                    val on = c == cat
                    Row(
                        Modifier.weight(1f).clip(RoundedCornerShape(8.dp)).clickable { onCat(c) }.padding(vertical = 8.dp),
                        horizontalArrangement = Arrangement.Center,
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Icon(c.icon, null, Modifier.size(18.dp), tint = if (on) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant)
                        Spacer(Modifier.width(4.dp))
                        Text(c.label, fontSize = 12.sp, fontWeight = if (on) FontWeight.Bold else FontWeight.Normal,
                            color = if (on) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
                IconButton(onClick = ed::undo, enabled = ed.undoCount > 0 && !ed.busy) { Icon(Icons.AutoMirrored.Outlined.Undo, "Geri al") }
                IconButton(onClick = ed::redo, enabled = ed.redoCount > 0 && !ed.busy) { Icon(Icons.AutoMirrored.Outlined.Redo, "Yinele") }
            }
        }
    }
}

/**
 * Geniş ekran (yatay telefon, tablet): solda 4 ana düğmeli dar çubuk; birine dokununca o
 * kategorinin araçları sağa kayarak açılır. Araç seçilince panel kapanır (ayarlar hariç).
 */
@Composable
private fun SideTools(
    ed: CadEditor, open: ToolCat?, onOpen: (ToolCat?) -> Unit,
    onProps: () -> Unit, onSymbols: () -> Unit, onTables: () -> Unit, modifier: Modifier,
) {
    val cs = MaterialTheme.colorScheme
    // kapanma animasyonu sırasında içerik boşalmasın
    var shown by remember { mutableStateOf(ToolCat.EDIT) }
    if (open != null) shown = open
    Row(modifier.fillMaxHeight()) {
        Surface(color = BarColor, contentColor = cs.onSurface) {
            Column(
                Modifier.width(RAIL_W).fillMaxHeight().windowInsetsPadding(WindowInsets.navigationBars.only(WindowInsetsSides.Bottom))
                    .verticalScroll(rememberScrollState()).padding(vertical = 6.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                for (c in ToolCat.entries) {
                    val on = c == open
                    // etkin araç bu kategoride mi: kapalıyken de görünsün
                    val holds = c != ToolCat.SETTINGS && toolsOf(c, ed, onProps, onSymbols, onTables).any { it.active }
                    Column(
                        Modifier.padding(vertical = 3.dp).width(RAIL_W - 8.dp).clip(RoundedCornerShape(12.dp))
                            .background(if (on) cs.primary.copy(alpha = 0.22f) else Color.Transparent)
                            .clickable { onOpen(if (on) null else c) }.padding(vertical = 8.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                    ) {
                        Icon(c.icon, c.label, tint = if (on || holds) cs.primary else cs.onSurface)
                        Text(c.label, fontSize = 10.sp, color = if (on || holds) cs.primary else cs.onSurfaceVariant, maxLines = 1)
                        Box(Modifier.padding(top = 3.dp).size(5.dp).clip(CircleShape).background(if (holds) cs.primary else Color.Transparent))
                    }
                }
                HorizontalDivider(Modifier.padding(vertical = 6.dp).width(40.dp), color = Color(0x33FFFFFF))
                IconButton(onClick = ed::undo, enabled = ed.undoCount > 0 && !ed.busy) { Icon(Icons.AutoMirrored.Outlined.Undo, "Geri al") }
                IconButton(onClick = ed::redo, enabled = ed.redoCount > 0 && !ed.busy) { Icon(Icons.AutoMirrored.Outlined.Redo, "Yinele") }
            }
        }
        AnimatedVisibility(
            visible = open != null,
            enter = expandHorizontally(expandFrom = Alignment.Start) + fadeIn(),
            exit = shrinkHorizontally(shrinkTowards = Alignment.Start) + fadeOut(),
        ) {
            Surface(color = Color(0xF21A1E21), contentColor = cs.onSurface) {
                Column(
                    Modifier.width(SIDE_W).fillMaxHeight().windowInsetsPadding(WindowInsets.navigationBars.only(WindowInsetsSides.Bottom))
                        .verticalScroll(rememberScrollState()).padding(vertical = 6.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    Text(shown.label, fontSize = 11.sp, fontWeight = FontWeight.Bold, color = cs.primary, modifier = Modifier.padding(bottom = 4.dp))
                    for (t in toolsOf(shown, ed, onProps, onSymbols, onTables)) {
                        ToolButton(t.icon, t.label, t.active, t.enabled, {
                            t.onClick()
                            if (shown != ToolCat.SETTINGS) onOpen(null) // araç seçildi: çizime yer aç
                        }, wide = true)
                    }
                }
            }
        }
    }
}

private val RAIL_W = 64.dp
private val SIDE_W = 92.dp

@Composable
private fun ToolButton(icon: ImageVector, label: String, active: Boolean, enabled: Boolean = true, onClick: () -> Unit, wide: Boolean = false) {
    val cs = MaterialTheme.colorScheme
    val fg = when {
        !enabled -> cs.onSurface.copy(alpha = 0.3f)
        active -> cs.onPrimary
        else -> cs.onSurface
    }
    Column(
        (if (wide) Modifier.width(SIDE_W - 8.dp) else Modifier.widthIn(min = 56.dp)).background(if (active) cs.primary else Color.Transparent, RoundedCornerShape(10.dp))
            .clickable(enabled = enabled, onClick = onClick).padding(horizontal = 8.dp, vertical = 6.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Icon(icon, label, tint = fg, modifier = Modifier.size(22.dp))
        Text(label, fontSize = 11.sp, color = fg, maxLines = 1)
    }
}

@Composable
private fun ChipButton(text: String, onClick: () -> Unit) {
    Text(
        text,
        fontSize = 13.sp,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(horizontal = 2.dp).border(1.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.5f), RoundedCornerShape(8.dp))
            .clickable(onClick = onClick).padding(horizontal = 8.dp, vertical = 6.dp),
    )
}

@Composable
private fun NumberDialog(title: String, label: String, initial: String, onDismiss: () -> Unit, onOk: (Double) -> Unit) {
    var v by remember { mutableStateOf(initial) }
    val n = v.replace(',', '.').toDoubleOrNull()
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            OutlinedTextField(v, { v = it }, label = { Text(label) }, singleLine = true, isError = n == null,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal))
        },
        confirmButton = { TextButton(enabled = n != null, onClick = { onOk(n!!) }) { Text("Uygula") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("İptal") } },
    )
}

@Composable
private fun SymbolPreview(sym: CadSymbol, modifier: Modifier) {
    Canvas(modifier) {
        val b = sym.bounds
        val bw = (b[2] - b[0]).coerceAtLeast(1e-3f)
        val bh = (b[3] - b[1]).coerceAtLeast(1e-3f)
        val k = minOf(size.width / bw, size.height / bh) * 0.9f
        val ox = size.width / 2 - (b[0] + b[2]) / 2 * k
        val oy = size.height / 2 + (b[1] + b[3]) / 2 * k
        fun px(x: Float) = ox + x * k
        fun py(y: Float) = oy - y * k
        for ((rgb, a, pts) in sym.fills) {
            val path = androidx.compose.ui.graphics.Path()
            var i = 0
            while (i + 1 < pts.size) {
                if (i == 0) path.moveTo(px(pts[0]), py(pts[1])) else path.lineTo(px(pts[i]), py(pts[i + 1]))
                i += 2
            }
            path.close()
            drawPath(path, Color(0xFF000000 or CadRenderer.adapt(rgb).toLong()).copy(alpha = a))
        }
        for ((rgb, seg) in sym.lines) {
            val c = Color(0xFF000000 or CadRenderer.adapt(rgb).toLong())
            var i = 0
            while (i + 3 < seg.size) {
                drawLine(c, Offset(px(seg[i]), py(seg[i + 1])), Offset(px(seg[i + 2]), py(seg[i + 3])), strokeWidth = 1.6f)
                i += 4
            }
        }
        val paint = android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG)
        for (t in sym.texts) {
            val hpx = t.h * k
            if (hpx < 4f) continue
            paint.color = (0xFF000000.toInt() or CadRenderer.adapt(t.rgb))
            paint.textSize = hpx / 0.72f
            paint.textAlign = when (t.halign) { 1 -> android.graphics.Paint.Align.CENTER; 2 -> android.graphics.Paint.Align.RIGHT; else -> android.graphics.Paint.Align.LEFT }
            val dy = when (t.valign) { 3 -> hpx; 2 -> hpx / 2; else -> 0f }
            drawContext.canvas.nativeCanvas.apply {
                save()
                translate(px(t.x), py(t.y))
                rotate(-t.rot)
                drawText(t.text, 0f, dy, paint)
                restore()
            }
        }
    }
}

@Composable
private fun SymbolSheet(current: CadSymbol?, onPick: (CadSymbol) -> Unit) {
    var cat by remember { mutableStateOf(current?.category ?: SymbolLibrary.categories.firstOrNull() ?: "") }
    Column(Modifier.padding(horizontal = 16.dp)) {
        Text("Elektrik sembolleri", style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp))
        Row(Modifier.horizontalScroll(rememberScrollState())) {
            for (c in SymbolLibrary.categories) {
                FilterChip(selected = c == cat, onClick = { cat = c }, label = { Text(c) }, modifier = Modifier.padding(end = 6.dp))
            }
        }
        LazyVerticalGrid(
            columns = GridCells.Adaptive(100.dp),
            modifier = Modifier.fillMaxWidth().heightIn(max = 460.dp),
            contentPadding = PaddingValues(vertical = 8.dp, horizontal = 0.dp),
        ) {
            items(SymbolLibrary.all.filter { it.category == cat }) { sym ->
                val on = sym.id == current?.id
                Column(
                    Modifier.padding(4.dp).border(1.dp, if (on) MaterialTheme.colorScheme.primary else Color(0x33FFFFFF), RoundedCornerShape(10.dp))
                        .clickable { onPick(sym) }.padding(8.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    SymbolPreview(sym, Modifier.size(64.dp).background(Color(0xFF0B0E10), RoundedCornerShape(6.dp)))
                    Spacer(Modifier.height(4.dp))
                    Text(sym.name, fontSize = 11.sp, textAlign = TextAlign.Center, maxLines = 2, minLines = 2)
                }
            }
        }
        Spacer(Modifier.height(16.dp))
    }
}

@Composable
private fun CountSheet(fileName: String, rows: List<Triple<String, Int, Boolean>>, onShare: (String) -> Unit) {
    Column(Modifier.padding(horizontal = 16.dp).padding(bottom = 24.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Keşif listesi", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
            if (rows.isNotEmpty()) {
                TextButton(onClick = {
                    val body = buildString {
                        append("Keşif listesi — ").append(fileName).append('\n')
                        for ((n, c, _) in rows) append(n).append(": ").append(c).append(" adet\n")
                    }
                    onShare(body)
                }) {
                    Icon(Icons.Outlined.Share, null)
                    Spacer(Modifier.width(6.dp))
                    Text("Paylaş")
                }
            }
        }
        Text(
            "Çizimdeki sembol ve blok yerleştirmeleri. MühenKit sembolleri üstte.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(Modifier.height(8.dp))
        if (rows.isEmpty()) Text("Çizimde sembol ya da blok yok.", color = MaterialTheme.colorScheme.onSurfaceVariant)
        LazyColumn(Modifier.fillMaxWidth().heightIn(max = 480.dp)) {
            itemsIndexed(rows) { _, (name, n, ours) ->
                Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        name, modifier = Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis,
                        color = if (ours) MaterialTheme.colorScheme.onSurface else MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Text("$n adet", fontWeight = FontWeight.Medium, color = MaterialTheme.colorScheme.primary)
                }
                HorizontalDivider(color = Color(0x22FFFFFF))
            }
        }
    }
}

@Composable
private fun <T> OptionRow(title: String, options: List<T>, selected: T, label: (T) -> String, onSelect: (T) -> Unit) {
    Text(title, style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
    Row(Modifier.horizontalScroll(rememberScrollState()).padding(top = 4.dp, bottom = 10.dp)) {
        for (o in options) {
            FilterChip(selected = o == selected, onClick = { onSelect(o) }, label = { Text(label(o)) }, modifier = Modifier.padding(end = 6.dp))
        }
    }
}

@Composable
private fun ExportSheet(busy: Boolean, onGo: (ExportOptions, Int) -> Unit) {
    var o by remember { mutableStateOf(ExportOptions()) }
    Column(Modifier.padding(horizontal = 16.dp).padding(bottom = 24.dp)) {
        Text("Dışa aktar", style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(10.dp))
        OptionRow("Biçim", ExportFormat.entries, o.format, { it.label }) { o = o.copy(format = it) }
        OptionRow("Kâğıt", Paper.entries, o.paper, { it.label }) { o = o.copy(paper = it) }
        OptionRow("Yön", Orientation.entries, o.orientation, { it.label }) { o = o.copy(orientation = it) }
        OptionRow("Alan", listOf(false, true), o.viewOnly, { if (it) "Ekrandaki görünüm" else "Tüm çizim" }) { o = o.copy(viewOnly = it) }
        OptionRow("Renk", listOf(true, false), o.monochrome, { if (it) "Siyah-beyaz" else "Renkli" }) { o = o.copy(monochrome = it) }
        Text(
            "Gizlediğin katmanlar çıktıya girmez. Çizim kâğıda sığdırılır.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(Modifier.height(12.dp))
        if (busy) {
            LinearProgressIndicator(Modifier.fillMaxWidth())
            Spacer(Modifier.height(12.dp))
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = { onGo(o, 0) }, enabled = !busy) {
                Icon(Icons.Outlined.Share, null)
                Spacer(Modifier.width(6.dp))
                Text("Paylaş")
            }
            OutlinedButton(onClick = { onGo(o, 1) }, enabled = !busy) { Text("Kaydet") }
            if (o.format == ExportFormat.PDF) OutlinedButton(onClick = { onGo(o, 2) }, enabled = !busy) {
                Icon(Icons.Outlined.Print, null)
                Spacer(Modifier.width(6.dp))
                Text("Yazdır")
            }
        }
    }
}

@Composable
private fun NumberDialog2(
    title: String, label1: String, init1: String, label2: String, init2: String,
    onDismiss: () -> Unit, onOk: (Double, Double) -> Unit,
) {
    var a by remember { mutableStateOf(init1) }
    var b by remember { mutableStateOf(init2) }
    val x = a.replace(',', '.').toDoubleOrNull()
    val y = b.replace(',', '.').toDoubleOrNull()
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            Column {
                OutlinedTextField(a, { a = it }, label = { Text(label1) }, singleLine = true, isError = a.isNotEmpty() && x == null,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal))
                Spacer(Modifier.height(8.dp))
                OutlinedTextField(b, { b = it }, label = { Text(label2) }, singleLine = true, isError = b.isNotEmpty() && y == null,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal))
            }
        },
        confirmButton = { TextButton(enabled = x != null && y != null, onClick = { onOk(x!!, y!!) }) { Text("Uygula") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("İptal") } },
    )
}

@Composable
private fun EditTextDialog(current: String, onDismiss: () -> Unit, onOk: (String) -> Unit) {
    var t by remember { mutableStateOf(current) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Yazıyı düzenle") },
        text = { OutlinedTextField(t, { t = it }, label = { Text("Yazı") }, minLines = 1, maxLines = 6) },
        confirmButton = { TextButton(enabled = t.isNotBlank(), onClick = { onOk(t) }) { Text("Kaydet") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("İptal") } },
    )
}

/** AutoCAD standart renkleri (ACI) */
private val AciColors = listOf(
    1 to ("Kırmızı" to 0xFF0000), 2 to ("Sarı" to 0xFFFF00), 3 to ("Yeşil" to 0x00FF00), 4 to ("Camgöbeği" to 0x00FFFF),
    5 to ("Mavi" to 0x0000FF), 6 to ("Eflatun" to 0xFF00FF), 7 to ("Beyaz" to 0xFFFFFF), 8 to ("Gri" to 0x808080),
    9 to ("Açık gri" to 0xC0C0C0), 30 to ("Turuncu" to 0xFF7F00),
)

@Composable
private fun PropsSheet(scene: CadScene, count: Int, onLayer: (String) -> Unit, onColor: (Int) -> Unit) {
    Column(Modifier.padding(horizontal = 16.dp).padding(bottom = 24.dp)) {
        Text("$count nesnenin özellikleri", style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(12.dp))
        Text("Renk", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Spacer(Modifier.height(6.dp))
        Row(Modifier.horizontalScroll(rememberScrollState()), verticalAlignment = Alignment.CenterVertically) {
            Text(
                "Katman rengi",
                fontSize = 13.sp,
                modifier = Modifier.border(1.dp, Color(0x55FFFFFF), RoundedCornerShape(8.dp)).clickable { onColor(256) }
                    .padding(horizontal = 10.dp, vertical = 8.dp),
            )
            for ((aci, c) in AciColors) {
                Spacer(Modifier.width(8.dp))
                Box(
                    Modifier.size(34.dp).background(Color(0xFF000000 or c.second.toLong()), RoundedCornerShape(8.dp))
                        .border(1.dp, Color(0x55FFFFFF), RoundedCornerShape(8.dp)).clickable { onColor(aci) },
                )
            }
        }
        Spacer(Modifier.height(16.dp))
        Text("Katmana taşı", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
        LazyColumn(Modifier.fillMaxWidth().heightIn(max = 360.dp)) {
            itemsIndexed(scene.layers) { _, l ->
                Row(
                    Modifier.fillMaxWidth().clickable { onLayer(l.name) }.padding(vertical = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Box(
                        Modifier.size(14.dp).background(Color(0xFF000000 or CadRenderer.adapt(l.rgb).toLong()), RoundedCornerShape(3.dp)),
                    )
                    Spacer(Modifier.width(12.dp))
                    Text(l.name, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
        }
    }
}

@Composable
private fun TextDialog(defaultHeight: Double, onDismiss: () -> Unit, onOk: (String, Double) -> Unit) {
    var text by remember { mutableStateOf("") }
    var height by remember { mutableStateOf(trimNum(defaultHeight)) }
    val h = height.replace(',', '.').toDoubleOrNull()
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Yazı ekle") },
        text = {
            Column {
                OutlinedTextField(text, { text = it }, label = { Text("Yazı") }, singleLine = true)
                Spacer(Modifier.height(8.dp))
                OutlinedTextField(height, { height = it }, label = { Text("Yükseklik") }, singleLine = true,
                    isError = h == null || h <= 0, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal))
            }
        },
        confirmButton = { TextButton(enabled = text.isNotBlank() && h != null && h > 0, onClick = { onOk(text, h!!) }) { Text("Ekle") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("İptal") } },
    )
}

private fun trimNum(v: Double): String = if (v == Math.floor(v)) v.toLong().toString() else "%.3f".format(v).trimEnd('0').trimEnd('.', ',')

@Composable
private fun LayerSheet(scene: CadScene, hidden: Set<Int>, active: String, onActive: (String) -> Unit, onChange: (Set<Int>) -> Unit) {
    Column(Modifier.padding(horizontal = 16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Katmanlar", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
            TextButton(onClick = { onChange(emptySet()) }) { Text("Tümünü aç") }
            TextButton(onClick = { onChange(scene.layers.indices.toSet()) }) { Text("Tümünü kapat") }
        }
        Text(
            "Yeni çizgi ve yazılar etkin katmana eklenir. Etkin yapmak için katman adına dokunun.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        LazyColumn(Modifier.fillMaxWidth().heightIn(max = 480.dp), contentPadding = PaddingValues(top = 8.dp, bottom = 24.dp)) {
            itemsIndexed(scene.layers) { i, l ->
                val c = CadRenderer.adapt(l.rgb)
                val isActive = l.name == active
                Row(
                    Modifier.fillMaxWidth().clickable { onActive(l.name) }.padding(vertical = 2.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Box(
                        Modifier.size(14.dp)
                            .background(Color(0xFF000000 or c.toLong()), RoundedCornerShape(3.dp))
                            .border(1.dp, Color(0x33FFFFFF), RoundedCornerShape(3.dp)),
                    )
                    Spacer(Modifier.width(12.dp))
                    Column(Modifier.weight(1f)) {
                        Text(
                            l.name, maxLines = 1, overflow = TextOverflow.Ellipsis,
                            fontWeight = if (isActive) FontWeight.Bold else FontWeight.Medium,
                            color = if (isActive) MaterialTheme.colorScheme.primary else Color.Unspecified,
                        )
                        Text(
                            if (isActive) "etkin · ${l.count} nesne" else "${l.count} nesne",
                            fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    Switch(checked = i !in hidden, onCheckedChange = { on -> onChange(if (on) hidden - i else hidden + i) })
                }
            }
        }
    }
}
