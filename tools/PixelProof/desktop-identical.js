// TESTING ONLY (never shipped). See docs/foundation/COMBINED-HOOKS.md for how to run: preview hosts from
// tests/ChuvadiReader.MobileUi.Preview, before on :5101 and after on :5102 (CHVD_FF=desktop), phone :5104, tablet :5105.
const { chromium } = require('playwright');
const routes = process.env.ROUTES ? process.env.ROUTES.split(',') : ['/', '/reader', '/bench', '/redact', '/library', '/settings', '/settings?section=language', '/settings?section=about', '/reader|Control+p'];
const mask = '.sb-clock{visibility:hidden!important} *{caret-color:transparent!important;animation:none!important;transition:none!important}';
async function shot(b, port, route, dpr) {
  const p = await b.newPage({ deviceScaleFactor: dpr, viewport: { width: 1440, height: 900 } });
  // "route|Key" presses a shortcut after load, e.g. "/reader|Control+p" opens the Print card.
  const [path, key] = route.split('|');
  await p.goto(`http://127.0.0.1:${port}${path}`, { waitUntil: 'networkidle' });
  await p.waitForSelector('.statusbar', { timeout: 20000 });
  if (key) { await p.waitForTimeout(1500); await p.keyboard.press(key); }
  await p.addStyleTag({ content: mask });
  await p.waitForTimeout(2500);
  const buf = await p.screenshot();
  const attrs = await p.evaluate(() => [...document.documentElement.attributes].map(a => a.name).join(','));
  await p.close();
  return { buf, attrs };
}
(async () => {
  const b = await chromium.launch(); const rows = [];
  for (const dpr of [1, 1.25, 1.5]) for (const r of routes) {
    const before = await shot(b, 5101, r, dpr); const after = await shot(b, 5102, r, dpr);
    const name = `${(r === '/' ? 'home' : r.slice(1)).replace(/[^a-z0-9]+/gi, '-')}@${dpr}x`;
    require('fs').writeFileSync(`before-${name}.png`, before.buf); require('fs').writeFileSync(`after-${name}.png`, after.buf);
    let diff = 0, box = null;
    if (!before.buf.equals(after.buf)) {
      const p = await b.newPage();
      const res = await p.evaluate(async ([x, y]) => {
        const load = async s => { const im = new Image(); im.src = 'data:image/png;base64,' + s; await im.decode(); const c = document.createElement('canvas'); c.width = im.width; c.height = im.height; const g = c.getContext('2d'); g.drawImage(im, 0, 0); return g.getImageData(0, 0, c.width, c.height); };
        const A = await load(x), B = await load(y); if (A.width !== B.width || A.height !== B.height) return { n: -1 };
        let n = 0, x0 = 1e9, y0 = 1e9, x1 = -1, y1 = -1;
        for (let i = 0; i < A.data.length; i += 4) if (A.data[i] !== B.data[i] || A.data[i+1] !== B.data[i+1] || A.data[i+2] !== B.data[i+2]) { n++; const px = (i / 4) % A.width, py = Math.floor(i / 4 / A.width); x0 = Math.min(x0, px); y0 = Math.min(y0, py); x1 = Math.max(x1, px); y1 = Math.max(y1, py); }
        return { n, box: n ? [x0, y0, x1, y1] : null };
      }, [before.buf.toString('base64'), after.buf.toString('base64')]);
      diff = res.n; box = res.box; await p.close();
    }
    rows.push(`${name.padEnd(16)} ${diff === 0 ? 'IDENTICAL' : 'DIFF ' + diff + ' px ' + JSON.stringify(box)}   <html> attrs before[${before.attrs}] after[${after.attrs}]`);
    console.log(rows[rows.length - 1]);
  }
  await b.close();
})();
