using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MinaDesk {
    internal sealed class DeveloperConsole : IDisposable {
        static readonly Dictionary<string,string> Requests=new Dictionary<string,string> {
            {"codex-new",""},{"codex-compose","오늘 할 일과 이전 작업을 확인하고 다음 작업을 함께 정하고 싶습니다."},
            {"codex-approval","실행하거나 수정하기 전에 대상, 영향 범위와 검증 방법을 알려주고 제 확인을 기다려 주세요."},
            {"codex-bug","문제를 재현할 조건을 확인하고 원인과 수정 범위를 정리해 주세요. 재발을 확인할 테스트도 알려주세요."},
            {"codex-feature","요구 사항을 정리하고 관련 코드와 구현 범위, 검증 방법을 제안해 주세요."},
            {"codex-analyze","관련 코드의 흐름과 주요 파일을 찾아 쉽게 설명해 주세요."},
            {"codex-refactor","기존 동작을 보존하며 읽기 어려운 부분을 개선할 계획과 검증 방법을 정리해 주세요."},
            {"codex-skill-use","선택한 스킬의 SKILL.md와 연결된 자료를 확인한 뒤 이 프로젝트에 적용해 주세요."},
            {"codex-skill-create","프로젝트에서 반복되는 작업을 한 가지 스킬로 만드는 계획을 세워 주세요."},
            {"codex-git","작업 브랜치의 Git 변경 내용을 검토하고 위험과 빠진 검증을 알려주세요."},
            {"gh-codex","이 GitHub 저장소와 선택한 브랜치의 작업을 이어가고 싶습니다."}
        };
        static readonly Dictionary<string,string> Shortcuts=new Dictionary<string,string> {
            {"codex-palette","명령 메뉴는 Codex에서 Ctrl+K로 여세요."},
            {"codex-previous","Codex에서 Ctrl+Shift+Tab으로 이전 채팅 또는 탭으로 이동하세요."},
            {"codex-next","Codex에서 Ctrl+Tab으로 다음 채팅 또는 탭으로 이동하세요."},
            {"codex-find-file","Codex에서 Ctrl+P로 프로젝트 파일을 검색하세요."},
        };
        static readonly Dictionary<string,string> RepoFavorites=new Dictionary<string,string> {
            {"gh-hub","Workstation_Hub"},{"gh-work-cycle","work-cycle"}
        };
        static readonly string[] PromptKeys={"prompt-1","prompt-2","prompt-3"};
        static readonly string[] ThreadKeys={"codex-recent-1","codex-recent-2"};
        static readonly HashSet<string> Checks=new HashSet<string>{"validate-types","validate-test","validate-build","validate-all"};
        readonly ProjectSkills skills;
        readonly MacroCommands commands;
        readonly DeveloperPreferenceStore prefs;
        readonly DeveloperGitHub github;
        readonly DeveloperWeather weather;
        readonly CodexModelComparison models=new CodexModelComparison();
        readonly AiAccounts accounts;
        readonly Hub hub;
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        readonly Dictionary<string,DeveloperBranches> branches=new Dictionary<string,DeveloperBranches>();
        CancellationTokenSource checking;
        string branchError,retrievedKey,contextKey,scope,resultKey,activePr,prRepo;
        DeveloperResult lastResult;
        bool branchLoading,running,disposed;

        internal DeveloperConsole(Hub hub,ProjectSkills skills,MacroCommands commands) {
            this.hub=hub;this.skills=skills;this.commands=commands;
            prefs=new DeveloperPreferenceStore(hub.DataDir);
            github=new DeveloperGitHub(skills.GitHub);
            weather=new DeveloperWeather(prefs,()=>hub.BroadcastDeveloper());
            accounts=new AiAccounts(prefs,hub.DataDir);
        }
        SkillProject Project { get { return skills.Current; } }
        GitHubRepository Repo { get { return Project==null?null:Project.Repository; } }
        string RepoKey {
            get { return Repo==null || skills.GitHub.AccountId<=0?null:skills.GitHub.AccountId+"/"+Repo.Id; }
        }
        DeveloperBranches Branches {
            get {
                DeveloperBranches options;
                string key=RepoKey;
                return key!=null && branches.TryGetValue(key,out options)?options:null;
            }
        }
        string Branch {
            get {
                var options=Branches;
                if(options==null)return null;
                string saved=DeveloperPreferenceStore.Lookup(prefs.Value.Branches,RepoKey);
                return options.Names.Contains(saved)?saved:options.Default;
            }
        }
        string Scope() {
            var project=Project;
            string next=project==null?"":project.Id+"\n"+project.Location+"\n"+project.TaskKind+"\n"+project.Folder+"\n"+RepoKey+"\n"+Branch;
            if(next!=contextKey) {
                if(prRepo!=RepoKey)activePr=null;
                prRepo=RepoKey;contextKey=next;scope=Guid.NewGuid().ToString("N");
                lastResult=null;resultKey=null;
            }
            return scope;
        }
        string SavedKey(string card) { return (Project==null?"default":Project.Id)+"/"+card; }
        internal object Snapshot() {
            var project=Project;
            var options=Branches;
                return new {kind="developer-state",desktop=true,scope=Scope(),project=project==null?null:new {
                    id=project.Id,name=project.Name,location=project.Location,taskKind=project.TaskKind,folder=project.Folder,
                    repository=Repo==null?null:Repo.FullName,shared=project.Shared,conflict=project.SyncConflict,pending=project.SharedDirty
                },
                projects=skills.Projects.Select(p=>new {id=p.Id,name=p.Name,cloud=p.Repository!=null}).ToArray(),
                account=skills.GitHub.LoginName,projectBusy=skills.Busy,projectStatus=skills.Status,readOnly=skills.ReadOnly||prefs.ReadOnly,
                shareReady=skills.ShareReady,branch=Branch,defaultBranch=options==null?null:options.Default,
                branches=options==null?new string[0]:options.Names.ToArray(),
                branchBusy=branchLoading,needBranches=RepoKey!=null && options==null && !branchLoading && retrievedKey!=RepoKey,
                branchError=retrievedKey==RepoKey?branchError:null,activePr,model=prefs.Model,effort=prefs.Effort,
                models=DeveloperPreferenceStore.Models,efforts=DeveloperPreferenceStore.Efforts,
                prompts=PromptKeys.Select(key=>DeveloperPreferenceStore.Lookup(prefs.Value.Prompts,SavedKey(key))??"").ToArray(),
                threads=ThreadKeys.Select(key=>DeveloperPreferenceStore.Lookup(prefs.Value.Threads,SavedKey(key))??"").ToArray(),
                requests=Requests,catalog=skills.Installed.Take(120).Select(s=>new{id=s.Id,name=s.Name,source=s.Source}).ToArray(),
                links=DeveloperPreferenceStore.LinkNames.Select(kv=>new {id=kv.Key,label=kv.Value,value=prefs.WebAddress(kv.Key)}).ToArray(),
                folders=DeveloperPreferenceStore.FolderNames.Select(kv=>new {id=kv.Key,label=kv.Value,value=DeveloperPreferenceStore.Lookup(prefs.Value.Folders,kv.Key)}).ToArray(),
                layout=hub.Config.Layout,theme=hub.Config.Theme,enabled=hub.Config.Panels.ToDictionary(p=>p.Key,p=>p.Value.Enabled),notice=prefs.LoadNotice,
                running,verifying=checking!=null,focus=hub.FocusEnabled
            };
        }
        internal object WidgetSnapshot() {return weather.Snapshot();}
        internal async Task HandleUtility(DeskWindow owner,string type,Dictionary<string,object> message) {
            string action=Text(message,"id");
            try {
                if(type=="developer-ai"){
                    Action send=()=>owner.Send(accounts.Snapshot());
                    send();
                    await accounts.Handle(action,Text(message,"provider"),send);
                    return;
                }
                if(action=="guide"){Open(CodexModelComparison.Guide);return;}
                if(action=="release"){Open(CodexModelComparison.Release);return;}
                if(action=="show" || action=="refresh"){
                    Action send=()=>owner.Send(models.Snapshot(prefs.Model));
                    send();await models.Refresh(action=="refresh",send);
                }
            }catch(Exception ex){
                if(!disposed)owner.Send(new{kind="notice",text=DeveloperResult.Limit(ex.Message,350)});
            }
        }
        static string Text(Dictionary<string,object> message,string key) {return GitHubValues.Text(message,key);}
        static DeveloperResult Notice(string title,string summary) {return new DeveloperResult{title=title,summary=summary};}
        void Show(DeskWindow owner,DeveloperResult result) {
            if(disposed || owner.IsDisposed)return;
            string stamp=Scope();
            lastResult=result;resultKey=Guid.NewGuid().ToString("N");
            owner.Send(new {kind="developer-result",scope=stamp,key=resultKey,result.title,result.summary,result.body,result.graph,
                rows=result.rows.Select((row,i)=>new {row.title,row.detail,row.pr,row.branch,row.commit,row.parents,index=i,open=row.Address!=null}).ToArray()});
        }
        static bool Confirm(DeskWindow owner,string title,string detail) {
            return MessageBox.Show(owner,detail,title,MessageBoxButtons.OKCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.OK;
        }
        static void Open(string target) {
            using(var opened=Process.Start(new ProcessStartInfo(target){UseShellExecute=true})){}
        }
        static void Codex(string path) {
            using(var protocol=Registry.ClassesRoot.OpenSubKey("codex"))
                if(protocol==null)throw new InvalidOperationException("Windows에 Codex 데스크톱 앱을 설치하고 다시 시도하세요.");
            Open("codex://"+path);
        }
        GitHubRepository RequireRepo() {
            DeveloperGitHub.Route(Repo);
            if(skills.GitHub.AccountId<=0)throw new InvalidOperationException("프로젝트 추가 화면에서 GitHub에 로그인하세요.");
            return Repo;
        }
        async Task RefreshBranches() {
            var repo=RequireRepo();
            string key=RepoKey;
            if(branchLoading)return;
            branchLoading=true;retrievedKey=key;branchError=null;hub.BroadcastDeveloper();
            try {
                var list=await github.Branches(repo,lifetime.Token);
                if(!disposed) {
                    if(branches.Count>=50)branches.Clear();
                    branches[key]=list;
                }
            }catch(Exception ex) {
                if(!disposed)branchError=DeveloperResult.Limit(ex.Message,500);
            }finally{branchLoading=false;if(!disposed)hub.BroadcastDeveloper();}
        }
        async Task ConfigureProject(DeskWindow owner,string location,bool shared) {
            var project=Project;
            if(project==null)throw new InvalidOperationException("프로젝트를 먼저 선택하세요.");
            if(location==null)location=project.Location;
            await skills.Handle(owner,"project-config",new Dictionary<string,object>{
                {"projectId",project.Id},{"location",location},{"taskKind",project.TaskKind},{"shared",shared}
            });
        }
        async Task<string> SwitchFavorite(DeskWindow owner,string id) {
            string name=RepoFavorites[id];
            var matches=skills.Projects.Where(p=>p.Repository!=null
                && String.Equals(p.Repository.Name,name,StringComparison.OrdinalIgnoreCase)).ToArray();
            var owned=matches.FirstOrDefault(p=>String.Equals(p.Repository.FullName,skills.GitHub.LoginName+"/"+name,StringComparison.OrdinalIgnoreCase));
            var project=owned??(matches.Length==1?matches[0]:null);
            if(project!=null) {
                await skills.Handle(owner,"skill-project-select",new Dictionary<string,object>{{"projectId",project.Id}});
                return project.Name+" · 프로젝트 선택 요청";
            }
            hub.ShowMacro("projects");
            return "프로젝트 추가에서 "+name+" 저장소를 선택해 등록하세요.";
        }
        internal async Task Handle(DeskWindow owner,string type,Dictionary<string,object> message) {
            string id=Text(message,"id"),input=Text(message,"value");
            if(type=="developer-widget") {
                try {await HandleWidget(id,input);}catch(Exception ex) {owner.Send(new {kind="notice",text=ex.Message});}
                return;
            }
            if(type=="developer-cancel"){if(checking!=null)checking.Cancel();return;}
            if(type=="developer-auto-branches") {
                if(RepoKey!=null && !branchLoading && retrievedKey!=RepoKey)await RefreshBranches();
                return;
            }
            if(Scope()!=Text(message,"scope")){owner.Send(Snapshot());return;}
            if(running){owner.Send(new{kind="notice",text="실행 중인 카드가 끝난 뒤 다시 누르세요."});return;}
            string start=Scope();
            running=true;hub.BroadcastDeveloper();
            try {
                DeveloperResult result;
                if(type=="developer-result-open"){OpenResult(message);return;}
                if(type=="developer-choose") {
                    await Choose(owner,id,input,Text(message,"effort"));
                    return;
                }
                if(type!="developer-run" || String.IsNullOrEmpty(id))return;
                result=await Run(owner,id,input,start,Text(message,"skill"));
                if(result==null || disposed || owner.IsDisposed)return;
                if(start!=Scope() && id!="gh-refresh" && !RepoFavorites.ContainsKey(id))return;
                Show(owner,result);
                if(RecordsAction(id))hub.LogDeveloper(result.title,DeveloperResult.Limit(result.summary,145));
            }catch(OperationCanceledException){if(!disposed && start==Scope())Show(owner,Notice("실행 취소","실행을 취소했습니다."));}
            catch(Exception ex){if(!disposed && start==Scope())Show(owner,Notice("확인 필요",DeveloperResult.Limit(ex.Message,500)));}
            finally{running=false;if(!disposed)hub.BroadcastDeveloper();}
        }
        static bool RecordsAction(string id) {
            return Checks.Contains(id) || id=="tool-lock" || id=="tool-record" || id=="tool-screenshot" || id=="gh-new-pr" || id=="gh-new-issue";
        }
        async Task Choose(DeskWindow owner,string id,string input,string effort) {
            switch(id) {
                case "project":
                    await skills.Handle(owner,"skill-project-select",new Dictionary<string,object>{{"projectId",input}});
                    return;
                case "branch":
                    var options=Branches;
                    var repoKey=RepoKey;
                    if(options==null || !options.Names.Contains(input))throw new ArgumentException("조회된 브랜치를 선택하세요.");
                    prefs.Change(v=>v.Branches[repoKey]=input);return;
                case "location": await ConfigureProject(owner,input,Project!=null && Project.Shared);return;
                case "share":
                    if(input!="on" && input!="off")return;
                    if(!skills.ShareReady){hub.ShowMacro("projects");return;}
                    await ConfigureProject(owner,null,input=="on");return;
                case "model":
                    if(DeveloperPreferenceStore.Models.Contains(input))prefs.Change(v=>v.Model=input);return;
                case "effort":
                    if(DeveloperPreferenceStore.Efforts.Contains(input))prefs.Change(v=>v.Effort=input);return;
                case "execution":
                    if(!DeveloperPreferenceStore.Models.Contains(input) || !DeveloperPreferenceStore.Efforts.Contains(effort))
                        throw new ArgumentException("지원하는 모델과 추론 강도를 선택하세요.");
                    prefs.Change(v=>{v.Model=input;v.Effort=effort;});return;
                case "layout": hub.ChangeLayout(input);return;
                case "theme": hub.ChangeTheme(input);return;
                case "restore": hub.Restore(true);return;
                case "panel":
                    if(input!=null && Hub.Keys.Contains(input))hub.Enable(input,!hub.Config.Panels[input].Enabled);return;
            }
            if(id==null)return;
            if(ThreadKeys.Contains(id)) {
                string thread=DeveloperPreferenceStore.ThreadId(input),key=SavedKey(id);
                prefs.Change(v=>v.Threads[key]=thread);return;
            }
            if(PromptKeys.Contains(id)) {
                string key=SavedKey(id);
                prefs.Change(v=>v.Prompts[key]=CheckedPrompt(input));return;
            }
            if(DeveloperPreferenceStore.LinkNames.ContainsKey(id)) {
                string address=DeveloperPreferenceStore.CheckWeb(id,input);
                prefs.Change(v=>v.Links[id]=address);return;
            }
            if(DeveloperPreferenceStore.FolderNames.ContainsKey(id))ConfigureFolder(owner,id);
        }
        void OpenResult(Dictionary<string,object> message) {
            int index;
            if(lastResult==null || resultKey!=Text(message,"key")
                || !Int32.TryParse(Text(message,"index"),NumberStyles.None,CultureInfo.InvariantCulture,out index)
                || index<0 || index>=lastResult.rows.Count)return;
            string address=lastResult.rows[index].Address;
            if(DeveloperGitHub.GitHubUrl(address) || (address!=null && address==prefs.WebAddress("web-preview")))Open(address);
        }
        async Task<DeveloperResult> Run(DeskWindow owner,string id,string input,string start,string skillId) {
            if(DeveloperGitHub.Titles.ContainsKey(id)) {
                var repo=RequireRepo();
                var result=await github.Read(id,repo,skills.GitHub.LoginName,Branch,input,activePr,Branches,prefs.WebAddress("web-preview"),lifetime.Token);
                if(disposed || start!=Scope())return null;
                if(new[]{"gh-pr","gh-files","gh-checks","gh-copy"}.Contains(id))activePr=DeveloperGitHub.Number(String.IsNullOrWhiteSpace(input)?activePr:input);
                if(id=="gh-copy") {
                    string url=repo.Url+"/pull/"+activePr;
                    Clipboard.SetText(url);
                    result.summary="확인한 PR 링크를 클립보드에 복사했습니다.";
                }
                return result;
            }
            if(RepoFavorites.ContainsKey(id))return Notice(RepoFavorites[id],await SwitchFavorite(owner,id));
            if(Checks.Contains(id))return await Verify(owner,id);
            if(Requests.ContainsKey(id) || PromptKeys.Contains(id))return await Draft(owner,id,input,skillId);
            if(id=="git-graph") {
                var project=Project;
                if(project==null)throw new InvalidOperationException("Git 그래프를 볼 프로젝트를 선택하세요.");
                if(project.Repository!=null && (project.Location=="cloud" || RepoKey!=null))
                    return await github.Graph(RequireRepo(),Branch,lifetime.Token);
                return await DeveloperGitHub.LocalGraph(project.Name,skills.ToolFolder,lifetime.Token);
            }
            if(id=="codex-find-chat" || id=="codex-file-tree") {
                bool search=id=="codex-find-chat";
                string label=search?"채팅 찾기":"파일 트리";
                Codex("");
                bool opened=await CodexDesktop.OpenPanel(search,lifetime.Token);
                return Notice(label,opened?"Codex 앱에서 "+label+" 창을 확인했습니다.":search?
                    "Codex에서 검색창을 확인하지 못했습니다. 명령 메뉴(Ctrl+Shift+P)에서 Search chats를 선택하세요.":
                    "Codex에서 파일 트리를 확인하지 못했습니다. 프로젝트 작업공간에서 Ctrl+Shift+E를 누르세요.");
            }
            string tip;
            if(Shortcuts.TryGetValue(id,out tip)) {
                Codex("");return Notice("Codex에서 이어서 실행",tip);
            }
            if(ThreadKeys.Contains(id)) {
                string thread=DeveloperPreferenceStore.Lookup(prefs.Value.Threads,SavedKey(id));
                if(thread==null)throw new InvalidOperationException("채팅 링크를 먼저 저장하세요.");
                Codex("threads/"+DeveloperPreferenceStore.ThreadId(thread));
                return Notice("저장한 채팅","Codex에 이 채팅을 열도록 요청했습니다.");
            }
            switch(id) {
                case "gh-refresh":
                    await RefreshBranches();
                    return Notice("GitHub 새로고침",branchError??"저장소 브랜치 정보를 갱신했습니다.");
                case "gh-open": case "gh-projects":
                    Open(RequireRepo().Url+(id=="gh-projects"?"/projects":""));
                    return Notice("GitHub","현재 저장소의 화면을 브라우저에서 열었습니다.");
                case "gh-new-pr": case "gh-new-issue":
                    return ComposeGitHub(owner,id,input);
                case "codex-settings": Codex("settings");return Notice("Codex 설정","설정 열기를 요청했습니다.");
                case "codex-root": Codex("");return Notice("Codex","데스크톱 앱 열기를 요청했습니다.");
                case "codex-skills":
                    var catalog=new DeveloperResult{title="설치된 스킬",summary="현재 PC 및 프로젝트의 스킬 · 매크로 패드에서 버튼 등록"};
                    foreach(var item in skills.Installed.Take(120))catalog.Add(item.Name,SkillLabel(item));
                    return catalog;
                case "open-skills":hub.ShowMacro("skills");return null;
                case "open-projects":hub.ShowMacro("projects");return null;
                case "open-project-settings":hub.ShowMacro("project-settings");return null;
                case "tool-code": return Notice("VS Code",await commands.Execute("vscode",owner));
                case "tool-terminal": case "tool-bash": return Notice("터미널",await commands.Execute("terminal",owner));
                case "tool-manager":Open(Path.Combine(Environment.SystemDirectory,"Taskmgr.exe"));return Notice("작업 관리자","Windows 작업 관리자를 열었습니다.");
                case "tool-settings":Open("ms-settings:");return Notice("Windows 설정","Windows 설정을 열었습니다.");
                case "tool-accounts":commands.ConfigureAccounts(owner);return null;
                case "tool-focus":return Notice("집중 표시",hub.ToggleFocus());
                case "tool-record": case "tool-screenshot": case "tool-lock":
                    return await ConfirmTool(owner,id);
                case "tool-find":return await SearchFolders(input);
                case "dock-gpt":ProjectExecution.OpenRoot("gpt","conversation");return null;
                case "dock-claude":ProjectExecution.OpenRoot("claude","code");return null;
            }
            if(id.StartsWith("web-",StringComparison.Ordinal)) {
                string target=prefs.WebAddress(id);
                if(target==null && id=="web-ha")target=hub.HomeDashboard;
                if(target==null)throw new InvalidOperationException("웹앱 주소 설정에서 사용할 주소를 저장하세요.");
                Open(target);return Notice("웹앱 열기",new Uri(target).Host);
            }
            if(id.StartsWith("folder-",StringComparison.Ordinal)) {
                string target=Folder(id);
                if(!Directory.Exists(target))throw new DirectoryNotFoundException("이 PC에 연결된 폴더를 찾지 못했습니다. 프로젝트 또는 폴더 설정을 확인하세요.");
                OpenFolder(target);return Notice("탐색기 열기",target);
            }
            return null;
        }
        static string SkillLabel(InstalledSkill item) {return item.Source;}
        DeveloperResult ComposeGitHub(DeskWindow owner,string id,string input) {
            var repo=RequireRepo();
            bool pull=id=="gh-new-pr";
            string title=pull?"새 PR":"새 이슈",address;
            if(pull) {
                var options=Branches;
                if(options==null || !options.Names.Contains(input) || !options.Names.Contains(Branch))
                    throw new InvalidOperationException("실제 브랜치 목록에서 기준 브랜치와 작업 브랜치를 선택하세요.");
                address=DeveloperGitHub.CompareAddress(repo,input,Branch)+"?expand=1";
            }else address=repo.Url+"/issues/new/choose";
            if(!Confirm(owner,title,repo.FullName+"\r\n"+(pull?input+" → "+Branch+"\r\n":"")+"GitHub에서 작성 화면을 엽니다. 내용을 확인한 뒤 GitHub에서 제출하세요."))
                return Notice(title,"작성 화면 열기를 취소했습니다.");
            Open(address);return Notice(title,repo.FullName+" · GitHub 작성 화면 열기");
        }
        async Task<DeveloperResult> ConfirmTool(DeskWindow owner,string id) {
            string action=id.Substring(5);
            string title=action=="record"?"OBS 녹화 전환":action=="lock"?"PC 잠금":"스크린샷";
            string detail=action=="record"?"연결된 OBS의 녹화 상태를 시작 또는 정지로 전환합니다.":
                action=="lock"?"확인을 누르면 지금 이 Windows PC가 잠깁니다. 다시 사용하려면 Windows에 로그인하세요.":
                "현재 화면을 캡처해 지정된 폴더에 저장합니다.\r\n"+commands.ScreenshotFolder;
            if(!Confirm(owner,title,detail))return Notice(title,"실행을 취소했습니다.");
            return Notice(title,await commands.Execute(action=="lock"?"pc_lock":action,owner));
        }
        async Task<DeveloperResult> Verify(DeskWindow owner,string id) {
            var plan=DeveloperVerification.Plan(id,skills.Folder);
            string preview=plan.Folder+"\r\n\r\n"+String.Join("\r\n",plan.Steps.Select(step=>step.Display))+
                "\r\n\r\n현재 프로젝트의 스크립트가 실행됩니다. 테스트나 빌드가 로컬 파일을 만들 수 있습니다.";
            if(!Confirm(owner,"로컬 검증 실행",preview))return Notice("로컬 검증","실행을 취소했습니다.");
            using(var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)) {
                checking=cancel;hub.BroadcastDeveloper();
                try {return await DeveloperVerification.Run(plan,cancel.Token);}
                finally {checking=null;}
            }
        }
        static string CheckedPrompt(string input) {
            input=(input??"").Trim();
            if(input.Length>4000 || input.Contains("\0"))throw new ArgumentException("요청문은 4,000자 이내로 작성하세요.");
            return input;
        }
        async Task<DeveloperResult> Draft(DeskWindow owner,string id,string input,string skillId) {
            if(id=="gh-codex")RequireRepo();
            string request=CheckedPrompt(input);
            var project=Project;
            var lines=new List<string>();
            if(project!=null) {
                lines.Add("프로젝트 · "+project.Name);
                if(Repo!=null)lines.Add("GitHub · "+Repo.FullName);
                if(Branch!=null)lines.Add("선택 브랜치 · "+Branch);
                lines.Add("실행 위치 · "+(project.Location=="cloud"?"클라우드":"이 PC"));
            }
            if(id=="codex-skill-use") {
                var skill=skills.Installed.FirstOrDefault(s=>s.Id==skillId);
                if(skill==null)throw new InvalidOperationException("설치된 스킬을 목록에서 선택하세요.");
                lines.Add("사용할 스킬 · "+skill.Name);
                if(Directory.Exists(skills.Folder))lines.Add("이 PC의 스킬 문서 · "+skill.Path);
                else lines.Add("이 작업 환경에 스킬이 설치돼 있는지 먼저 확인해 주세요.");
            }
            if(request.Length>0)lines.Add("\r\n"+request);
            string draft=String.Join("\r\n",lines).Trim();
            string folder=skills.Folder;
            if(Directory.Exists(folder) && request.Length>0) {
                string cli=AiCli.Find("gpt");
                var sent=await CodexLocal.Submit(cli,folder,draft,CodexLocal.ModelId(prefs.Model),CodexLocal.EffortId(prefs.Effort),lifetime.Token);
                ObserveCodexCompletion(owner,sent);
                return Notice("Codex 요청 전송",sent.Model+(sent.Effort==null?"":" · "+sent.Effort)+
                    (TryOpenCodexThread(sent.ThreadId)?" · 전송을 접수해 Codex 대화를 열었습니다.":" · 전송을 접수했습니다. Codex 대화 ID "+sent.ThreadId));
            }
            if(project!=null && project.Location=="local") {
                if(!Directory.Exists(folder))throw new DirectoryNotFoundException("로컬 프로젝트 폴더를 프로젝트 설정에서 다시 연결하세요.");
                DesktopSkillRunner.OpenDraft(folder,draft);
                return Notice("새 채팅","선택한 프로젝트의 Codex 작성창을 열었습니다.");
            }
            if(draft.Length==0) {
                Codex("threads/new");
                return Notice("새 채팅","Codex에 새 채팅 열기를 요청했습니다.");
            }
            string target="threads/new?prompt="+Uri.EscapeDataString(draft);
            if(target.Length>30000)throw new ArgumentException("Codex 작성창에 전달할 요청문이 너무 깁니다.");
            Codex(target);
            string settings=prefs.Model+" · "+prefs.Effort;
            return new DeveloperResult{title="Codex 요청 준비",summary=project==null?
                "Codex에 요청문을 넣은 작성창 열기를 요청했습니다. 작업 환경과 "+settings+" 설정을 확인한 뒤 보내세요.":
                "Codex에 요청문을 넣은 작성창 열기를 요청했습니다. 클라우드와 저장소·브랜치, "+settings+" 설정을 확인한 뒤 보내세요.",body=draft};
        }
        async void ObserveCodexCompletion(DeskWindow owner,CodexSubmission sent) {
            if(!await sent.Completion && !disposed && !owner.IsDisposed)
                owner.Send(new {kind="notice",text="Codex가 작업 완료를 확인하지 못했습니다. 대화 "+sent.ThreadId+"에서 진행 상태를 확인하세요."});
        }
        static bool TryOpenCodexThread(string id) {
            try {Codex("threads/"+id);return true;}
            catch(InvalidOperationException){return false;}
            catch(System.ComponentModel.Win32Exception){return false;}
        }
        string Folder(string id) {
            string custom=DeveloperPreferenceStore.Lookup(prefs.Value.Folders,id);
            if(custom!=null && DeveloperPreferenceStore.FolderNames.ContainsKey(id))return custom;
            string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            switch(id) {
                case "folder-project":return skills.ToolFolder;
                case "folder-documents":return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                case "folder-downloads":return WindowsDownloads.PathOrDefault(profile);
                case "folder-screenshot":return commands.ScreenshotFolder;
                case "folder-work":return skills.ToolFolder??commands.WorkingFolder??profile;
                case "folder-skills":return Path.Combine(profile,".codex","skills");
                case "folder-build":
                    return Directory.Exists(skills.ToolFolder)?new[]{"dist","build"}.Select(name=>Path.Combine(skills.ToolFolder,name)).FirstOrDefault(Directory.Exists):null;
                default:return null;
            }
        }
        static void OpenFolder(string path) {
            using(var explorer=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),ProjectLauncher.Quote(path)){UseShellExecute=false})){}
        }
        void ConfigureFolder(DeskWindow owner,string id) {
            using(var picker=new FolderBrowserDialog{Description=DeveloperPreferenceStore.FolderNames[id]+"에 사용할 폴더를 선택하세요.",
                SelectedPath=Folder(id)??Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),ShowNewFolderButton=false}) {
                if(picker.ShowDialog(owner)==DialogResult.OK)prefs.Change(v=>v.Folders[id]=picker.SelectedPath);
            }
        }
        async Task<DeveloperResult> SearchFolders(string input) {
            string folder=skills.ToolFolder??Folder("folder-work");
            if(!Directory.Exists(folder))throw new DirectoryNotFoundException("검색할 프로젝트 또는 작업 폴더를 연결하세요.");
            string query=(input??"").Trim();
            if(query.Length<2 || query.Length>80)throw new ArgumentException("파일 이름 일부를 2~80자로 입력하세요.");
            return await Task.Run(()=>FindFiles(folder,query,lifetime.Token),lifetime.Token);
        }
        static DeveloperResult FindFiles(string folder,string query,CancellationToken token) {
            var result=Notice("파일 이름 검색","탐색 경로 · "+folder);
            var queue=new Queue<string>();
            var ignored=new HashSet<string>(StringComparer.OrdinalIgnoreCase){".git","node_modules",".venv","bin","obj"};
            queue.Enqueue(folder);
            int inspected=0;const int limit=12000;
            while(queue.Count>0 && inspected<limit && result.rows.Count<40) {
                token.ThrowIfCancellationRequested();
                try {
                    foreach(string item in Directory.EnumerateFileSystemEntries(queue.Dequeue())) {
                        token.ThrowIfCancellationRequested();
                        if(++inspected>limit)break;
                        var attrs=File.GetAttributes(item);
                        if((attrs&FileAttributes.ReparsePoint)!=0)continue;
                        string name=Path.GetFileName(item);
                        if((attrs&FileAttributes.Directory)!=0) {
                            if(!ignored.Contains(name))queue.Enqueue(item);
                            continue;
                        }
                        if(name.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)
                            result.Add(name,item.Substring(folder.TrimEnd(Path.DirectorySeparatorChar).Length+1));
                        if(result.rows.Count>=40)break;
                    }
                }catch(IOException){}catch(UnauthorizedAccessException){}
            }
            result.summary+=(result.rows.Count>=40?" · 처음 40개":inspected>=limit?" · 처음 12,000개 항목에서 "+result.rows.Count+"건":" · "+result.rows.Count+"건");
            return result;
        }
        async Task HandleWidget(string id,string input) {
            switch(id) {
                case "weather":await weather.Refresh();return;
                case "city":await weather.Choose(input);return;
                case "dock-gpt":ProjectExecution.OpenRoot("gpt","conversation");return;
                case "dock-claude":ProjectExecution.OpenRoot("claude","code");return;
                case "codex-root":Codex("");return;
                case "tool-manager":Open(Path.Combine(Environment.SystemDirectory,"Taskmgr.exe"));return;
                case "tool-settings":Open("ms-settings:");return;
                case "folder-explorer":OpenFolder(Folder("folder-documents"));return;
                case "folder-work":
                    string folder=Folder(id);
                    if(!Directory.Exists(folder))throw new DirectoryNotFoundException("작업 폴더를 개발자 컨트롤에서 연결하세요.");
                    OpenFolder(folder);return;
                case "web-github":case "web-drive":case "web-calendar":case "usage-codex":case "usage-claude":case "weather-source":
                    Open(prefs.WebAddress(id));return;
            }
        }
        public void Dispose(){disposed=true;lifetime.Cancel();weather.Dispose();models.Dispose();accounts.Dispose();}
    }

    internal static class WindowsDownloads {
        internal static string PathOrDefault(string profile) {
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders")) {
                var path=key==null?null:key.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
                if(Directory.Exists(path))return path;
            }
            return Path.Combine(profile,"Downloads");
        }
    }
}
