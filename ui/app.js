'use strict';
const panels={macro:'매크로 패드',pc:'PC 성능',home:'스마트홈',log:'작업 기록',dev:'개발자 컨트롤',widgets:'보조 위젯'};
const key=new URLSearchParams(location.search).get('panel');
const $=id=>document.getElementById(id);
const channel=window.chrome.webview;
const reduced=matchMedia('(prefers-reduced-motion: reduce)');
const send=(type,detail={})=>channel.postMessage({type,...detail});
function loadScript(path){return new Promise((resolve,reject)=>{const tag=document.createElement('script');tag.src=path;tag.onload=resolve;tag.onerror=reject;document.head.append(tag);});}
const definitions=[
 ['work_start','업무 시작','work-cycle · AI','rocket'],['work_end','퇴근 · 잠금','PC 잠금','moon'],['volume_mute','볼륨 음소거','PC 소리','volume-x'],['vscode','VS Code','프로젝트','code-2'],
 ['terminal','터미널','Git Bash','terminal-square'],['record','녹화 시작','OBS','video'],['media','재생 / 정지','미디어','play'],['screenshot','스크린샷','화면','camera'],
 ['focus','집중 표시','허브 표시만','bell-off'],['skills','스킬','프로젝트별 등록','code-2'],['pc_lock','PC 잠금','Windows','lock'],
 ['youtube_upload','유튜브 업로드','업로드 계정 연결','upload'],['naver_blog_upload','네이버 블로그 업로드','글쓰기 계정 연결','notebook-pen']
];
const labels=new Map();let editing=false,selected=null,model=null,noticeTimer,chartHistory=[],lastLogId=0;
function announce(text){$('notice').textContent=text;clearTimeout(noticeTimer);noticeTimer=setTimeout(()=>$('notice').textContent='',6500);}
function press(el){if(!reduced.matches)gsap.fromTo(el,{scale:.95,y:2},{scale:1,y:0,duration:.32,ease:'back.out(2.4)',overwrite:true,clearProps:'transform'});}
function glyph(name){
 const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');
 for(const [a,b] of Object.entries({viewBox:'0 0 24 24',fill:'none',stroke:'currentColor','stroke-width':'1.8','stroke-linecap':'round','stroke-linejoin':'round','aria-hidden':'true'}))svg.setAttribute(a,b);
 for(const [tag,attrs] of (window.deskIcons[name]||[])){const child=document.createElementNS(svg.namespaceURI,tag);for(const [a,b] of Object.entries(attrs))child.setAttribute(a,b);svg.append(child);}return svg;
}
function setupKeys(){
 let cache={};try{cache=JSON.parse(localStorage.getItem('key-names')||'{}')||{}}catch{}
 if(!cache.volume_mute&&cache.mic_mute&&cache.mic_mute!=='마이크 음소거')cache.volume_mute=cache.mic_mute;
 for(const [id,original,hint,icon] of definitions){
   labels.set(id,typeof cache[id]==='string'&&cache[id].trim()?cache[id].slice(0,24):original);
   const b=document.createElement('button');b.className='key';b.dataset.key=id;
   const title=document.createElement('b'),sub=document.createElement('small');title.textContent=labels.get(id);sub.textContent=hint;b.append(glyph(icon),title,sub);
   b.onclick=()=>{press(b);if(editing){selected=id;$('key-name').value=labels.get(id);$('editor').showModal();$('key-name').select();}else if(id==='skills')window.projectSkills.open();else send('macro',{id,label:labels.get(id)})};
   if(id==='work_start'){
     const group=document.createElement('div');group.className='work-start-group';group.append(b);
     const choice=document.createElement('select');choice.id='start-apps';choice.setAttribute('aria-label','업무 시작 시 열 AI 앱');choice.title='마지막 선택을 기억합니다';
     for(const [value,text] of [['both','GPT + Claude'],['gpt','GPT'],['claude','Claude']]){const o=document.createElement('option');o.value=value;o.textContent=text;choice.append(o);}
     choice.onchange=()=>send('skill-start-apps',{value:choice.value});group.append(choice);$('keys').append(group);
   }else $('keys').append(b);
 }
 $('edit').onclick=()=>{editing=!editing;$('keys').classList.toggle('editing',editing);$('edit').textContent=editing?'편집 완료':'이름 편집';if(model)paint(model);};
 $('apply').onclick=()=>{const label=$('key-name').value.trim().slice(0,24);if(!label)return;$('keys').querySelector('[data-key="'+selected+'"] b').textContent=label;labels.set(selected,label);try{localStorage.setItem('key-names',JSON.stringify(Object.fromEntries(labels)))}catch{}$('editor').close();};
 $('key-name').onkeydown=e=>{if(e.key==='Enter'&&!e.isComposing)$('apply').click();};$('cancel').onclick=()=>$('editor').close();
 $('macro-settings').onclick=()=>send('macro-settings');
 $('account-settings').onclick=()=>send('account-settings');
}
function paint(state){
 model=state;const h=state.home;
 if(key==='macro'){
   if(state.version&&$('badge'))$('badge').textContent='v'+state.version+' · PC 명령';
   $('mode').textContent=state.mode+' 모드';
   const lit=new Set(state.active);if(state.mode==='업무')lit.add('work_start');if(state.mode==='퇴근')lit.add('work_end');if(h.lamp||h.bar)lit.add('light_toggle');if(h.ac)lit.add('ac_toggle');
   const native=state.macro||{},busy=new Set(native.busy||[]);
   const project=state.skills?.projects?.find(p=>p.id===state.skills.selectedProjectId),connections=native.accounts?.connections||[],connected=id=>connections.find(a=>a.key===id)?.connected===true;
   if(native.muted===true)lit.add('volume_mute');if(native.recording===true)lit.add('record');
   for(const b of $('keys').querySelectorAll('.key')){
     const id=b.dataset.key,active=lit.has(id);
     b.classList.toggle('on',active);
     if(['work_start','work_end','volume_mute','record','focus','light_toggle','ac_toggle'].includes(id))b.setAttribute('aria-pressed',String(active));
     b.disabled=!editing&&(busy.has(id)||(id==='work_end'&&busy.has('pc_lock'))||(!state.enabled.home&&['light_toggle','ac_toggle'].includes(id)));
     b.setAttribute('aria-busy',String(busy.has(id)));
     if(id==='volume_mute')b.querySelector('small').textContent=native.muted==null?'상태 확인 대기':native.muted?'음소거 중':'소리 켜짐';
     if(id==='record')b.querySelector('small').textContent=native.recording==null?'OBS 미연결':native.recording?'녹화 중':'녹화 대기';
     if(id==='record'&&labels.get(id)==='녹화 시작')b.querySelector('b').textContent=native.recording===true?'녹화 정지':'녹화 시작';
     if(id==='vscode')b.querySelector('small').textContent=project?.folder?'현재 프로젝트':project?'앱 열기':'프로젝트 선택';
     if(id==='terminal')b.querySelector('small').textContent=project?.folder?'프로젝트 · Git Bash':'Git Bash';
     if(id==='media')b.querySelector('small').textContent=connected('media')?(native.accounts.mediaProvider==='music'?'YouTube Music':'YouTube'):'재생 계정 연결';
     if(id==='youtube_upload')b.querySelector('small').textContent=connected(id)?'연결 계정 · 업로드 화면':'업로드 계정 연결';
     if(id==='naver_blog_upload')b.querySelector('small').textContent=connected(id)?'연결 계정 · 글쓰기 화면':'네이버 계정 연결';
   }
   if(window.projectSkills)window.projectSkills.render(state.skills);
   if(window.projectConnections)window.projectConnections.render(state.skills);
 }
 if(key==='home'){
   if(window.smartHomeView)window.smartHomeView.render(state.smart);
   for(const b of document.querySelectorAll('[data-home]')){const id=b.dataset.home;b.setAttribute('aria-pressed',String(h[id]));b.textContent=id==='presence'?(h.presence?'재실':'부재'):id==='door'?(h.door?'방문 열림':'방문 닫힘'):(h[id]?'켜짐':'꺼짐');}
   $('brightness').value=h.lamp?h.brightness:0;$('lamp-pct').textContent='밝기 '+$('brightness').value+'%';$('target').textContent=h.target+'°C';
   for(const b of document.querySelectorAll('[data-k]'))b.setAttribute('aria-pressed',String(h.bar&&h.kelvin===Number(b.dataset.k)));
   if(window.roomView)window.roomView.sync();
 }
}
function setupHome(){
 for(const b of document.querySelectorAll('[data-home]'))b.onclick=()=>{press(b);send('home',{id:b.dataset.home})};
 for(const b of document.querySelectorAll('[data-k]'))b.onclick=()=>send('home',{id:'kelvin',value:Number(b.dataset.k)});
 $('brightness').oninput=e=>$('lamp-pct').textContent='밝기 '+e.target.value+'%';
 $('brightness').onchange=e=>send('home',{id:'brightness',value:Number(e.target.value)});
 $('cool-down').onclick=()=>{if(model)send('home',{id:'target',value:Math.max(18,model.home.target-1)})};
 $('cool-up').onclick=()=>{if(model)send('home',{id:'target',value:Math.min(28,model.home.target+1)})};
}
function addLog(row){
 const li=document.createElement('li');for(const [tag,cls,val] of [['time','',row.time],['span','origin',row.pc+' · '+row.source],['span','',row.detail]]){const node=document.createElement(tag);node.className=cls;node.textContent=val;li.append(node);}
 $('history').prepend(li);if($('history').children.length>60)$('history').lastElementChild.remove();$('count').textContent=$('history').children.length+'개 · 최근 순';
 if(!reduced.matches)gsap.from(li,{opacity:.25,duration:.2});
}
function sample(data){
 const fmt=(v,n=0)=>v==null?'—':Number(v).toFixed(n);
 for(const id of ['cpu','ram']){
   const value=Math.max(0,Math.min(100,data[id]??0));
   $(id).textContent=data[id]==null?'—':fmt(value)+'%';
   $(id+'-bar').style.width=value+'%';
   $(id+'-arc').style.strokeDasharray=value+' '+(100-value);
 }
 $('memory').textContent=fmt(data.usedGb,1)+' / '+fmt(data.totalGb,1)+' GiB';$('volume').textContent=data.volume+' 여유';$('disk').textContent=fmt(data.driveFree,1)+' / '+fmt(data.driveTotal,1)+' GiB';$('network').textContent='↓ '+fmt(data.down,1)+' / ↑ '+fmt(data.up,1)+' Mbps';
 if(chartHistory.length&&data.time-chartHistory.at(-1).time>6500)chartHistory=[];
 chartHistory.push(data);chartHistory=chartHistory.filter(d=>d.time>=data.time-60000);
 const svg=d3.select('#plot'),x=d3.scaleLinear().domain([data.time-60000,data.time]).range([3,357]),y=d3.scaleLinear().domain([0,100]).range([104,6]);
 svg.selectAll('line').data([0,25,50,75,100]).join('line').attr('x1',0).attr('x2',360).attr('y1',y).attr('y2',y).attr('stroke','#547662').attr('opacity',.46).attr('stroke-dasharray','2 5');
 for(const [k,col] of [['cpu','#ffd46b'],['ram','#b8f392']]){const line=d3.line().defined(d=>d[k]!=null).x(d=>x(d.time)).y(d=>y(d[k])).curve(d3.curveMonotoneX);svg.selectAll('.'+k).data([chartHistory]).join('path').attr('class',k).attr('d',line).attr('stroke',col).attr('stroke-width',2.4).attr('fill','none');}
 $('interval').textContent='최근 60초 · 1.5초 간격 · '+new Date(data.time).toLocaleTimeString('ko-KR');
}
async function boot(){
 if(!Object.hasOwn(panels,key))return;
 $('title').textContent=panels[key];$('badge').textContent={pc:'실제 측정',home:'기기 시연',macro:'PC 명령',log:'이 PC',dev:'개발 도구',widgets:'이 PC'}[key];$(key).hidden=false;
 if(key==='macro'){await loadScript('icons.js');await loadScript('skill-descriptions.ko.js');await loadScript('skills.js');await loadScript('projects.js');setupKeys();window.projectSkills.setup();window.projectConnections.setup();$('developer-show').onclick=()=>send('developer-show');}
 if(key==='pc'){
   await loadScript('vendor/d3.min.js');
   $('gpu-details').addEventListener('toggle',()=>send('gpu-watch',{id:$('gpu-details').open?'on':'off'}));
 }
 if(key==='dev'||key==='widgets'){await loadScript('developer.js');window.developerView.setup(key,send);}
 if(key==='home'){setupHome();await loadScript('room.js');await loadScript('smart-home.js');$('badge').textContent='방별 연결';}
 if(key==='log')$('history-settings').onclick=()=>send('history-settings');
 channel.addEventListener('message',({data})=>{
   if(data.kind==='state'||data.kind==='theme')document.body.classList.toggle('neon',data.theme==='neon');
   if(data.kind==='state')paint(data);
   if(data.kind==='sample'&&key==='pc')sample(data);
   if(data.kind==='history'&&key==='log'){
     $('badge').textContent=data.status;
     const signature=data.items.map(row=>row.id).join(',');
     if(signature!==lastLogId){lastLogId=signature;$('history').replaceChildren();$('count').textContent='기록 없음';data.items.forEach(addLog);}
   }
   if(data.kind==='notice')announce(data.text);
   if(data.kind==='gpu-sample'&&key==='pc'){
     $('gpu-load').textContent=data.load==null?'—':Math.round(data.load)+'%';
     const gb=value=>value==null?'—':value.toFixed(2)+' GiB';
     $('gpu-memory').textContent=gb(data.dedicated)+' / '+gb(data.shared);
     $('gpu-status').textContent=data.status;
   }
   if(data.kind==='macro-handoff'&&key==='macro'){
     if(data.view==='projects'&&!$('projects-dialog').open)window.projectConnections.open();
     if(data.view==='skills'&&!$('skills-dialog').open)window.projectSkills.open();
     if(data.view==='project-settings'&&!$('project-settings-dialog').open)$('project-settings').click();
   }
   if(key==='dev'||key==='widgets')window.developerView.receive(data);
   if(data.kind==='skills-result'&&key==='macro'){window.projectSkills.result(data.action);window.projectConnections.result(data.action);}
 });
 send('ready');
}
boot().catch(error=>{document.querySelector('main').textContent='화면 파일을 불러오지 못했습니다. 앱 폴더를 확인한 뒤 트레이에서 이 창을 다시 켜세요.';console.error(error);});

