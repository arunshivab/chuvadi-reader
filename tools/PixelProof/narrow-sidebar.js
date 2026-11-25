// TESTING ONLY. ST15 (D-214): at 540 px wide the side bar floats over the page, the tool rail and Save stay in the
// window, and a pick closes the bar; at 1440 px nothing changes.
const { chromium } = require('playwright');
const [port] = process.argv.slice(2);
const base = `http://127.0.0.1:${port}`;
let fail = 0;
const check = (ok, what, extra = '') => { if (!ok) fail++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${what}${extra ? '  ' + extra : ''}`); };
(async () => {
  const b = await chromium.launch();
  for (const w of [540, 1440]) {
    const p = await b.newPage({ viewport: { width: w, height: 800 } });
    const errs = []; p.on('pageerror', e => errs.push(String(e)));
    await p.goto(base + '/reader', { waitUntil: 'networkidle' }); await p.waitForTimeout(3500);
    const st = () => p.evaluate(() => {
      const lb = document.querySelector('.leftbar'), c = document.querySelector('.shell-body > .content'), tools = document.querySelector('.srail-tools');
      const tr = tools ? tools.getBoundingClientRect() : null;
      return { open: lb.classList.contains('open'), lbW: lb.getBoundingClientRect().width, contentLeft: c.getBoundingClientRect().left,
               toolsIn: tr ? tr.right <= innerWidth + 0.5 && tr.left >= 0 : null };
    });
    let s = await st();
    if (w === 540) {
      check(!s.open && s.contentLeft <= 48.5, '540 px: the window starts with the bar closed and the page at its side', JSON.stringify(s));
      check(s.toolsIn === true, '540 px: the tool rail is inside the window', JSON.stringify(s));
      await p.locator('.lb-toggle').click(); await p.waitForTimeout(400); s = await st();
      check(s.open && s.contentLeft <= 48.5, '540 px: opened, the bar floats over the page (the page does not move)', JSON.stringify(s));
      await p.locator('.lb-btn', { hasText: 'Library' }).click(); await p.waitForTimeout(800); s = await st();
      check(!s.open, '540 px: a pick (Library) closes it', JSON.stringify(s));
    } else {
      check(s.open && Math.abs(s.contentLeft - 208) < 1, '1440 px: as before, open bar beside the page (208 px)', JSON.stringify(s));
    }
    check(errs.length === 0, `${w} px: no script errors`, JSON.stringify(errs.slice(0, 2)));
    await p.close();
  }
  await b.close(); process.exit(fail ? 1 : 0);
})();
