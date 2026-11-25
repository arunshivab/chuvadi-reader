// TESTING ONLY. BUG-20: scroll the Windows test's F02 (500 pages) through every page, as the tester did.
const { chromium } = require('playwright');
const fs = require('fs');
const [port, pid] = process.argv.slice(2);
const rss = () => Math.round(parseInt(fs.readFileSync(`/proc/${pid}/status`, 'utf8').match(/VmRSS:\s+(\d+)/)[1]) / 1024);
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1440, height: 900 } });
  const cdp = await p.context().newCDPSession(p);
  const heap = async () => Math.round((await cdp.send('Runtime.getHeapUsage')).usedSize / 1048576);
  const drawn = () => p.evaluate(() => ({ n: document.querySelectorAll('.rpage svg, .rpage img').length,
     kb: Math.round([...document.querySelectorAll('.rpage')].reduce((a, e) => a + (e.innerHTML ? e.innerHTML.length : 0), 0) / 1024) }));
  await p.goto(`http://127.0.0.1:${port}/reader`, { waitUntil: 'networkidle' }); await p.waitForTimeout(5000);
  let peak = 0;
  for (let step = 0; step < 520; step++) {
    await p.evaluate(() => { const d = document.querySelector('.rdesk'); d.scrollTop += d.clientHeight * 0.95; });
    await p.waitForTimeout(120);
    if (step % 100 === 0) { const d = await drawn(); const r = rss(); peak = Math.max(peak, r); console.log(`step ${step}: app ${r} MB, JS heap ${await heap()} MB, drawn ${d.n} (${d.kb} KB of page markup)`); }
  }
  await p.waitForTimeout(3000);
  let d = await drawn(); console.log(`at the end: app ${rss()} MB, JS heap ${await heap()} MB, drawn ${d.n} (${d.kb} KB)`);
  await p.keyboard.press('Control+w'); await p.waitForTimeout(12000);
  console.log(`tab closed: app ${rss()} MB, JS heap ${await heap()} MB; app peak ${peak} MB`);
  await b.close();
})();
