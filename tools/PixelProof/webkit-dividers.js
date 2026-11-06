// TESTING ONLY (never shipped). Needs Playwright WebKit (test machine only); after-build preview on :5102.
// WebKit check for D-39: desktop shell with data-platform="macos" set exactly as the Mac host sets it.
const { webkit } = require('playwright');
const sets = [
  { sel: '.sb-div', vertical: true, revert: 'html[data-platform] .sb-div{width:0.5px!important;background:var(--chvd-border)!important;border-left:0!important}' },
  { sel: '.lb-sep', vertical: false, revert: 'html[data-platform] .lb-sep{height:0.5px!important;background:var(--chvd-border-soft)!important;border-top:0!important}' },
];
(async () => {
  const b = await webkit.launch();
  const q = await b.newPage();
  const inkOf = async buf => q.evaluate(async s => { const im = new Image(); im.src = 'data:image/png;base64,' + s; await im.decode(); const c = document.createElement('canvas'); c.width = im.width; c.height = im.height; const g = c.getContext('2d'); g.drawImage(im, 0, 0); const d = g.getImageData(0, 0, c.width, c.height).data; const bg = [d[0], d[1], d[2]]; let n = 0; for (let i = 0; i < d.length; i += 4) if (Math.abs(d[i]-bg[0]) + Math.abs(d[i+1]-bg[1]) + Math.abs(d[i+2]-bg[2]) > 6) n++; return n; }, buf.toString('base64'));
  for (const dpr of [1, 1.25, 1.5, 2]) for (const s of sets) for (const mode of ['before', 'after']) { let painted = 0, total = 0; for (const off of [0,0.1,0.2,0.3,0.4,0.5,0.6,0.7,0.8,0.9]) {
    const p = await b.newPage({ deviceScaleFactor: dpr, viewport: { width: 1440, height: 900 } });
    await p.goto('http://127.0.0.1:5102/', { waitUntil: 'networkidle' });
    await p.waitForSelector('.statusbar', { timeout: 20000 });
    await p.evaluate(() => document.documentElement.setAttribute('data-platform', 'macos'));
    if (mode === 'before') await p.addStyleTag({ content: s.revert });
    await p.addStyleTag({ content: `${s.sel}{position:relative;${s.vertical ? 'left' : 'top'}:${off}px}` });
    await p.waitForTimeout(800);
    const rects = await p.$$eval(s.sel, els => els.map(e => e.getBoundingClientRect()).filter(r => r.width + r.height > 0 && r.bottom > 0).map(r => [r.x, r.y, r.width, r.height]));
    for (const [x, y, w, h] of rects) {
      const clip = s.vertical ? { x: Math.floor(x) - 1, y: Math.floor(y), width: 3, height: Math.max(2, Math.ceil(h)) }
                              : { x: Math.floor(x), y: Math.floor(y) - 1, width: Math.max(2, Math.ceil(w)), height: 3 };
      total++; if ((await inkOf(await p.screenshot({ clip }))) > 0) painted++;
    }
    await p.close();
  }
    console.log(`WebKit ${String(dpr).padEnd(4)}x ${s.sel.padEnd(8)} ${mode.padEnd(6)} painted ${painted}/${total} sub-pixel offsets`);
  }
  await b.close();
})();
