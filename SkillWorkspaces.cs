using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MinaDesk {
    public sealed class SkillButton {
        public string Id { get; set; }
        public string SkillPath { get; set; }
        public string SkillName { get; set; }
        public string Label { get; set; }
        public string Target { get; set; }
        public string Instruction { get; set; }
        public string Reference { get; set; }
        public SkillButtonOverride LocalOverride { get; set; }
        public SkillButtonOverride CloudOverride { get; set; }
    }
    public sealed class SkillButtonOverride {
        public string Target { get; set; }
        public string Instruction { get; set; }
    }
    public sealed class SkillProject {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Folder { get; set; }
        public List<SkillButton> Buttons { get; set; }
        public GitHubRepository Repository { get; set; }
        public string Location { get; set; }
        public string TaskKind { get; set; }
        public bool Shared { get; set; }
        public long SharedAccountId { get; set; }
        public string SharedSha { get; set; }
        public bool SharedDirty { get; set; }
        public bool PendingUnshare { get; set; }
        public bool SyncConflict { get; set; }
        public bool LocalRegistered { get; set; }
        public SkillProject() { Id=Guid.NewGuid().ToString("N"); Buttons=new List<SkillButton>();Location="local";TaskKind="code";LocalRegistered=true; }
    }
    public sealed class SkillWorkspaceSettings {
        public int Version { get; set; }
        public string SelectedProjectId { get; set; }
        public string StartApps { get; set; }
        public List<SkillProject> Projects { get; set; }
        public long SyncAccountId { get; set; }
        public long SyncRepositoryId { get; set; }
        public long PendingSyncRepositoryId { get; set; }
        public long PendingSyncAccountId { get; set; }
        public SkillWorkspaceSettings() { Version=2; StartApps="both"; Projects=new List<SkillProject>(); }
    }
    // Separate from macros.json so older macro settings cannot overwrite project buttons.
    internal sealed class SkillWorkspaces {
        readonly string file;
        internal SkillWorkspaceSettings Value { get; private set; }
        internal string LoadNotice { get; private set; }
        internal bool ReadOnly { get; private set; }
        internal SkillProject Current { get { return Value.Projects.FirstOrDefault(p=>p.Id==Value.SelectedProjectId); } }
        internal SkillWorkspaces(string data) {
            file=Path.Combine(data,"skill-workspaces.json");
            if(!File.Exists(file)) { Value=new SkillWorkspaceSettings(); return; }
            try { Value=Load(file); }
            catch(Exception ex) {
                if(!IsStorageError(ex))throw;
                try {
                    Value=Load(file+".bak");
                    // Keep the corrupt original for recovery before allowing an atomic rewrite.
                    File.Copy(file,file+".unreadable-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
                    LoadNotice="프로젝트 설정을 이전 백업에서 복구했습니다.";
                } catch(Exception backupError) {
                    if(!IsStorageError(backupError))throw;
                    Value=new SkillWorkspaceSettings();ReadOnly=true;
                    LoadNotice="프로젝트 설정 파일을 읽지 못했습니다. 기존 파일을 보존하고 저장을 중지했습니다.";
                }
            }
        }
        static bool IsStorageError(Exception ex) {
            return ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException;
        }
        static SkillWorkspaceSettings Load(string path) {
            var value=LocalData.Read<SkillWorkspaceSettings>(path,()=>null);
            if(value==null || (value.Version!=1 && value.Version!=2) || value.Projects==null || value.Projects.Any(p=>p==null || String.IsNullOrEmpty(p.Id) || (String.IsNullOrEmpty(p.Folder) && p.Repository==null) || (p.Repository!=null && !GitHubRepository.Valid(p.Repository)) || p.Buttons==null || p.Buttons.Any(b=>b==null || String.IsNullOrEmpty(b.Id) || String.IsNullOrEmpty(b.SkillName) || !IsTarget(b.Target))))
                throw new InvalidOperationException("프로젝트 설정 형식을 확인하세요.");
            if(value.Projects.Select(p=>p.Id).Distinct().Count()!=value.Projects.Count)throw new InvalidOperationException("중복 프로젝트입니다.");
            foreach(var p in value.Projects)if(p.Buttons.Select(b=>b.Id).Distinct().Count()!=p.Buttons.Count)throw new InvalidOperationException("중복 스킬 버튼입니다.");
            foreach(var p in value.Projects) {
                if(p.Location!="local" && p.Location!="cloud")p.Location=p.Repository==null?"local":"cloud";
                if(p.TaskKind!="code" && p.TaskKind!="conversation")p.TaskKind="code";
                if(p.Shared && p.SharedAccountId<=0)throw new InvalidOperationException("공유 계정이 없습니다.");
            }
            value.Version=2;
            if(!new[]{"gpt","claude","both"}.Contains(value.StartApps))value.StartApps="both";
            if(!value.Projects.Any(p=>p.Id==value.SelectedProjectId))value.SelectedProjectId=value.Projects.Select(p=>p.Id).FirstOrDefault();
            return value;
        }
        internal static bool IsTarget(string target) { return target=="gpt" || target=="claude"; }
        internal void Change(Action<SkillWorkspaceSettings> edit) {
            if(ReadOnly)throw new InvalidOperationException(LoadNotice);
            var next=LocalData.Decode<SkillWorkspaceSettings>(LocalData.Encode(Value));
            edit(next);
            LocalData.Write(file,next); // Commit memory only after disk succeeds.
            Value=next;
        }
        internal string AddProject(string folder) {
            if(!Directory.Exists(folder))throw new DirectoryNotFoundException("프로젝트 폴더를 찾을 수 없습니다.");
            string full=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            if(full.Length==2 && full[1]==':')full+=Path.DirectorySeparatorChar;
            var existing=Value.Projects.FirstOrDefault(p=>String.Equals(p.Folder,full,StringComparison.OrdinalIgnoreCase));
            string id=existing==null?Guid.NewGuid().ToString("N"):existing.Id;
            Change(next=>{
                if(existing==null)next.Projects.Add(new SkillProject{Id=id,Name=new DirectoryInfo(full).Name,Folder=full});
                next.SelectedProjectId=id;
            });
            return id;
        }
        internal void Select(string id) {
            if(!Value.Projects.Any(p=>p.Id==id))throw new ArgumentException("등록한 프로젝트를 선택하세요.");
            Change(next=>next.SelectedProjectId=id);
        }
        internal void SaveButton(string projectId,string buttonId,InstalledSkill skill,string label,string target,string instruction) {
            if(skill==null || !File.Exists(skill.Path))throw new FileNotFoundException("설치된 스킬을 다시 선택하세요.");
            if(!IsTarget(target))throw new ArgumentException("GPT 또는 Claude를 선택하세요.");
            label=(label??"").Trim();instruction=(instruction??"").Trim();
            if(label.Length<1 || label.Length>40 || instruction.Length>4000)throw new ArgumentException("버튼 이름은 1~40자, 요청은 4,000자까지 입력하세요.");
            Change(next=>{
                var project=next.Projects.SingleOrDefault(p=>p.Id==projectId);
                if(project==null)throw new ArgumentException("프로젝트를 다시 선택하세요.");
                SkillButton button;
                if(String.IsNullOrEmpty(buttonId)) { button=new SkillButton{Id=Guid.NewGuid().ToString("N")}; project.Buttons.Add(button); }
                else { button=project.Buttons.SingleOrDefault(b=>b.Id==buttonId); if(button==null)throw new ArgumentException("수정할 버튼을 찾을 수 없습니다."); }
                button.SkillPath=skill.Path;button.SkillName=skill.Name;button.Label=label;button.Target=target;button.Instruction=instruction;
                if(project.Shared)project.SharedDirty=true;
            });
        }
        internal void RemoveButton(string projectId,string buttonId) {
            Change(next=>{var project=next.Projects.SingleOrDefault(p=>p.Id==projectId);if(project==null || project.Buttons.RemoveAll(b=>b.Id==buttonId)!=1)throw new ArgumentException("삭제할 버튼을 찾을 수 없습니다.");if(project.Shared)project.SharedDirty=true;});
        }
        internal void Register(IEnumerable<GitHubRepository> repositories,HashSet<long> shareIds,long accountId) {
            var repos=repositories.ToArray();
            if(repos.Length==0 || repos.Any(r=>!GitHubRepository.Valid(r)))throw new ArgumentException("저장소를 선택하세요.");
            Change(next=>{
                foreach(var repo in repos) {
                    var p=next.Projects.FirstOrDefault(x=>x.Repository!=null && x.Repository.Id==repo.Id);
                    if(p==null){p=new SkillProject{Id="github-"+repo.Id,Name=repo.Name,Repository=repo,Location="cloud"};next.Projects.Add(p);}
                    p.Repository=repo;p.LocalRegistered=true;
                    if(shareIds.Contains(repo.Id))SetSharing(p,true,accountId);
                    next.SelectedProjectId=p.Id;
                }
            });
        }
        internal static void SetSharing(SkillProject p,bool shared,long accountId) {
            if(p.Shared==shared)return;
            if(shared && p.Repository==null)throw new InvalidOperationException("GitHub 프로젝트에서 PC 공유를 사용할 수 있습니다.");
            if(shared && accountId<=0)throw new InvalidOperationException("GitHub 로그인 후 공유하세요.");
            if((p.Shared || p.PendingUnshare) && p.SharedAccountId!=accountId)throw new InvalidOperationException("이 프로젝트를 공유한 GitHub 계정으로 로그인하세요.");
            p.Shared=shared;p.SharedAccountId=accountId;p.SharedDirty=true;p.PendingUnshare=!shared;p.SyncConflict=false;
        }
        internal void Configure(string id,string location,string kind,bool shared,long accountId) {
            if(location!="cloud" && location!="local")throw new ArgumentException("실행 위치를 선택하세요.");
            if(kind!="code" && kind!="conversation")throw new ArgumentException("작업 종류를 선택하세요.");
            Change(next=>{
                var p=next.Projects.Single(x=>x.Id==id);
                if(location=="local" && !Directory.Exists(p.Folder))throw new DirectoryNotFoundException("먼저 이 PC의 로컬 폴더를 연결하세요.");
                if(location=="cloud" && p.Repository==null)throw new InvalidOperationException("GitHub 프로젝트에서 클라우드를 선택하세요.");
                SetSharing(p,shared,accountId);p.Location=location;p.TaskKind=kind;
            });
        }
        internal void BindLocal(string id,string folder,long verifiedRepositoryId) {
            if(!Directory.Exists(folder))throw new DirectoryNotFoundException("폴더를 찾을 수 없습니다.");
            string full=Path.GetFullPath(folder);
            Change(next=>{
                var p=next.Projects.Single(x=>x.Id==id);
                if(p.Repository==null || p.Repository.Id!=verifiedRepositoryId)throw new InvalidOperationException("같은 GitHub 저장소의 폴더만 연결할 수 있습니다.");
                if(next.Projects.Any(x=>x.Id!=id && String.Equals(x.Folder,full,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("이 폴더는 이미 다른 프로젝트로 등록돼 있습니다. 기존 설정을 보존하기 위해 연결을 중지했습니다.");
                p.Folder=full;p.LocalRegistered=true;
            });
        }
        internal void ButtonOptions(string projectId,string buttonId,string scope,string target,string instruction,string reference,string label=null) {
            if(!IsTarget(target) || (scope!="common" && scope!="location" && scope!="reset"))throw new ArgumentException("버튼 범위를 선택하세요.");
            instruction=(instruction??"").Trim();reference=(reference??"").Trim();
            if(instruction.Length>4000 || !ValidReference(reference) || (label!=null && (String.IsNullOrWhiteSpace(label) || label.Length>40)))throw new ArgumentException("버튼 이름은 1~40자, 요청은 4,000자까지, 스킬 경로는 저장소 안의 상대 경로를 입력하세요.");
            Change(next=>{
                var p=next.Projects.Single(x=>x.Id==projectId);var b=p.Buttons.Single(x=>x.Id==buttonId);
                if(scope=="common") { b.Target=target;b.Instruction=instruction;b.Reference=reference;if(label!=null)b.Label=label.Trim();if(p.Shared)p.SharedDirty=true; }
                else { var setting=scope=="reset"?null:new SkillButtonOverride{Target=target,Instruction=instruction};if(p.Location=="cloud")b.CloudOverride=setting;else b.LocalOverride=setting; }
            });
        }
        internal static bool ValidReference(string value) {
            return String.IsNullOrEmpty(value) || (value.Length<=500 && !Path.IsPathRooted(value) && !value.Contains(":") && !value.Contains("\\") && !value.Split('/').Any(s=>s==".." || s==".") && !value.Any(Char.IsControl));
        }
        internal static SkillButton Effective(SkillProject p,SkillButton original) {
            var b=LocalData.Decode<SkillButton>(LocalData.Encode(original));var setting=p.Location=="cloud"?b.CloudOverride:b.LocalOverride;
            if(setting!=null){b.Target=setting.Target;b.Instruction=setting.Instruction;}return b;
        }
    }
}

