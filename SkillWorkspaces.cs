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
    }
    public sealed class SkillProject {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Folder { get; set; }
        public List<SkillButton> Buttons { get; set; }
        public SkillProject() { Id=Guid.NewGuid().ToString("N"); Buttons=new List<SkillButton>(); }
    }
    public sealed class SkillWorkspaceSettings {
        public int Version { get; set; }
        public string SelectedProjectId { get; set; }
        public string StartApps { get; set; }
        public List<SkillProject> Projects { get; set; }
        public SkillWorkspaceSettings() { Version=1; StartApps="both"; Projects=new List<SkillProject>(); }
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
            if(value==null || value.Version!=1 || value.Projects==null || value.Projects.Any(p=>p==null || String.IsNullOrEmpty(p.Id) || String.IsNullOrEmpty(p.Folder) || p.Buttons==null || p.Buttons.Any(b=>b==null || String.IsNullOrEmpty(b.Id) || String.IsNullOrEmpty(b.SkillPath) || !IsTarget(b.Target))))
                throw new InvalidOperationException("프로젝트 설정 형식을 확인하세요.");
            if(value.Projects.Select(p=>p.Id).Distinct().Count()!=value.Projects.Count)throw new InvalidOperationException("중복 프로젝트입니다.");
            foreach(var p in value.Projects)if(p.Buttons.Select(b=>b.Id).Distinct().Count()!=p.Buttons.Count)throw new InvalidOperationException("중복 스킬 버튼입니다.");
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
            });
        }
        internal void RemoveButton(string projectId,string buttonId) {
            Change(next=>{var project=next.Projects.SingleOrDefault(p=>p.Id==projectId);if(project==null || project.Buttons.RemoveAll(b=>b.Id==buttonId)!=1)throw new ArgumentException("삭제할 버튼을 찾을 수 없습니다.");});
        }
    }
}

