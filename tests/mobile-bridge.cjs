// Android WebView 브리지(android/app/src/main/assets/hub-bridge.js) 회귀 검사. Node 만 필요합니다.
// 데스크톱 WebView2 와 같은 window.chrome.webview 모양을 만드는지, Kotlin 쪽 AndroidHub 로 JSON 문자열을 넘기는지,
// window.__hubDeliver 가 등록한 리스너에 {data} 로 전달하는지 확인합니다.
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert');

const bridge = fs.readFileSync(path.join(__dirname, '../android/app/src/main/assets/hub-bridge.js'), 'utf8');
const css = fs.readFileSync(path.join(__dirname, '../android/app/src/main/assets/mobile.css'), 'utf8');
const html = fs.readFileSync(path.join(__dirname, '../ui/desk.html'), 'utf8');

function boot(panel, { withHost = true } = {}) {
 const sent = [];
 const classes = new Set();
 const listeners = {};
 const title = { textContent: '' };
 const body = {};
 const context = {
  console, URLSearchParams,
  location: { search: '?panel=' + panel },
  document: {
   documentElement: { classList: { add: c => classes.add(c) } },
   addEventListener: (type, f) => { (listeners[type] = listeners[type] || []).push(f); },
   getElementById: id => id === 'title' ? title : null,
   querySelector: selector => selector === '#title' ? title : null,
   body
  },
  MutationObserver: class { constructor(f) { this.f = f; } observe(target) { target.__observer = this.f; } }
 };
 context.window = context;
 if (withHost) context.AndroidHub = { postMessage: text => sent.push(text) };
 vm.createContext(context);
 vm.runInContext(bridge, context);
 return { context, sent, classes, listeners, title, body };
}

// 1. 브리지 모양과 Kotlin 으로의 전달
{
 const { context, sent, classes } = boot('macro');
 assert.equal(typeof context.chrome.webview.postMessage, 'function');
 assert.equal(typeof context.chrome.webview.addEventListener, 'function');
 assert.ok(classes.has('android'), 'html.android 클래스로 mobile.css 가 적용돼야 합니다');
 context.chrome.webview.postMessage({ type: 'macro', id: 'media', label: '재생 / 정지' });
 assert.deepEqual(JSON.parse(sent[0]), { type: 'macro', id: 'media', label: '재생 / 정지' });
 context.chrome.webview.postMessage({ type: 'x', big: 'a'.repeat(20000) });
 assert.equal(sent.length, 1, '16KB 를 넘는 메시지는 데스크톱과 같이 버립니다');
}

// 2. 허브 → 화면 전달과 리스너 격리
{
 const { context } = boot('pc');
 const received = [];
 const failing = () => { throw new Error('listener failure'); };
 context.chrome.webview.addEventListener('message', failing);
 context.chrome.webview.addEventListener('message', ({ data }) => received.push(data));
 context.chrome.webview.addEventListener('click', () => assert.fail('message 외 이벤트는 등록하지 않습니다'));
 const original = console.error; console.error = () => {};
 try {
  context.__hubDeliver(JSON.stringify({ kind: 'sample', cpu: 12.5 }));
  context.__hubDeliver('{not json');
  context.__hubDeliver('"문자열"');
 } finally { console.error = original; }
 assert.deepEqual(received, [{ kind: 'sample', cpu: 12.5 }], '깨진 JSON 과 객체가 아닌 값은 전달하지 않고, 앞선 리스너 오류가 다음 리스너를 막지 않습니다');
 context.chrome.webview.removeEventListener('message', failing);
 assert.equal(context.__hubListenerCount(), 1);
}

// 3. 호스트가 없을 때(브라우저 미리보기) 예외 없이 경고만
{
 const { context, sent } = boot('home', { withHost: false });
 const original = console.warn; let warned = 0; console.warn = () => warned++;
 try { context.chrome.webview.postMessage({ type: 'ready' }); } finally { console.warn = original; }
 assert.equal(sent.length, 0); assert.equal(warned, 1);
}

// 4. WebView2 안에서는 기존 브리지를 덮어쓰지 않음
{
 const sent = [];
 const context = { console, URLSearchParams, location: { search: '?panel=macro' }, document: { documentElement: { classList: { add() {} } }, addEventListener() {} } };
 context.window = context;
 context.chrome = { webview: { postMessage: m => sent.push(m), addEventListener() {} } };
 vm.createContext(context); vm.runInContext(bridge, context);
 context.chrome.webview.postMessage({ type: 'ready' });
 assert.deepEqual(sent, [{ type: 'ready' }], 'WebView2 브리지가 있으면 그대로 둡니다');
}

// 5. 휴대폰 문구 바꾸기: app.js 가 제목을 쓴 뒤 MutationObserver 가 고치고, 같은 값이면 다시 쓰지 않습니다
{
 const { context, listeners, title, body } = boot('pc');
 for (const f of listeners.DOMContentLoaded) f();
 title.textContent = 'PC 성능'; body.__observer();
 assert.equal(title.textContent, '휴대폰 성능');
 let writes = 0; const proxy = new Proxy(title, { set(t, k, v) { writes++; t[k] = v; return true; } });
 context.document.querySelector = s => s === '#title' ? proxy : null; body.__observer();
 assert.equal(writes, 0, '이미 맞는 문구는 다시 쓰지 않아 관찰 루프가 생기지 않습니다');
}

// 6. PanelView.inject 과 같은 끼워 넣기 지점이 desk.html 에 있는지, CSS 가 PC 전용 버튼을 숨기는지
{
 assert.ok(html.includes('<script src="vendor/gsap.min.js">'), 'hub-bridge.js 는 gsap 앞에 들어갑니다');
 assert.ok(html.includes('</head>'), 'mobile.css 는 </head> 앞에 들어갑니다');
 for (const key of ['vscode', 'terminal', 'record', 'screenshot', 'pc_lock', 'skills']) assert.ok(css.includes(`[data-key=${key}]`), key + ' 버튼은 휴대폰에서 숨깁니다');
 assert.ok(/'self'/.test(html.match(/script-src[^;]*/)[0]), 'CSP script-src self 이므로 같은 출처의 hub-bridge.js 가 실행됩니다');
}
console.log('PASS · Android bridge shim: WebView2-compatible shape, JSON hand-off, listener isolation, no-host fallback, phone titles, inject markers');
