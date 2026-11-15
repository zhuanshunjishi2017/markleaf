package com.markleaf.app

import android.view.KeyEvent
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class KeyboardShortcutPolicyTest {
    private fun key(code: Int, meta: Int = KeyEvent.META_CTRL_ON): KeyEvent =
        KeyEvent(0L, 0L, KeyEvent.ACTION_DOWN, code, 0, meta)

    @Test
    fun `maps common editor shortcuts`() {
        assertEquals("save", KeyboardShortcutPolicy.commandFor(key(KeyEvent.KEYCODE_S)))
        assertEquals("undo", KeyboardShortcutPolicy.commandFor(key(KeyEvent.KEYCODE_Z)))
        assertEquals("redo", KeyboardShortcutPolicy.commandFor(key(KeyEvent.KEYCODE_Y)))
        assertEquals("selectAll", KeyboardShortcutPolicy.commandFor(key(KeyEvent.KEYCODE_A)))
        assertEquals("find", KeyboardShortcutPolicy.commandFor(key(KeyEvent.KEYCODE_F)))
        assertEquals("toggleBold", KeyboardShortcutPolicy.commandFor(key(KeyEvent.KEYCODE_B)))
    }

    @Test
    fun `shift z and meta modifier map to redo`() {
        assertEquals(
            "redo",
            KeyboardShortcutPolicy.commandFor(key(KeyEvent.KEYCODE_Z, KeyEvent.META_CTRL_ON or KeyEvent.META_SHIFT_ON)),
        )
        assertEquals(
            "undo",
            KeyboardShortcutPolicy.commandFor(key(KeyEvent.KEYCODE_Z, KeyEvent.META_META_ON)),
        )
    }

    @Test
    fun `clipboard alt and key up are left to native handling`() {
        assertNull(KeyboardShortcutPolicy.commandFor(key(KeyEvent.KEYCODE_C)))
        assertNull(KeyboardShortcutPolicy.commandFor(key(KeyEvent.KEYCODE_V)))
        assertNull(KeyboardShortcutPolicy.commandFor(key(KeyEvent.KEYCODE_X)))
        assertNull(KeyboardShortcutPolicy.commandFor(key(KeyEvent.KEYCODE_S, KeyEvent.META_CTRL_ON or KeyEvent.META_ALT_ON)))
        assertNull(KeyboardShortcutPolicy.commandFor(
            KeyEvent(0L, 0L, KeyEvent.ACTION_UP, KeyEvent.KEYCODE_S, 0, KeyEvent.META_CTRL_ON),
        ))
    }
}
