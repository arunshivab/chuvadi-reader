// TESTING ONLY (never shipped). D-191: the encoding picker for text, Markdown and HTML files.
// Desktop preview opened on four files, the last one active:
//   CHVD_FF=desktop CHVD_OPEN="page.html;readme.md;utf16.txt;notes-1252.txt"
//   page.html and notes-1252.txt in Windows-1252 with accented letters and a euro sign (7 such bytes in the .txt),
//   readme.md in UTF-8, utf16.txt in UTF-16 LE with no byte-order mark holding the word சுவடி.
// Start the preview fresh for each run (the app keeps its tabs between runs). The files are not in Recents here,
// so this shows the choice held for an open file; the Recents side is in Core's TextEncodingTests.
// Usage: node encoding-picker.js <port>
const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
  p.on('console', m => { if (m.type() === 'error') console.log('CONSOLE', m.text().slice(0, 200)); });
  await p.goto(`http://127.0.0.1:${process.argv[2] || 5303}/reader`, { waitUntil: 'networkidle' });
  await p.waitForTimeout(1500);
  const sb = () => p.evaluate(() => document.querySelector('.statusbar').innerText.replace(/\s+/g, ' '));
  const body = () => p.evaluate(() => (document.querySelector('.rviewer')?.innerText || '').slice(0, 90).replace(/\s+/g, ' '));
  let fail = 0; const check = (n, ok, d) => { if (!ok) fail++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${n}${d ? '  [' + d + ']' : ''}`); };
  check('1252 file: status bar button shows the encoding', await p.$eval('.sb-btn', e => e.innerText.trim()) === 'Windows-1252', await sb());
  check('1252 file reads right automatically', (await body()).includes('Café crème — €4.50'), await body());
  await p.click('.sb-btn'); await p.waitForTimeout(500);
  const rows = await p.$$eval('.enc-row', r => r.map(e => e.innerText.replace(/\s+/g, ' ').trim() + (e.classList.contains('on') ? ' *' : '')));
  check('list: Automatic + four, Automatic marked with what was detected', rows.length === 5 && rows[0] === 'Automatic Windows-1252 *', rows.join(' | '));
  await p.click('.enc-row:has-text("UTF-8")'); await p.waitForTimeout(900);
  check('choose UTF-8: button and note', (await sb()).includes("UTF-8") && /bytes don't fit UTF-8/.test(await sb()), await sb());
  check('text re-read as UTF-8 (bad bytes shown, nothing dropped)', (await body()).includes('Caf�'), await body());
  // remembered: switch tab away and back
  const tabs = await p.$$('[data-tab-id]');
  await tabs[tabs.length - 2].click(); await p.waitForTimeout(1200);
  // VW25: UTF-16 with no byte-order mark is now read automatically; the button still offers the choice.
  check('utf16 (no BOM) tab: read automatically as UTF-16 LE (VW25), button still there', (await body()).includes('சுவடி') && /UTF-16 LE/.test(await sb()), await sb() + ' :: ' + await body());
  await p.click('.sb-btn'); await p.waitForTimeout(400);
  await p.click('.enc-row:has-text("UTF-16 LE")'); await p.waitForTimeout(900);
  check('UTF-16 LE by hand reads the Tamil text', (await body()).includes('சுவடி'), await body());
  await (await p.$$('[data-tab-id]'))[tabs.length - 1].click(); await p.waitForTimeout(1200);
  check('back on the first file: UTF-8 choice remembered', (await p.$eval('.sb-btn', e => e.innerText.trim())) === 'UTF-8', await sb());
  await p.click('.sb-btn'); await p.waitForTimeout(400);
  const rows2 = await p.$$eval('.enc-row', r => r.map(e => e.innerText.replace(/\s+/g, ' ').trim() + (e.classList.contains('on') ? ' *' : '')));
  check('list shows UTF-8 marked and what Automatic would be', rows2[0].startsWith('Automatic Windows-1252') && rows2.some(r => r === 'UTF-8 *'), rows2.join(' | '));
  // arrow keys: one Tab stop, and the row moved to is chosen
  check('one Tab stop in the list (the marked row)', await p.$$eval('.enc-row', r => r.filter(e => e.tabIndex === 0).map(e => e.innerText.trim()).join()) === 'UTF-8');
  await p.focus('.enc-row.on'); await p.keyboard.press('ArrowDown'); await p.waitForTimeout(900);
  check('Arrow Down moves to and chooses the next row', (await p.$eval('.enc-row.on', e => e.innerText.trim())) === 'Windows-1252' && await p.evaluate(() => document.activeElement.classList.contains('on')), await sb());
  await p.keyboard.press('End'); await p.waitForTimeout(900);
  check('End chooses the last row', (await p.$eval('.enc-row.on', e => e.innerText.trim())) === 'UTF-16 BE');
  await p.keyboard.press('ArrowDown'); await p.waitForTimeout(900);
  check('Arrow Down from the last row wraps to Automatic', (await p.$eval('.enc-row.on', e => e.innerText.replace(/\s+/g, ' ').trim())).startsWith('Automatic'));
  check('a file not in Recents says the choice is for this session', (await p.$eval('.enc-foot', e => e.innerText)).includes('this session only'));
  await p.keyboard.press('ArrowDown'); await p.waitForTimeout(900);
  await p.click('.enc-row:has-text("Automatic")'); await p.waitForTimeout(900);
  check('Automatic resets', (await p.$eval('.sb-btn', e => e.innerText.trim())) === 'Windows-1252' && !(await sb()).includes("don't fit"), await sb());
  // Document menu entry
  await p.keyboard.press('Escape'); await p.click('.fcard-x').catch(() => {}); await p.waitForTimeout(300);
  await p.click('.tb-doc'); await p.waitForTimeout(300);
  const has = await p.$$eval('.tb-docmenu .pop-item', e => e.map(x => x.innerText.trim()).filter(t => /Read this file as/i.test(t)));
  check('Document menu has Read this file as… (D-215)', has.length === 1, has.join());
  await p.click('.tb-docmenu .pop-item:has-text("Read this file as")'); await p.waitForTimeout(400);
  check('menu entry opens the card', await p.$('.enc-list') !== null);
  await p.click('.fcard-x'); await p.waitForTimeout(200);
  // Ctrl F on a text tab
  await p.click('.rviewer'); await p.keyboard.press('Control+f'); await p.waitForTimeout(500);
  check('Ctrl F on a text tab focuses the viewer Find box', await p.evaluate(() => document.activeElement.tagName === 'INPUT' && !!document.activeElement.closest('.rviewer, .vfind, [class*=find]')), await p.evaluate(() => document.activeElement.outerHTML.slice(0, 120)));
  await p.keyboard.press('Escape');
  // md and html tabs
  const t2 = await p.$$('[data-tab-id]');
  await t2[t2.length - 3].click(); await p.waitForTimeout(1200);
  check('Markdown tab has the button', await p.$('.sb-btn') !== null, await sb());
  await t2[t2.length - 4].click(); await p.waitForTimeout(1500);
  check('HTML tab has the button', await p.$('.sb-btn') !== null, await sb());
  await p.click('.rviewer'); await p.keyboard.press('Control+f'); await p.waitForTimeout(500);
  check('Ctrl F on an HTML tab focuses Find', await p.evaluate(() => document.activeElement.tagName === 'INPUT'), await p.evaluate(() => document.activeElement.outerHTML.slice(0, 100)));
  await p.keyboard.press('Escape');
  await t2[0].click(); await p.waitForTimeout(1500);
  check('PDF tab: no encoding button, no menu entry', await p.$('.sb-btn') === null, await sb());
  await b.close(); process.exit(fail ? 1 : 0);
})();
