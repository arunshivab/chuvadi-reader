// TESTING ONLY (never shipped). File-fidelity audit E-05 to E-16, D-171, D-172: the Excel grid on screen.
// Usage: node excel-grid.js <port>   with a desktop preview on <port> opened on tests/…/Fixtures/Office/layout.xlsx
const { chromium } = require('playwright');
const port = process.argv[2];
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1440, height: 900 } });
  const errors = []; p.on('pageerror', e => errors.push(String(e))); p.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  await p.goto(`http://127.0.0.1:${port}/reader`, { waitUntil: 'networkidle' });
  await p.waitForSelector('.xv-canvas', { timeout: 20000 }); await p.waitForTimeout(1000);
  const out = []; const check = (name, ok, detail) => out.push(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  (' + detail + ')' : ''}`);
  const cell = a => p.evaluate(a => { const e = document.querySelector(`.xv-cell[data-a="${a}"]`); if (!e) return null; const r = e.getBoundingClientRect(); const t = e.querySelector('.xv-t'); const tr = t ? t.getBoundingClientRect() : null;
    return { x: r.left, y: r.top, w: r.width, h: r.height, tw: tr ? tr.width : 0, text: e.textContent.trim(), cls: e.className }; }, a);
  const headers = sel => p.evaluate(sel => [...document.querySelectorAll(sel)].map(e => e.textContent.trim()), sel);

  const a1 = await cell('A1'), a3 = await cell('A3'), a4 = await cell('A4'), b9 = await cell('B9'), a16 = await cell('A16');
  check('E-07 merged A1:B2 spans two columns and two rows', a1 && Math.abs(a1.w - 205) < 2 && Math.abs(a1.h - 41) < 2, a1 && `${a1.w} × ${a1.h}`);
  check('E-08 column A is 140 px, row 3 is 40 px, row 16 is 64 px', a3 && Math.abs(a3.w - 141) < 2 && Math.abs(a3.h - 41) < 2 && Math.abs(a16.h - 65) < 2, a3 && `${a3.w} × ${a3.h}, row 16 ${a16.h}`);
  const rows = await headers('.xv-rowhdr'), cols = await headers('.xv-colhdr');
  check('E-06 hidden row 5 and hidden column C are not drawn, the numbers and letters skip them', !rows.includes('5') && rows.includes('4') && rows.includes('6') && !cols.includes('C') && cols.includes('D'), `rows ${rows.slice(0, 7)} cols ${cols.slice(0, 5)}`);
  check('E-09 long text runs over the empty cell beside it and stops at the next filled one', a4 && a4.tw > a4.w + 40, a4 && `cell ${a4.w}, text ${a4.tw}`);
  check('E-12 rich text is drawn as runs', await p.evaluate(() => document.querySelectorAll('.xv-cell[data-a="A8"] .xv-s > span').length === 4));
  // D-200: conditional formats are not evaluated until the next Sheets update; E-13 (bars and icons) returns with it.
  check('D-200 no data bars or icons are drawn, the values are, and the sheet says its conditional colours return', await p.evaluate(() => document.querySelectorAll('.xv-databar, .xv-icon').length === 0 && document.querySelector('.xv-cell[data-a="A9"]').textContent.trim() === '10' && /Conditional colours return with the next update/.test((document.querySelector('.xv-condheld') || {}).textContent || '')));
  check('E-11 borders are drawn', await p.evaluate(() => getComputedStyle(document.querySelector('.xv-cell[data-a="A6"]')).borderBottomWidth === '3px'));
  check('E-15 a comment is marked', await p.evaluate(() => !!document.querySelector('.xv-cell[data-a="A12"] .xv-note')));
  // D-214 (amends D-172): hidden sheets are left out, as in Excel; the View rail's switch shows them, dimmed and marked.
  check('D-214 hidden sheets are not shown by default', await p.evaluate(() => [...document.querySelectorAll('.xv-tab')].map(t => t.classList.contains('hidden-sheet')).join() === 'false,false' && document.querySelectorAll('.xv-tab-hidden').length === 0));
  await p.click('.srail-view .rb[aria-label^="Show hidden rows"], .srail-view button:has-text("Show hidden rows")').catch(() => {}); await p.waitForTimeout(500);
  check('D-214 the switch shows them, dimmed and marked', await p.evaluate(() => [...document.querySelectorAll('.xv-tab')].map(t => t.classList.contains('hidden-sheet')).join() === 'false,false,true,true' && document.querySelectorAll('.xv-tab-hidden').length === 2));
  await p.click('.srail-view .rb[aria-label^="Show hidden rows"], .srail-view button:has-text("Show hidden rows")').catch(() => {}); await p.waitForTimeout(500);

  // The value bar, with the link.
  await p.click('.xv-cell[data-a="A13"]');
  check('E-15 the value bar shows the link', (await p.textContent('.xv-val-more')).includes('https://example.org/page'));

  // E-05: scroll far down and right; the window follows and the far cell is drawn. Frozen parts stay.
  const a1Shown = await cell('A1');   // the value bar has opened above the grid, so take A1's place again
  await p.evaluate(() => { const g = document.querySelector('.xv-gridwrap'); g.scrollTop = 5990 * 20; g.scrollLeft = 295 * 64; });
  await p.waitForTimeout(900);
  const far = await cell('KN6000'); const stillA1 = await cell('A1');
  const vw = await p.evaluate(() => { const r = document.querySelector('.xv-gridwrap').getBoundingClientRect(); return { l: r.left, t: r.top, r: r.right, b: r.bottom }; });
  check('E-05 a cell at row 6,000, column 300 is drawn when scrolled to', far && far.text === 'far away' && far.x > vw.l && far.x < vw.r && far.y > vw.t && far.y < vw.b, far && `at ${Math.round(far.x)}, ${Math.round(far.y)}`);
  check('frozen rows and column stay in place while scrolling', stillA1 && Math.abs(stillA1.x - a1Shown.x) < 1 && Math.abs(stillA1.y - a1Shown.y) < 1, stillA1 && `A1 was at ${a1Shown.x}, ${a1Shown.y}; now ${stillA1.x}, ${stillA1.y}`);
  const drawn = await p.evaluate(() => document.querySelectorAll('.xv-cell, .xv-rowhdr, .xv-colhdr, .xv-hl, .xv-vl').length);
  check('only the window is drawn (elements stay few on a 6,000 × 300 sheet)', drawn < 1500, `${drawn} elements`);

  // D-171: Find on the shown text and on the stored value.
  const find = async q => { await p.fill('.rfind-box', q); await p.press('.rfind-box', 'Enter'); await p.waitForTimeout(1100); return p.evaluate(() => [...document.querySelectorAll('.xv-cell.hit')].map(e => e.getAttribute('data-a'))); };
  await p.keyboard.press('Control+f'); await p.waitForTimeout(500);
  if (!await p.$('.rfind-box')) { await p.getByText('Find', { exact: true }).first().click(); }
  await p.waitForSelector('.rfind-box', { timeout: 8000 });
  const shown = await find('54,321'), stored = await find('7654321');   // A18 shows 7,654,321 (76,54,321 on en-IN) and stores 7654321
  check('D-171 Find matches the shown text and the stored value', shown.includes('A18') && stored.includes('A18'), `shown-text hits ${shown}, stored-value hits ${stored}`);
  const farHit = await find('far away'); const farAt = await cell('KN6000');
  check('Find scrolls to a far hit', farHit.includes('KN6000') && farAt && farAt.x > vw.l && farAt.x < vw.r && farAt.y > vw.t && farAt.y < vw.b);
  await p.fill('.rfind-box', ''); await p.press('.rfind-box', 'Enter'); await p.waitForTimeout(300);

  // E-16: the Settings sheet is right-to-left, without gridlines, at 150 %.
  await p.click('.xv-tab:nth-child(2)'); await p.waitForTimeout(900);
  const rtl = await p.evaluate(() => { const g = document.querySelector('.xv-gridwrap'); const a = document.querySelector('.xv-cell[data-a="A1"]').getBoundingClientRect(), b = document.querySelector('.xv-cell[data-a="B1"]').getBoundingClientRect();
    return { dir: g.getAttribute('dir'), aRightOfB: a.left > b.left, lines: document.querySelectorAll('.xv-hl, .xv-vl').length, zoom: document.querySelector('.xv-canvas').style.zoom, aw: a.width }; });
  check('E-16 right-to-left sheet: column A is on the right', rtl.dir === 'rtl' && rtl.aRightOfB);
  check('E-16 gridlines off as the sheet says', rtl.lines === 0);
  check('E-16 the sheet opens at its own zoom (150 %)', rtl.zoom === '1.5', `zoom ${rtl.zoom}, column A ${rtl.aw} px`);
  await p.screenshot({ path: 'excel-grid-settings.png' });
  await p.click('.xv-tab:nth-child(1)'); await p.waitForTimeout(700); await p.screenshot({ path: 'excel-grid-layout.png' });
  check('no script errors', errors.length === 0, errors.slice(0, 2).join(' | '));
  console.log(out.join('\n')); await b.close(); process.exit(out.some(l => l.startsWith('FAIL')) ? 1 : 0);
})();
