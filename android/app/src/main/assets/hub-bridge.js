'use strict';
// Android WebView 전용 브리지. desk.html 을 열 때 PanelView 가 gsap 앞에 끼워 넣습니다.
// 데스크톱의 window.chrome.webview(WebView2) 와 같은 모양을 만들어 ui/*.js 를 수정 없이 사용합니다.
// - postMessage(message)  → AndroidHub.postMessage(JSON 문자열)  (Kotlin @JavascriptInterface)
// - addEventListener('message', f) → Kotlin 이 window.__hubDeliver(JSON 문자열) 로 전달
(() => {
 const listeners=[];
 const native=()=>window.AndroidHub;
 const existing=window.chrome&&window.chrome.webview;
 if(existing&&typeof existing.postMessage==='function'&&!existing.__androidShim)return; // WebView2 안에서는 아무 일도 하지 않음
 // Android system images can contain Chromium 80–85. Keep the shared UI's newer APIs usable.
 if(!Object.hasOwn)Object.defineProperty(Object,'hasOwn',{value:(object,key)=>Object.prototype.hasOwnProperty.call(object,key),configurable:true,writable:true});
 if(window.Element&&!Element.prototype.replaceChildren)Object.defineProperty(Element.prototype,'replaceChildren',{value:function(...nodes){while(this.firstChild)this.removeChild(this.firstChild);for(const node of nodes)this.appendChild(typeof node==='string'?document.createTextNode(node):node);},configurable:true,writable:true});
 window.chrome=window.chrome||{};
 window.chrome.webview={
  __androidShim:true,
  postMessage(message){
   const host=native();
   if(!host||typeof host.postMessage!=='function'){console.warn('AndroidHub 브리지가 없어 메시지를 보내지 못했습니다.',message);return;}
   let text;try{text=JSON.stringify(message);}catch(error){console.error(error);return;}
   if(typeof text==='string'&&text.length<=16384)host.postMessage(text);
  },
  addEventListener(type,handler){if(type==='message'&&typeof handler==='function')listeners.push(handler);},
  removeEventListener(type,handler){const index=listeners.indexOf(handler);if(type==='message'&&index>=0)listeners.splice(index,1);}
 };
 window.__hubDeliver=json=>{
  let data;
  try{data=typeof json==='string'?JSON.parse(json):json;}catch(error){console.error('허브 메시지를 읽지 못했습니다.',error);return;}
  if(!data||typeof data!=='object')return;
  for(const handler of listeners.slice()){try{handler({data});}catch(error){console.error(error);}}
 };
 window.__hubListenerCount=()=>listeners.length;
 document.documentElement.classList.add('android');
 // 휴대폰에 맞는 문구. app.js 가 부팅·상태 수신 때마다 다시 쓰기 때문에 바뀐 뒤에 고치고, 같은 값이면 건드리지 않습니다.
 const panel=new URLSearchParams(location.search).get('panel');
 const fixes={
  pc:[['#title',()=>'휴대폰 성능']],
  dev:[['#title',()=>'개발자 컨트롤 · 휴대폰']],
  widgets:[['#badge',()=>'이 휴대폰'],['.dev-clock h2',()=>'이 휴대폰의 시간']],
  macro:[['#badge',text=>text.replace(' · PC 명령',' · 휴대폰')],['.key[data-key=work_end] small',()=>'퇴근 모드 · 잠금은 PC']]
 }[panel];
 if(fixes)document.addEventListener('DOMContentLoaded',()=>{
  let applying=false;
  const apply=()=>{
   if(applying)return;applying=true;
   try{for(const [selector,next] of fixes){const node=document.querySelector(selector);if(!node)continue;const value=next(node.textContent);if(typeof value==='string'&&node.textContent!==value)node.textContent=value;}}
   finally{applying=false;}
  };
  new MutationObserver(apply).observe(document.body,{childList:true,characterData:true,subtree:true});apply();
 });
})();
