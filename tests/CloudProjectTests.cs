using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MinaDesk;

static class CloudProjectTests {
    static int checks;
    static void Check(bool value,string text){if(!value)throw new Exception(text);checks++;}
    static void Throws(Action action,string text){bool failed=false;try{action();}catch{failed=true;}Check(failed,text);}
    static GitHubRepository Repo(long id,bool hidden){return new GitHubRepository{Id=id,Name="repo-"+id,FullName="tester/repo-"+id,Url="https://github.com/tester/repo-"+id,Private=hidden};}
    internal static int Run(string root) {
        Directory.CreateDirectory(root);string folder=Path.Combine(root,"local-only-path");Directory.CreateDirectory(folder);
        string skillPath=Path.Combine(folder,"SKILL.md");File.WriteAllText(skillPath,"---\nname: fix-guide\n---\nInstructions");
        var skill=new InstalledSkill{Name="fix-guide",Path=skillPath};
        var store=new SkillWorkspaces(Path.Combine(root,"pc-a"));var privateRepo=Repo(1001,true);var publicRepo=Repo(1002,false);
        store.Register(new[]{privateRepo,publicRepo},new HashSet<long>{1001},10);store.Select("github-1001");
        Check(store.Value.Projects.Count==2 && store.Current.Location=="cloud" && store.Current.Folder==null,"public/private registration needs no folder");
        string id=store.Current.Id;store.Register(new[]{privateRepo},new HashSet<long>(),10);Check(store.Value.Projects.Count==2 && store.Current.Id==id,"repository ID deduplicates");
        Throws(()=>store.BindLocal(id,folder,1002),"different repository cannot bind");store.BindLocal(id,folder,1001);
        Check(store.Current.Id==id && store.Current.Location=="cloud","binding preserves ID and does not change location");
        store.Configure(id,"local","code",true,10);store.SaveButton(id,null,skill,"가이드","gpt","공통 요청");string button=store.Current.Buttons[0].Id;
        store.ButtonOptions(id,button,"common","gpt","공통 요청","skills/fix-guide/SKILL.md");
        store.ButtonOptions(id,button,"location","claude","private-device-override",null);
        Check(SkillWorkspaces.Effective(store.Current,store.Current.Buttons[0]).Instruction=="private-device-override","local override applied");
        string exported=LocalData.Encode(ProjectSharing.Export(store.Current,10));
        Check(!exported.Contains(folder) && !exported.Contains("SkillPath") && !exported.Contains("private-device-override") && !exported.Contains("Location"),"explicit shared DTO excludes paths and local exceptions");
        store.Configure(id,"cloud","conversation",true,0);Check(SkillWorkspaces.Effective(store.Current,store.Current.Buttons[0]).Instruction=="공통 요청","offline execution change preserves shared account and isolates override");
        var reload=new SkillWorkspaces(Path.Combine(root,"pc-a"));Check(reload.Current.Repository.Private && reload.Current.Location=="cloud" && reload.Current.TaskKind=="conversation" && reload.Current.Buttons[0].LocalOverride!=null,"v2 persistence");
        var prompt=ProjectExecution.Prompt(reload.Current,reload.Current.Buttons[0]);Check(prompt.Contains(privateRepo.Url) && !prompt.Contains(folder) && !prompt.Contains(skillPath),"cloud prompt has repository reference without local files");
        foreach(string bad in new[]{"../SKILL.md","C:/Users/name/SKILL.md","https://host/SKILL.md","skills\\SKILL.md"})Check(!SkillWorkspaces.ValidReference(bad),"invalid cloud reference "+bad);
        Check(GitHubProjects.ParseOrigin("git@github.com:tester/repo-1001.git")==privateRepo.FullName,"SSH origin");
        Check(GitHubProjects.ParseOrigin("https://github.com/tester/repo-1001.git")==privateRepo.FullName,"HTTPS origin");
        Throws(()=>GitHubProjects.ParseOrigin("https://github.com.evil.test/tester/repo-1001"),"host lookalike blocked");
        var api=new FakeApi();var a=new ProjectSharing(api,store);a.Initialize(10,"tester",true,CancellationToken.None).GetAwaiter().GetResult();
        Check(api.CreatedPrivate && api.Files.ContainsKey("workstation-hub.json"),"explicit initialization creates private marked target");
        Check(a.Sync(10,CancellationToken.None).GetAwaiter().GetResult()==0,"first upload succeeds");
        Check(api.Files.Count==2 && api.Files.ContainsKey("projects/github-1001.json") && !api.Files.ContainsKey("projects/github-1002.json"),"only selected project exported");
        var other=new SkillWorkspaces(Path.Combine(root,"pc-b"));var b=new ProjectSharing(api,other);b.Initialize(10,"tester",false,CancellationToken.None).GetAwaiter().GetResult();b.Sync(10,CancellationToken.None).GetAwaiter().GetResult();
        Check(other.Value.Projects.Count==1 && other.Current.Folder==null && other.Current.Location=="cloud","other PC imports shared project without path");
        Check(other.Current.Buttons[0].SkillPath==null && other.Current.Buttons[0].LocalOverride==null,"import has no PC-local skill path/override");
        // Two edits from the same revision: the later upload must not overwrite the winner.
        store.ButtonOptions(id,button,"common","gpt","PC A new common","skills/fix-guide/SKILL.md");
        other.ButtonOptions(other.Current.Id,button,"common","claude","PC B conflicting common","skills/fix-guide/SKILL.md");
        a.Sync(10,CancellationToken.None).GetAwaiter().GetResult();int writes=api.Writes;
        Check(b.Sync(10,CancellationToken.None).GetAwaiter().GetResult()==1 && other.Current.SyncConflict && api.Writes==writes,"conflict retains both versions and prevents overwrite");
        b.Resolve(other.Current.Id,false,10,CancellationToken.None).GetAwaiter().GetResult();
        Check(other.Current.Buttons[0].Instruction=="PC A new common" && !other.Current.SyncConflict,"explicit remote conflict resolution");
        store.ButtonOptions(id,button,"common","gpt","A pending","skills/fix-guide/SKILL.md");other.ButtonOptions(other.Current.Id,button,"common","gpt","B winner","skills/fix-guide/SKILL.md");
        b.Sync(10,CancellationToken.None).GetAwaiter().GetResult();a.Sync(10,CancellationToken.None).GetAwaiter().GetResult();Check(store.Current.SyncConflict,"second conflict detected");
        a.Resolve(id,true,10,CancellationToken.None).GetAwaiter().GetResult();a.Sync(10,CancellationToken.None).GetAwaiter().GetResult();b.Sync(10,CancellationToken.None).GetAwaiter().GetResult();
        Check(other.Current.Buttons[0].Instruction=="A pending" && String.Equals(store.Current.Folder,Path.GetFullPath(folder),StringComparison.OrdinalIgnoreCase) && store.Current.Buttons[0].LocalOverride!=null,"explicit local resolution preserves local binding and override");
        api.IsPrivate=false;writes=api.Writes;Throws(()=>a.Sync(10,CancellationToken.None).GetAwaiter().GetResult(),"target switched public blocks sync");Check(api.Writes==writes,"no upload to public target");api.IsPrivate=true;
        Throws(()=>a.Sync(99,CancellationToken.None).GetAwaiter().GetResult(),"account switch cannot publish another account settings");
        store.Configure(id,"cloud","code",false,10);a.Sync(10,CancellationToken.None).GetAwaiter().GetResult();b.Sync(10,CancellationToken.None).GetAwaiter().GetResult();
        Check(other.Value.Projects.Count==0 && store.Current.Buttons.Count==1,"unshare tombstone removes imported view while retaining local buttons");
        var pages=new FakeApi{Paginated=true};var repos=GitHubProjects.ReadRepositories(pages,CancellationToken.None).GetAwaiter().GetResult();Check(repos.Count==101 && pages.Pages==2 && repos.Any(r=>r.Private) && repos.Any(r=>!r.Private),"all repository pages and visibility included");
        var publicApi=new FakeApi{Exists=true,IsPrivate=false};var blocked=new ProjectSharing(publicApi,new SkillWorkspaces(Path.Combine(root,"pc-c")));Throws(()=>blocked.Initialize(10,"tester",true,CancellationToken.None).GetAwaiter().GetResult(),"existing public settings repository refused");Check(publicApi.Writes==0,"existing public repository unchanged");
        var unmarked=new FakeApi{Exists=true};var protectedStore=new ProjectSharing(unmarked,new SkillWorkspaces(Path.Combine(root,"pc-d")));Throws(()=>protectedStore.Initialize(10,"tester",true,CancellationToken.None).GetAwaiter().GetResult(),"unmarked existing repository refused");Check(unmarked.Writes==0,"unrelated existing repository unchanged");
        var interruptedApi=new FakeApi{FailMarkerOnce=true};var interruptedStore=new SkillWorkspaces(Path.Combine(root,"pc-e"));var interrupted=new ProjectSharing(interruptedApi,interruptedStore);
        Throws(()=>interrupted.Initialize(10,"tester",true,CancellationToken.None).GetAwaiter().GetResult(),"interrupted marker creation reports failure");
        interrupted.Initialize(10,"tester",true,CancellationToken.None).GetAwaiter().GetResult();Check(interruptedStore.Value.SyncRepositoryId==9000 && interruptedStore.Value.PendingSyncRepositoryId==0,"own pending private repository setup resumes safely");
        // Real v1 data migrates without deleting a project, button, ID or folder.
        string migration=Path.Combine(root,"migration");Directory.CreateDirectory(migration);File.WriteAllText(Path.Combine(migration,"skill-workspaces.json"),LocalData.Encode(new{Version=1,SelectedProjectId="legacy",StartApps="both",Projects=new[]{new{Id="legacy",Name="Legacy",Folder=folder,Buttons=new[]{new{Id="legacy-button",SkillName="fix-guide",SkillPath=skillPath,Label="Legacy skill",Target="gpt",Instruction="old"}}}}}));
        var migrated=new SkillWorkspaces(migration);Check(!migrated.ReadOnly && migrated.Value.Version==2 && migrated.Current.Id=="legacy" && migrated.Current.Buttons[0].Id=="legacy-button","v1 migration retains identity and buttons");migrated.Select("legacy");Check(File.ReadAllText(Path.Combine(migration,"skill-workspaces.json.bak")).Contains("\"Version\":1"),"first migration save preserves v1 backup");
        Console.WriteLine("PASS · "+checks+" GitHub/cloud checks: migration, identity, sharing/privacy, pagination, conflict/unshare and handoff");return checks;
    }
    sealed class FakeApi : IGitHubApi {
        internal bool Exists,IsPrivate=true,CreatedPrivate,Paginated,FailMarkerOnce;
        internal int Writes,Pages,revision;
        internal readonly Dictionary<string,Tuple<string,string>> Files=new Dictionary<string,Tuple<string,string>>();
        object Metadata(){return new{id=9000,name=ProjectSharing.RepositoryName,full_name="tester/"+ProjectSharing.RepositoryName,html_url="https://github.com/tester/"+ProjectSharing.RepositoryName,@private=IsPrivate,owner=new{id=10}};}
        GitHubResponse Reply(int status,object value){return new GitHubResponse{Status=status,Json=LocalData.Encode(value)};}
        public Task<GitHubResponse> Send(string path,string method,object body,CancellationToken stop) {
            if(Paginated && path.StartsWith("user/repos?")) {
                Pages++;bool first=path.EndsWith("page=1");var values=Enumerable.Range(first?1:101,first?100:1).Select(i=>new{id=i,name="repo-"+i,full_name="tester/repo-"+i,html_url="https://github.com/tester/repo-"+i,@private=i%2==0}).ToArray();return Task.FromResult(Reply(200,values));
            }
            if(path=="user/repos" && method=="POST"){var data=LocalData.Decode<Dictionary<string,object>>(LocalData.Encode(body));CreatedPrivate=GitHubValues.Bool(data,"private");Exists=true;return Task.FromResult(Reply(201,Metadata()));}
            if(path=="repos/tester/"+ProjectSharing.RepositoryName || path=="repositories/9000")return Task.FromResult(Reply(Exists?200:404,Metadata()));
            const string prefix="repos/tester/mini-workstation-hub-settings/contents/";
            if(!path.StartsWith(prefix))throw new Exception("Unexpected API: "+path);string file=path.Substring(prefix.Length);
            if(file=="projects")return Task.FromResult(Files.Keys.Any(k=>k.StartsWith("projects/"))?Reply(200,Files.Keys.Where(k=>k.StartsWith("projects/")).Select(k=>new{name=k.Substring(9),type="file"}).ToArray()):Reply(404,null));
            Tuple<string,string> old;Files.TryGetValue(file,out old);
            if(method=="GET")return Task.FromResult(old==null?Reply(404,null):Reply(200,new{sha=old.Item1,encoding="base64",content=old.Item2}));
            var payload=LocalData.Decode<Dictionary<string,object>>(LocalData.Encode(body));string expected=GitHubValues.Text(payload,"sha");
            if(file=="workstation-hub.json" && FailMarkerOnce){FailMarkerOnce=false;return Task.FromResult(Reply(503,null));}
            if((old==null?null:old.Item1)!=expected)return Task.FromResult(Reply(409,null));
            string sha="revision-"+(++revision);Files[file]=Tuple.Create(sha,GitHubValues.Text(payload,"content"));Writes++;return Task.FromResult(Reply(200,new{content=new{sha=sha}}));
        }
    }
}
