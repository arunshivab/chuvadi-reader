// TESTING ONLY (never shipped). See docs/foundation/COMBINED-HOOKS.md for how to run: preview hosts from
// tests/ChuvadiReader.MobileUi.Preview, before on :5101 and after on :5102 (CHVD_FF=desktop), phone :5104, tablet :5105.
const { chromium } = require('playwright');
(async () => { const b = await chromium.launch();
 for (const [n, port, vp] of [['phone', 5104, { width: 390, height: 844 }], ['tablet', 5105, { width: 1180, height: 820 }]]) for (const r of ['/', '/reader', '/bench', '/library', '/settings']) {
  const p = await b.newPage({ viewport: vp, hasTouch: true }); const errs = [];
  p.on('pageerror', e => errs.push(e.message)); p.on('console', m => { if (m.type() === 'error') errs.push(m.text()); });
  await p.goto(`http://127.0.0.1:${port}${r}`, { waitUntil: 'networkidle' }); await p.waitForTimeout(1500);
  const a = await p.evaluate(() => ['data-shell','data-platform','data-input'].map(k => document.documentElement.getAttribute(k)).join('/'));
  const ui = await p.evaluate(() => !!document.querySelector('#blazor-error-ui[style*="block"]'));
  console.log(n.padEnd(7), r.padEnd(10), a, 'errorUI=' + ui, errs.length ? 'ERR ' + errs.join(' | ') : 'ok'); await p.close(); }
 await b.close(); })();
