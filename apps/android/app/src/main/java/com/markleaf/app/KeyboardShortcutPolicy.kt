package com.markleaf.app

import android.view.KeyEvent

/**
 * Physical keyboard shortcuts owned by the host. Clipboard and cursor keys are
 * intentionally left to WebView so Android's native IME/selection behavior is
 * preserved.
 */
object KeyboardShortcutPolicy {
    fun commandFor(event: KeyEvent): String? {
        if (event.action != KeyEvent.ACTION_DOWN) return null
        if (event.isAltPressed) return null
        if (!event.isCtrlPressed && !event.isMetaPressed) return null

        return when (event.keyCode) {
            KeyEvent.KEYCODE_S -> "save"
            KeyEvent.KEYCODE_Z -> if (event.isShiftPressed) "redo" else "undo"
            KeyEvent.KEYCODE_Y -> "redo"
            KeyEvent.KEYCODE_A -> "selectAll"
            KeyEvent.KEYCODE_F -> "find"
            KeyEvent.KEYCODE_B -> "toggleBold"
            KeyEvent.KEYCODE_I -> "toggleItalic"
            KeyEvent.KEYCODE_U -> "toggleUnderline"
            // C/V/X remain WebView-native: intercepting them would lose the
            // Android clipboard payload and IME's selection semantics.
            else -> null
        }
    }
}
