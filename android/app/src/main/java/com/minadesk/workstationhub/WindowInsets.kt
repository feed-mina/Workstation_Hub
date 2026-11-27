package com.minadesk.workstationhub

import android.os.Build
import android.view.View
import android.view.WindowInsets

/** Preserve design padding while keeping content clear of bars, cutouts and the keyboard. */
internal fun View.applyHubInsets() {
    val left = paddingLeft; val top = paddingTop; val right = paddingRight; val bottom = paddingBottom
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
        setOnApplyWindowInsetsListener { view, insets ->
            val bars = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout() or WindowInsets.Type.ime())
            view.setPadding(left + bars.left, top + bars.top, right + bars.right, bottom + bars.bottom)
            WindowInsets.CONSUMED
        }
        requestApplyInsets()
    } else fitsSystemWindows = true
}
