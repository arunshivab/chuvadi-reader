// Document-level appearance switches. Everything here only sets attributes / properties on
// <html>; the matching CSS lives in tokens.css (themes) and css/prefs.css (scale, motion).
window.chuvadiTheme = {
    // Theme key: light | dark | palm | indigo | contrast (ChuvadiTheme.Key()).
    set: function (name) {
        document.documentElement.setAttribute('data-theme', name || 'light');
    },

    // Interface size in percent (100 / 125 / 150). Zooms the whole UI; prefs.css divides
    // viewport-height layouts by --chvd-ui-scale so the zoomed shell still fits the window.
    // Interface size. hostZoom = true when the host zooms the whole window (the Windows app sets
    // the WebView zoom), so the page itself must not zoom again; the attribute stays for the
    // larger hit targets. Otherwise (web/preview hosts) CSS zoom on <html> is the fallback and
    // data-ui-zoom="css" turns on the viewport-unit corrections in prefs.css.
    setScale: function (pct, hostZoom) {
        var n = parseInt(pct, 10);
        if (n !== 125 && n !== 150) n = 100;
        var root = document.documentElement;
        root.setAttribute('data-ui-scale', String(n));
        var css = !hostZoom && n !== 100;
        root.style.setProperty('--chvd-ui-scale', css ? String(n / 100) : '1');
        root.style.zoom = css ? String(n / 100) : '';
        if (css) root.setAttribute('data-ui-zoom', 'css');
        else root.removeAttribute('data-ui-zoom');
    },

    // Reduce motion: no transitions or animations anywhere (prefs.css).
    setReducedMotion: function (on) {
        var root = document.documentElement;
        if (on) root.setAttribute('data-reduce-motion', '');
        else root.removeAttribute('data-reduce-motion');
    },

    // Platform attributes (MainLayout.razor, first render and on form-factor change): data-shell desktop|tablet|phone|portal, data-platform,
    // data-form-factor, data-input. Absent = Windows desktop (the Windows host never calls this, so
    // it costs nothing at its startup). Other hosts should also put their initial values on <html>
    // in their host page so the first paint already has the right layout.
    setPlatform: function (shell, platform, formFactor, input) {
        var root = document.documentElement;
        root.setAttribute('data-shell', shell || 'desktop');
        root.setAttribute('data-platform', platform || 'windows');
        root.setAttribute('data-form-factor', formFactor || 'desktop');
        root.setAttribute('data-input', input || '');
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
