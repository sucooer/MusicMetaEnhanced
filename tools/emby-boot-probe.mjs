import { writeFileSync } from 'node:fs'

const CDP = 'http://127.0.0.1:9224'
const KEY = '(API key removed)'
const SITE = `http://localhost:8096/web/index.html?api_key=${KEY}#!/item.html?id=31689`

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
ws.onmessage = e => {
  const m = JSON.parse(e.data)
  if (m.id && pending.has(m.id)) {
    const p = pending.get(m.id); pending.delete(m.id)
    m.error ? p.reject(new Error(JSON.stringify(m.error))) : p.resolve(m.result)
    return
  }
  if (m.method === 'Network.requestWillBeSent') console.log('REQ', m.params.request.method, m.params.request.url.slice(0, 140))
  if (m.method === 'Network.responseReceived') console.log('RES', m.params.response.status, m.params.request?.url?.slice(0, 140) ?? '')
  if (m.method === 'Network.loadingFailed') console.log('FAIL', m.params.errorText, m.params.blockedReason ?? '')
  if (m.method === 'Runtime.consoleAPICalled') {
    const txt = (m.params.args || []).map(a => a.value ?? a.description ?? '').join(' ').slice(0, 200)
    console.log('CONSOLE', m.params.type, txt)
  }
  if (m.method === 'Runtime.exceptionThrown') {
    console.log('EXC', m.params.exceptionDetails.text, (m.params.exceptionDetails.exception?.description ?? '').slice(0, 300))
  }
}
const send = (method, params = {}) => new Promise((resolve, reject) => {
  const n = ++id; pending.set(n, { resolve, reject })
  ws.send(JSON.stringify({ id: n, method, params }))
})
await send('Page.enable'); await send('Runtime.enable'); await send('Network.enable')
await send('Emulation.setDeviceMetricsOverride', { width: 1400, height: 900, deviceScaleFactor: 1, mobile: false })
await send('Page.navigate', { url: SITE })
await new Promise(r => setTimeout(r, 18000))
const { data } = await send('Page.captureScreenshot', { format: 'png' })
writeFileSync('emby-boot.png', Buffer.from(data, 'base64'))
ws.close()
console.log('done')
