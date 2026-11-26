using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MinaDesk {
    internal sealed class ProjectSkills : IDisposable {
        readonly SkillWorkspaces store;
        readonly Action changed;
        readonly Action<string,string> log;
        readonly CancellationTokenSource stop=new CancellationTokenSource();
        SkillCatalogResult catalog=new SkillCatalogResult();
        string status,runningId;
        int generation;
        bool disposed,loading;
        internal string Folder { get { return store.Current==null?null:store.Current.Folder; } }
        internal ProjectSkills(string data,Action onChanged,Action<string,string> onLog) {
            store=new SkillWorkspaces(data);changed=onChanged;log=onLog;status=store.LoadNotice;
        }
        internal object Snapshot() {
            var current=store.Current;
            return new {readOnly=store.ReadOnly,loading,status,runningId,selectedProjectId=store.Value.SelectedProjectId,startApps=store.Value.StartApps,
                projects=store.Value.Projects.Select(p=>new{id=p.Id,name=p.Name,folder=p.Folder}).ToArray(),
                buttons=(current==null?new List<SkillButton>():current.Buttons).Select(b=>new{id=b.Id,label=b.Label,skillName=b.SkillName,target=b.Target,instruction=b.Instruction,
                    skillId=catalog.Items.Where(s=>String.Equals(s.Path,b.SkillPath,StringComparison.OrdinalIgnoreCase)).Select(s=>s.Id).FirstOrDefault()}).ToArray(),
                catalog=catalog.Items.Select(s=>new{id=s.Id,name=s.Name,description=s.Description,source=s.Source,path=s.Path}).ToArray(),
                warnings=catalog.Warnings.Distinct().ToArray()};
        }
        internal async Task Refresh() {
            int request=++generation;string folder=Folder;loading=true;catalog=new SkillCatalogResult();Notify();
            try {
                var found=await Task.Run(()=>SkillCatalog.Scan(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),folder));
                if(disposed || request!=generation)return;
                catalog=found;
            }catch(Exception) { if(!disposed && request==generation)status="스킬 목록을 읽지 못했습니다. 새로고침으로 다시 확인하세요."; }
            finally { if(!disposed && request==generation){loading=false;Notify();} }
        }
        void Notify() { if(!disposed)changed(); }
        static string Text(Dictionary<string,object> message,string key) { object value;return message.TryGetValue(key,out value)?value as string:null; }
        internal async Task Handle(Form owner,string type,Dictionary<string,object> message) {
            try {
                if(type=="skills-open" || type=="skills-refresh") { await Refresh();return; }
                if(type=="skill-project-add") {
                    using(var picker=new FolderBrowserDialog{Description="이 프로젝트에 사용할 폴더를 선택하세요.",SelectedPath=Folder??Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),ShowNewFolderButton=false}) {
                        if(picker.ShowDialog(owner)!=DialogResult.OK)return;
                        store.AddProject(picker.SelectedPath);status="프로젝트를 저장했습니다. 이 프로젝트의 스킬 버튼을 추가하세요.";
                    }
                    await Refresh();return;
                }
                if(type=="skill-project-select") { store.Select(Text(message,"projectId"));status=null;await Refresh();return; }
                if(type=="skill-start-apps") {
                    string value=Text(message,"value");
                    if(!new[]{"gpt","claude","both"}.Contains(value))throw new ArgumentException("열 앱을 선택하세요.");
                    store.Change(next=>next.StartApps=value);status="업무 시작 앱 선택을 저장했습니다.";Notify();return;
                }
                string projectId=Text(message,"projectId");
                if(store.Current==null || store.Current.Id!=projectId)throw new ArgumentException("현재 프로젝트를 다시 선택하세요.");
                if(type=="skill-save") {
                    if(loading)throw new InvalidOperationException("스킬 목록 확인이 끝난 뒤 저장하세요.");
                    var skill=catalog.Items.FirstOrDefault(s=>s.Id==Text(message,"skillId"));
                    store.SaveButton(projectId,Text(message,"buttonId"),skill,Text(message,"label"),Text(message,"target"),Text(message,"instruction"));
                    status="이 프로젝트에 스킬 버튼을 저장했습니다.";Notify();return;
                }
                if(type=="skill-remove") { store.RemoveButton(projectId,Text(message,"buttonId"));status="스킬 버튼을 삭제했습니다.";Notify();return; }
                if(type=="skill-run") {
                    if(runningId!=null) { status="다른 스킬 요청을 전달 중입니다. 완료 후 다시 눌러 주세요.";Notify();return; }
                    var project=LocalData.Decode<SkillProject>(LocalData.Encode(store.Current));
                    var button=project.Buttons.FirstOrDefault(b=>b.Id==Text(message,"buttonId"));
                    if(button==null)throw new ArgumentException("등록한 스킬 버튼을 선택하세요.");
                    if(!Directory.Exists(project.Folder))throw new DirectoryNotFoundException("프로젝트 폴더가 이동됐습니다. 폴더를 다시 등록하세요.");
                    runningId=button.Id;status="스킬 파일을 확인하고 있습니다…";Notify();
                    try {
                        // A saved path is never permission to launch an arbitrary file or shell command.
                        var installed=await Task.Run(()=>SkillCatalog.Scan(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),project.Folder));
                        if(disposed)return;
                        var selected=installed.Items.FirstOrDefault(s=>String.Equals(s.Path,button.SkillPath,StringComparison.OrdinalIgnoreCase));
                        if(selected==null)throw new FileNotFoundException("이 스킬이 설치 목록에 없습니다. 스킬 화면에서 다시 연결하세요.");
                        button.SkillName=selected.Name;
                        status=DesktopSkillRunner.AppName(button.Target)+"에 스킬 요청을 전달하는 중입니다…";Notify();
                        var result=await DesktopSkillRunner.Run(project,button,stop.Token);
                        if(disposed)return;
                        status=result.Text;
                        // Shared history contains no prompt, local path or project content.
                        log("스킬",DesktopSkillRunner.AppName(button.Target)+(result.Sent?" · 요청 전송됨":" · 앱에서 확인 필요"));
                    }finally { runningId=null;Notify(); }
                    return;
                }
            }catch(Exception ex) {
                if(!disposed){status="확인 필요 · "+ex.Message;Notify();}
            }
        }
        internal string StartApps() {
            string folder=Folder;
            if(folder==null || !Directory.Exists(folder))return "업무 모드로 전환했습니다. 스킬 화면에서 프로젝트 폴더를 먼저 등록하세요.";
            var results=new List<string>();
            try { using(var opened=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://work-cycle.evolvix.workers.dev/"){UseShellExecute=true})){}results.Add("work-cycle 열기 요청됨"); }
            catch(Exception) { results.Add("work-cycle 열기 실패 · 기본 브라우저를 확인하세요"); }
            foreach(string app in store.Value.StartApps=="both"?new[]{"gpt","claude"}:new[]{store.Value.StartApps}) {
                try { DesktopSkillRunner.Open(app,folder);results.Add(DesktopSkillRunner.AppName(app)+" 열기 요청됨"); }
                catch(Exception) { results.Add(DesktopSkillRunner.AppName(app)+" 열기 실패 · 데스크톱 앱 설치를 확인하세요"); }
            }
            return String.Join(" · ",results);
        }
        public void Dispose() { disposed=true;stop.Cancel(); }
    }
}

