// Browser-only fixture. Not shipped in the app; every native action stays inside this in-memory fixture.
'use strict';
(() => {
 const listeners=[],repos=[{id:'1001',name:'work-demo',fullName:'demo/work-demo',isPrivate:true,registered:false},{id:'1002',name:'public-demo',fullName:'demo/public-demo',isPrivate:false,registered:false},{id:'1003',name:'team-demo',fullName:'demo/team-demo',isPrivate:true,registered:false}];
 const skills={readOnly:false,loading:false,busy:false,status:'실제 UI 검증용 · 로그인·공유·AI 실행은 모의 응답입니다.',runningId:null,selectedProjectId:null,startApps:'both',syncStatus:'',projects:[],buttons:[],catalog:[{id:'fix',name:'fix-guide',description:'쉬운 한국어 수정 보고서를 만듭니다.',source:'검증용 스킬',path:'test-fixture/SKILL.md'}],warnings:[],github:{available:true,connected:false,accountId:0,login:null,authCode:null,authenticating:false,shareReady:false,shareRepository:null,repositories:[]}};
 const state={kind:'state',version:'0.6.0',mode:'대기',home:{lamp:true,bar:true,ac:false},active:[],enabled:{home:true},macro:{muted:false,recording:null,busy:[]},skills};
 const current=()=>skills.projects.find(p=>p.id===skills.selectedProjectId);
 function emit(){skills.buttons=current()?.buttons||[];for(const f of listeners)f({data:state});}
 function result(type){for(const f of listeners)f({data:{kind:'skills-result',action:type}});}
 function handle(m){
  const gh=skills.github;
  if(m.type==='ready'){emit();return;}
  if(m.type==='github-login'){Object.assign(gh,{connected:true,login:'demo',accountId:10,repositories:repos});skills.status='검증용 계정 연결 완료';}
  if(m.type==='github-share-connect'){gh.shareReady=true;gh.shareRepository='demo/mini-workstation-hub-settings';skills.status='검증용 비공개 공유 저장소 연결됨';}
  if(m.type==='github-register'){
   for(const id of m.repositoryIds){const r=repos.find(r=>r.id===id);if(r&&!r.registered){r.registered=true;skills.projects.push({id:'github-'+id,name:r.name,folder:null,repository:{id:r.id,fullName:r.fullName,isPrivate:r.isPrivate,url:'https://github.com/'+r.fullName},location:'cloud',taskKind:'code',shared:m.shareIds.includes(id),pending:false,conflict:false,buttons:[]});skills.selectedProjectId='github-'+id;}}
   skills.status='프로젝트 '+m.repositoryIds.length+'개를 등록했습니다.';
  }
  if(m.type==='skill-project-select')skills.selectedProjectId=m.projectId;
  if(m.type==='project-bind'){current().folder='C:\\Projects\\'+current().name;skills.status='검증용 같은 저장소 폴더 연결 완료';}
  if(m.type==='project-config'){Object.assign(current(),{location:m.location,taskKind:m.taskKind,shared:m.shared});skills.status='프로젝트 설정을 저장했습니다.';}
  if(m.type==='skill-save'){current().buttons.push({id:'button-'+Date.now(),label:m.label,target:m.target,commonTarget:m.target,instruction:m.instruction,commonInstruction:m.instruction,skillName:'fix-guide',skillId:'fix',reference:m.reference});skills.status='프로젝트에 스킬 버튼을 저장했습니다.';}
  if(m.type==='skill-start-apps')skills.startApps=m.value;
  if(m.type==='github-logout'){Object.assign(gh,{connected:false,accountId:0,login:null,shareReady:false,repositories:[]});}
  if(m.type==='macro'||m.type==='skill-run'||m.type==='project-open-app')skills.status='검증 모드 · 실제 PC 명령과 AI 실행은 차단했습니다.';
  emit();result(m.type);
 }
 window.chrome={webview:{postMessage:m=>queueMicrotask(()=>handle(m)),addEventListener:(type,f)=>{if(type==='message')listeners.push(f);}}};
 document.addEventListener('DOMContentLoaded',()=>{const b=document.createElement('div');b.textContent='화면 검증 모드 · 모든 계정·저장소는 샘플 · 실제 로그인·공유·PC 명령은 실행하지 않음';b.style.cssText='padding:12px;background:#fff0b8;color:#20382b;font:15px sans-serif';document.body.prepend(b);});
})();
