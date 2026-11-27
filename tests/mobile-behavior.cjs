// Execute the shared renderers with Android and desktop contracts, including lifecycle transitions.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const read=file=>fs.readFileSync(path.join(__dirname,'../ui',file),'utf8');
class Element {
 constructor(tag='div'){this.tag=tag;this.children=[];this.dataset={};this.attributes={};this.value='';this.textContent='';this.classList={toggle(){}};}
 append(...children){this.children.push(...children);}
 replaceChildren(...children){this.children=children;}
 setAttribute(key,value){this.attributes[key]=value;}
 addEventListener(){}
 querySelectorAll(tag){return this.children.flatMap(c=>[...(c.tag===tag?[c]:[]),...c.querySelectorAll(tag)]);}
 get options(){return this.children;}
 get text(){return this.textContent+' '+this.children.map(c=>c.text).join(' ');}
 focus(){this.focused=true;}
 showModal(){this.open=true;}
 close(){this.open=false;}
}
function environment(android=true){
 const elements=new Map(),events=[],timers=new Map(),domEvents={},windowEvents={};let timerId=0;
 const get=id=>{if(!elements.has(id))elements.set(id,new Element());return elements.get(id);};
 const document={hidden:false,createElement:tag=>new Element(tag),createElementNS:(_,tag)=>new Element(tag),getElementById:get,querySelectorAll:()=>[],addEventListener:(name,action)=>(domEvents[name]??=[]).push(action)};
 const window={chrome:{webview:{__androidShim:android}},addEventListener:(name,action)=>(windowEvents[name]??=[]).push(action)};
 const context=vm.createContext({window,document,console,setInterval:action=>{const id=++timerId;timers.set(id,action);return id;},clearInterval:id=>timers.delete(id)});
 return {get,window,context,events,timers,document,emitDom:name=>(domEvents[name]??[]).forEach(f=>f()),emitWindow:name=>(windowEvents[name]??[]).forEach(f=>f())};
}
const skills={readOnly:true,startAppsEditable:true,busy:false,loading:false,selectedProjectId:null,startApps:'both',projects:[],buttons:[],catalog:[],warnings:[]};
{
 const {window,context,get}=environment();vm.runInContext(read('skills.js'),context);
 window.projectSkills.render(skills);assert.equal(get('start-apps').disabled,false,'mobile can choose GPT/Claude while project editing stays read-only');
 assert.equal(get('project-select').disabled,true);
 window.projectSkills.render({...skills,busy:true});assert.equal(get('start-apps').disabled,true,'busy requests still block duplicate changes');
 const {startAppsEditable,...desktop}=skills;
 window.projectSkills.render(desktop);assert.equal(get('start-apps').disabled,true,'desktop read-only behavior preserved');
 window.projectSkills.render({...desktop,readOnly:false});assert.equal(get('start-apps').disabled,false);
}
const developer={kind:'developer-state',scope:'phone',project:null,projects:[],account:null,projectBusy:false,projectStatus:'phone',readOnly:false,shareReady:false,branch:null,defaultBranch:null,branches:[],branchBusy:false,needBranches:false,model:'앱 기본 모델',effort:'앱 기본값',models:['앱 기본 모델'],efforts:['앱 기본값'],prompts:['','',''],threads:['',''],requests:{},catalog:[],links:[],folders:[],layout:'original',theme:'green',enabled:{},running:false,verifying:false,focus:false,supportedCards:['model','effort','codex-new','web-chatgpt']};
{
 const {window,context,get,events}=environment();
 get('dev-location').append(new Element('option'),new Element('option'),new Element('option'));
 vm.runInContext(read('developer.js'),context);
 window.developerView.setup('dev',(type,details)=>events.push({type,...details}));window.developerView.receive(developer);
 for(const name of ['Codex','GitHub','도구','웹앱','폴더']){
  get('dev-tabs').children.find(tab=>tab.textContent===name).onclick();
  for(const card of get('dev-cards').children){
   if(!card.dataset.card||developer.supportedCards.includes(card.dataset.card))continue;
   const before=events.length;card.onclick();
   assert.equal(events.length,before+1,card.dataset.card+' must reach the native platform notice');
   assert.equal(events.at(-1).type,'developer-run');assert.equal(events.at(-1).id,card.dataset.card);
   assert.ok(!get('dev-dialog').open,'unsupported cards must not enter a desktop-only dialog');
  }
 }
 get('dev-tabs').children[0].onclick();get('dev-cards').children.find(card=>card.dataset.card==='model').onclick();
 assert.equal(get('dev-dialog').open,true,'supported model selection still opens');get('dev-dialog').close();
 const {supportedCards,...desktop}=developer;window.developerView.receive(desktop);
 const before=events.length;get('dev-cards').children.find(card=>card.dataset.card==='project').onclick();
 assert.equal(events.length,before,'desktop project card retains its original local selector behavior');assert.equal(get('dev-project').focused,true);
}
for(const android of [true,false]){
 const env=environment(android);const {window,context,get,timers,document,emitDom,emitWindow}=env;
 vm.runInContext(read('developer.js'),context);window.developerView.setup('widgets',()=>{});
 assert.equal(timers.size,android?0:1,'Android waits for native visibility; desktop clock starts normally');
 window.developerView.receive({kind:'visibility',visible:true});assert.equal(timers.size,1);
 window.developerView.receive({kind:'visibility',visible:true});assert.equal(timers.size,1,'no duplicate timer on resume');
 document.hidden=true;emitDom('visibilitychange');assert.equal(timers.size,0);
 document.hidden=false;emitDom('visibilitychange');assert.equal(timers.size,1);assert.ok(get('widget-clock').textContent);
 window.developerView.receive({kind:'visibility',visible:false});assert.equal(timers.size,0,'native app/tab pause stops the clock even without document.hidden');
 emitWindow('pageshow');assert.equal(timers.size,0,'a hidden native tab cannot restart on pageshow');
 window.developerView.receive({kind:'visibility',visible:true});emitWindow('pagehide');assert.equal(timers.size,0);
 emitWindow('pageshow');assert.equal(timers.size,1);
}
console.log('PASS mobile behavior: independent start-app choice, every unsupported card routed before dialogs, desktop compatibility, native/DOM clock visibility and resume');
