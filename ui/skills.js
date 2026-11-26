'use strict';
(() => {
 const el=id=>document.getElementById(id);
 const post=(type,detail={})=>window.chrome.webview.postMessage({type,...detail});
 let state=null,selectedId=null,editId=null,page=0,pending=null,lastProject,signatures={};
 const pageSize=5;
 const node=(tag,text,className)=>{const n=document.createElement(tag);if(text!=null)n.textContent=text;if(className)n.className=className;return n;};
 const current=()=>state?.projects.find(p=>p.id===state.selectedProjectId);
 const selected=()=>state?.catalog.find(s=>s.id===selectedId);
 const appName=target=>target==='gpt'?'GPT':'Claude';
 const description=skill=>window.skillDescriptionsKo.describe(skill.name,skill.description);
 function projectPayload(extra={}){return {projectId:state?.selectedProjectId,...extra};}
 function request(type,detail={}){if(pending)return;pending=type;controls();post(type,detail);}
 function controls(){
  const blocked=!state||state.readOnly||!!pending||!!state.busy;
  el('project-add').disabled=blocked;el('project-select').disabled=blocked||!state?.projects.length;
  el('skills-refresh').disabled=!!pending||!!state?.loading;
  el('skill-save').disabled=blocked||!current()||(!selected()&&!editId)||state.loading||!el('skill-label').value.trim();
  el('skill-scope').disabled=blocked||!editId;
  el('skill-label').disabled=blocked||el('skill-scope').value==='location';
  el('skill-reference').disabled=blocked||el('skill-scope').value==='location';
  el('skill-save').textContent=pending==='skill-save'?'저장 중…':editId?'이 버튼 수정 저장':'이 프로젝트에 버튼 추가';
  el('skill-new').hidden=!editId;
  const choice=el('start-apps');if(choice)choice.disabled=blocked;
  for(const button of document.querySelectorAll('[data-skill-run]'))button.disabled=blocked||!!state?.runningId||!current();
  for(const button of document.querySelectorAll('[data-skill-remove],[data-skill-edit]'))button.disabled=blocked;
 }
 function resetEditor(){editId=null;selectedId=null;el('skill-label').value='';el('skill-instruction').value='';el('skill-reference').value='';el('skill-scope').value='common';el('skill-target').value=state?.startApps==='claude'?'claude':'gpt';detail();}
 function detail(){
  const skill=selected();
  el('skill-source').textContent=skill?.source||'스킬을 선택하세요';
  el('skill-name').textContent=skill?.name||'버튼 하나로 AI에 전달';
  el('skill-description').textContent=skill?description(skill):'설치된 스킬을 고르면 이 프로젝트의 버튼으로 저장할 수 있어요.';
  controls();
 }
 function choose(skill){selectedId=skill.id;if(!editId)el('skill-label').value=skill.name;catalog();detail();}
 function catalog(){
  const query=el('skill-search').value.trim().toLocaleLowerCase();
  const items=(state?.catalog||[]).filter(s=>(s.name+' '+description(s)+' '+s.description+' '+s.source).toLocaleLowerCase().includes(query));
  const pages=Math.max(1,Math.ceil(items.length/pageSize));page=Math.min(page,pages-1);
  el('skills-count').textContent=state?.catalog.length||0;
  el('skill-page').textContent=`${page+1} / ${pages}`;
  el('skill-prev').disabled=page===0;el('skill-next').disabled=page>=pages-1;
  const list=el('skill-catalog');list.replaceChildren();
  if(state?.loading)list.append(node('p','설치된 스킬을 읽고 있습니다…','empty'));
  else if(!items.length)list.append(node('p',query?'검색 결과가 없습니다.':'설치된 스킬이 없습니다. 사용자 또는 프로젝트의 skills 폴더를 확인하세요.','empty'));
  for(const skill of items.slice(page*pageSize,(page+1)*pageSize)){
   const button=node('button',null,'skill-option');button.type='button';button.setAttribute('aria-pressed',String(skill.id===selectedId));
   button.append(node('b',skill.name),node('small',skill.source),node('span',description(skill)));button.onclick=()=>choose(skill);list.append(button);
  }
  el('skills-warning').textContent=state?.warnings.length?state.warnings.join(' '):'내 Codex·공용·Claude 스킬과 프로젝트의 skills 폴더를 확인합니다. 앱마다 사용 가능한 도구가 달라 실행 결과가 다를 수 있습니다.';
 }
 function edit(button){
  editId=button.id;selectedId=button.skillId;el('skill-label').value=button.label;el('skill-target').value=button.commonTarget||button.target;el('skill-instruction').value=button.commonInstruction||'';el('skill-reference').value=button.reference||'';el('skill-scope').value='common';
  const index=state.catalog.findIndex(s=>s.id===selectedId);page=index<0?0:Math.floor(index/pageSize);el('skill-search').value='';catalog();detail();
  if(!button.skillId)el('skills-dialog-status').textContent='연결된 스킬이 설치 목록에 없습니다. 목록에서 스킬을 다시 선택해 저장하세요.';
  el('skill-label').focus();
 }
 function run(button){
  post('skill-run',projectPayload({buttonId:button.id}));
  // The native side also serializes handoffs. Disable immediately for double clicks.
  for(const b of document.querySelectorAll('[data-skill-run]'))b.disabled=true;
 }
 function saved(){
  const grid=el('project-skill-keys'),list=el('skill-saved-list');grid.replaceChildren();list.replaceChildren();
  const buttons=state?.buttons||[];
  el('skill-empty').hidden=buttons.length>0;
  el('skill-empty').textContent=current()?'스킬 버튼에서 설치된 스킬을 골라 등록하세요.':'＋에서 GitHub 또는 로컬 프로젝트를 추가한 뒤 스킬 버튼을 등록하세요.';
  if(!buttons.length)list.append(node('p','아직 등록한 스킬 버튼이 없습니다.','empty'));
  for(const button of buttons){
   const key=node('button',null,'key skill-key');key.type='button';key.dataset.skillRun=button.id;
   const prepared=current()?.location==='cloud'||current()?.taskKind==='conversation';
   const icon=node('span','✦','skill-key-icon');icon.setAttribute('aria-hidden','true');key.append(icon,node('b',button.label),node('small',`${appName(button.target)} · ${prepared?'요청 준비':button.skillId?'스킬 실행':'연결 확인'}${button.hasOverride?' · 위치 예외':''}`));
   key.setAttribute('aria-busy',String(state.runningId===button.id));key.title=button.skillName+' · '+appName(button.target);key.onclick=()=>run(button);grid.append(key);
   const row=node('div',null,'skill-saved-row'),info=node('div');info.append(node('b',button.label),node('small',`${button.skillName} · ${appName(button.target)}${button.skillId?'':' · 스킬 재연결 필요'}`));
   const actions=node('div',null,'skill-saved-actions');
   const execute=node('button','실행');execute.dataset.skillRun=button.id;execute.onclick=()=>run(button);
   const modify=node('button','수정');modify.dataset.skillEdit=button.id;modify.onclick=()=>edit(button);modify.disabled=state.readOnly;
   const remove=node('button','삭제');remove.dataset.skillRemove=button.id;remove.onclick=()=>request('skill-remove',projectPayload({buttonId:button.id}));remove.disabled=state.readOnly||!!pending;
   if(button.hasOverride){const reset=node('button','위치 예외 해제');reset.dataset.skillEdit=button.id;reset.onclick=()=>request('skill-button-options',projectPayload({buttonId:button.id,scope:'reset',target:button.commonTarget||button.target}));actions.append(reset);}
   actions.append(execute,modify,remove);row.append(info,actions);list.append(row);
  }
  controls();
 }
 function render(data){
  if(!data)return;state=data;
  const projectChanged=lastProject!==data.selectedProjectId;lastProject=data.selectedProjectId;
  const projectSignature=JSON.stringify([data.projects,data.selectedProjectId]);
  if(projectSignature!==signatures.projects){
   signatures.projects=projectSignature;const select=el('project-select');select.replaceChildren();
   if(!data.projects.length){const option=node('option','프로젝트 추가');option.value='';select.append(option);}
   for(const project of data.projects){const option=node('option',(project.repository?'☁ ':'')+project.name);option.value=project.id;select.append(option);}select.value=data.selectedProjectId||'';
  }
  const start=el('start-apps');if(start)start.value=data.startApps;
  const project=current();el('skills-project-hint').textContent=project?`${project.name} · ${project.location==='cloud'?'클라우드 · '+(project.repository?.fullName||''):project.folder||'로컬 폴더 연결 필요'} · ${project.shared?'내 PC 공유':'이 PC만'}`:'＋에서 프로젝트를 먼저 추가하세요. 스킬 버튼은 프로젝트마다 저장됩니다.';
  el('skill-status').textContent=data.status||'';el('skills-dialog-status').textContent=data.status||'';
  const catalogSignature=JSON.stringify([data.catalog,data.loading,data.warnings]);
  if(catalogSignature!==signatures.catalog||projectChanged){signatures.catalog=catalogSignature;catalog();}
  if(projectChanged){page=0;el('skill-search').value='';resetEditor();catalog();}
  const buttonSignature=JSON.stringify([data.buttons,data.runningId,data.selectedProjectId,data.readOnly,project?.location,project?.taskKind]);
  if(buttonSignature!==signatures.buttons){signatures.buttons=buttonSignature;saved();}
  if(!data.loading&&selectedId&&!selected())detail();
  controls();
 }
 function close(){el('skills-dialog').close();}
 function setup(){
  el('project-add').onclick=()=>request('skill-project-add');
  el('project-select').onchange=event=>request('skill-project-select',{projectId:event.target.value});
  el('manage-skills').onclick=open;
  el('skills-close').onclick=close;
  el('skills-dialog').addEventListener('close',()=>post('skills-close'));
  el('skills-refresh').onclick=()=>request('skills-refresh');
  el('skill-search').oninput=()=>{page=0;catalog();};
  el('skill-prev').onclick=()=>{page--;catalog();};el('skill-next').onclick=()=>{page++;catalog();};
  el('skill-label').oninput=controls;
  el('skill-scope').onchange=()=>{const button=state?.buttons.find(b=>b.id===editId);if(button){const common=el('skill-scope').value==='common';el('skill-target').value=common?(button.commonTarget||button.target):button.target;el('skill-instruction').value=(common?button.commonInstruction:button.instruction)||'';}controls();};
  el('skill-label').onkeydown=event=>{if(event.key==='Enter'&&!event.isComposing&&!el('skill-save').disabled){event.preventDefault();el('skill-save').click();}};
  el('skill-save').onclick=()=>{
   if(el('skill-save').disabled)return;
   const detail=projectPayload({buttonId:editId,skillId:selectedId,label:el('skill-label').value.trim(),target:el('skill-target').value,instruction:el('skill-instruction').value.trim(),reference:el('skill-reference').value.trim(),scope:el('skill-scope').value});
   request(editId&&(detail.scope==='location'||!selected())?'skill-button-options':'skill-save',detail);
  };
  el('skill-new').onclick=()=>{editId=null;el('skill-scope').value='common';el('skill-instruction').value='';controls();};
  controls();
 }
 function open(){const dialog=el('skills-dialog');if(!dialog.open)dialog.showModal();request('skills-open');}
 function result(action){if(pending===action)pending=null;controls();}
 window.projectSkills={setup,open,render,result};
})();

