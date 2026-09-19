import { writeFileSync } from 'node:fs'

const CDP = 'http://127.0.0.1:9224'
const KEY = process.env.EMBY_API_KEY || '';
const SITE = `http://localhost:8096/web/index.html?api_key=${KEY}#!/item.html?id=31689`
const log = (...a) => console.log('[probe]', ...a)

let list = await (await fetch(`${CDP}/json/list`)).json()
let page = list.find(t => t.type === 'page')
if (!page) {
  await fetch(`${CDP}/json/new?about:blank`, { method: 'PUT' })
  await new Promise(r => setTimeout(r, 800))
  list = await (await fetch(`${CDP}/json/list`)).json()
  page = list.find(t => t.type === 'page')
}
const ws = new WebSocket(page.webSocketDebuggerUrl)
await new Promise((resolve, reject) => {
  ws.addEventListener('open', resolve, { once: true })
  ws.addEventListener('error', reject, { once: true })
})

let id = 0
const pending = new Map()
const imgEvents = []
ws.onmessage = e => {
  const m = JSON.parse(e.data)
  if (m.id && pending.has(m.id)) {
    const p = pending.get(m.id); pending.delete(m.id)
    m.error ? p.reject(new Error(JSON.stringify(m.error))) : p.resolve(m.result)
    return
  }
  if (m.method === 'Network.requestWillBeSent') {
    const u = m.params.request.url
    if (/Images\/Remote|mzstatic|RemoteSearch\/Image/i.test(u)) imgEvents.push(`REQ  ${u.slice(0, 150)}`)
  }
  if (m.method === 'Network.responseReceived') {
    const u = m.params.response.url
    if (/Images\/Remote|mzstatic|RemoteSearch\/Image/i.test(u)) imgEvents.push(`RES ${m.params.response.status} ${u.slice(0, 150)}`)
  }
  if (m.method === 'Network.loadingFailed') {
    const u = m.params.requestId
    imgEvents.push(`FAIL ${m.params.errorText}`)
  }
}
const send = (method, params = {}) => new Promise((resolve, reject) => {
  const n = ++id; pending.set(n, { resolve, reject })
  ws.send(JSON.stringify({ id: n, method, params }))
})
await send('Page.enable'); await send('Runtime.enable'); await send('Network.enable')

const ev = async expr => {
  const r = await send('Runtime.evaluate', { expression: `(async () => { ${expr} })()`, awaitPromise: true, returnByValue: true })
  if (r.exceptionDetails) throw new Error(`${r.exceptionDetails.text} :: ${r.exceptionDetails.exception?.description ?? ''}`)
  return r.result.value
}
const sleep = ms => new Promise(r => setTimeout(r, ms))
const shot = async n => { const { data } = await send('Page.captureScreenshot', { format: 'png' }); writeFileSync(n, Buffer.from(data, 'base64')); log('shot', n) }

await send('Emulation.setDeviceMetricsOverride', { width: 1500, height: 1000, deviceScaleFactor: 1, mobile: false })
await send('Page.navigate', { url: SITE })

let st = null
for (let i = 0; i < 45; i++) {
  await sleep(2000)
  st = JSON.parse(await ev(`return JSON.stringify({ nodes: document.querySelectorAll('*').length, text: document.body.innerText.slice(0,80) })`))
  if (st.nodes > 3000) { log('rendered at', i, JSON.stringify(st)); break }
}
log('state', JSON.stringify(st))
await shot('emby-item.png')

// open the "more" menu (…)
const moreInfo = await ev(`
  const btns = [...document.querySelectorAll('button')].filter(b => b.getBoundingClientRect().width > 0)
  return JSON.stringify(btns.map(b => ({ t: (b.textContent||'').trim().slice(0,20), title: b.title||'', cls: (b.className||'').toString().slice(0,60) })).slice(0, 25))
`)
log('buttons', moreInfo)

const clickMore = await ev(`
  const b = [...document.querySelectorAll('button')].filter(x => x.getBoundingClientRect().width > 0)
    .find(x => /更多|more|btnMore|…|\\.\\.\\./i.test((x.title||'') + (x.className||'') + (x.textContent||'')))
  if (!b) return 'more button not found'
  b.click(); return 'clicked more: ' + (b.title || b.className).slice(0, 60)
`)
log(clickMore)
await sleep(2000)
const menu = await ev(`
  return JSON.stringify([...document.querySelectorAll([...document.querySelectorAll('*')].length && 'a, button, [role=menuitem], div')]
    .filter(e => e.getBoundingClientRect().width > 0 && /识别|Identify|Identify/i.test(e.textContent || ''))
    .map(e => ({ tag: e.tagName, text: (e.textContent||'').trim().slice(0,24), cls: (e.className||'').toString().slice(0,40) })).slice(0, 8))
`)
log('menu items with 识别:', menu)

const clickIdentify = await ev(`
  const e = [...document.querySelectorAll('a, button, [role=menuitem], div')]
    .filter(x => x.getBoundingClientRect().width > 0 && /识别|Identify/i.test(x.textContent || ''))
    .pop()
  if (!e) return 'no identify item'
  e.click(); return 'clicked identify: ' + (e.textContent||'').trim().slice(0,30)
`)
log(clickIdentify)
await sleep(4000)
await shot('emby-identify.png')

// click search inside dialog
const searchClick = await ev(`
  const dlg = document.querySelector('.dialog, [role=dialog], .formDialog') || document.body
  const btn = [...dlg.querySelectorAll('button')].filter(b => b.getBoundingClientRect().width > 0)
    .find(b => /搜索|Search/i.test((b.textContent||'') + (b.title||'')))
  if (!btn) return JSON.stringify({ err: 'no search button', buttons: [...dlg.querySelectorAll('button')].map(b=>(b.textContent||'').trim().slice(0,14)).slice(0,12) })
  btn.click(); return 'clicked search'
`)
log('search:', searchClick)
await sleep(8000)
await shot('emby-identify-results.png')

const results = await ev(`
  const dlg = document.querySelector('.dialog, [role=dialog], .formDialog') || document.body
  const cards = [...dlg.querySelectorAll('*')].filter(e => /result/i.test(e.className||''))
  const imgs = [...dlg.querySelectorAll('img')].map(im => ({
    src: (im.currentSrc || im.src || '').slice(0, 130),
    nw: im.naturalWidth, nh: im.naturalHeight, complete: im.complete,
    alt: (im.alt||'').slice(0,40), parentText: (im.closest('div')?.textContent||'').trim().slice(0,40),
  }))
  return JSON.stringify({ dialogText: dlg.innerText.slice(0, 400), imgCount: imgs.length, imgs: imgs.slice(0, 8), cardCount: cards.length })
`)
log('RESULTS', results)
log('IMG NETWORK', JSON.stringify(imgEvents, null, 1))
ws.close()
