package com.muhenkit.app.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// Eski uygulamanın devre kartı paleti (bakır / altın / ipek baskı)
private val Colors = darkColorScheme(
    primary = Color(0xFFF2B266),
    onPrimary = Color(0xFF1A1205),
    secondary = Color(0xFFE4C06A),
    background = Color(0xFF0B0E10),
    onBackground = Color(0xFFEDEADF),
    surface = Color(0xFF141719),
    onSurface = Color(0xFFEDEADF),
    surfaceContainerLow = Color(0xFF141719),
    onSurfaceVariant = Color(0xFF8DA897),
    error = Color(0xFFFF6B5E),
)

@Composable
fun MuhenKitTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = Colors, content = content)
}
