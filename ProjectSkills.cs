using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MinaDesk {
    internal sealed class ProjectSkills : IDisposable {
        readonly SkillWorkspaces store;
        readonly GitHubProjects github;
        readonly ProjectSharing sharing;
        readonly Action changed;
        readonly Action<string,string> log;
        readonly CancellationTokenSource stop=new CancellationTokenSource();
        CancellationTokenSource loginStop;
        SkillCatalogResult catalog=new SkillCatalogResult();
        string status,runningId,authCode,syncStatus;
        int generation;
        bool disposed,loading,busy,restored;
        internal string Folder { get { return store.Current==null || store.Current.Location!="local"?null:store.Current.Folder; } }
        internal string ToolFolder { get { return store.Current==null?null:store.Current.Folder; } }
        internal bool HasProject { get { return store.Current!=null; } }
        internal SkillProject Current { get { return store.Current; } }
        internal IEnumerable<SkillProject> Projects { get { return store.Value.Projects; } }
        internal IEnumerable<InstalledSkill> Installed { get { return catalog.Items; } }
        internal GitHubProjects GitHub { get { return github; } }
        internal bool Busy { get { return busy; } }
        internal bool ReadOnly { get { return store.ReadOnly; } }
        internal bool ShareReady { get { return github.AccountId>0 && store.Value.SyncAccountId==github.AccountId && store.Value.SyncRepositoryId>0; } }
        internal string Status { get { return status; } }
        internal ProjectSkills(string data,Action onChanged,Action<string,string> onLog) {
            store=new SkillWorkspaces(data);github=new GitHubProjects(data);sharing=new ProjectSharing(github,store);changed=onChanged;log=onLog;status=store.LoadNotice;
        }
        internal object Snapshot() {
            var current=store.Current;
            return new {readOnly=store.ReadOnly,loading,busy,status,runningId,selectedProjectId=store.Value.SelectedProjectId,startApps=store.Value.StartApps,syncStatus,
                github=new{available=github.Available,connected=github.AccountId>0,accountId=github.AccountId,login=github.LoginName,authCode,authenticating=loginStop!=null,shareReady=github.AccountId>0 && store.Value.SyncAccountId==github.AccountId && store.Value.SyncRepositoryId>0,shareRepository=github.LoginName==null?null:github.LoginName+"/"+ProjectSharing.RepositoryName,
                    repositories=github.Repositories.Select(r=>new{id=r.Id.ToString(),name=r.Name,fullName=r.FullName,isPrivate=r.Private,registered=store.Value.Projects.Any(p=>p.Repository!=null && p.Repository.Id==r.Id)}).ToArray()},
                projects=store.Value.Projects.Select(p=>new{id=p.Id,name=p.Name,folder=p.Folder,location=p.Location,taskKind=p.TaskKind,shared=p.Shared,conflict=p.SyncConflict,pending=p.SharedDirty,repository=p.Repository==null?null:new{id=p.Repository.Id.ToString(),fullName=p.Repository.FullName,url=p.Repository.Url,isPrivate=p.Repository.Private}}).ToArray(),
                buttons=(current==null?new List<SkillButton>():current.Buttons).Select(b=>{
                    var effective=SkillWorkspaces.Effective(current,b);var installed=catalog.Items.FirstOrDefault(s=>String.Equals(s.Path,b.SkillPath,StringComparison.OrdinalIgnoreCase))??catalog.Items.FirstOrDefault(s=>s.Name==b.SkillName);
                    return new{id=b.Id,label=b.Label,skillName=b.SkillName,target=effective.Target,instruction=effective.Instruction,commonTarget=b.Target,commonInstruction=b.Instruction,reference=b.Reference,hasOverride=current.Location=="cloud"?b.CloudOverride!=null:b.LocalOverride!=null,skillId=installed==null?null:installed.Id};
                }).ToArray(),
                catalog=catalog.Items.Select(s=>new{id=s.Id,name=s.Name,description=s.Description,source=s.Source,path=s.Path}).ToArray(),warnings=catalog.Warnings.Distinct().ToArray()};
        }
        internal async Task Refresh() {
            int request=++generation;string folder=store.Current==null?null:store.Current.Folder;loading=true;catalog=new SkillCatalogResult();Notify();
            try {
                var found=await Task.Run(()=>SkillCatalog.Scan(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),folder));
                if(disposed || request!=generation)return;catalog=found;
                if(!restored && !busy) {
                    restored=true;
                    if(github.HasSession && !store.ReadOnly) {
                        busy=true;Notify();
                        try {await github.User(stop.Token);await github.List(stop.Token);if(store.Value.SyncAccountId==github.AccountId)await Synchronize();}
                        catch(Exception ex){status="GitHub 확인 필요 · "+ex.Message;}
                        finally{busy=false;}
                    }
                }
            }catch(Exception) { if(!disposed && request==generation)status="스킬 목록을 읽지 못했습니다. 새로고침으로 다시 확인하세요."; }
            finally { if(!disposed && request==generation){loading=false;Notify();} }
        }
        void Notify() { if(!disposed)changed(); }
        static string Text(Dictionary<string,object> message,string key) { object value;return message.TryGetValue(key,out value)?value as string:null; }
        static bool Flag(Dictionary<string,object> message,string key) {object v;return message.TryGetValue(key,out v) && v is bool && (bool)v;}
        static HashSet<long> Ids(Dictionary<string,object> message,string key) {
            object value;if(!message.TryGetValue(key,out value) || value is string || !(value is IEnumerable))return new HashSet<long>();
            var ids=new HashSet<long>();foreach(var item in (IEnumerable)value){long id;if(!Int64.TryParse(item as string,out id) || id<=0 || ids.Count>=200)throw new ArgumentException("한 번에 저장소 200개까지 선택하세요.");ids.Add(id);}return ids;
        }
        async Task Synchronize() {
            if(github.AccountId<=0 || store.Value.SyncAccountId!=github.AccountId || store.Value.SyncRepositoryId<=0)return;
            syncStatus="공유 설정을 맞추는 중…";Notify();int conflicts=await sharing.Sync(github.AccountId,stop.Token);
            syncStatus=conflicts>0?"공유 충돌 "+conflicts+"개 · 프로젝트 설정에서 유지할 내용을 선택하세요.":"공유 설정 확인 완료 · "+DateTime.Now.ToString("HH:mm");Notify();
        }
        internal async Task SyncBackground() {
            if(disposed || busy || loading || store.ReadOnly || !ShareReady)return;busy=true;
            try{await Synchronize();}catch(Exception ex){syncStatus="공유 연결 확인 필요 · "+ex.Message;}finally{busy=false;Notify();}
        }
        internal async Task Handle(Form owner,string type,Dictionary<string,object> message) {
            if(type=="github-cancel"){if(loginStop!=null)loginStop.Cancel();return;}
            if(type=="github-device-page"){try{if(loginStop!=null)GitHubProjects.OpenDevicePage();}catch(Exception){status="기본 브라우저에서 https://github.com/login/device 를 직접 열어 주세요.";Notify();}return;}
            if(busy){status="진행 중인 작업이 끝난 뒤 다시 눌러 주세요.";Notify();return;}
            busy=true;Notify();
            try {
                if(store.ReadOnly)throw new InvalidOperationException(store.LoadNotice);
                if(type=="skills-open" || type=="skills-refresh") { await Refresh();return; }
                if(type=="github-login") {
                    authCode=null;status="기본 브라우저에서 GitHub 로그인을 완료하세요.";
                    using(loginStop=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)) {
                        await github.Login(code=>{try{if(!owner.IsDisposed)owner.BeginInvoke(new Action(()=>{authCode=code;Notify();}));}catch(InvalidOperationException){}},loginStop.Token);
                    }
                    loginStop=null;authCode=null;await github.List(stop.Token);status="GitHub 연결 완료 · 저장소를 체크해 프로젝트로 등록하세요.";
                    if(store.Value.SyncAccountId==github.AccountId)await Synchronize();return;
                }
                if(type=="github-refresh") {await github.User(stop.Token);await github.List(stop.Token);status="허용된 Public·Private 저장소 목록을 모두 새로고침했습니다.";return;}
                if(type=="github-logout") {await github.Logout(stop.Token);authCode=null;status="이 PC의 GitHub 연결을 해제했습니다. 등록한 프로젝트는 보존됩니다.";return;}
                if(type=="github-share-connect") {
                    if(github.AccountId<=0)throw new InvalidOperationException("GitHub에 먼저 로그인하세요.");
                    await sharing.Initialize(github.AccountId,github.LoginName,true,stop.Token);await Synchronize();status="전용 비공개 공유 저장소를 연결했습니다. 공유할 프로젝트만 선택하세요.";await Refresh();return;
                }
                if(type=="github-sync") {if(github.AccountId<=0)throw new InvalidOperationException("GitHub에 먼저 로그인하세요.");await Synchronize();await Refresh();return;}
                if(type=="github-register") {
                    var ids=Ids(message,"repositoryIds");var shared=Ids(message,"shareIds");
                    if(github.AccountId<=0 || ids.Count==0 || !shared.IsSubsetOf(ids))throw new ArgumentException("등록할 저장소를 선택하세요.");
                    var repos=github.Repositories.Where(r=>ids.Contains(r.Id)).ToArray();if(repos.Length!=ids.Count)throw new InvalidOperationException("현재 계정의 저장소 목록을 새로고침한 뒤 다시 선택하세요.");
                    if(shared.Count>0 && store.Value.SyncAccountId!=github.AccountId)throw new InvalidOperationException("공유 저장소 연결 후 프로젝트 공유를 선택하세요.");
                    store.Register(repos,shared,github.AccountId);status="프로젝트 "+repos.Length+"개를 등록했습니다. 구름 표시는 GitHub 연결 출처입니다.";await Synchronize();await Refresh();return;
                }
                if(type=="skill-project-add") {
                    using(var picker=new FolderBrowserDialog{Description="이 PC에서 사용할 프로젝트 폴더를 선택하세요.",SelectedPath=Folder??Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),ShowNewFolderButton=false}) {
                        if(picker.ShowDialog(owner)!=DialogResult.OK)return;store.AddProject(picker.SelectedPath);status="로컬 프로젝트를 저장했습니다.";
                    }
                    await Refresh();return;
                }
                if(type=="skill-project-select") {store.Select(Text(message,"projectId"));status=null;await Refresh();return;}
                if(type=="skill-start-apps") {
                    string value=Text(message,"value");if(!new[]{"gpt","claude","both"}.Contains(value))throw new ArgumentException("열 앱을 선택하세요.");
                    store.Change(next=>next.StartApps=value);status="업무 시작 앱 선택을 저장했습니다.";return;
                }
                string projectId=Text(message,"projectId");
                if(store.Current==null || store.Current.Id!=projectId)throw new ArgumentException("현재 프로젝트를 다시 선택하세요.");
                if(type=="project-config") {
                    if(Flag(message,"shared")!=store.Current.Shared && Flag(message,"shared") && (github.AccountId<=0 || store.Value.SyncAccountId!=github.AccountId))throw new InvalidOperationException("전용 공유 저장소를 먼저 연결하세요.");
                    store.Configure(projectId,Text(message,"location"),Text(message,"taskKind"),Flag(message,"shared"),github.AccountId);
                    status="프로젝트별 실행 위치·작업 종류·공유 선택을 저장했습니다.";await Synchronize();await Refresh();return;
                }
                if(type=="project-bind") {
                    if(store.Current.Repository==null)throw new InvalidOperationException("GitHub 프로젝트를 선택하세요.");
                    using(var picker=new FolderBrowserDialog{Description="같은 GitHub 저장소를 복제한 이 PC의 폴더를 선택하세요.",ShowNewFolderButton=false}) {
                        if(picker.ShowDialog(owner)!=DialogResult.OK)return;
                        string origin=await GitHubProjects.LocalOrigin(picker.SelectedPath,stop.Token);
                        var response=await github.Send("repos/"+origin,"GET",null,stop.Token);response.RequireSuccess();var repo=GitHubRepository.From(LocalData.Decode<Dictionary<string,object>>(response.Json));
                        store.BindLocal(projectId,picker.SelectedPath,repo.Id);status="같은 프로젝트에 로컬 폴더를 연결했습니다. 실행 위치에서 ‘이 PC’를 선택할 수 있습니다.";
                    }
                    await Refresh();return;
                }
                if(type=="project-conflict") {await sharing.Resolve(projectId,Text(message,"choice")=="local",github.AccountId,stop.Token);await Synchronize();status="선택한 공유 설정으로 충돌을 해결했습니다.";await Refresh();return;}
                if(type=="project-open-app") {ProjectExecution.Show(owner,store.Current,null,store.Value.StartApps);return;}
                if(type=="skill-button-options") {
                    store.ButtonOptions(projectId,Text(message,"buttonId"),Text(message,"scope"),Text(message,"target"),Text(message,"instruction"),Text(message,"reference"),Text(message,"label"));status="버튼의 공통 설정·위치 예외를 저장했습니다.";await Synchronize();return;
                }
                if(type=="skill-save") {
                    if(loading)throw new InvalidOperationException("스킬 목록 확인이 끝난 뒤 저장하세요.");
                    if(!SkillWorkspaces.ValidReference(Text(message,"reference")))throw new ArgumentException("스킬 문서는 저장소 안의 상대 경로를 입력하세요.");
                    var skill=catalog.Items.FirstOrDefault(s=>s.Id==Text(message,"skillId"));
                    store.SaveButton(projectId,Text(message,"buttonId"),skill,Text(message,"label"),Text(message,"target"),Text(message,"instruction"));
                    string buttonId=Text(message,"buttonId")??store.Current.Buttons.Last().Id;
                    store.ButtonOptions(projectId,buttonId,"common",Text(message,"target"),Text(message,"instruction"),Text(message,"reference"));
                    status="프로젝트에 스킬 버튼을 저장했습니다.";await Synchronize();return;
                }
                if(type=="skill-remove") {store.RemoveButton(projectId,Text(message,"buttonId"));status="스킬 버튼을 삭제했습니다.";await Synchronize();return;}
                if(type=="skill-run") {
                    if(runningId!=null)throw new InvalidOperationException("다른 스킬 요청 전달이 끝난 뒤 다시 눌러 주세요.");
                    var project=LocalData.Decode<SkillProject>(LocalData.Encode(store.Current));
                    var saved=project.Buttons.FirstOrDefault(b=>b.Id==Text(message,"buttonId"));if(saved==null)throw new ArgumentException("등록한 스킬 버튼을 선택하세요.");
                    var button=SkillWorkspaces.Effective(project,saved);
                    if(project.Location=="cloud" || project.TaskKind=="conversation") {ProjectExecution.Show(owner,project,button,button.Target);status="요청 준비 화면을 열었습니다. AI 앱에서 실행 위치·저장소를 확인하고 전송하세요.";return;}
                    if(!Directory.Exists(project.Folder))throw new DirectoryNotFoundException("프로젝트의 로컬 폴더를 먼저 연결하세요.");
                    runningId=button.Id;status="설치된 스킬을 확인하고 있습니다…";Notify();
                    try {
                        var installed=await Task.Run(()=>SkillCatalog.Scan(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),project.Folder));
                        if(disposed)return;
                        var selected=installed.Items.FirstOrDefault(s=>String.Equals(s.Path,button.SkillPath,StringComparison.OrdinalIgnoreCase));
                        if(selected==null){var matches=installed.Items.Where(s=>s.Name==button.SkillName).ToArray();if(matches.Length==1)selected=matches[0];}
                        if(selected==null)throw new FileNotFoundException("이 PC에 스킬이 없거나 같은 이름이 여러 개입니다. 스킬 화면에서 다시 연결하세요.");
                        button.SkillPath=selected.Path;button.SkillName=selected.Name;
                        var result=await DesktopSkillRunner.Run(project,button,stop.Token);if(disposed)return;status=result.Text;
                        log("스킬",DesktopSkillRunner.AppName(button.Target)+(result.Sent?" · 요청 전송됨":" · 앱에서 확인 필요"));
                    }finally {runningId=null;}
                    return;
                }
            }catch(OperationCanceledException){if(!disposed)status="작업을 취소했습니다. 저장된 프로젝트는 유지됩니다.";}
            catch(Exception ex){if(!disposed)status="확인 필요 · "+ex.Message;}
            finally {if(loginStop!=null){loginStop.Dispose();loginStop=null;}authCode=null;busy=false;Notify();}
        }
        internal string StartApps() {
            var results=new List<string>();
            try{using(var opened=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://work-cycle.evolvix.workers.dev/"){UseShellExecute=true})){}results.Add("work-cycle 열기 요청됨");}catch(Exception){results.Add("work-cycle 열기 실패 · 기본 브라우저 확인");}
            var project=store.Current;if(project==null)return String.Join(" · ",results)+" · ＋에서 프로젝트를 등록하세요.";
            if(project.Location=="cloud" || project.TaskKind=="conversation") {ProjectExecution.Show(Form.ActiveForm,project,null,store.Value.StartApps);return String.Join(" · ",results)+" · AI 요청 준비 화면 열림";}
            if(!Directory.Exists(project.Folder))return "로컬 폴더를 먼저 연결하세요.";
            foreach(string app in store.Value.StartApps=="both"?new[]{"gpt","claude"}:new[]{store.Value.StartApps})try{DesktopSkillRunner.Open(app,project.Folder);results.Add(DesktopSkillRunner.AppName(app)+" 열기 요청됨");}catch(Exception){results.Add(DesktopSkillRunner.AppName(app)+" 앱 설치 확인");}
            return String.Join(" · ",results);
        }
        public void Dispose(){disposed=true;stop.Cancel();if(loginStop!=null)loginStop.Cancel();}
    }
}
