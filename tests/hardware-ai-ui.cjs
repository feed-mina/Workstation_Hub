const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

class Element {
 constructor(tag='div'){this.tag=tag;this.children=[];this.dataset={};this.attributes={};}
 setAttribute(name,value){this.attributes[name]=value;}
 append(...children){this.children.push(...children);}
 replaceChildren(...children){this.children=children;}
 querySelectorAll(tag){return this.children.flatMap(child=>[...(child.tag===tag?[child]:[]),...child.querySelectorAll(tag)]);}
 get options(){return this.children;}
 get text(){return (this.textContent??'')+this.children.map(child=>child.text).join(' ');}
 focus(){}
 showModal(){this.open=true;}
 close(){this.open=false;}
 addEventListener(){}
}
const elements = new Map();
function get(id) {
 if(!elements.has(id)) elements.set(id,new Element());
 return elements.get(id);
}
const document={createElement:(tag)=>new Element(tag),createElementNS:(_,tag)=>new Element(tag),getElementById:get};
const events=[];
const window={chrome:{webview:{}}};
const env=vm.createContext({window,document,URLSearchParams,location:{search:'?panel=pc'},matchMedia:()=>({matches:true})});
const write=(name)=>fs.readFileSync(path.join(__dirname,'../ui',name),'utf8');
const app=write('app.js');
vm.runInContext(app.slice(0,app.indexOf('boot().catch')),env);
env.sampleData={load:0,dedicated:0,shared:1.5,cpuTemperature:null,gpuTemperature:{celsius:46.2,sensor:'GPU 코어',source:'Windows'},status:'Windows'};
vm.runInContext('paintGpu(sampleData)',env);
assert.equal(get('gpu-load').text,'0%');
assert.match(get('gpu-memory').text,/전용 0.00 GiB · 공유 1.50 GiB/);
assert.equal(get('cpu-temperature').text,'센서 미제공');
assert.equal(get('gpu-temperature').text,'46.2 °C');
assert.equal(get('gpu-temperature').title,'GPU 코어 · Windows');

vm.runInContext(write('developer.js'),env);
const view=window.developerView;
view.setup('dev',(type,details)=>events.push({type,details}));
assert.ok(get('dev-cards').children.some(card=>card.dataset.card==='codex-model-comparison'));
assert.ok(!get('dev-cards').children.some(card=>card.dataset.card==='codex-sidebar'));
get('dev-ai').onclick();
assert.equal(events.at(-1).type,'developer-ai');
view.receive({kind:'ai-account-state',providers:[
 {id:'gpt',title:'GPT',status:'Account',connected:false,statusText:'계정 로그인 확인'},
 {id:'claude',title:'Claude Code',status:'SignedOut',connected:false,statusText:'로그인 필요'}
]});
assert.match(get('ai-providers').children[0].text,/기존 계정 연결/);
assert.match(get('ai-providers').children[1].text,/공식 계정 로그인/);
view.receive({kind:'ai-account-state',providers:[{id:'gpt',title:'GPT',status:'Account',linked:true,connected:true}]});
assert.match(get('ai-providers').text,/연결됨.*허브 연동 해제/);

get('model-dialog').showModal();
const recent={id:'new',title:'Newest',context:272000,latest:true,listed:true,work:'복잡한 작업'};
const older={id:'old',title:'Previous',context:500000,latest:false,listed:true,work:'반복 작업'};
const data={kind:'model-comparison',models:[recent,older],preferred:'앱 기본 모델',builtInDate:'2026-11-27'};
view.receive(data);
const selects=()=>get('model-comparison').querySelectorAll('select');
assert.deepEqual(selects().map(select=>select.value),['new','old']);
assert.match(get('model-comparison').text,/272,000 토큰.*500,000 토큰/);
view.receive({...data,models:[{...recent,id:'later',title:'Later'},{...recent,latest:false},older]});
assert.deepEqual(selects().map(select=>select.value),['later','new']);
selects()[1].value='old';selects()[1].onchange();
view.receive({...data,models:[{...recent,id:'later',title:'Later'},{...recent,latest:false},older]});
assert.deepEqual(selects().map(select=>select.value),['later','old']);
console.log('PASS hardware and AI UI: zero GPU counts, temperature source labels, account states and changing model comparisons');
