// Headless-Chrome CDP probe: load the plugin dashboard page, click the netease test
// link inside the iframe, and report what actually renders.
import { setTimeout as sleep } from 'node:timers/promises';

const DEBUG_PORT = process.env.DEBUG_PORT || '9226';
const KEY = process.env.EMBY_API_KEY || '';
const SITE = `http://localhost:8096/emby/web/configurationpage?name=applemusic&api_key=${KEY}`;

const send = (ws, msg) => new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('cdp timeout: ' + msg.method)), 30000);
    const onMsg = (ev) => {
        const data = JSON.parse(ev.data.toString());
        if (data.id === msg.id) {
            clearTimeout(timer);
            ws.removeEventListener('message', onMsg);
            data.error ? reject(new Error(JSON.stringify(data.error))) : resolve(data.result);
        }
    };
    ws.addEventListener('message', onMsg);
    ws.send(JSON.stringify(msg));
});

let id = 0;
const browserWs = await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/version`).then(r => r.json()).then(j => j.webSocketDebuggerUrl);
const ws = new WebSocket(browserWs);
await new Promise(r => ws.addEventListener('open', r));

const list = await fetch(`http://127.0.0.1:${DEBUG_PORT}/json`).then(r => r.json());
const page = list.find(t => t.type === 'page');
const pageWs = new WebSocket(page.webSocketDebuggerUrl);
await new Promise(r => pageWs.addEventListener('open', r));

const pid = ++id;
pageWs.addEventListener('message', (ev) => {
    const d = JSON.parse(ev.data.toString());
    if (d.method === 'Runtime.consoleAPICalled') {
        console.log('[console]', d.params.args.map(a => a.value ?? a.description ?? '').join(' '));
    }
});

await send(pageWs, { id: pid, method: 'Page.enable' });
await send(pageWs, { id: ++id, method: 'Page.navigate', params: { url: SITE } });
await sleep(9000);

const evalIn = async (expr) => {
    const r = await send(pageWs, { id: ++id, method: 'Runtime.evaluate', params: { expression: expr, returnByValue: true, awaitPromise: true } });
    return r.result.value;
};

// Wait for the iframe to carry the form
for (let i = 0; i < 20; i++) {
    const ready = await evalIn(`!!document.querySelector('iframe') && !!document.querySelector('iframe').contentDocument && document.querySelector('iframe').contentDocument.querySelectorAll('select').length >= 4`);
    if (ready) break;
    await sleep(500);
}

console.log('--- initial iframe state ---');
console.log(await evalIn(`(() => {
  const f = document.querySelector('iframe');
  const d = f.contentDocument;
  return JSON.stringify({
    iframeStyleHeight: f.style.height,
    docBg: getComputedStyle(d.body).backgroundColor,
    formHeight: d.body.scrollHeight,
    selects: d.querySelectorAll('select').length,
  });
})()`));

console.log('--- clicking test-netease link inside iframe ---');
await evalIn(`(() => {
  const d = document.querySelector('iframe').contentDocument;
  const a = d.querySelector('a[href*="op=test-netease"]');
  if (!a) return 'LINK NOT FOUND';
  a.click();
  return 'clicked';
})()`);

await sleep(3000);

console.log('--- after click ---');
console.log(await evalIn(`(() => {
  const f = document.querySelector('iframe');
  const d = f.contentDocument;
  return JSON.stringify({
    title: d.title,
    docBg: getComputedStyle(d.body).backgroundColor,
    bodyScrollHeight: d.body.scrollHeight,
    iframeStyleHeight: f.style.height,
    hasFitScript: !!d.querySelector('script'),
    text: (d.body.innerText || '').slice(0, 80),
  });
})()`));

const shot = await send(pageWs, { id: ++id, method: 'Page.captureScreenshot', params: { format: 'png' } });
const { writeFileSync } = await import('node:fs');
writeFileSync('C:/Users/user/Documents/applemusic-emby/tools/dashboard-test.png', Buffer.from(shot.data, 'base64'));
console.log('screenshot saved: tools/dashboard-test.png');
ws.close(); pageWs.close();
process.exit(0);
