// TESTING ONLY (never shipped). AT-15: on a touch device whose host says "pen" (Android always does), a finger
// in Mark places a text box with a tap and draws a rectangle with a drag; once a pen has touched the page, a
// finger pans instead (design 12e) and the pen marks. Tablet preview (ASPNETCORE_ENVIRONMENT=Development
// CHVD_FF=tablet), before :5105, after :5106. Usage: node touch-markup.js [port]
const { chromium } = require('playwright');
const port = process.argv[2] || 5106;
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1180, height: 820 }, hasTouch: true });
  await p.goto(`http://127.0.0.1:${port}/reader`, { waitUntil: 'networkidle' });
  await p.waitForFunction(() => document.querySelectorAll('.rdesk .rpage svg').length > 0, null, { timeout: 30000 });
  await p.keyboard.press('2'); await p.waitForTimeout(800);
  const cdp = await p.context().newCDPSession(p);
  const touch = async (pts) => {   // a finger: start, moves, end (real touch events, so pointerType 'touch')
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: pts[0][0], y: pts[0][1] }] });
    for (const [x, y] of pts.slice(1)) { await p.waitForTimeout(16); await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x, y }] }); }
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
    await p.waitForTimeout(500);
  };
  const box = await p.evaluate(() => { const r = document.querySelector('.mk-layer').getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; });
  const at = (fx, fy) => [box.x + box.w * fx, box.y + Math.min(box.h, 700) * fy];
  const count = (sel) => p.evaluate((s) => document.querySelectorAll(s).length, sel);
  const scrollTop = () => p.evaluate(() => document.querySelector('.rdesk').scrollTop);
  let fail = 0;
  const check = (name, ok, detail) => { if (!ok) fail++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ' (' + detail + ')' : ''}`); };

  // 1. Text tool (the default): a finger tap places a text box.
  const t0 = await count('.mk-text');
  await touch([at(0.3, 0.3)]);
  check('finger tap with Text places a text box', await count('.mk-text') === t0 + 1, `${t0} → ${await count('.mk-text')}`);
  // 1b. AT-17: the new box has the caret, and typing goes into it — not to one-key commands ("i" opened a picker).
  const active = () => p.evaluate(() => document.activeElement.tagName + '.' + document.activeElement.className);
  check('the new text box takes the caret', (await active()) === 'TEXTAREA.mk-ta', await active());
  await p.keyboard.type('chuvadimark 123', { delay: 30 }); await p.waitForTimeout(400);
  const typed = await p.evaluate(() => { const t = document.querySelector('.mk-text .mk-ta'); return t ? t.value : null; });
  check('typing goes into the box', typed === 'chuvadimark 123', JSON.stringify(typed));
  check('still on the page, in Mark', await count('.mk-layer.textmode') > 0 && await count('.mk-text') === t0 + 1, '');
  // 1c. The box selected but the caret elsewhere (a tap on its edge): a plain key still goes into the box.
  await p.evaluate(() => document.activeElement.blur());
  await p.keyboard.type('ix', { delay: 30 }); await p.waitForTimeout(400);
  const typed2 = await p.evaluate(() => { const t = document.querySelector('.mk-text .mk-ta'); return t ? t.value : null; });
  check('a plain key with the box selected goes into the box', typed2 === 'chuvadimark 123ix', JSON.stringify(typed2));
  // 2. Rectangle: a finger drag draws one.
  await p.click('button[aria-label="Rectangle"]'); await p.waitForTimeout(300);
  const s0 = await count('.mk-shape');
  await touch([at(0.2, 0.5), at(0.3, 0.6), at(0.4, 0.7), at(0.5, 0.75)]);
  check('finger drag with Rectangle draws a rectangle', await count('.mk-shape') === s0 + 1, `${s0} → ${await count('.mk-shape')}`);
  // 3. A slow device: the layer's rectangle takes 300 ms to read, so the finger lifts before the press is ready.
  await p.evaluate(() => {
    const read = window.chuvadiReaderMkRect;
    window.chuvadiReaderMkRect = (n) => new Promise(r => setTimeout(() => r(read(n)), 300));
  });
  await p.click('button[aria-label="Text"]'); await p.waitForTimeout(300);
  const t1 = await count('.mk-text');
  await touch([at(0.75, 0.12)]);
  await p.waitForTimeout(400);
  check('slow device: a quick tap with Text still places a text box', await count('.mk-text') === t1 + 1, `${t1} → ${await count('.mk-text')}`);
  await p.click('button[aria-label="Rectangle"]'); await p.waitForTimeout(300);
  const s2 = await count('.mk-shape');
  await touch([at(0.55, 0.45), at(0.65, 0.5), at(0.75, 0.55)]);
  await p.waitForTimeout(400);
  const big = await p.evaluate(() => { const s = [...document.querySelectorAll('.mk-shape')].pop(); return s ? Math.round(s.getBoundingClientRect().width) : 0; });
  check('slow device: a quick drag with Rectangle draws it to where the finger lifted', await count('.mk-shape') === s2 + 1 && big > 50, `${s2} → ${await count('.mk-shape')}, width ${big}px`);
  // 4. A pen touches the page; from then on the pen marks and a finger pans (design 12e, D-42).
  await p.evaluate(() => {
    const l = document.querySelector('.mk-layer');
    l.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, composed: true, pointerType: 'pen', pointerId: 9, isPrimary: true }));
    l.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, composed: true, pointerType: 'pen', pointerId: 9, isPrimary: true }));
  });
  await p.waitForTimeout(300);
  const s1 = await count('.mk-shape'), y0 = await scrollTop();
  await touch([at(0.5, 0.8), at(0.5, 0.6), at(0.5, 0.4), at(0.5, 0.2)]);
  check('after a pen, a finger drag pans and does not draw', await count('.mk-shape') === s1 && await scrollTop() > y0, `shapes ${s1} → ${await count('.mk-shape')}, scroll ${y0} → ${await scrollTop()}`);
  await b.close();
  process.exit(fail ? 1 : 0);
})();
