package com.muhenkit.app.ui

import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.waitForUpOrCancellation
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController

/**
 * Boş yere dokununca yazı alanı odağı bırakır (imleç söner) ve klavye kapanır.
 * Final geçişinde bakılır: dokunuşu tüketen öğeler (düğme, yazı alanı) ve kaydırmalar etkilemez.
 * Dialog ayrı pencere olduğundan onun içeriğine de ayrıca verilmeli.
 */
fun Modifier.clearFocusOnTap(): Modifier = composed {
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    pointerInput(Unit) {
        awaitEachGesture {
            awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Final)
            val up = waitForUpOrCancellation(PointerEventPass.Final) ?: return@awaitEachGesture
            if (!up.isConsumed) {
                focus.clearFocus()
                keyboard?.hide()
            }
        }
    }
}
