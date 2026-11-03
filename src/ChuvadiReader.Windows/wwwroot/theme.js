// Document-level appearance switches. Everything here only sets attributes / properties on
// <html>; the matching CSS lives in tokens.css (themes) and css/prefs.css (scale, motion).
window.chuvadiTheme = {
    // Theme key: light | dark | palm | indigo | contrast (ChuvadiTheme.Key()).
    set: function (name) {
        document.documentElement.setAttribute('data-theme', name || 'light');
    },

    // Interface size in percent (100 / 125 / 150). Zooms the whole UI; prefs.css divides
    // viewport-height layouts by --chvd-ui-scale so the zoomed shell still fits the window.
    setScale: function (pct) {
        var n = parseInt(pct, 10);
        if (n !== 125 && n !== 150) n = 100;
        var root = document.documentElement;
        root.setAttribute('data-ui-scale', String(n));
        root.style.setProperty('--chvd-ui-scale', String(n / 100));
        root.style.zoom = n === 100 ? '' : String(n / 100);
    },

    // Reduce motion: no transitions or animations anywhere (prefs.css).
    setReducedMotion: function (on) {
        var root = document.documentElement;
        if (on) root.setAttribute('data-reduce-motion', '');
        else root.removeAttribute('data-reduce-motion');
    },

    // Convenience for startup: apply everything in one interop call.
    // prefs = { theme: 'light', scale: 100, reduceMotion: false } (any field optional).
    apply: function (prefs) {
        if (!prefs) return;
        if (prefs.theme) this.set(prefs.theme);
        if (prefs.scale !== undefined && prefs.scale !== null) this.setScale(prefs.scale);
        if (prefs.reduceMotion !== undefined && prefs.reduceMotion !== null) this.setReducedMotion(!!prefs.reduceMotion);
    }
};
