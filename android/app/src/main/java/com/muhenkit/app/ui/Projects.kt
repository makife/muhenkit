package com.muhenkit.app.ui

import android.graphics.BitmapFactory
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
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
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.muhenkit.app.cad.ProjectMeta
import com.muhenkit.app.cad.ProjectStore
import com.muhenkit.app.ui.Exporter
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Projelerim: otomatik kaydedilen çizimler */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ProjectsScreen(onBack: () -> Unit, onOpen: (CadRequest) -> Unit) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var list by remember { mutableStateOf<List<ProjectMeta>?>(null) }
    var renaming by remember { mutableStateOf<ProjectMeta?>(null) }
    var deleting by remember { mutableStateOf<ProjectMeta?>(null) }
    fun reload() = scope.launch { list = withContext(Dispatchers.IO) { ProjectStore.list(context) } }
    LaunchedEffect(Unit) { reload() }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri -> uri?.let { onOpen(CadRequest.OpenUri(it)) } }

    Column(Modifier.fillMaxSize()) {
        TopAppBar(
            title = { Text("Projelerim") },
            navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Outlined.ArrowBack, "Geri") } },
            colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.surface),
        )
        LazyVerticalGrid(
            columns = GridCells.Adaptive(170.dp),
            modifier = Modifier.fillMaxSize().navigationBarsPadding(),
            contentPadding = PaddingValues(12.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            item(span = { GridItemSpan(maxLineSpan) }) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Button(onClick = { picker.launch(arrayOf("*/*")) }, modifier = Modifier.weight(1f)) {
                        Icon(Icons.Outlined.FolderOpen, null); Spacer(Modifier.width(6.dp)); Text("DXF aç")
                    }
                    OutlinedButton(onClick = { onOpen(CadRequest.NewBlank) }, modifier = Modifier.weight(1f)) {
                        Icon(Icons.Outlined.NoteAdd, null); Spacer(Modifier.width(6.dp)); Text("Boş proje")
                    }
                }
            }
            val l = list
            if (l != null && l.isEmpty()) item(span = { GridItemSpan(maxLineSpan) }) {
                Text(
                    "Henüz proje yok. Açtığın her DXF burada saklanır ve kaldığın yerden devam edersin.",
                    color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(8.dp),
                )
            }
            items(l ?: emptyList(), key = { it.id }) { p ->
                ProjectCard(p, onOpen = { onOpen(CadRequest.OpenProject(p.id)) }, onRename = { renaming = p }, onDelete = { deleting = p },
                    onShare = {
                        scope.launch {
                            val bytes = withContext(Dispatchers.IO) { ProjectStore.read(context, p.id) }
                            Exporter.share(context, bytes, p.name + ".dxf", "application/dxf", "DXF dosyasını paylaş")
                        }
                    })
            }
        }
    }

    renaming?.let { p ->
        var name by remember(p.id) { mutableStateOf(p.name) }
        AlertDialog(
            onDismissRequest = { renaming = null },
            title = { Text("Yeniden adlandır") },
            text = { OutlinedTextField(name, { name = it }, singleLine = true, label = { Text("Proje adı") }) },
            confirmButton = {
                TextButton(enabled = name.isNotBlank(), onClick = {
                    renaming = null
                    scope.launch {
                        withContext(Dispatchers.IO) { ProjectStore.rename(context, p.id, name.trim()) }
                        reload()
                    }
                }) { Text("Kaydet") }
            },
            dismissButton = { TextButton(onClick = { renaming = null }) { Text("İptal") } },
        )
    }
    deleting?.let { p ->
        AlertDialog(
            onDismissRequest = { deleting = null },
            title = { Text("Projeyi sil") },
            text = {
                Text("\"${p.name}\" Projelerim'den silinsin mi?" +
                    if (p.sourceUri != null) " Orijinal DXF dosyan silinmez." else " Bu proje bir dosyaya kaydedilmedi; tamamen silinir.")
            },
            confirmButton = {
                TextButton(onClick = {
                    deleting = null
                    scope.launch {
                        withContext(Dispatchers.IO) { ProjectStore.delete(context, p.id) }
                        reload()
                    }
                }) { Text("Sil", color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = { TextButton(onClick = { deleting = null }) { Text("İptal") } },
        )
    }
}

@Composable
private fun ProjectCard(p: ProjectMeta, onOpen: () -> Unit, onRename: () -> Unit, onDelete: () -> Unit, onShare: () -> Unit) {
    val context = LocalContext.current
    val thumb = remember(p.id, p.modified) {
        ProjectStore.thumb(context, p.id).takeIf { it.exists() }?.let { BitmapFactory.decodeFile(it.path)?.asImageBitmap() }
    }
    var menu by remember { mutableStateOf(false) }
    Column(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Color(0xFF141719))
            .border(1.dp, Color(0x22FFFFFF), RoundedCornerShape(14.dp)).clickable(onClick = onOpen),
    ) {
        Box(Modifier.fillMaxWidth().aspectRatio(1.6f).background(Color.White)) {
            if (thumb != null) Image(thumb, null, Modifier.fillMaxSize(), contentScale = ContentScale.Fit)
            else Icon(Icons.Outlined.Architecture, null, Modifier.align(Alignment.Center).size(40.dp), tint = Color(0xFF8DA897))
        }
        Row(Modifier.padding(start = 10.dp, top = 6.dp, bottom = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(p.name, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(SimpleDateFormat("dd.MM.yyyy HH:mm", Locale("tr")).format(Date(p.modified)), fontSize = 11.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                if (p.dirtyToSource) Text("Dosyaya kaydedilmedi", fontSize = 11.sp, color = MaterialTheme.colorScheme.primary)
                else if (p.sourceUri == null) Text("Yalnız uygulamada", fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Box {
                IconButton(onClick = { menu = true }) { Icon(Icons.Outlined.MoreVert, "Menü") }
                DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                    DropdownMenuItem(text = { Text("Aç") }, leadingIcon = { Icon(Icons.Outlined.Edit, null) }, onClick = { menu = false; onOpen() })
                    DropdownMenuItem(text = { Text("Yeniden adlandır") }, leadingIcon = { Icon(Icons.Outlined.DriveFileRenameOutline, null) },
                        onClick = { menu = false; onRename() })
                    DropdownMenuItem(text = { Text("DXF paylaş") }, leadingIcon = { Icon(Icons.Outlined.Share, null) }, onClick = { menu = false; onShare() })
                    DropdownMenuItem(text = { Text("Sil") }, leadingIcon = { Icon(Icons.Outlined.Delete, null) }, onClick = { menu = false; onDelete() })
                }
            }
        }
    }
}
