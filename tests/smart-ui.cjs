const fs=require('fs'),vm=require('vm'),assert=require('assert'),path=require('path');
function node(tag='div') {
  return {tag,children:[],attributes:{},classList:{toggle(){}},textContent:'',append(...items){this.children.push(...items);},replaceChildren(...items){this.children=items;},setAttribute(k,v){this.attributes[k]=v;}};
}
const ids=Object.fromEntries(['smart-settings','smart-status','smart-updated','smart-error','smart-rooms','smart-cards'].map(id=>[id,node()]));
const sent=[];
const context=vm.createContext({window:{},localStorage:{getItem(){return '';},setItem(){}},document:{createElement:node},$:id=>ids[id],send:(type,detail)=>sent.push({type,...detail}),press(){}});
vm.runInContext(fs.readFileSync(path.join(__dirname,'../ui/smart-home.js'),'utf8'),context);
const room={id:'bedroom',name:'침실',window:{state:'unknown',available:false},start:false,pause:false,home:false,roomClean:false,classic:false,pop:false,stopMusic:false};
const state={status:'연결 전',rooms:[room],archive:{items:[],jobs:[],photoCounts:[]}};
function all(root){return [root,...root.children.flatMap(all)];}
function text(root){return all(root).map(item=>item.textContent).join(' ');}
function button(label){return all(ids['smart-cards']).find(item=>item.tag==='button'&&item.textContent===label);}
context.window.smartHomeView.render(state);
assert(button('시작').disabled);
assert(text(ids['smart-cards']).includes('창문 · 확인 불가'));
assert(!text(ids['smart-cards']).includes('창문 · 닫힘'));
state.connected=true;room.start=true;room.classic=true;room.window={state:'off',available:true};
state.archive.jobs=[{id:'job',roomId:'bedroom',day:'2026-11-23',count:30,state:'review',detail:'Studio 확인 필요',extra:2}];
context.window.smartHomeView.render(state);
assert(!button('시작').disabled);button('클래식 ▶').onclick();
assert.deepEqual(sent.at(-1),{type:'smart',action:'classic',roomId:'bedroom',itemId:null});
assert(text(ids['smart-cards']).includes('창문 · 닫힘'));
assert(button('업로드 결과 확인'));assert(button('늦게 온 원본만 추가 편집'));
state.busy=true;context.window.smartHomeView.render(state);
assert(button('시작').disabled&&ids['smart-settings'].disabled);
console.log('PASS smart-home UI: capability gating, unknown sensor, mapped room action, review, late originals, busy controls');
