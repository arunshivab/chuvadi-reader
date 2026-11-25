// TESTING ONLY (never shipped). D-198, until Pdf 3.35.1: every page operation asks first and names what each file
// will lose (with a line about layers when a file has them); redaction is hidden and says when it comes back.
// Usage: node library-holds.js <port> <plain.pdf> <layered.pdf> <rich.pdf>   (desktop preview on <port>)
const { chromium } = require('playwright');
const [port, PLAIN, LAYERED, RICH] = process.argv.slice(2);
const base = `http://127.0.0.1:${port}`;
let fail = 0;
const check = (ok, what, extra = '') => { if (!ok) fail++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${what}${extra ? '  ' + extra : ''}`); };
const post = (path) => fetch(base + path, { method: 'POST' }).then(r => r.json());
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1440, height: 900 } });
  const errs = []; p.on('pageerror', e => errs.push(String(e)));
  const card = () => p.evaluate(() => { const c = document.querySelector('.pageopq'); return c ? { title: c.querySelector('.closeq-title').textContent.trim(), text: c.querySelector('.closeq-text').textContent.replace(/\s+/g, ' ').trim(), buttons: [...c.querySelectorAll('button')].map(x => x.textContent.trim()), focus: document.activeElement && document.activeElement.textContent.trim() } : null; });
  const status = () => p.evaluate(() => document.body.innerText.match(/Bound →[^\n]*/)?.[0] || '');
  const bindLast = async () => { const n = await p.locator('.bt-bind').count(); await p.locator('.bt-bind').nth(n - 1).click(); await p.waitForSelector('.pageopq', { timeout: 8000 }).catch(() => { }); await p.waitForTimeout(500); };
  const toBench = async (file) => { await post('/__preview/arrange?path=' + encodeURIComponent(file)); await p.goto(base + '/bench', { waitUntil: 'networkidle' }); await p.waitForSelector('.bt-bind', { timeout: 30000 }); await p.waitForTimeout(800); };

  // A file with nothing to lose and no layers is still asked about (OPS-03 is on every file).
  await toBench(PLAIN); await bindLast();
  let c = await card();
  check(!!c && c.title === 'The new file will not have everything' && /plain\.pdf has none of what is left out/.test(c.text) && !/layer/.test(c.text), 'a plain file: asked first, and told it holds none of what is left out', c && c.text);
  check(!!c && JSON.stringify(c.buttons) === JSON.stringify(['Continue', 'Cancel']) && c.focus === 'Cancel', 'Continue and Cancel; the keyboard starts on Cancel', c && JSON.stringify(c.buttons) + ' focus ' + c.focus);
  await p.keyboard.press('Tab'); await p.waitForTimeout(150);
  check(await p.evaluate(() => !!document.activeElement.closest('.pageopq')), 'Tab stays inside the card');
  const before = await status(); await p.keyboard.press('Escape'); await p.waitForTimeout(800);
  check(!(await card()) && (await status()) === before, 'Esc cancels: nothing is bound');
  await bindLast(); await p.locator('.pageopq button', { hasText: 'Continue' }).click(); await p.waitForTimeout(2500);
  check(!(await card()) && /plain/.test(await status()), 'Continue binds the desk', await status());
  await bindLast(); await p.waitForTimeout(1500);
  check(!(await card()), 'the same file is not asked about again while the app runs');

  // A file holding bookmarks, page labels and a language: each is named.
  await toBench(RICH); await bindLast(); c = await card();
  check(!!c && /The new file will not have the bookmarks, page labels and language of rich\.pdf\./.test(c.text), 'a file with bookmarks, page labels and a language: each is named', c && c.text);
  await p.screenshot({ path: 'holds-pageop-card.png' });
  await p.locator('.pageopq button', { hasText: 'Continue' }).click(); await p.waitForTimeout(2000);

  // A file with layers: the layer line is added.
  await toBench(LAYERED); await bindLast(); c = await card();
  check(!!c && /It has layers: a layer that is hidden in the file may show in the new one/.test(c.text), 'a file with layers: the layer line is added', c && c.text);
  await p.locator('.pageopq button', { hasText: 'Cancel' }).click(); await p.waitForTimeout(600);

  // Redaction: greyed in Protect, saying when it comes back; no way in.
  await post('/__preview/open?path=' + encodeURIComponent(PLAIN));
  await p.goto(base + '/reader', { waitUntil: 'networkidle' }); await p.waitForFunction(() => document.querySelectorAll('.rdesk .rpage svg').length > 0, null, { timeout: 30000 });
  await p.locator('.rdesk').click({ position: { x: 20, y: 20 } }).catch(() => { }); await p.keyboard.press('4'); await p.waitForTimeout(900);
  const rail = await p.evaluate(() => [...document.querySelectorAll('button')].filter(x => /Redact|Redaction|Find & redact/.test(x.getAttribute('aria-label') || x.textContent || '')).map(x => ({ label: (x.getAttribute('aria-label') || x.textContent).trim(), disabled: x.disabled || x.getAttribute('aria-disabled') === 'true', title: x.title || '' })));
  check(rail.length === 1 && rail[0].disabled && /Redaction comes back with the next update/.test(JSON.stringify(rail[0])), 'Protect\'s rail: one greyed "Redaction" saying it comes back with the next update, no Redact box or Find & redact', JSON.stringify(rail));
  await p.screenshot({ path: 'holds-protect-rail.png' });
  await p.keyboard.press('r'); await p.waitForTimeout(900);
  check(!/\/redact/.test(p.url()), 'the R key no longer opens redaction', p.url());
  await p.goto(base + '/redact', { waitUntil: 'networkidle' }); await p.waitForTimeout(800);
  const page = await p.evaluate(() => document.querySelector('.rd-held') ? document.querySelector('.rd-held').innerText.replace(/\s+/g, ' ').trim() : null);
  check(!!page && /Redaction comes back with the next update/.test(page) , 'the redaction page itself only says it comes back', page);
  check(await p.locator('.mk-layer, .rd-find').count() === 0, 'and offers nothing to draw or search');
  await p.screenshot({ path: 'holds-redact-page.png' });
  check(errs.length === 0, 'no script errors', errs.join(' | '));
  console.log(fail === 0 ? 'ALL PASS' : `${fail} FAILED`); await b.close(); process.exit(fail ? 1 : 0);
})();
