// TESTING ONLY (never shipped). Android Test round 5: the page box and the view after (1) opening another document
// while one is on a later page — the new one must stay on page 1 (it drifted to 2–4 of 4), and (2) Find — the match is
// brought into view and the page box says its page (AT-09). Tablet preview (ASPNETCORE_ENVIRONMENT=Development
// CHVD_FF=tablet) with CHVD_OPEN="doc-4p.pdf;doc-12p.pdf" (Android Test's fixtures.py: "zephyrine" at the foot of
// page 3 of 12). Before :5105, after :5106. The preview's tabs live in the server, so restart it before each case.
// Usage: node page-box.js port case   (case: open-after-9 | open-after-3 | open-after-12 | find | find-width | find-keyboard)
const { chromium } = require('playwright');
const port = process.argv[2] || 5106;
const which = process.argv[3] || 'open-after-9';
(async () => {
  const b = await chromium.launch();
  let fail = 0;
  const check = (name, ok, detail) => { if (!ok) fail++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ' (' + detail + ')' : ''}`); };
  const open = async () => {
    const p = await b.newPage({ viewport: { width: 1180, height: 820 }, hasTouch: true });
    await p.goto(`http://127.0.0.1:${port}/reader`, { waitUntil: 'networkidle' });
    await p.waitForFunction(() => document.querySelectorAll('.rdesk .rpage svg').length > 0, null, { timeout: 30000 });
    await p.waitForTimeout(1200);
    return p;
  };
  const state = (p) => p.evaluate(() => {
    const d = document.querySelector('.rdesk'), t = d.getBoundingClientRect();
    let top = -1, most = -1, px = 0;
    for (const w of d.querySelectorAll('.rpage-wrap')) {
      const r = w.getBoundingClientRect(), v = Math.min(r.bottom, t.bottom) - Math.max(r.top, t.top);
      if (top < 0 && r.bottom > t.top + 1) top = +w.dataset.page;
      if (v > px) { px = v; most = +w.dataset.page; }
    }
    const hl = d.querySelector('.rfind-hl.cur'), h = hl && hl.getBoundingClientRect();
    return { box: document.querySelector('.tb-pg-box').value, top: top + 1, most: most + 1, scroll: Math.round(d.scrollTop),
             match: h ? (h.top >= t.top && h.bottom <= t.bottom ? 'in view' : 'off screen') : 'none' };
  });

  // 1. The 12-page document on page 9, then the 4-page one is shown for the first time.
  if (which.startsWith('open-after-')) {
    const target = which.slice('open-after-'.length);
    const p = await open();
    await p.fill('.tb-pg-box', target); await p.press('.tb-pg-box', 'Enter');
    await p.waitForTimeout(1800);
    // The tablet keeps the tab strip hidden; its tab switch is the same TabsService call.
    await p.evaluate(() => document.querySelector('.rtab[title$="doc-4p.pdf"]').click());
    await p.waitForTimeout(3000);   // "with no input, on page 4 of 4 two seconds later"
    const s = await state(p);
    check(`opening the 4-page document after page ${target} of 12 stays on page 1`, s.box === '1' && s.top === 1, JSON.stringify(s));
    await p.close();
  }
  // 2. Find (AT-09): Ctrl+F, "zephyrine", Enter.
  if (which === 'find' || which === 'find-width') {
    const p = await open();
    await p.click('.rdesk', { position: { x: 20, y: 200 } });
    if (which === 'find-width') { await p.keyboard.press('w'); await p.waitForTimeout(1200); }   // the page taller than the view
    await p.keyboard.press('Control+f'); await p.waitForTimeout(400);
    await p.keyboard.type('zephyrine', { delay: 40 }); await p.keyboard.press('Enter');
    await p.waitForTimeout(3000);
    const s = await state(p);
    check(`${which}: Find brings the match into view and the page box says 3`, s.match === 'in view' && s.box === '3', JSON.stringify(s));
    await p.close();
  }
  // 3. AT-16: Find with the on-screen keyboard — the view shrinks while typing and grows back after Enter. The page
  //    box must name the page that fills the screen.
  if (which === 'find-keyboard') {
    const p = await open();
    await p.click('.rdesk', { position: { x: 20, y: 200 } });
    await p.keyboard.press('Control+f'); await p.waitForTimeout(400);
    await p.setViewportSize({ width: 1180, height: 470 }); await p.waitForTimeout(500);   // the keyboard is up
    await p.keyboard.type('zephyrine', { delay: 40 }); await p.keyboard.press('Enter');
    await p.waitForTimeout(700);
    await p.setViewportSize({ width: 1180, height: 820 });                               // and down again
    await p.waitForTimeout(3500);
    const s = await state(p);
    check('find-keyboard: the page box names the page on screen', s.box === String(s.most) && s.most === 3, JSON.stringify(s));
    await p.close();
  }
  await b.close();
  process.exit(fail ? 1 : 0);
})();
