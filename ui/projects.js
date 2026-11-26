'use strict';
(() => {
 const el=id=>document.getElementById(id),post=(type,detail={})=>window.chrome.webview.postMessage({type,...detail});
 const node=(tag,text,cls)=>{const n=document.createElement(tag);if(text!=null)n.textContent=text;if(cls)n.className=cls;return n;};
 let state=null,pending=null,tab='github',listSignature='',lastProject=null;
 const selections=new Map(),current=()=>state?.projects.find(p=>p.id===state.selectedProjectId);
 function request(type,detail={}){if(pending||state?.busy)return;pending=type;controls();post(type,detail);}
 function payload(extra={}){return {projectId:state?.selectedProjectId,...extra};}
 function controls(){
  const blocked=!state||state.readOnly||state.busy||!!pending,gh=state?.github||{},p=current();
  for(const id of ['github-login','local-project-add','project-github-tab','project-local-tab'])el(id).disabled=blocked;
  for(const id of ['github-refresh','github-logout','github-share-connect'])el(id).disabled=blocked||!gh.connected;
  el('github-login').disabled=blocked||!gh.available;
  el('github-sync').disabled=blocked||!gh.shareReady;
  el('github-register').disabled=blocked||!selections.size||!gh.connected;
  el('project-settings').disabled=blocked||!p;
  for(const id of ['project-location','project-task-kind','project-config-save','project-open-app'])el(id).disabled=blocked||!p;
  if(p)el('project-open-app').disabled=blocked||el('project-location').value!==p.location||el('project-task-kind').value!==p.taskKind||el('project-shared').checked!==p.shared;
  el('project-shared').disabled=blocked||!p?.repository||(!gh.shareReady&&!p?.shared);
  el('project-bind').disabled=blocked||!p?.repository||!gh.connected;
  for(const id of ['project-conflict-remote','project-conflict-local'])el(id).disabled=blocked||!gh.shareReady;
  el('github-selected').textContent=`선택 ${selections.size}개 · 공유 ${[...selections.values()].filter(Boolean).length}개`;
  for(const input of el('github-repositories').querySelectorAll('input'))input.disabled=blocked||input.dataset.registered==='true'||(input.dataset.share==='true'&&(!gh.shareReady||!selections.has(input.dataset.repo)));
 }
 function repositoryList(){
  const gh=state?.github||{},query=el('github-search').value.trim().toLocaleLowerCase(),repos=gh.repositories||[],visible=repos.filter(r=>r.fullName.toLocaleLowerCase().includes(query));
  el('github-repo-count').textContent=`조회 ${repos.length}개 · 검색 ${visible.length}개`;
  const list=el('github-repositories');list.replaceChildren();
  if(!repos.length)list.append(node('p',gh.connected?'저장소 목록을 새로고침하세요. 권한이 허용된 저장소가 여기에 표시됩니다.':'GitHub 로그인 후 저장소를 선택할 수 있습니다.','empty'));
  else if(!visible.length)list.append(node('p','검색 결과가 없습니다.','empty'));
  for(const repo of visible){
   const row=node('div',null,'github-repo-row'),label=node('label',null,'github-repo-label'),check=node('input');check.type='checkbox';check.checked=selections.has(repo.id);check.dataset.repo=repo.id;check.dataset.registered=String(repo.registered);check.setAttribute('aria-label',repo.fullName+' 등록');
   check.onchange=()=>{if(check.checked)selections.set(repo.id,false);else selections.delete(repo.id);repositoryList();};
   const info=node('span');info.append(node('b',repo.fullName),node('small',`${repo.isPrivate?'Private · 비공개':'Public · 공개'}${repo.registered?' · 등록됨':''}`));label.append(check,info);row.append(label);
   const shareLabel=node('label',null,'github-repo-share'),share=node('input');share.type='checkbox';share.checked=selections.get(repo.id)===true;share.dataset.repo=repo.id;share.dataset.share='true';share.dataset.registered=String(repo.registered);share.setAttribute('aria-label',repo.fullName+' 내 PC 공유');share.onchange=()=>{selections.set(repo.id,share.checked);controls();};shareLabel.append(share,document.createTextNode('내 PC 공유'));row.append(shareLabel);list.append(row);
  }
  controls();
 }
 function settings(){
  const p=current();if(!p)return;
  el('project-settings-title').textContent=p.name+' · 설정';el('project-settings-source').textContent=p.repository?`☁ ${p.repository.fullName} · ${p.repository.isPrivate?'Private':'Public'}`:'이 PC의 로컬 프로젝트';
  el('project-location').value=p.location;el('project-location').querySelector('[value="cloud"]').disabled=!p.repository;el('project-location').querySelector('[value="local"]').disabled=!p.folder;
  el('project-local-path').textContent=p.folder||'이 PC에는 연결된 폴더가 없습니다.';el('project-bind').hidden=!p.repository;
  el('project-task-kind').value=p.taskKind;el('project-shared').checked=p.shared;el('project-conflict').hidden=!p.conflict;controls();
 }
 function render(data){
  if(!data)return;state=data;const gh=data.github||{},p=current();
  el('project-context').textContent=p?`${p.repository?'☁ GitHub · ':''}${p.location==='cloud'?'클라우드':'이 PC'} · ${p.taskKind==='conversation'?'대화·검토':'코딩·PR'} · ${p.shared?'내 PC 공유':'이 PC만'}${p.pending?' · 공유 대기':''}${p.conflict?' · 충돌 확인':''}`:'＋에서 GitHub 또는 로컬 프로젝트를 추가하세요.';
  el('github-account').textContent=gh.connected?`${gh.login} · GitHub 연결됨`:gh.available?'GitHub 연결 전':'GitHub 로그인 구성 요소를 설치해야 합니다.';
  el('github-auth-note').hidden=!gh.authenticating;el('github-auth-code').textContent=gh.authCode||'인증 코드를 기다리고 있습니다…';
  el('github-share-hint').textContent=gh.shareReady?`${gh.shareRepository} · 비공개 설정 공유 연결됨. ${data.syncStatus||''}`:'연결·만들기를 누르면 내 계정의 mini-workstation-hub-settings 비공개 저장소를 사용합니다. 체크한 프로젝트의 목록·공통 스킬 버튼만 보관하며 폴더 경로·로그인 정보·위치 예외는 올리지 않습니다.';
  el('github-share-connect').textContent=gh.shareReady?'공유 저장소 다시 연결':'비공개 공유 저장소 연결·만들기';
  for(const id of ['project-dialog-status','project-settings-status'])el(id).textContent=data.status||'';
  const signature=JSON.stringify([gh.accountId,gh.repositories]);
  if(signature!==listSignature){if(listSignature&&lastProject?.accountId!==gh.accountId)selections.clear();listSignature=signature;for(const repo of gh.repositories||[])if(repo.registered)selections.delete(repo.id);repositoryList();}
  const nextSignature=JSON.stringify([p?.id,p?.folder,p?.location,p?.taskKind,p?.shared,p?.conflict]);
  if(nextSignature!==lastProject?.signature){settings();lastProject={signature:nextSignature,accountId:gh.accountId};}
  controls();
 }
 function open(){tab='github';pane();el('projects-dialog').showModal();post('projects-open');}
 function pane(){el('github-project-pane').hidden=tab!=='github';el('local-project-pane').hidden=tab!=='local';el('project-github-tab').setAttribute('aria-pressed',String(tab==='github'));el('project-local-tab').setAttribute('aria-pressed',String(tab==='local'));}
 function setup(){
  el('project-add').onclick=open;el('projects-close').onclick=()=>el('projects-dialog').close();
  el('projects-dialog').addEventListener('close',()=>{if(state?.github?.authenticating)post('github-cancel');post('skills-close');});
  el('project-github-tab').onclick=()=>{tab='github';pane();};el('project-local-tab').onclick=()=>{tab='local';pane();};
  for(const [id,type] of [['github-login','github-login'],['github-refresh','github-refresh'],['github-logout','github-logout'],['github-share-connect','github-share-connect'],['github-sync','github-sync'],['local-project-add','skill-project-add']])el(id).onclick=()=>request(type);
  el('github-cancel').onclick=()=>post('github-cancel');el('github-search').oninput=repositoryList;
  el('github-device-page').onclick=()=>post('github-device-page');
  el('github-register').onclick=()=>request('github-register',{repositoryIds:[...selections.keys()],shareIds:[...selections].filter(([,share])=>share).map(([id])=>id)});
  el('project-settings').onclick=()=>{settings();el('project-settings-dialog').showModal();post('projects-open');};
  el('project-settings-close').onclick=()=>el('project-settings-dialog').close();el('project-settings-dialog').addEventListener('close',()=>post('skills-close'));
  el('project-bind').onclick=()=>request('project-bind',payload());
  el('project-config-save').onclick=()=>request('project-config',payload({location:el('project-location').value,taskKind:el('project-task-kind').value,shared:el('project-shared').checked}));
  for(const id of ['project-location','project-task-kind','project-shared'])el(id).onchange=controls;
  el('project-open-app').onclick=()=>request('project-open-app',payload());
  el('project-conflict-remote').onclick=()=>request('project-conflict',payload({choice:'remote'}));el('project-conflict-local').onclick=()=>request('project-conflict',payload({choice:'local'}));
  controls();
 }
 function result(action){if(pending===action){pending=null;controls();}}
 window.projectConnections={setup,render,result,open};
})();
