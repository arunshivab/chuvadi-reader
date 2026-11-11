// TESTING ONLY (never shipped). Pinch-to-zoom on every host (Arun's Linux laptop, 11 Nov).
// Usage: node pinch-zoom.js <port> [chromium|webkit]   with a desktop preview on <port> opened on a PDF, a .docx and
// an .xlsx (CHVD_OPEN="a.pdf;b.docx;c.xlsx"; the last one opened is the active tab).
// Three ways a trackpad pinch arrives are played: Ctrl + wheel (WebView2, Chromium), gesture events (WebKit on Apple
// systems), and the host passing its own gesture on through window.chuvadiPinch (WebKitGTK).
const pw = require('playwright');
const [port, engine = 'chromium'] = process.argv.slice(2);
(async () => {
  const b = await pw[engine].launch();
  const p = await b.newPage({ viewport: { width: 1440, height: 900 } });
  const errors = []; p.on('pageerror', e => errors.push(String(e)));
  await p.goto(`http://127.0.0.1:${port}/reader`, { waitUntil: 'networkidle' });
  const out = []; const check = (name, ok, detail) => out.push(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  (' + detail + ')' : ''}`);
  const tabs = p.locator('.tabstrip .tab, .tab');
  const openTab = async (ext) => { const t = p.locator(`[title$="${ext}"], .tab:has-text("${ext}")`).first(); await t.click(); await p.waitForTimeout(900); };
  const gesture = (sel, scale) => p.evaluate(([sel, scale]) => {
    const el = document.querySelector(sel), r = el.getBoundingClientRect(), x = r.left + r.width / 2, y = r.top + r.height / 2;
    const fire = (type, s) => { const e = new Event(type, { bubbles: true, cancelable: true }); e.scale = s; e.clientX = x; e.clientY = y; el.dispatchEvent(e); return e.defaultPrevented; };
    fire('gesturestart', 1); const stopped = fire('gesturechange', scale); fire('gestureend', scale); return stopped;
  }, [sel, scale]);
  const host = (sel, scale) => p.evaluate(([sel, scale]) => {
    const r = document.querySelector(sel).getBoundingClientRect(), x = r.left + r.width / 2, y = r.top + r.height / 2;
    window.chuvadiPinch('begin', 1, x, y); window.chuvadiPinch('change', (1 + scale) / 2, x, y); const ok = window.chuvadiPinch('change', scale, x, y); window.chuvadiPinch('end', scale, x, y); return ok;
  }, [sel, scale]);
  const wheel = async (sel, dy) => { const r = await p.locator(sel).first().boundingBox(); await p.mouse.move(r.x + r.width / 2, r.y + r.height / 2); await p.keyboard.down('Control'); await p.mouse.wheel(0, dy); await p.keyboard.up('Control'); await p.waitForTimeout(500); };
  const near = (a, b) => Math.abs(a - b) / b < 0.03;

  // ── PDF ──
  await openTab('.pdf'); await p.waitForSelector('.rpage-wrap', { timeout: 20000 }); await p.waitForTimeout(1200);
  const pdfZoom = () => p.evaluate(() => parseFloat(getComputedStyle(document.querySelector('.rpage-wrap').closest('[style*="--rpage-zoom"], .desk, .rdesk') || document.body).getPropertyValue('--rpage-zoom')) || parseFloat(getComputedStyle(document.querySelector('.rpage-wrap')).getPropertyValue('--rpage-zoom')));
  const deskSel = await p.evaluate(() => { let e = document.querySelector('.rpage-wrap'); while (e && !(e.style && e.style.getPropertyValue('--rpage-zoom'))) e = e.parentElement; if (e) { e.setAttribute('data-proof-desk', ''); } return e ? '[data-proof-desk]' : null; });
  let z0 = await pdfZoom();
  if (engine === 'chromium') { await wheel(deskSel, -300); const z1 = await pdfZoom(); check('PDF: Ctrl + wheel zooms (WebView2, Chromium)', z1 > z0 * 1.2, `${z0} → ${z1}`); z0 = z1; }
  const stopped = await gesture(deskSel, 1.5); await p.waitForTimeout(400); let z1 = await pdfZoom();
  check('PDF: gesture events zoom by their scale and are not left to the browser (WebKit on Apple systems)', near(z1, z0 * 1.5) && stopped, `${z0} → ${z1}`);
  // The point under the pointer stays put: a spot on the page under the view's middle, before and after.
  const spot = () => p.evaluate((sel) => { const d = document.querySelector(sel).getBoundingClientRect(), x = d.left + d.width / 2, y = d.top + d.height / 2; const w = document.elementFromPoint(x, y).closest('.rpage-wrap'); if (!w) return null; const r = (w.querySelector('.rpage svg') || w).getBoundingClientRect(); return { page: [...document.querySelectorAll('.rpage-wrap')].indexOf(w), fx: (x - r.left) / r.width, fy: (y - r.top) / r.height }; }, deskSel);
  const s0 = await spot(); z0 = z1;
  const took = await host(deskSel, 1.3); await p.waitForTimeout(400); z1 = await pdfZoom(); const s1 = await spot();
  check('PDF: the host passing its pinch on zooms (window.chuvadiPinch, WebKitGTK)', took && near(z1, z0 * 1.3), `${z0} → ${z1}`);
  check('PDF: the place under the pointer stays under it', s0 && s1 && s0.page === s1.page && Math.abs(s0.fx - s1.fx) < 0.03 && Math.abs(s0.fy - s1.fy) < 0.03, JSON.stringify([s0, s1]));

  // ── Word and Excel: their text size is their zoom ──
  for (const [ext, name, sel, zoomed] of [['.docx', 'Word', '.dv-stage', '.dv-sheet'], ['.xlsx', 'Excel', '.xv-gridwrap', '.xv-canvas']]) {
    await openTab(ext); await p.waitForSelector(zoomed, { timeout: 20000 }); await p.waitForTimeout(900);
    const zoom = () => p.evaluate((s) => parseFloat(document.querySelector(s).style.zoom), zoomed);
    let v0 = await zoom();
    if (engine === 'chromium') { await wheel(sel, -300); const v1 = await zoom(); check(`${name}: Ctrl + wheel zooms`, v1 > v0 * 1.2, `${v0} → ${v1}`); v0 = v1; }
    await gesture(sel, 0.8); await p.waitForTimeout(600); let v1 = await zoom();
    check(`${name}: gesture events zoom by their scale`, near(v1, v0 * 0.8), `${v0} → ${v1}`); v0 = v1;
    await host(sel, 1.25); await p.waitForTimeout(600); v1 = await zoom();
    check(`${name}: the host passing its pinch on zooms`, near(v1, v0 * 1.25), `${v0} → ${v1}`);
  }
  check('no script errors', errors.length === 0, errors.slice(0, 2).join(' | '));
  console.log(out.join('\n')); await b.close(); process.exit(out.some(l => l.startsWith('FAIL')) ? 1 : 0);
})();
