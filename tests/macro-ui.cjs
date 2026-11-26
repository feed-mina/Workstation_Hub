const fs = require('fs');
const vm = require('vm');
const path = require('path');
const assert = require('assert');
const ids = ['work_start','work_end','volume_mute','vscode','terminal','record','media','screenshot','focus','light_toggle','ac_toggle','pc_lock','youtube_upload','naver_blog_upload'];
function element(id) {
 const attributes = {};
 const nodes = {b:{},small:{}};
 return {dataset:{key:id},attributes,classList:{toggle(){}},setAttribute:(key,value)=>attributes[key]=value,querySelector:selector=>nodes[selector]};
}
const buttons = ids.map(element);
const elements = {mode:{},keys:{children:buttons,querySelectorAll:()=>buttons}};
const context = vm.createContext({URLSearchParams,location:{search:'?panel=macro'},window:{chrome:{webview:{}}},document:{getElementById:id=>elements[id]},matchMedia:()=>({matches:true})});
const app = fs.readFileSync(path.join(__dirname,'../ui/app.js'),'utf8');
vm.runInContext(app.slice(0,app.indexOf('boot().catch')), context);
function paint(native, homeEnabled=true) {
 context.input = {kind:'state',mode:'대기',home:{lamp:true,bar:true,ac:false},active:[],enabled:{home:homeEnabled},macro:native};
 vm.runInContext("labels.set('record','녹화 시작');paint(input)",context);
}
paint({muted:true,recording:true,busy:[]});
assert.equal(buttons[2].attributes['aria-pressed'],'true');
assert.equal(buttons[5].querySelector('b').textContent,'녹화 정지');
assert.equal(buttons[5].attributes['aria-pressed'],'true');
assert.equal(buttons[6].attributes['aria-pressed'],undefined);
paint({muted:false,recording:null,busy:['record']},false);
assert.equal(buttons[2].attributes['aria-pressed'],'false');
assert.equal(buttons[2].disabled,false);
assert.equal(buttons[5].disabled,true);
assert.equal(buttons[5].attributes['aria-pressed'],'false');
assert.equal(buttons[5].querySelector('small').textContent,'OBS 미연결');
assert.equal(buttons[0].disabled,false);
assert.equal(buttons[1].disabled,false);
assert.equal(buttons[9].disabled,true);
assert.equal(buttons[11].disabled,false);
console.log('PASS · actual state indicators, unknown OBS state, busy guard, media statelessness, PC actions with home disabled');

const connected={mediaProvider:'music',connections:[{key:'media',connected:true},{key:'youtube_upload',connected:true},{key:'naver_blog_upload',connected:true}]};
paint({muted:true,recording:true,busy:[],accounts:connected});
for(const button of buttons)if(button.dataset.key!=='record')button.querySelector('b').textContent=button.dataset.key+' custom label';
context.input.mode='업무';context.input.active=['focus'];
context.input.skills={selectedProjectId:'local',projects:[{id:'local',folder:'C:/project-A',location:'local'}]};
vm.runInContext('paint(input)',context);
const common=['work_start','work_end','volume_mute','record','media','screenshot','focus','pc_lock','youtube_upload','naver_blog_upload'];
const snapshot=()=>common.map(id=>{const b=buttons.find(b=>b.dataset.key===id);return [id,b.querySelector('b').textContent,b.querySelector('small').textContent,b.attributes['aria-pressed']];});
const before=snapshot();
assert.equal(buttons[3].querySelector('small').textContent,'현재 프로젝트');
assert.equal(buttons[4].querySelector('small').textContent,'프로젝트 · Git Bash');
context.input.skills={selectedProjectId:'cloud',projects:[{id:'cloud',folder:null,location:'cloud'}]};
vm.runInContext('paint(input)',context);
assert.deepStrictEqual(snapshot(),before);
assert.equal(buttons[3].querySelector('small').textContent,'앱 열기');
assert.equal(buttons[4].querySelector('small').textContent,'Git Bash');
assert.equal(buttons[6].querySelector('small').textContent,'YouTube Music');
assert.equal(buttons[12].attributes['aria-pressed'],undefined);
assert.equal(buttons[13].attributes['aria-pressed'],undefined);
console.log('PASS · common button names/state and service accounts survive project switch; local-only tool hints and stateless upload actions');

