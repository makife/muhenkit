package com.muhenkit.app

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.mutableStateOf
import androidx.lifecycle.lifecycleScope
import com.muhenkit.app.cad.SymbolLibrary
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import com.muhenkit.app.ui.AppRoot
import com.muhenkit.app.ui.MuhenKitTheme

class MainActivity : ComponentActivity() {
    /** Dosya yöneticisinden "Birlikte aç" ile gelen DXF. */
    private val openUri = mutableStateOf<Uri?>(null)

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge(statusBarStyle = SystemBarStyle.dark(0), navigationBarStyle = SystemBarStyle.dark(0))
        super.onCreate(savedInstanceState)
        openUri.value = viewUri(intent)
        lifecycleScope.launch(Dispatchers.IO) { SymbolLibrary.load(applicationContext) }
        setContent {
            MuhenKitTheme {
                // Surface: renk belirtilmeyen tüm yazı ve simgeler koyu zeminde açık renk olsun
                // (yoksa varsayılan siyah kalıyor ve okunmuyordu)
                androidx.compose.material3.Surface(
                    color = androidx.compose.material3.MaterialTheme.colorScheme.background,
                    contentColor = androidx.compose.material3.MaterialTheme.colorScheme.onSurface,
                ) {
                    AppRoot(openUri.value)
                }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        viewUri(intent)?.let { openUri.value = it }
    }

    private fun viewUri(i: Intent?): Uri? = if (i?.action == Intent.ACTION_VIEW) i.data else null
}
