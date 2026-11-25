// TESTING ONLY. BUG-02 / BUG-10: keys typed at once after Ctrl K, or after Ctrl B (new bookmark), are not lost and
// don't run one-key commands; they land in the field that opens.
const { chromium } = require('playwright');
const [port] = process.argv.slice(2);
const base = `http://127.0.0.1:${port}`;
let fail = 0;
const check = (ok, what, extra = '') => { if (!ok) fail++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${what}${extra ? '  ' + extra : ''}`); };
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1440, height: 900 } });
  const errs = []; p.on('pageerror', e => errs.push(String(e)));
  await p.goto(base + '/reader', { waitUntil: 'networkidle' }); await p.waitForTimeout(3500);
  await p.click('.rdesk', { position: { x: 5, y: 5 } }).catch(() => {});
  for (let i = 0; i < 3; i++) {
    await p.keyboard.press('Control+k'); await p.keyboard.type('layer', { delay: 0 }); await p.waitForTimeout(600);
    const v = await p.evaluate(() => { const i = document.querySelector('.cmd-in'); return i ? i.value : null; });
    check(v === 'layer', `Ctrl K then "layer" typed at once (try ${i + 1}): the bar holds all of it`, JSON.stringify(v));
    await p.keyboard.press('Escape'); await p.waitForTimeout(300);
  }
  // Ctrl B: a bookmark named at once.
  await p.click('.rdesk', { position: { x: 5, y: 5 } }).catch(() => {});
  await p.keyboard.press('Control+b'); await p.keyboard.type('Results', { delay: 0 }); await p.waitForTimeout(700);
  const bm = await p.evaluate(() => { const i = document.querySelector('.bm-rename'); return i ? { v: i.value, focused: document.activeElement === i } : null; });
  check(bm && bm.v === 'Results' && bm.focused, 'Ctrl B then "Results" typed at once: the new bookmark is named Results, field focused', JSON.stringify(bm));
  check(errs.length === 0, 'no script errors', JSON.stringify(errs.slice(0, 2)));
  await b.close(); process.exit(fail ? 1 : 0);
})();
