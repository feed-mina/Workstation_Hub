using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MinaDesk;

static class DeveloperConsoleTests {
    static int count;
    static void Check(bool condition,string name) {
        if(!condition)throw new Exception(name);
        count++;
    }
    static void Reject(Action action,string name) {
        try{action();}catch(ArgumentException){count++;return;}catch(InvalidOperationException){count++;return;}
        throw new Exception(name);
    }

    internal static int Run(string folder) {
        Directory.CreateDirectory(folder);
        var repo=new GitHubRepository{Id=1,Name="sample",FullName="owner/sample",Url="https://github.com/owner/sample",Private=true};
        var transport=new Fixture();
        var github=new DeveloperGitHub(transport);
        var token=CancellationToken.None;
        var branches=github.Branches(repo,token).GetAwaiter().GetResult();
        Check(branches.Default=="main" && branches.Names.SequenceEqual(new[]{"main","feature/panel"}),"branch names from the selected repository");
        Func<string,string,string,DeveloperResult> read=(id,input,pr)=>github.Read(id,repo,"tester","feature/panel",input,pr,branches,null,token).GetAwaiter().GetResult();
        var pull=read("gh-pr","7",null);
        Check(pull.rows[0].pr=="7" && pull.rows[0].Address==repo.Url+"/pull/7" && pull.body=="User text","first requested PR is resolved");
        Check(read("gh-copy","7","123").rows[0].pr=="7","typed PR overrides previous selection");
        Check(read("gh-files",null,"7").rows.Single().title=="ui/panel.js","files use selected PR");
        Check(read("gh-checks","7",null).rows.Count==2,"checks include check runs and commit statuses");
        Check(read("gh-compare","main",null).summary.Contains("2개"),"compare uses actual selected branches");
        Check(read("gh-code",null,null).rows.Single().Address==repo.Url+"/blob/feature/panel/README.md","paths use selected branch");
        Check(read("gh-search","program",null).rows.Single().title=="Program.cs","code search filters cross-repository matches");
        Check(read("gh-actions",null,null).rows.Single().detail.Contains("성공"),"actions from production-shaped data");
        Check(read("gh-deployments",null,null).rows.Single().detail.Contains("성공"),"deployment includes latest status");
        var graph=github.Graph(repo,"feature/panel",token).GetAwaiter().GetResult();
        Check(graph.graph && graph.summary.Contains("owner/sample · feature/panel"),"graph keeps the selected repository and branch");
        Check(graph.rows.Single().title=="Merge branch" && graph.rows.Single().parents.Length==2 &&
            graph.rows.Single().Address==repo.Url+"/commit/"+graph.rows.Single().commit,"graph keeps both parents and a scoped commit link");
        Check(transport.Routes.All(path=>path.StartsWith("repos/owner/sample",StringComparison.Ordinal)||path.StartsWith("search/code?q=repo%3Aowner%2Fsample",StringComparison.Ordinal)),"every request stays scoped");
        int requests=transport.Routes.Count;
        Reject(()=>read("gh-pr","7/commits",null),"PR number cannot change REST path");
        Reject(()=>read("gh-search","x repo:other/demo",null),"code query cannot introduce qualifiers");
        Reject(()=>read("gh-delete","7",null),"only enumerated read actions");
        Reject(()=>read("gh-compare","feature/panel",null),"same branch cannot compare");
        Reject(()=>github.Graph(repo,"refs/../main",token).GetAwaiter().GetResult(),"invalid branch cannot change the graph request");
        Check(transport.Routes.Count==requests,"validation happens before network");
        Check(!DeveloperGitHub.GitHubUrl("https://github.com.invalid/a") && !DeveloperGitHub.GitHubUrl("https://user@github.com/a"),"GitHub result link allowlist");

        var preferences=new DeveloperPreferenceStore(folder);
        preferences.Change(v=>{v.Prompts["a/prompt-1"]="First project";v.Prompts["b/prompt-1"]="Second project";v.City="서울";});
        preferences.Change(v=>v.Branches["20/1"]="feature/panel");
        Check(new DeveloperPreferenceStore(folder).Value.Prompts["b/prompt-1"]=="Second project","project prompt persistence");
        string prefs=Path.Combine(folder,"developer-console.json");
        File.WriteAllText(prefs,"{ broken");
        var backup=new DeveloperPreferenceStore(folder);
        Check(!backup.ReadOnly && backup.Value.City=="서울" && backup.LoadNotice!=null,"settings recover from backup");
        File.WriteAllText(prefs+".bak","{ broken");
        Check(new DeveloperPreferenceStore(folder).ReadOnly,"damaged preferences are protected");
        Check(DeveloperPreferenceStore.ThreadId("codex://threads/abcdef01")=="abcdef01","Codex technical ID decoded");
        Check(CodexLocal.ModelId("GPT-6.2 Sol")=="gpt-6.2-sol" && CodexLocal.EffortId("High")=="high" &&
            CodexLocal.EffortId("앱 기본값")==null,"Codex run uses protocol model and effort values");
        Reject(()=>DeveloperPreferenceStore.ThreadId("codex://threads/abcdef01?prompt=x"),"pinned chat cannot add launch parameters");
        Reject(()=>DeveloperPreferenceStore.CheckWeb("web-nblog","file:///example"),"a browser shortcut cannot launch files");
        Reject(()=>DeveloperPreferenceStore.CheckWeb("web-nblog","https://user:pass@example.com/"),"a shortcut cannot retain credentials");

        File.WriteAllText(Path.Combine(folder,"package.json"),"{\"packageManager\":\"pnpm@9\",\"scripts\":{\"test\":\"placeholder\",\"build\":\"placeholder\",\"typecheck\":\"placeholder\"}}");
        var plan=DeveloperVerification.Plan("validate-all",folder);
        Check(plan.Steps.Select(step=>step.Display).SequenceEqual(new[]{"pnpm run typecheck","pnpm run test","pnpm run build"}),"verification preview matches ordered commands");
        Reject(()=>DeveloperVerification.Plan("validate-any",folder),"arbitrary execution request rejected");
        Console.WriteLine("PASS · "+count+" developer console checks");
        return count;
    }

    sealed class Fixture : IGitHubApi {
        internal readonly List<string> Routes=new List<string>();
        public Task<GitHubResponse> Send(string path,string method,object body,CancellationToken token) {
            if(method!="GET" || body!=null)throw new Exception("developer GitHub issued a write");
            Routes.Add(path);
            const string route="repos/owner/sample",url="https://github.com/owner/sample";
            const string sha="1234567890abcdef1234567890abcdef12345678";
            object value;
            if(path==route)value=new{default_branch="main"};
            else if(path.StartsWith(route+"/branches?"))value=new[]{new{name="main"},new{name="feature/panel"}};
            else if(path==route+"/pulls/7")value=new {number=7,title="Native panel",state="open",body="User text",head=new {@ref="feature/panel",sha},@base=new {@ref="main"}};
            else if(path.StartsWith(route+"/pulls/7/files?"))value=new[]{new{filename="ui/panel.js",status="added",additions=10}};
            else if(path.StartsWith(route+"/commits/"+sha+"/check-runs?"))value=new{check_runs=new[]{new{name="Build",status="completed",conclusion="success"}}};
            else if(path.StartsWith(route+"/commits/"+sha+"/status?"))value=new{statuses=new[]{new{context="coverage",state="pending"}}};
            else if(path==route+"/compare/main...feature%2Fpanel")value=new{ahead_by=2,behind_by=1};
            else if(path==route+"/contents?ref=feature%2Fpanel")value=new[]{new{path="README.md",type="file",html_url=url+"/blob/feature/panel/README.md"}};
            else if(path==route+"/commits?per_page=30&sha=feature%2Fpanel")value=new[]{new{sha,
                commit=new{message="Merge branch\n\nDetails",committer=new{date="2026-11-28T02:43:06Z"}},
                parents=new[]{new{sha="12abcdef1234567890abcdef1234567890abcdef"},new{sha="34abcdef1234567890abcdef1234567890abcdef"}}}};
            else if(path.StartsWith("search/code?q="))value=new{items=new[]{
                new{path="Program.cs",html_url=url+"/blob/main/Program.cs",repository=new{full_name="owner/sample"}},
                new{path="Foreign.cs",html_url="https://github.com/other/demo/blob/main/Foreign.cs",repository=new{full_name="other/demo"}}
            }};
            else if(path.StartsWith(route+"/actions/runs?") && path.EndsWith("&branch=feature%2Fpanel"))value=new{workflow_runs=new[]{new{id=4,display_title="CI",conclusion="success"}}};
            else if(path.StartsWith(route+"/deployments?"))value=new[]{new{id=8,environment="production",@ref="feature/panel"}};
            else if(path==route+"/deployments/8/statuses?per_page=1")value=new[]{new{state="success"}};
            else throw new Exception("Unexpected route "+path);
            return Task.FromResult(new GitHubResponse{Status=200,Json=LocalData.Encode(value)});
        }
    }
}
