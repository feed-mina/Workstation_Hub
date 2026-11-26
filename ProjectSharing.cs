using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MinaDesk {
    // Explicit transport shape: no local folder, SkillPath, credential, exception, selection or run location.
    public sealed class SharedSkillButton {
        public string Id { get; set; }
        public string Label { get; set; }
        public string SkillName { get; set; }
        public string Reference { get; set; }
        public string Target { get; set; }
        public string Instruction { get; set; }
    }
    public sealed class SharedProjectRecord {
        public int Version { get; set; }
        public long AccountId { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public GitHubRepository Repository { get; set; }
        public bool Shared { get; set; }
        public List<SharedSkillButton> Buttons { get; set; }
    }
    internal sealed class SharedFile { internal string Sha;internal SharedProjectRecord Record; }
    internal sealed class ProjectSharing {
        internal const string RepositoryName="mini-workstation-hub-settings";
        const string Marker="workstation-hub.json";
        readonly IGitHubApi api;
        readonly SkillWorkspaces store;
        long account;
        GitHubRepository target;
        internal ProjectSharing(IGitHubApi client,SkillWorkspaces workspaces) { api=client;store=workspaces; }
        internal static string Key(SkillProject p) {
            if(p.Repository!=null)return "github-"+p.Repository.Id;
            using(var hash=System.Security.Cryptography.SHA256.Create())return "local-"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(p.Id))).Replace("-","").ToLowerInvariant();
        }
        internal static SharedProjectRecord Export(SkillProject p,long accountId) {
            return new SharedProjectRecord{Version=1,AccountId=accountId,Id=p.Id,Name=p.Name,Repository=p.Repository,Shared=p.Shared,
                Buttons=p.Shared?p.Buttons.Select(b=>new SharedSkillButton{Id=b.Id,Label=b.Label,SkillName=b.SkillName,Reference=b.Reference,Target=b.Target,Instruction=b.Instruction}).ToList():new List<SharedSkillButton>()};
        }
        internal static void Validate(SharedProjectRecord r,long accountId) {
            if(r==null || r.Version!=1 || r.AccountId!=accountId || String.IsNullOrEmpty(r.Id) || r.Id.Length>128 || String.IsNullOrWhiteSpace(r.Name) || r.Name.Length>300 || !GitHubRepository.Valid(r.Repository) || r.Buttons==null || r.Buttons.Count>200 || r.Buttons.Any(b=>b==null || String.IsNullOrEmpty(b.Id) || b.Id.Length>128 || String.IsNullOrEmpty(b.Label) || b.Label.Length>40 || String.IsNullOrEmpty(b.SkillName) || b.SkillName.Length>200 || !SkillWorkspaces.IsTarget(b.Target) || (b.Instruction??"").Length>4000 || !SkillWorkspaces.ValidReference(b.Reference)) || r.Buttons.Select(b=>b.Id).Distinct().Count()!=r.Buttons.Count)
                throw new InvalidOperationException("공유 설정 형식을 확인할 수 없어 기존 설정을 보존했습니다.");
        }
        async Task<Dictionary<string,object>> GetObject(string path,CancellationToken stop) {
            var response=await api.Send(path,"GET",null,stop);response.RequireSuccess();return LocalData.Decode<Dictionary<string,object>>(response.Json);
        }
        internal async Task Initialize(long accountId,string login,bool create,CancellationToken stop) {
            if(accountId<=0 || !GitHubRepository.ValidName(login+"/"+RepositoryName))throw new InvalidOperationException("GitHub 로그인이 필요합니다.");
            var response=await api.Send("repos/"+login+"/"+RepositoryName,"GET",null,stop);
            bool created=false;
            if(response.Status==404 && create) {
                response=await api.Send("user/repos","POST",new{name=RepositoryName,description="Mini Workstation Hub private project settings",@private=true,auto_init=true},stop);created=true;
            }
            response.RequireSuccess();var metadata=LocalData.Decode<Dictionary<string,object>>(response.Json);var repo=GitHubRepository.From(metadata);
            object owner;var ownerObject=metadata.TryGetValue("owner",out owner)?owner as Dictionary<string,object>:null;
            if(!repo.Private || GitHubValues.Long(ownerObject,"id")!=accountId)throw new InvalidOperationException("공유 설정은 내 계정의 비공개 전용 저장소에서만 보관합니다.");
            if(created)store.Change(next=>{next.PendingSyncAccountId=accountId;next.PendingSyncRepositoryId=repo.Id;});
            var marker=await api.Send("repos/"+repo.FullName+"/contents/"+Marker,"GET",null,stop);
            if(marker.Status==404 && (created || (store.Value.PendingSyncAccountId==accountId && store.Value.PendingSyncRepositoryId==repo.Id))) {
                var write=await api.Send("repos/"+repo.FullName+"/contents/"+Marker,"PUT",new{message="Initialize private Hub settings",content=Convert.ToBase64String(Encoding.UTF8.GetBytes(LocalData.Encode(new{version=1,application="mini-workstation-hub",accountId=accountId})))},stop);write.RequireSuccess();
            }else {
                marker.RequireSuccess();var content=LocalData.Decode<Dictionary<string,object>>(marker.Json);var identity=LocalData.Decode<Dictionary<string,object>>(DecodeContent(content));
                if(GitHubValues.Text(identity,"application")!="mini-workstation-hub" || GitHubValues.Long(identity,"version")!=1 || GitHubValues.Long(identity,"accountId")!=accountId)throw new InvalidOperationException("동명 저장소가 Hub 전용 설정 저장소가 아닙니다. 기존 저장소는 변경하지 않았습니다.");
            }
            account=accountId;target=repo;
            store.Change(next=>{next.SyncAccountId=accountId;next.SyncRepositoryId=repo.Id;next.PendingSyncAccountId=0;next.PendingSyncRepositoryId=0;});
        }
        async Task VerifyTarget(long accountId,CancellationToken stop) {
            if(store.Value.SyncAccountId!=accountId || store.Value.SyncRepositoryId<=0)throw new InvalidOperationException("먼저 이 계정의 공유 저장소를 연결하세요.");
            var meta=await GetObject("repositories/"+store.Value.SyncRepositoryId,stop);var repo=GitHubRepository.From(meta);object owner;
            if(!repo.Private || !meta.TryGetValue("owner",out owner) || GitHubValues.Long(owner as Dictionary<string,object>,"id")!=accountId)throw new InvalidOperationException("공유 저장소의 비공개 상태·소유자를 확인하세요. 동기화를 중지했습니다.");
            account=accountId;target=repo;
        }
        static string DecodeContent(Dictionary<string,object> content) {
            string encoded=GitHubValues.Text(content,"content");
            if(GitHubValues.Text(content,"encoding")!="base64" || encoded==null || encoded.Length>1400000)throw new InvalidOperationException("공유 파일을 읽을 수 없습니다.");
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        async Task<SharedFile> Read(string key,CancellationToken stop) {
            var r=await api.Send("repos/"+target.FullName+"/contents/projects/"+key+".json","GET",null,stop);
            if(r.Status==404)return null;r.RequireSuccess();var payload=LocalData.Decode<Dictionary<string,object>>(r.Json);
            var record=LocalData.Decode<SharedProjectRecord>(DecodeContent(payload));Validate(record,account);
            var probe=new SkillProject{Id=record.Id,Repository=record.Repository};
            if(Key(probe)!=key)throw new InvalidOperationException("공유 프로젝트 식별자가 일치하지 않습니다.");
            return new SharedFile{Sha=GitHubValues.Text(payload,"sha"),Record=record};
        }
        internal void Import(SharedFile file) {
            var record=file.Record;
            store.Change(next=>{
                var p=next.Projects.FirstOrDefault(x=>Key(x)==Key(new SkillProject{Id=record.Id,Repository=record.Repository}));
                if(p!=null && (p.Shared || p.PendingUnshare) && p.SharedAccountId!=account)return;
                if(p!=null && p.SharedDirty && p.SharedSha!=file.Sha){p.SyncConflict=true;return;}
                if(!record.Shared) {
                    if(p==null)return;
                    if(!p.LocalRegistered && String.IsNullOrEmpty(p.Folder) && p.Buttons.All(b=>b.LocalOverride==null && b.CloudOverride==null))next.Projects.Remove(p);
                    else {p.Shared=false;p.SharedSha=file.Sha;p.SharedDirty=false;p.PendingUnshare=false;p.SyncConflict=false;}
                    return;
                }
                if(p==null){p=new SkillProject{Id=record.Id,Location=record.Repository==null?"local":"cloud",LocalRegistered=false};next.Projects.Add(p);}
                // Link common buttons by name on this PC; retain PC-local paths and exceptions for the same button IDs.
                var old=p.Buttons;p.Name=record.Name;p.Repository=record.Repository;p.Shared=true;p.SharedAccountId=account;p.SharedSha=file.Sha;p.SharedDirty=false;p.PendingUnshare=false;p.SyncConflict=false;
                p.Buttons=record.Buttons.Select(b=>{
                    var local=old.FirstOrDefault(x=>x.Id==b.Id);
                    return new SkillButton{Id=b.Id,Label=b.Label,SkillName=b.SkillName,Reference=b.Reference,Target=b.Target,Instruction=b.Instruction,SkillPath=local==null?null:local.SkillPath,LocalOverride=local==null?null:local.LocalOverride,CloudOverride=local==null?null:local.CloudOverride};
                }).ToList();
                if(!next.Projects.Any(x=>x.Id==next.SelectedProjectId))next.SelectedProjectId=p.Id;
            });
        }
        internal async Task<int> Sync(long accountId,CancellationToken stop) {
            await VerifyTarget(accountId,stop);int conflicts=0;
            var local=store.Value.Projects.Where(p=>(p.Shared || p.PendingUnshare) && p.SharedAccountId==account).Select(p=>p.Id).ToArray();
            foreach(var id in local) {
                stop.ThrowIfCancellationRequested();var p=store.Value.Projects.FirstOrDefault(x=>x.Id==id);if(p==null)continue;
                var remote=await Read(Key(p),stop);
                if(p.SharedDirty) {
                    if((remote==null?null:remote.Sha)!=p.SharedSha) {store.Change(next=>next.Projects.Single(x=>x.Id==id).SyncConflict=true);conflicts++;continue;}
                    var record=Export(p,account);Validate(record,account);var bytes=Encoding.UTF8.GetBytes(LocalData.Encode(record));
                    if(bytes.Length>700000)throw new InvalidOperationException("공유 설정이 너무 큽니다. 버튼 요청문을 줄이세요.");
                    var body=new Dictionary<string,object>{{"message",p.Shared?"Update shared Hub project":"Stop sharing Hub project"},{"content",Convert.ToBase64String(bytes)}};
                    if(remote!=null)body["sha"]=remote.Sha;
                    var written=await api.Send("repos/"+target.FullName+"/contents/projects/"+Key(p)+".json","PUT",body,stop);
                    if(written.Status==409 || written.Status==422){store.Change(next=>next.Projects.Single(x=>x.Id==id).SyncConflict=true);conflicts++;continue;}
                    written.RequireSuccess();var response=LocalData.Decode<Dictionary<string,object>>(written.Json);object content;
                    if(!response.TryGetValue("content",out content))throw new InvalidOperationException("공유 저장 결과를 확인하지 못했습니다. 다시 동기화하세요.");
                    string sha=GitHubValues.Text(content as Dictionary<string,object>,"sha");if(String.IsNullOrEmpty(sha))throw new InvalidOperationException("공유 버전을 확인하지 못했습니다.");
                    store.Change(next=>{var saved=next.Projects.Single(x=>x.Id==id);saved.SharedSha=sha;saved.SharedDirty=false;saved.PendingUnshare=false;saved.SyncConflict=false;});
                }else if(remote!=null && remote.Sha!=p.SharedSha)Import(remote);
            }
            var list=await api.Send("repos/"+target.FullName+"/contents/projects","GET",null,stop);
            if(list.Status==404)return conflicts;list.RequireSuccess();var files=LocalData.Decode<List<Dictionary<string,object>>>(list.Json);
            if(files==null || files.Count>=1000)throw new InvalidOperationException("공유 프로젝트 목록이 너무 크거나 올바르지 않습니다.");
            foreach(var entry in files) {
                string name=GitHubValues.Text(entry,"name");
                if(GitHubValues.Text(entry,"type")!="file" || !System.Text.RegularExpressions.Regex.IsMatch(name??"",@"\A(?:github-\d+|local-[a-f0-9]{64})\.json\z"))continue;
                string key=name.Substring(0,name.Length-5);
                if(store.Value.Projects.Any(p=>Key(p)==key))continue;
                var remote=await Read(key,stop);if(remote!=null && remote.Record.Shared)Import(remote);
            }
            return conflicts;
        }
        internal async Task Resolve(string id,bool useLocal,long accountId,CancellationToken stop) {
            await VerifyTarget(accountId,stop);var p=store.Value.Projects.Single(x=>x.Id==id);
            if(!p.SyncConflict || p.SharedAccountId!=accountId)throw new InvalidOperationException("해결할 공유 충돌이 없습니다.");
            var remote=await Read(Key(p),stop);
            if(useLocal)store.Change(next=>{var item=next.Projects.Single(x=>x.Id==id);item.SharedSha=remote==null?null:remote.Sha;item.SharedDirty=true;item.SyncConflict=false;});
            else {
                if(remote==null)throw new InvalidOperationException("원격 설정이 없습니다. 이 PC 설정을 선택하세요.");
                // Keep an explicit local backup before a user-requested replacement.
                store.Change(next=>{var item=next.Projects.Single(x=>x.Id==id);item.SharedDirty=false;});Import(remote);
            }
        }
    }
}
