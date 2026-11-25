// TESTING ONLY. BUG-25: a 504-page file on the shelf (and its desk) draws only the pages in view.
const { chromium } = require('playwright');
const [port, file] = process.argv.slice(2);
const base = `http://127.0.0.1:${port}`;
(async () => {
  const r0 = await fetch(base + '/__preview/arrange?path=' + encodeURIComponent(file), { method: 'POST' }).then(x => x.json());
  const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1440, height: 900 } });
  const errs = []; p.on('pageerror', e => errs.push(String(e)));
  await p.goto(base + '/bench', { waitUntil: 'networkidle' }); await p.waitForTimeout(8000);
  const r = await p.evaluate(() => ({ shelfPlaces: document.querySelectorAll('.shelf-pg').length, shelfDrawn: document.querySelectorAll('.shelf-pg .thumb').length,
     deskPlaces: document.querySelectorAll('.bpage[data-bench-id]').length, deskDrawn: [...document.querySelectorAll(".bpage[data-bench-id]")].filter(e => [...e.querySelectorAll("svg")].some(s => (s.getAttribute("viewBox")||"").split(" ").length === 4 && parseFloat((s.getAttribute("viewBox")||"0 0 0 0").split(" ")[2]) > 100)).length }));
  console.log(JSON.stringify(r0), JSON.stringify(r), 'errors', errs.length);
  await b.close();
})();
