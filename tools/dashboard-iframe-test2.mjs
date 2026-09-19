// Reproduce the user scenario: open the dashboard first, then navigate to the plugin
// page, and check whether the previous page bleeds through the shell.
import { setTimeout as sleep } from 'node:timers/promises';

const DEBUG_PORT = '9226';
const KEY = process.env.EMBY_API_KEY || '';
const DASHBOARD = `http://localhost:8096/emby/web/index.html#!/home`;
const PLUGIN_PAGE = `http://localhost:8096/emby/web/configurationpage?name=musicmeta&api_key=${KEY}`;

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
const list = await fetch(`http://127.0.0.1:${DEBUG_PORT}/json`).then(r => r.json());
const page = list.find(t => t.type === 'page');
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r));

page.ws = ws;
ws.addEventListener('message', (ev) => {
    const d = JSON.parse(ev.data.toString());
    if (d.method === 'Runtime.consoleAPICalled') {
        console.log('[console]', d.params.args.map(a => a.value ?? a.description ?? '').join(' '));
    }
});

const evaluate = async (expr) => {
    const r = await send(ws, { id: ++id, method: 'Runtime.evaluate', params: { expression: expr, returnByValue: true, awaitPromise: true } });
    return r.result.value;
};
const navigate = async (url) => {
    await send(ws, { id: ++id, method: 'Page.enable' });
    await send(ws, { id: ++id, method: 'Page.navigate', params: { url } });
};

// Step 1: open the dashboard home so there is content underneath
await navigate(DASHBOARD);
await sleep(8000);
const dash = await evaluate(`(() => {
  const t = (document.body.innerText || '');
  return JSON.stringify({ isDashboard: t.includes('活动') || t.includes('Emby Server') || t.includes('控制台'), len: t.length });
})()`);
console.log('step1 dashboard open:', dash);

// Step 2: navigate to the plugin page the way the sidebar does
await navigate(PLUGIN_PAGE);
await sleep(9000);
for (let i = 0; i < 16; i++) {
    const ready = await evaluate(`!!document.querySelector('iframe') && !!document.querySelector('iframe').contentDocument && document.querySelector('iframe').contentDocument.querySelectorAll('select').length >= 4`);
    if (ready) break;
    await sleep(500);
}

console.log('step2 plugin page state:', await evaluate(`(() => {
  const f = document.querySelector('iframe');
  if (!f) return JSON.stringify({ error: 'no iframe' });
  const d = f.contentDocument;
  const bg = getComputedStyle(document.getElementById('AppleMusicConfigurationPage')).backgroundColor;
  return JSON.stringify({
    shellBg: bg,
    iframeSrc: f.src,
    formHeight: d.body.scrollHeight,
    iframeHeight: f.style.height,
    selects: d.querySelectorAll('select').length,
    albumSelected: (d.querySelector('select[name=AlbumStorefront] option[selected]') || {}).textContent || '(none)',
  });
})()`));

// Step 3: click the netease test link inside the iframe
await evaluate(`(() => {
  const d = document.querySelector('iframe').contentDocument;
  const a = d.querySelector('a[href*="op=test-netease"]');
  if (!a) return 'LINK NOT FOUND';
  a.click();
  return 'clicked';
})()`);
await sleep(8000);

console.log('step3 after test click:', await evaluate(`(() => {
  const f = document.querySelector('iframe');
  const d = f.contentDocument;
  return JSON.stringify({
    title: d.title,
    bg: getComputedStyle(d.body).backgroundColor,
    bodyH: d.body.scrollHeight,
    frameH: f.style.height,
    text: (d.body.innerText || '').slice(0, 60),
  });
})()`));

const shot = await send(ws, { id: ++id, method: 'Page.captureScreenshot', params: { format: 'png' } });
const { writeFileSync } = await import('node:fs');
writeFileSync('C:/Users/user/Documents/applemusic-emby/tools/dashboard-test2.png', Buffer.from(shot.data, 'base64'));
console.log('screenshot saved: tools/dashboard-test2.png');
ws.close();
process.exit(0);
