'use strict';
(() => {
 const $=id=>document.getElementById(id);
 const node=(tag,text='',className='')=>{const el=document.createElement(tag);el.textContent=text;el.className=className;return el;};
 const button=(label,action,className='')=>{const el=node('button',label,className);el.type='button';el.onclick=action;return el;};
 const card=(id,label,icon,mode='open')=>({id,label,icon,mode});
 const groups={
  'Codex':[
   card('codex-compose','Codex 작업','file','draft'),card('execution','실행 설정','settings','choice'),
   card('saved-chats','저장한 채팅','clock','thread'),card('codex-find-chat','채팅 찾기','search'),
   card('codex-file-tree','파일 트리','folder'),card('git-graph','Git 검토','branch','read'),
   card('local-checks','로컬 검증','shield','confirm'),card('skills-menu','스킬','star','read'),
   card('prompt-1','프롬프트 1','chat','prompt'),card('prompt-2','프롬프트 2','chat','prompt'),
   card('prompt-3','프롬프트 3','chat','prompt'),card('codex-settings','앱 설정','settings')
  ],
  'GitHub':[
   card('gh-hub','Workstation_Hub','star','select'),card('gh-work-cycle','work-cycle','star','select'),card('project','저장소 선택','list','select'),card('gh-code','코드','code','read'),
   card('gh-branches','브랜치','branch','read'),card('gh-commits','커밋','clock','read'),card('gh-search','코드 검색','search','search'),card('gh-open','웹으로 열기','link'),
   card('gh-my-pr','내 PR','person','read'),card('gh-reviews','리뷰 요청','chat','read'),card('gh-pr','PR 상세','check','pr'),card('gh-new-pr','새 PR','plus','compare'),
   card('gh-compare','브랜치 비교','branch','compare'),card('gh-files','변경 파일','file','pr'),card('gh-checks','PR 체크','check','pr'),card('gh-copy','PR 링크 복사','link','pr'),
   card('gh-my-issues','내 이슈','person','read'),card('gh-issues','열린 이슈','list','read'),card('gh-new-issue','새 이슈','plus','confirm'),card('gh-notifications','알림','alert','read'),
   card('gh-mentions','내 멘션','person','read'),card('gh-projects','프로젝트','folder'),card('gh-readme','README','file','read'),card('gh-codex','Codex 열기','terminal','draft'),
   card('gh-actions','Actions','play','read'),card('gh-running','실행 중','clock','read'),card('gh-failed','실패 내역','alert','read'),card('gh-artifacts','빌드 산출물','download','read'),
   card('gh-preview','프리뷰 열기','link','read'),card('gh-deployments','운영 배포 기록','cloud','read'),card('gh-releases','릴리스','box','read'),card('gh-refresh','새로고침','refresh','read')
  ],
  '도구':[
   card('tool-code','VS Code','code'),card('tool-terminal','터미널','terminal'),card('tool-bash','Git Bash','terminal'),card('tool-manager','작업 관리자','monitor'),
   card('tool-settings','Windows 설정','settings'),card('tool-record','OBS 녹화','play','confirm'),card('tool-screenshot','스크린샷','monitor','confirm'),card('validate-all','전체 검증','shield','confirm'),
   card('tool-lock','PC 잠금','shield','confirm'),card('tool-focus','집중 모드','star'),card('tool-find','파일 검색','search','search'),card('tool-accounts','계정 연결','person')
  ],
  '웹앱':[
   card('web-work-cycle','Work-cycle','grid'),card('web-nblog','NBlog','file'),card('web-kmovement','KMovement','cloud'),card('web-portfolio','포트폴리오','monitor'),
   card('web-github','GitHub','branch'),card('web-drive','Google Drive','folder'),card('web-calendar','Google Calendar','clock'),card('web-ha','Home Assistant','monitor'),
   card('web-chatgpt','ChatGPT','chat'),card('web-claude','Claude','chat'),card('web-youtube','YouTube','play'),card('web-naver','네이버 블로그','file')
  ],
  '폴더':[
   card('folder-project','프로젝트 폴더','folder'),card('folder-documents','문서','file'),card('folder-downloads','다운로드','download'),card('folder-screenshot','스크린샷 폴더','monitor'),
   card('folder-me','ME 문서','file'),card('folder-skills','스킬 폴더','star'),card('folder-build','빌드 결과','box'),card('folder-settings','폴더 설정','settings','choice')
  ]
 };
 const icons={
  folder:'M3 7h7l2 2h9v11H3z',code:'m8 6-6 6 6 6m8-12 6 6-6 6m-2-15-4 18',
  branch:'M6 5v14m0-6c8 0 12-2 12-8M4 3h4v4H4zM16 3h4v4h-4zM4 17h4v4H4z',box:'M3 6l9-4 9 4v12l-9 4-9-4zM3 6l9 4 9-4m-9 4v12',
  search:'m16 16 5 5M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0',play:'M7 4l14 8-14 8z',check:'m3 12 6 6L21 6',
  shield:'M12 2l9 4v6c0 5-5 9-9 10-4-1-9-5-9-10V6zM8 12l3 3 5-5',clock:'M12 6v6l4 2M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20',
  chat:'M3 4h18v13H9l-6 5zM7 8h10M7 12h7',plus:'M12 4v16M4 12h16',arrow:'M4 12h16m-6-6 6 6-6 6',back:'M20 12H4m6-6-6 6 6 6',
  list:'M8 5h13M8 12h13M8 19h13M3 5h1M3 12h1M3 19h1',settings:'M4 6h16M4 12h16M4 18h16M8 3v6m8 0v6m-6 0v6',
  star:'m12 2 3 7 7 3-7 3-3 7-3-7-7-3 7-3z',terminal:'M3 4h18v16H3zM6 8l4 4-4 4m7 0h5',
  file:'M5 2h10l4 4v16H5zM14 2v5h5M8 12h8m-8 4h8',link:'m10 14 4-4m-6 7-1 1a4 4 0 0 1-6-6l5-5a4 4 0 0 1 6 0m0 10a4 4 0 0 0 6 0l5-5a4 4 0 0 0-6-6l-1 1',
  refresh:'M20 10a8 8 0 0 0-14-5L3 8m0-6v6h6M4 14a8 8 0 0 0 14 5l3-3m0 6v-6h-6',grid:'M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z',
  person:'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8M4 21v-2a8 8 0 0 1 16 0v2',alert:'M12 3L2 21h20zM12 9v5m0 3h.01',
  download:'M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4',cloud:'M5 18h14a4 4 0 0 0 0-8 7 7 0 0 0-13-2 5 5 0 0 0-1 10',
  monitor:'M3 4h18v13H3zM12 17v4m-5 0h10'
 };
 const modes={open:'열기',select:'선택',choice:'설정',read:'조회',search:'검색',pr:'PR 조회',compare:'브랜치 선택','compare-models':'최신 · 기존 모델',confirm:'확인 필요',draft:'요청 준비',prompt:'요청 저장',thread:'채팅 링크',help:'앱에서 실행'};
 const titles={'Codex':'AI 작업 제어','GitHub':'저장소 및 개발 흐름','도구':'Windows 도구','웹앱':'웹 바로가기','폴더':'이 PC의 폴더'};
 const hints={'Codex':'선택한 프로젝트의 작업을 준비하고 시작합니다.',
   'GitHub':'선택한 저장소의 실제 데이터를 조회합니다. PR·이슈는 GitHub에서 작성합니다.',
   '도구':'현재 PC에서 실행합니다. 집중 모드는 허브의 집중 표시를 바꿉니다.',
   '웹앱':'기본 브라우저에서 엽니다. 개인 링크는 바로가기 설정에서 저장하세요.',
   '폴더':'이 PC의 폴더를 탐색기에서 엽니다.'};
 const names={macro:'매크로 패드',pc:'PC 성능',home:'스마트홈',log:'작업 기록',dev:'개발자 컨트롤',widgets:'보조 위젯'};
 let publish,state,active='Codex',pendingAction=null,dialogScope=null,dialogBack=null,lastTabButton=null;
 let monthCursor,todayKey,clockInterval;
 let modelIds=[];

 function glyph(name){
  const ns='http://www.w3.org/2000/svg',svg=document.createElementNS(ns,'svg'),path=document.createElementNS(ns,'path');
  svg.setAttribute('viewBox','0 0 24 24');svg.setAttribute('aria-hidden','true');path.setAttribute('d',icons[name]);svg.append(path);return svg;
 }
 function options(select,rows,value){
  const signature=JSON.stringify(rows);
  if(select.dataset.options!==signature){
   select.replaceChildren(...rows.map(([key,label])=>{const opt=node('option',label);opt.value=key;return opt;}));select.dataset.options=signature;
  }
  select.value=value??'';
 }
 function send(type,id,value,extra={}){
  if(state)publish(type,{scope:state.scope,id,value,...extra});
 }
 function choose(id,value){send('developer-choose',id,value);}
 function run(id,value,extra){
  pendingAction=document.activeElement;
  send('developer-run',id,value,extra);
 }
 function closeDialog(){
  const modal=$('dev-dialog');
  if(modal.open)modal.close();
 }
 function dialog(title,description,render){
  closeDialog();
  dialogBack=document.activeElement;dialogScope=state.scope;
  const body=$('dev-dialog-content'),actions=$('dev-dialog-actions');
  body.replaceChildren();actions.replaceChildren();
  $('dev-dialog-title').textContent=title;$('dev-dialog-note').textContent=description;
  render(body,(label,action,primary=false)=>{const b=button(label,action,primary?'dev-primary':'');actions.append(b);return b;});
  $('dev-dialog').showModal();
 }
 function field(body,label,input){
  const wrapper=node('label',label,'dev-dialog-field');wrapper.append(input);body.append(wrapper);return input;
 }
 function dialogExecution(item){
  dialog(item.label,'새 Codex 작업에 사용할 모델과 추론 강도를 저장합니다.',(body,action)=>{
   const model=field(body,'모델',node('select')),effort=field(body,'추론 강도',node('select'));
   options(model,state.models.map(value=>[value,value]),state.model);
   options(effort,state.efforts.map(value=>[value,value]),state.effort);
   action('모델 비교',()=>{closeDialog();modelIds=[];$('model-dialog').showModal();utility('developer-models','show');});
   action('저장',()=>{send('developer-choose','execution',model.value,{effort:effort.value});closeDialog();},true);
  });
 }
 const requestTypes=[
  ['codex-compose','작성창'],['codex-approval','확인 요청'],['codex-bug','버그 수정'],
  ['codex-feature','기능 개발'],['codex-analyze','코드 분석'],['codex-refactor','리팩터링']
 ];
 function showShortcuts(){
  dialog('단축키','Codex 앱이 활성화된 상태에서 사용합니다.',body=>{
   for(const [label,key] of [['이전 채팅','Ctrl + Shift + Tab'],['다음 채팅','Ctrl + Tab'],['채팅 찾기','앱 설정에서 지정'],
     ['명령 메뉴','Ctrl + Shift + P'],['파일 찾기','Ctrl + P'],['파일 트리','Ctrl + Shift + E']]){
    const row=node('div','','dev-setting-row');row.append(node('span',label),node('kbd',key));body.append(row);
   }
  });
 }
 function dialogMenu(title,items){
  dialog(title,'',(body)=>{
   const list=node('div','','dev-menu');
   for(const [label,activateItem] of items)list.append(button(label,()=>{closeDialog();activateItem();}));
   body.append(list);
  });
 }
 function dialogDraft(item){
  const prompt=item.mode==='prompt',number=prompt?Number(item.id.slice(-1))-1:0;
  const cloud=state.project?.location==='cloud';
  const help=[state.project?.name,state.branch,cloud?'클라우드':'이 PC',state.model,state.effort].filter(Boolean).join(' · ');
  dialog(item.label,help,(body,action)=>{
   let skill,kind=item.id;
   if(item.id==='codex-compose'){
    const list=node('fieldset','','dev-request-types');
    list.append(node('legend','요청 유형'));
    const grid=node('div','','dev-menu');
    for(const [id,label] of requestTypes){
     const choice=node('label',label);
     const input=node('input');input.type='radio';input.name='dev-request-type';input.value=id;input.checked=id===kind;
     input.onchange=()=>{kind=id;text.value=state.requests[id]??'';};
     choice.prepend(input);grid.append(choice);
    }
    list.append(grid);body.append(list);
   }
   if(item.id==='codex-skill-use'){
    skill=field(body,'설치된 스킬',node('select'));
    options(skill,[['','스킬 선택'],...state.catalog.map(s=>[s.id,s.name+' · '+s.source])],'');
   }
   const text=field(body,'요청문',node('textarea'));text.maxLength=4000;text.rows=5;
   text.value=prompt?state.prompts[number]:(item.request??state.requests[item.id]??'');
   text.required=true;
   if(prompt)action('이 프로젝트에 저장',()=>{choose(item.id,text.value);closeDialog();});
   const launch=action(state.desktop===false?'요청 복사 · Codex 열기':cloud?'Codex에서 클라우드 요청 준비':
    state.project?.folder?'Codex에서 시작':'Codex 작성창 열기',()=>{
    if(!body.closest('dialog').open||!text.reportValidity())return;
    if(skill&&!skill.reportValidity())return;
    const value=text.value,selected=skill?.value;
    closeDialog();run(kind,value,{skill:selected});
   },true);
   if(skill){skill.required=true;launch.disabled=true;skill.onchange=()=>{launch.disabled=!skill.value;};}
  });
 }
 function dialogThread(item){
  const slot=Number(item.id.slice(-1))-1;
  dialog(item.label,'Codex에서 복사한 채팅 링크를 이 프로젝트에 저장해 다시 열 수 있습니다.',(body,action)=>{
   const input=field(body,'채팅 링크 또는 ID',node('input'));input.placeholder='codex://threads/…';input.maxLength=160;input.value=state.threads[slot];
   action('링크 저장',()=>{choose(item.id,input.value);closeDialog();});
   if(state.threads[slot])action('저장한 채팅 열기',()=>{closeDialog();run(item.id);},true);
  });
 }
 function dialogSearch(item){
  const repo=item.id==='gh-search';
  dialog(item.label,repo?'선택한 저장소의 GitHub 코드 검색을 사용합니다. 현재 브랜치의 코드는 코드 카드에서 확인하세요.':
   '현재 프로젝트나 작업 폴더에서 파일 이름으로 찾습니다.',(body,action)=>{
   const input=field(body,repo?'코드 검색어':'파일 이름 일부',node('input'));input.maxLength=80;input.minLength=2;input.required=true;
   const submit=()=>{if(input.reportValidity()){const value=input.value.trim();closeDialog();run(item.id,value);}};
   action('검색',submit,true);input.onkeydown=e=>{if(e.key==='Enter'&&!e.isComposing){e.preventDefault();submit();}};
  });
 }
 function dialogPr(item){
  dialog(item.label,'현재 저장소의 PR 번호를 입력하세요. 조회 결과에서 PR을 선택할 수도 있습니다.',(body,action)=>{
   const input=field(body,'PR 번호',node('input'));input.inputMode='numeric';input.pattern='[0-9]{1,10}';input.required=true;input.maxLength=10;
   input.value=state.activePr??'';
   const submit=()=>{if(input.reportValidity()){const value=input.value;closeDialog();run(item.id,value);}};
   action(item.id==='gh-copy'?'링크 복사':'조회',submit,true);input.onkeydown=e=>{if(e.key==='Enter'&&!e.isComposing){e.preventDefault();submit();}};
  });
 }
 function dialogCompare(item){
  const branches=state.branches.filter(name=>name!==state.branch);
  dialog(item.label,state.branch?'작업 브랜치 · '+state.branch:'브랜치 목록을 먼저 조회하세요.',(body,action)=>{
   if(!branches.length){body.append(node('p','비교할 다른 브랜치가 없습니다.','dev-muted'));return;}
   const select=field(body,'기준 브랜치',node('select'));
   options(select,branches.map(name=>[name,name]),branches.includes(state.defaultBranch)?state.defaultBranch:branches[0]);
   action(item.id==='gh-new-pr'?'작성 화면 열기':'비교 조회',()=>{const value=select.value;closeDialog();run(item.id,value);},true);
  });
 }
 function dialogSettings(section){
  dialog(section==='folders'?'폴더 설정':'바로가기 설정','이 PC에 저장합니다.',body=>{
   if(section!=='folders'){
    const links=node('div','','dev-settings-list');
    for(const item of state.links){
     const row=node('div','','dev-setting-row'),input=field(row,item.label,node('input'));
     input.value=item.value??'';input.maxLength=1500;input.placeholder='https://';
     row.append(button('주소 저장',()=>{choose(item.id,input.value);closeDialog();}));links.append(row);
    }
    body.append(links);
   }
   const folders=node('div','','dev-settings-list');
   for(const item of state.folders){
    const row=node('div','','dev-setting-row'),value=node('div');
    value.append(node('b',item.label),node('small',item.value||'이 PC에서 선택'));
    row.append(value,button('폴더 선택',()=>{choose(item.id,'');closeDialog();}));folders.append(row);
   }
   body.append(folders);
  });
 }
 function dialogWindows(){
  dialog('창 표시','작업 표시줄과 트레이에서도 각 창을 관리할 수 있습니다.',(body,action)=>{
   const list=node('div','','dev-window-list');
   for(const [id,name] of Object.entries(names)) {
    const toggle=button(name,()=>{choose('panel',id);closeDialog();});
    toggle.setAttribute('aria-pressed',String(state.enabled[id]===true));list.append(toggle);
   }
   body.append(list);
   action('켜진 창 모두 복원',()=>{choose('restore','');closeDialog();});
  });
 }
 function activate(item){
  if(!state||state.running)return;
  if(item.id==='execution'){dialogExecution(item);return;}
  if(item.id==='saved-chats'){
   dialogMenu(item.label,state.threads.map((_,index)=>['채팅 '+(index+1),()=>dialogThread(card('codex-recent-'+(index+1),'채팅 '+(index+1),'clock','thread'))]));return;
  }
  if(item.id==='local-checks'){
   dialogMenu(item.label,[['전체 검증',()=>run('validate-all')],['테스트',()=>run('validate-test')],['타입 검사',()=>run('validate-types')],['빌드',()=>run('validate-build')]]);return;
  }
  if(item.id==='skills-menu'){
   dialogMenu(item.label,[['스킬 목록',()=>run('codex-skills')],['스킬 사용',()=>dialogDraft(card('codex-skill-use','스킬 사용','star','draft'))],
    ['스킬 만들기',()=>dialogDraft(card('codex-skill-create','스킬 만들기','plus','draft'))]]);return;
  }
  if(item.id==='codex-model-comparison'){
   modelIds=[];
   $('model-dialog').showModal();utility('developer-models','show');return;
  }
  if(item.id==='project'){$('dev-project').focus();return;}
  if(item.id==='folder-settings'){dialogSettings('folders');return;}
  if(item.mode==='draft'||item.mode==='prompt'){dialogDraft(item);return;}
  if(item.mode==='thread'){dialogThread(item);return;}
  if(item.mode==='search'){dialogSearch(item);return;}
  if(item.mode==='compare'){dialogCompare(item);return;}
  if(item.mode==='pr'){dialogPr(item);return;}
  run(item.id);
 }
 function setupTabs(){
  const keys=Object.keys(groups);
  for(const name of keys){
   const tab=button(name,()=>changeTab(name));tab.id='dev-tab-'+keys.indexOf(name);
   tab.setAttribute('role','tab');tab.setAttribute('aria-controls','dev-action-panel');
   tab.onkeydown=e=>{
    if(e.key!=='ArrowLeft'&&e.key!=='ArrowRight'&&e.key!=='Home'&&e.key!=='End')return;
    e.preventDefault();const i=keys.indexOf(name);
    const next=e.key==='Home'?'Codex':e.key==='End'?keys.at(-1):keys[(i+(e.key==='ArrowRight'?1:keys.length-1))%keys.length];
    changeTab(next);lastTabButton.focus();
   };
   $('dev-tabs').append(tab);
  }
 }
 function changeTab(name){
  active=name;$('dev-search').value='';$('dev-result').hidden=true;
  $('dev-shortcuts').hidden=active!=='Codex';
  for(const tab of $('dev-tabs').children){const selected=tab.textContent===name;tab.setAttribute('aria-selected',String(selected));tab.tabIndex=selected?0:-1;if(selected)lastTabButton=tab;}
  $('dev-action-panel').setAttribute('aria-labelledby',lastTabButton.id);
  $('dev-tab-title').textContent=titles[active];$('dev-tab-hint').textContent=hints[active];renderCards();
 }
 function renderCards(){
  const query=$('dev-search').value.trim().toLowerCase();
  const list=groups[active].filter(item=>item.label.toLowerCase().includes(query)||item.id==='codex-compose'&&requestTypes.some(([,label])=>label.includes(query)));
  const cards=$('dev-cards');
  cards.replaceChildren();
  for(const item of list){
   const b=button('',()=>activate(item),'dev-card');b.dataset.card=item.id;
   const caption=node('small',modes[item.mode],item.mode==='confirm'||item.id==='gh-new-pr'?'dev-confirm':'');
   b.append(glyph(item.icon),node('b',item.label),caption);
   cards.append(b);
  }
  if(!list.length)cards.append(node('p','검색 결과 없음','dev-muted'));
  updateCards();
 }
 function updateCards(){
  for(const item of $('dev-cards').querySelectorAll('button')){
   item.disabled=!state||state.running;
   if(item.dataset.card==='execution'&&state)item.querySelector('small').textContent=state.model+' · '+state.effort;
   if(item.dataset.card==='tool-focus')item.setAttribute('aria-pressed',String(state?.focus===true));
  }
 }
 function renderState(data){
  if(state&&data.scope!==state.scope){
   $('dev-result').hidden=true;
   if($('dev-dialog').open&&dialogScope!==data.scope)closeDialog();
  }
  state=data;const p=data.project;
  options($('dev-project'),[['','프로젝트 선택'],...data.projects.map(item=>[item.id,(item.cloud?'☁ ':'')+item.name])],p?.id);
  const branchLabel=data.branchBusy?'조회 중':p?.repository?'기본 브랜치':'GitHub 프로젝트 필요';
  options($('dev-branch'),[...(!data.branches.includes(data.branch)?[['',branchLabel]]:[]),...data.branches.map(name=>[name,name])],data.branches.includes(data.branch)?data.branch:'');
  $('dev-location').value=p?.location??'';$('dev-location').options[2].disabled=!p?.repository;
  $('dev-location').options[1].disabled=!p?.folder;$('dev-share').checked=p?.shared===true;
  $('dev-layout').value=data.layout;$('dev-theme').textContent=data.theme==='neon'?'기존 녹색 테마':'네온 테마';
  const projectBusy=data.running||data.projectBusy||data.readOnly;
  $('dev-project').disabled=projectBusy;$('dev-location').disabled=!p||projectBusy;$('dev-share').disabled=!p?.repository||projectBusy;
  $('dev-project-settings').disabled=!p||data.running;
  $('dev-branch').disabled=data.running||!data.branches.length||data.branchBusy;
  $('dev-layout').disabled=data.running;$('dev-windows').disabled=data.running;
  $('dev-theme').disabled=data.running;$('dev-settings').disabled=data.running||data.readOnly;
  $('dev-share').title=p?.conflict?'프로젝트 설정에서 공유 충돌을 해결하세요.':data.shareReady?'내 PC끼리 공통 프로젝트 설정을 공유합니다.':'GitHub 연결 화면에서 공유 저장소를 연결하세요.';
  $('dev-context-line').textContent=[p?.repository??(p?.name??'프로젝트 미선택'),
   data.account?data.account+' · GitHub 연결':'GitHub 연결 전',
   p?.shared?(p.conflict?'공유 충돌':p.pending?'공유 대기':'설정 공유'):'이 PC만',data.model+' / '+data.effort].join('  ·  ');
  const status=data.notice||data.branchError||(data.projectBusy?data.projectStatus:null)||(p?.conflict?'공유 충돌은 프로젝트 설정에서 해결하세요.':data.projectStatus);
  $('dev-context-status').hidden=!status;$('dev-context-status').textContent=status??'';
  $('dev-working').hidden=!data.running;$('dev-cancel-run').hidden=!data.verifying;updateCards();
  if(data.needBranches)send('developer-auto-branches');
 }
 function orderCommits(commits){
  const descendants=new Map(commits.map(row=>[row.commit,0]));
  for(const row of commits)for(const parent of row.parents)if(descendants.has(parent))descendants.set(parent,descendants.get(parent)+1);
  const pending=commits.slice(),ordered=[];
  while(pending.length){
   const index=pending.findIndex(row=>descendants.get(row.commit)===0);
   if(index<0)return commits;
   const [row]=pending.splice(index,1);
   ordered.push(row);
   for(const parent of row.parents)if(descendants.has(parent))descendants.set(parent,descendants.get(parent)-1);
  }
  return ordered;
 }
 function renderGitGraph(container,data){
  const commits=orderCommits(data.rows.filter(row=>row.commit));
  if(!commits.length){container.append(node('p','표시할 커밋이 없습니다.','dev-muted'));return;}
  const height=38,width=82,svg=graphic('svg',{width,height:height*commits.length,'aria-hidden':'true'});
  const lanes=[commits[0].commit],known=new Set(commits.map(row=>row.commit)),palette=['#a4e289','#70c6de','#efc56f','#d9a4e5'];
  const graph=node('div','','dev-git-graph'),scroll=node('div','','dev-git-scroll'),detail=node('div','','dev-result-row');
  scroll.tabIndex=0;scroll.setAttribute('aria-label','커밋과 병합 관계');
  const positions=[],edges=[],controls=[];
  for(const [index,row] of commits.entries()){
   if(!lanes.includes(row.commit))lanes.push(row.commit);
   const lane=lanes.indexOf(row.commit),before=lanes.slice(),parents=row.parents.filter(parent=>known.has(parent));
   if(parents[0]&&!lanes.includes(parents[0]))lanes[lane]=parents[0];else lanes.splice(lane,1);
   for(const parent of parents.slice(1))if(!lanes.includes(parent))lanes.push(parent);
   for(const [from,id] of before.entries()){
    if(id===row.commit){
     if(index>0)edges.push([from,from,index,-.5,0]);
     for(const parent of parents)edges.push([from,lanes.indexOf(parent),index,0,.5]);
    }else edges.push([from,lanes.indexOf(id),index,-.5,.5]);
   }
   positions.push([lane,index,row.parents.length>1]);
   const control=button('',()=>select(row,control),'dev-git-commit');
   control.style.paddingLeft=(width+3)+'px';control.title=row.title;
   control.append(node('b',row.title),node('code',row.commit.slice(0,8)));
   controls.push(control);graph.append(control);
  }
  const maxLane=Math.max(...positions.map(p=>p[0]),...edges.flatMap(edge=>edge.slice(0,2)));
  const x=lane=>11+lane*Math.min(14,58/Math.max(1,maxLane));
  const y=(index,offset)=>height*(index+.5+offset);
  for(const [from,to,index,start,end] of edges){
   const middle=y(index,(start+end)/2);
   svg.append(graphic('path',{d:'M'+x(from)+','+y(index,start)+' C'+x(from)+','+middle+' '+x(to)+','+middle+' '+x(to)+','+y(index,end),
    stroke:palette[(end===0?from:to)%palette.length],'stroke-width':2,fill:'none'}));
  }
  for(const [lane,index,merge] of positions)svg.append(graphic('circle',{cx:x(lane),cy:y(index,0),r:merge?4.5:3.5,fill:palette[lane%palette.length]}));
  function select(row,control){
   for(const candidate of controls)candidate.setAttribute('aria-pressed',String(candidate===control));
   detail.replaceChildren();
   const label=node('div');label.append(node('b',row.title),node('small',row.commit.slice(0,8)+' · '+row.detail+(row.parents.length>1?' · 병합 커밋':'')));
   const actions=node('div','','dev-result-actions');
   if(row.open)actions.append(button('GitHub ↗',()=>send('developer-result-open',null,null,{key:data.key,index:String(row.index)})));
   actions.append(button('Codex에 검토 요청',()=>dialogDraft({...card('codex-git','Git 검토 요청','branch','draft'),
    request:'현재 프로젝트의 '+row.commit+' 커밋 변경을 검토하고 위험과 빠진 검증을 알려주세요.'})));
   detail.append(label,actions);
  }
  graph.prepend(svg);scroll.append(graph);container.append(scroll,detail);
  select(commits[0],controls[0]);
 }
 function graphic(tag,attributes){
  const el=document.createElementNS('http://www.w3.org/2000/svg',tag);
  for(const [key,value] of Object.entries(attributes))el.setAttribute(key,String(value));
  return el;
 }
 function renderResult(data){
  if(!state||data.scope!==state.scope)return;
  $('dev-result-title').textContent=data.title;$('dev-result-summary').textContent=data.summary;
  const rows=$('dev-result-rows');rows.replaceChildren();
  if(data.graph)renderGitGraph(rows,data);
  else for(const row of data.rows){
   const el=node('div','','dev-result-row'),text=node('div');
   text.append(node('b',row.title),node('small',row.detail));el.append(text);
   const actions=node('div','','dev-result-actions');
   if(row.pr)actions.append(button('PR 보기',()=>run('gh-pr',row.pr)));
   if(row.branch)actions.append(button('선택',()=>choose('branch',row.branch)));
   if(row.open)actions.append(button('열기 ↗',()=>send('developer-result-open',null,null,{key:data.key,index:String(row.index)})));
   el.append(actions);rows.append(el);
  }
  if(!data.rows.length&&!data.body&&active==='GitHub'&&pendingAction?.dataset?.card&&pendingAction.dataset.card!=='gh-refresh'&&data.summary.includes('GitHub 읽기 전용'))
   rows.append(node('p','표시할 항목이 없습니다.','dev-muted'));
  $('dev-result-body').textContent=data.body??'';$('dev-result-body').hidden=!data.body;
  $('dev-result').hidden=false;$('dev-result-title').focus();
 }
 function setupConsole(){
  setupTabs();changeTab('Codex');$('dev-search').oninput=renderCards;
  $('dev-shortcuts').onclick=showShortcuts;
  $('dev-project').onchange=e=>choose('project',e.target.value);$('dev-branch').onchange=e=>choose('branch',e.target.value);
  $('dev-location').onchange=e=>choose('location',e.target.value);$('dev-share').onchange=e=>choose('share',e.target.checked?'on':'off');
  $('dev-layout').onchange=e=>choose('layout',e.target.value);$('dev-add').onclick=()=>run('open-projects');
  $('dev-project-settings').onclick=()=>run('open-project-settings');
  $('dev-windows').onclick=()=>dialogWindows();$('dev-settings').onclick=()=>dialogSettings('all');
  $('dev-theme').onclick=()=>choose('theme',state.theme==='neon'?'green':'neon');
  $('dev-dialog-close').onclick=closeDialog;
  $('dev-dialog').addEventListener('close',()=>{if(!$('dev-dialog').open){dialogBack?.focus();dialogScope=null;}});
  $('dev-result-close').onclick=()=>{$('dev-result').hidden=true;(pendingAction?.isConnected?pendingAction:lastTabButton)?.focus();};
  $('dev-cancel-run').onclick=()=>send('developer-cancel');
  $('dev-ai').onclick=()=>{$('ai-dialog').showModal();utility('developer-ai','refresh');};
  $('ai-close').onclick=()=>$('ai-dialog').close();
  $('ai-refresh').onclick=()=>utility('developer-ai','refresh');
  $('model-close').onclick=()=>$('model-dialog').close();
  for(const id of ['guide','release','refresh'])$('model-'+id).onclick=()=>utility('developer-models',id);
 }
 function utility(type,id,provider){publish(type,{id,provider});}
 function renderAccounts(data){
  const root=$('ai-providers');root.replaceChildren();
  for(const provider of data.providers){
   const section=node('section','','ai-provider');
   const head=node('div','','row');
   const status=provider.connected?'연결됨':provider.login?'로그인 진행 중':provider.busy?'확인 중':provider.statusText;
   head.append(node('h3',provider.title),node('span',status,'ai-status'));
   section.append(head);
   if(provider.message)section.append(node('p',provider.message,'dev-muted'));
   const buttons=node('div','','widget-buttons');
   if(!provider.connected){
    const label=provider.status==='Account'?'기존 계정 연결':provider.status==='MissingCli'?'데스크톱에서 로그인':'공식 계정 로그인';
    const link=button(label,()=>utility('developer-ai','link',provider.id),'dev-primary');
    link.disabled=provider.busy||provider.login||data.readOnly;buttons.append(link);
   }
   if(provider.status==='MissingCli')buttons.append(button('공식 CLI 설치 안내',()=>utility('developer-ai','setup',provider.id)));
   else buttons.append(button('데스크톱 열기',()=>utility('developer-ai','desktop',provider.id)));
   if(provider.linked){
    const unlink=button('허브 연동 해제',()=>utility('developer-ai','unlink',provider.id));
    unlink.disabled=provider.busy||provider.login||data.readOnly;buttons.append(unlink);
   }
   buttons.append(button('공식 로그인 안내',()=>utility('developer-ai','guide',provider.id)));
   section.append(buttons);root.append(section);
  }
  $('ai-refresh').disabled=data.providers.some(provider=>provider.busy);
 }
 function modelCard(model,available,index,change){
  const section=node('section','','model-option');
  const select=node('select');
  field(section,index===0?'첫 번째 모델':'두 번째 모델',select);
  options(select,available.map(item=>[item.id,item.title]),model.id);
  select.onchange=()=>change(select.value);
  const generation=model.latest?'최신 세대':model.listed?'기존 세대':'이전 자료';
  section.append(node('span',generation,'model-generation'));
  const list=node('dl');
  for(const [label,value] of [
   ['주로 맞는 작업',model.work],['속도·비용과 용도',model.tradeoff],
   ['기본 콘텍스트',model.context?Number(model.context).toLocaleString('ko-KR')+' 토큰':'공식 비교 자료에 수치 없음']
  ]){
   const row=node('div');row.append(node('dt',label),node('dd',value));list.append(row);
  }
  section.append(list);return section;
 }
 function renderModels(data){
  if(!$('model-dialog').open||!data.models.length)return;
  const status=data.busy?'공식 모델 자료 확인 중':data.error??(data.checkedAt?'공식 문서 확인 '+data.checkedAt:'기본 비교 자료 '+data.builtInDate);
  $('model-updated').textContent=status;
  $('model-refresh').disabled=data.busy;
  const latest=data.models.find(item=>item.latest)??data.models[0];
  const preferred=data.models.find(item=>item.title===data.preferred&&item.id!==latest.id);
  const previous=preferred??data.models.find(item=>!item.latest)??data.models[1]??latest;
  const byId=id=>data.models.find(item=>item.id===id);
  const compared=[byId(modelIds[0])??latest,byId(modelIds[1])??previous];
  const layout=$('model-comparison');
  layout.replaceChildren(...compared.map((model,index)=>modelCard(model,data.models,index,value=>{
   modelIds[index]=value;renderModels(data);
   layout.querySelectorAll('select')[index].focus();
  })));
 }
 function widget(id,value){publish('developer-widget',{id,value});}
 function calendar(){
  const year=monthCursor.getFullYear(),month=monthCursor.getMonth(),days=$('calendar-days'),now=new Date();
  const offset=new Date(year,month,1).getDay(),total=new Date(year,month+1,0).getDate();
  $('calendar-month').textContent=year+'년 '+(month+1)+'월';days.replaceChildren();
  for(let i=0;i<offset;i++)days.append(node('span','','calendar-empty'));
  for(let i=1;i<=total;i++){
   const el=node('span',String(i));
   if(year===now.getFullYear()&&month===now.getMonth()&&i===now.getDate())el.setAttribute('aria-current','date');
   days.append(el);
  }
 }
 function clock(){
  const now=new Date(),key=now.toDateString();
  $('widget-clock').textContent=now.toLocaleTimeString('ko-KR',{hour12:false});
  if(todayKey!==key){todayKey=key;$('widget-date').textContent=now.toLocaleDateString('ko-KR',{weekday:'long',month:'long',day:'numeric'});calendar();}
 }
 function setupWidgets(){
  const now=new Date();monthCursor=new Date(now.getFullYear(),now.getMonth(),1);
  $('calendar-prev').onclick=()=>{monthCursor.setMonth(monthCursor.getMonth()-1);calendar();};
  $('calendar-next').onclick=()=>{monthCursor.setMonth(monthCursor.getMonth()+1);calendar();};
  $('calendar-today').onclick=()=>{const date=new Date();monthCursor=new Date(date.getFullYear(),date.getMonth(),1);calendar();};
  $('calendar-open').onclick=()=>widget('web-calendar');
  $('weather-city').onchange=e=>widget('city',e.target.value);
  $('weather-refresh').onclick=()=>widget('weather');$('weather-source').onclick=()=>widget('weather-source');
  for(const [id,label] of [
   ['dock-gpt','GPT'],['codex-root','Codex'],['dock-claude','Claude Code'],['web-github','GitHub'],['folder-explorer','탐색기'],
   ['web-drive','Google Drive'],['folder-work','작업 폴더'],['tool-manager','작업 관리자'],['tool-settings','Windows 설정']
  ])$('widget-dock').append(button(label,()=>widget(id)));
  for(const b of document.querySelectorAll('[data-widget]'))b.onclick=()=>widget(b.dataset.widget);
  clock();clockInterval=setInterval(clock,1000);
  window.addEventListener('pagehide',()=>clearInterval(clockInterval),{once:true});
 }
 function renderWidgets(data){
  options($('weather-city'),[['','도시 선택'],...data.cities.map(name=>[name,name])],data.city);
  $('weather-city').disabled=data.busy;$('weather-refresh').disabled=data.busy||!data.city;
  $('weather-status').textContent=[data.status,data.updated].filter(Boolean).join(' · ');
  $('weather-current').hidden=!data.weather;
  if(data.weather)$('weather-current').textContent=data.city+' '+data.weather.temperature+' · '+data.weather.description;
  const days=$('weather-days');days.replaceChildren();
  for(const day of data.days??[]){
   const el=node('div','','weather-day');el.append(node('small',day.date),node('b',day.description),node('span',day.high+' / '+day.low));days.append(el);
  }
 }
 window.developerView={
  setup(panel,send){publish=send;if(panel==='dev')setupConsole();else setupWidgets();},
  receive(data){
   if(data.kind==='developer-state')renderState(data);
   if(data.kind==='developer-result')renderResult(data);
   if(data.kind==='widget-state')renderWidgets(data);
   if(data.kind==='ai-account-state')renderAccounts(data);
   if(data.kind==='model-comparison')renderModels(data);
  }
 };
})();
