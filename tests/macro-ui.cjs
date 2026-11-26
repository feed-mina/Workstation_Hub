const fs = require('fs');
const vm = require('vm');
const path = require('path');
const assert = require('assert');
const ids = ['work_start','work_end','volume_mute','vscode','terminal','record','media','screenshot','focus','light_toggle','ac_toggle','pc_lock'];
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

