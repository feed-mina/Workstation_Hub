using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MinaDesk {
    internal sealed class DeveloperRow {
        public string title,detail,pr,branch;
        public string commit;
        public string[] parents;
        internal string Address;
    }
    internal sealed class DeveloperResult {
        public string title,summary,body;
        public bool graph;
        public List<DeveloperRow> rows=new List<DeveloperRow>();
        internal void Add(string title,string detail,string address=null,string pr=null,string branch=null) {
            rows.Add(new DeveloperRow{title=Limit(title,300),detail=Limit(detail,1400),Address=address,pr=pr,branch=branch});
        }
        internal static string Limit(string value,int max) {
            if(String.IsNullOrWhiteSpace(value))return "—";
            return value.Length>max?value.Substring(0,max)+"…":value;
        }
    }
    internal sealed class DeveloperBranches {
        internal string Default;
        internal readonly List<string> Names=new List<string>();
        internal bool Truncated;
    }

    // Each route is a GitHub read against the current repository or a search scoped to it.
    internal sealed class DeveloperGitHub {
        const int PageSize=20;
        const string PerPage="per_page=20";
        readonly IGitHubApi api;
        internal static readonly Dictionary<string,string> Titles=new Dictionary<string,string> {
            {"gh-code","코드"},{"gh-branches","브랜치"},{"gh-commits","커밋"},{"gh-search","코드 검색"},
            {"gh-my-pr","내 PR"},{"gh-reviews","리뷰 요청"},{"gh-pr","PR 상세"},{"gh-compare","브랜치 비교"},
            {"gh-files","변경 파일"},{"gh-checks","PR 체크"},{"gh-copy","PR 링크 복사"},
            {"gh-my-issues","내 이슈"},{"gh-issues","열린 이슈"},{"gh-notifications","알림"},{"gh-mentions","내 멘션"},
            {"gh-readme","README"},{"gh-actions","Actions"},{"gh-running","실행 중"},{"gh-failed","실패 내역"},
            {"gh-artifacts","빌드 산출물"},{"gh-preview","프리뷰 열기"},{"gh-deployments","운영 배포 기록"},{"gh-releases","릴리스"}
        };
        internal DeveloperGitHub(IGitHubApi api){this.api=api;}
        static Dictionary<string,object> Map(object value){return value as Dictionary<string,object>;}
        static object Member(Dictionary<string,object> item,string key) {
            object value;return item!=null && item.TryGetValue(key,out value)?value:null;
        }
        static string Val(Dictionary<string,object> row,string key){return GitHubValues.Text(row,key);}
        static string Num(Dictionary<string,object> item,string key) {
            long n=GitHubValues.Long(item,key);return n>0?n.ToString(CultureInfo.InvariantCulture):null;
        }
        static IEnumerable<Dictionary<string,object>> Rows(object value) {
            var list=value as System.Collections.IList;
            return list==null?Enumerable.Empty<Dictionary<string,object>>():list.Cast<object>().OfType<Dictionary<string,object>>();
        }
        static string Esc(string value){return Uri.EscapeDataString(value);}
        internal static string Route(GitHubRepository repo) {
            if(!GitHubRepository.Valid(repo))throw new InvalidOperationException("GitHub 저장소가 연결된 프로젝트를 선택하세요.");
            return "repos/"+repo.FullName;
        }
        internal async Task<DeveloperResult> Graph(GitHubRepository repo,string branch,CancellationToken token) {
            string path=Route(repo);
            if(!ValidBranch(branch))throw new InvalidOperationException("브랜치 조회가 끝난 뒤 Git 검토를 여세요.");
            var commits=Rows(await Get(path+"/commits?per_page=30&sha="+Esc(branch),token));
            var result=GraphResult(repo.FullName+" · "+branch+" · GitHub");
            foreach(var row in commits) {
                var meta=Map(Member(row,"commit"));
                string sha=Val(row,"sha");
                if(!ValidSha(sha) || meta==null)continue;
                AddCommit(result,sha,Val(meta,"message"),Date(Map(Member(meta,"committer")),"date"),
                    Rows(Member(row,"parents")).Select(p=>Val(p,"sha")),CommitLink(repo,sha));
            }
            return result;
        }
        internal static DeveloperResult GraphResult(string source) {
            return new DeveloperResult{title="Git 검토 · 커밋 그래프",summary=source+" · 최근 최대 30개 커밋",graph=true};
        }
        internal static async Task<DeveloperResult> LocalGraph(string name,string folder,CancellationToken token) {
            string branch=await LocalBranch(folder,token);
            var log=await LocalGit(folder,new[]{"log","--topo-order","-30","-z","--format=%H%x00%P%x00%aI%x00%s","HEAD","--"},token);
            if(log.Code!=0)throw new InvalidOperationException("이 프로젝트의 Git 커밋을 읽을 수 없습니다.");
            var result=GraphResult(name+" · "+branch+" · 이 PC");
            string[] fields=log.Output.Split('\0');
            for(int i=0;i+3<fields.Length;i+=4) {
                DateTimeOffset date;
                string time=DateTimeOffset.TryParse(fields[i+2],CultureInfo.InvariantCulture,DateTimeStyles.None,out date)?date.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"—";
                AddCommit(result,fields[i],fields[i+3],time,fields[i+1].Split(' '));
            }
            return result;
        }
        internal static async Task<string> LocalBranch(string folder,CancellationToken token) {
            var branch=await LocalGit(folder,new[]{"rev-parse","--abbrev-ref","HEAD"},token);
            if(branch.Code!=0)throw new InvalidOperationException("이 프로젝트의 Git 브랜치를 읽을 수 없습니다.");
            return branch.Output.Trim();
        }
        static Task<CommandOutput> LocalGit(string folder,string[] args,CancellationToken token) {
            if(!Directory.Exists(folder))throw new DirectoryNotFoundException("연결한 프로젝트 폴더를 확인하세요.");
            string git=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Git","cmd","git.exe");
            if(!File.Exists(git))throw new InvalidOperationException("로컬 Git 브랜치를 확인하려면 Git for Windows를 설치하세요.");
            return GitHubCommand.Run(git,new[]{"-C",folder}.Concat(args).ToArray(),Path.Combine(Path.GetTempPath(),"hub-git-unused"),null,null,token,15000);
        }
        internal static void AddCommit(DeveloperResult result,string sha,string message,string date,IEnumerable<string> parents,string address=null) {
            if(!ValidSha(sha))return;
            result.Add((message??"").Split('\n')[0].TrimEnd(),date,address);
            var row=result.rows.Last();
            row.commit=sha;
            row.parents=parents.Where(ValidSha).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        internal static bool ValidBranch(string branch) {
            return !String.IsNullOrWhiteSpace(branch) && branch.Length<=255 && !branch.Any(Char.IsControl) &&
                branch!="@" && !branch.EndsWith(".",StringComparison.Ordinal) && !branch.Contains("..") && !branch.Contains("\\") && !branch.Contains("@{") &&
                !branch.Any(c=>" ~^:?*[".Contains(c)) && branch.Split('/').All(part=>part.Length>0 && part[0]!='.' && !part.EndsWith(".lock",StringComparison.Ordinal));
        }
        internal static string Number(string input) {
            int n;
            if(!Int32.TryParse(input,NumberStyles.None,CultureInfo.InvariantCulture,out n) || n<=0)
                throw new ArgumentException("실제 PR 번호를 양의 정수로 입력하세요.");
            return n.ToString(CultureInfo.InvariantCulture);
        }
        internal static bool GitHubUrl(string address) {
            Uri url;
            return address!=null && address.Length<2200 && Uri.TryCreate(address,UriKind.Absolute,out url) &&
                url.Scheme=="https" && url.Host=="github.com" && url.IsDefaultPort && url.UserInfo.Length==0;
        }
        internal static string CompareAddress(GitHubRepository repo,string basis,string head) {
            Route(repo);
            if(!ValidBranch(basis) || !ValidBranch(head) || basis==head)
                throw new InvalidOperationException("서로 다른 기준 브랜치와 작업 브랜치를 선택하세요.");
            return repo.Url+"/compare/"+Esc(basis)+"..."+Esc(head);
        }
        static string CommitLink(GitHubRepository repo,string sha) {
            return ValidSha(sha)?repo.Url+"/commit/"+sha:null;
        }
        static bool ValidSha(string value){return Regex.IsMatch(value??"",@"\A[0-9a-fA-F]{7,64}\z");}
        static string ShortSha(string value){return ValidSha(value)?value.Substring(0,Math.Min(8,value.Length)):"—";}
        static string State(Dictionary<string,object> row) {
            var value=Val(row,"conclusion");
            if(String.IsNullOrEmpty(value))value=Val(row,"status")??Val(row,"state");
            switch(value) {
                case "open": return "열림";
                case "closed": return "닫힘";
                case "success": return "성공";
                case "failure": return "실패";
                case "queued": case "pending": return "대기";
                case "in_progress": return "실행 중";
                case "completed": return "완료";
                case "cancelled": return "취소됨";
                case "skipped": return "건너뜀";
                default: return value??"상태 확인 필요";
            }
        }
        static string Date(Dictionary<string,object> row,string key) {
            DateTimeOffset time;
            return DateTimeOffset.TryParse(Val(row,key),CultureInfo.InvariantCulture,DateTimeStyles.None,out time)?
                time.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"—";
        }
        async Task<object> Get(string path,CancellationToken token) {
            var reply=await api.Send(path,"GET",null,token);
            if(reply.Status==422)throw new InvalidOperationException("GitHub에서 요청을 처리할 수 없습니다. 선택한 브랜치, 검색어 또는 PR 번호를 확인하세요.");
            if(reply.Status==429)throw new InvalidOperationException("GitHub 조회 한도에 도달했습니다. 잠시 후 새로고침하세요.");
            reply.RequireSuccess();
            return LocalData.Decode<object>(reply.Json);
        }
        internal async Task<DeveloperBranches> Branches(GitHubRepository repo,CancellationToken token) {
            string path=Route(repo);
            var info=Map(await Get(path,token));
            string primary=Val(info,"default_branch");
            var result=new DeveloperBranches{Default=ValidBranch(primary)?primary:null};
            for(int page=1;page<=5;page++) {
                var list=Rows(await Get(path+"/branches?per_page=100&page="+page,token)).ToArray();
                result.Names.AddRange(list.Select(row=>Val(row,"name")).Where(ValidBranch));
                if(list.Length<100)break;
                if(page==5)result.Truncated=true;
            }
            if(result.Default!=null && !result.Names.Contains(result.Default) && result.Names.Count>0)result.Names.Insert(0,result.Default);
            return result;
        }
        async Task<Dictionary<string,object>> Pull(string path,string number,CancellationToken token) {
            var row=Map(await Get(path+"/pulls/"+Number(number),token));
            if(row==null || Num(row,"number")==null || Num(row,"number")!=Number(number))throw new InvalidOperationException("이 저장소에서 PR을 확인할 수 없습니다.");
            return row;
        }
        static void Issues(DeveloperResult result,GitHubRepository repo,IEnumerable<Dictionary<string,object>> rows,bool pull) {
            foreach(var row in rows) {
                string number=Num(row,"number");
                if(number==null)continue;
                result.Add("#"+number+" "+Val(row,"title"),State(row)+" · "+Date(row,"updated_at"),repo.Url+(pull?"/pull/":"/issues/")+number,pull?number:null);
            }
        }
        static string Search(string repo,string filters) {
            return "search/issues?q="+Esc("repo:"+repo+" "+filters)+"&sort=updated&"+PerPage;
        }
        internal async Task<DeveloperResult> Read(string action,GitHubRepository repo,string login,string branch,string input,string currentPr,DeveloperBranches cached,string preview,CancellationToken token) {
            string path=Route(repo),web=repo.Url;
            if(!Titles.ContainsKey(action))throw new ArgumentException("지원하지 않는 GitHub 카드입니다.");
            if(action=="gh-pr" || action=="gh-copy" || action=="gh-files" || action=="gh-checks")
                currentPr=Number(String.IsNullOrWhiteSpace(input)?currentPr:input);
            var result=new DeveloperResult{title=Titles[action],summary="현재 저장소 · 최대 "+PageSize+"건 · GitHub 읽기 전용"};
            string byBranch=ValidBranch(branch)?"&branch="+Esc(branch):"";
            string refQuery=ValidBranch(branch)?"ref="+Esc(branch):"";
            switch(action) {
                case "gh-branches":
                    if(cached==null)cached=await Branches(repo,token);
                    result.summary=cached.Truncated?"처음 500개 브랜치 · 기본 "+cached.Default:"브랜치 "+cached.Names.Count+"개";
                    foreach(string name in cached.Names.Take(100))result.Add(name,name==cached.Default?"기본 브랜치":"브랜치",web+"/tree/"+Esc(name),branch:name);
                    if(cached.Names.Count>100)result.summary+=" · 아래에 처음 100개 표시";
                    break;
                case "gh-code":
                    var files=Rows(await Get(path+"/contents"+(refQuery.Length>0?"?"+refQuery:""),token));
                    var root=files.Take(100).ToArray();
                    result.summary="저장소 최상위 · 최대 100개 · "+(branch??"기본 브랜치");
                    foreach(var file in root) {
                        string name=Val(file,"path"),kind=Val(file,"type");
                        if(String.IsNullOrEmpty(name))continue;
                        string url=Val(file,"html_url");
                        result.Add(name,kind=="dir"?"폴더":(kind??"파일"),GitHubUrl(url)?url:null);
                    }
                    break;
                case "gh-commits":
                    string commitBranch=ValidBranch(branch)?"&sha="+Esc(branch):"";
                    foreach(var commit in Rows(await Get(path+"/commits?"+PerPage+commitBranch,token))) {
                        string sha=Val(commit,"sha");
                        var content=Map(Member(commit,"commit"));
                        string message=(Val(content,"message")??"").Split('\n')[0].Trim();
                        result.Add(ShortSha(sha)+" · "+message,Val(Map(Member(content,"author")),"name")+" · "+Date(Map(Member(content,"author")),"date"),CommitLink(repo,sha));
                    }
                    break;
                case "gh-search":
                    if(input==null || input.Trim().Length<2 || input.Length>120 || !Regex.IsMatch(input,@"\A[\p{L}\p{N} _\.\-]+\z"))
                        throw new ArgumentException("코드 검색어는 2~120자의 문자, 숫자, 공백, 점, 하이픈, 밑줄로 입력하세요.");
                    string query="repo:"+repo.FullName+" \""+input.Trim()+"\"";
                    var found=Map(await Get("search/code?q="+Esc(query)+"&"+PerPage,token));
                    result.summary="GitHub 코드 색인 · 기본 브랜치 · 최대 "+PageSize+"건";
                    foreach(var file in Rows(Member(found,"items"))) {
                        var owner=Map(Member(file,"repository"));
                        if(!String.Equals(Val(owner,"full_name"),repo.FullName,StringComparison.OrdinalIgnoreCase))continue;
                        string url=Val(file,"html_url");
                        result.Add(Val(file,"path"),repo.FullName,GitHubUrl(url)?url:null);
                    }
                    break;
                case "gh-my-pr":case "gh-reviews":case "gh-my-issues":case "gh-issues":
                    if(!Regex.IsMatch(login??"",@"\A[A-Za-z0-9-]{1,100}\z"))throw new InvalidOperationException("GitHub 계정을 확인하세요.");
                    string filter=action=="gh-my-pr"?"is:pr author:"+login:action=="gh-reviews"?"is:pr is:open review-requested:"+login:
                        action=="gh-my-issues"?"is:issue assignee:"+login:"is:issue is:open";
                    var search=Map(await Get(Search(repo.FullName,filter),token));
                    result.summary="검색 결과 "+GitHubValues.Long(search,"total_count")+"건 · 최대 "+PageSize+"건 표시";
                    Issues(result,repo,Rows(Member(search,"items")),action=="gh-my-pr" || action=="gh-reviews");
                    break;
                case "gh-pr":case "gh-copy":
                    var pr=await Pull(path,currentPr,token);
                    string prNumber=Num(pr,"number");
                    result.Add("#"+prNumber+" "+Val(pr,"title"),(GitHubValues.Bool(pr,"merged")?"병합됨":State(pr))+" · "+Date(pr,"updated_at"),web+"/pull/"+prNumber,prNumber);
                    if(action=="gh-pr") {
                        result.Add("변경 방향",Val(Map(Member(pr,"base")),"ref")+" ← "+Val(Map(Member(pr,"head")),"ref"));
                        result.Add("변경 파일",GitHubValues.Long(pr,"changed_files")+"개 · +"+GitHubValues.Long(pr,"additions")+" / −"+GitHubValues.Long(pr,"deletions"));
                        string body=Val(pr,"body");if(!String.IsNullOrWhiteSpace(body))result.body=DeveloperResult.Limit(body,14000);
                    }
                    break;
                case "gh-compare":
                    string compare=CompareAddress(repo,input,branch);
                    var diff=Map(await Get(path+"/compare/"+Esc(input)+"..."+Esc(branch),token));
                    result.summary=input+" → "+branch+" · 앞선 커밋 "+GitHubValues.Long(diff,"ahead_by")+"개 / 뒤처진 커밋 "+GitHubValues.Long(diff,"behind_by")+"개";
                    result.Add("GitHub 비교 열기",result.summary,compare);
                    foreach(var change in Rows(Member(diff,"files")).Take(25)) result.Add(Val(change,"filename"),Val(change,"status")+" · +"+GitHubValues.Long(change,"additions")+" / −"+GitHubValues.Long(change,"deletions"));
                    break;
                case "gh-files":
                    foreach(var file in Rows(await Get(path+"/pulls/"+Number(currentPr)+"/files?"+PerPage,token)))
                        result.Add(Val(file,"filename"),Val(file,"status")+" · +"+GitHubValues.Long(file,"additions")+" / −"+GitHubValues.Long(file,"deletions"),web+"/pull/"+Number(currentPr)+"/files");
                    break;
                case "gh-checks":
                    var pull=await Pull(path,currentPr,token);
                    string head=Val(Map(Member(pull,"head")),"sha");
                    if(!ValidSha(head))throw new InvalidOperationException("PR의 커밋 SHA를 확인할 수 없습니다.");
                    var checks=Map(await Get(path+"/commits/"+head+"/check-runs?"+PerPage,token));
                    foreach(var check in Rows(Member(checks,"check_runs")))result.Add(Val(check,"name"),State(check),web+"/pull/"+Number(currentPr)+"/checks");
                    var legacy=Map(await Get(path+"/commits/"+head+"/status?"+PerPage,token));
                    foreach(var status in Rows(Member(legacy,"statuses")))result.Add(Val(status,"context"),State(status),web+"/pull/"+Number(currentPr)+"/checks");
                    result.summary="PR #"+Number(currentPr)+" · "+ShortSha(head)+" · 검사 실행과 커밋 상태";
                    break;
                case "gh-notifications":case "gh-mentions":
                    foreach(var notice in Rows(await Get(path+"/notifications?"+PerPage+"&all=true",token))) {
                        string reason=Val(notice,"reason");
                        if(action=="gh-mentions" && reason!="mention" && reason!="team_mention")continue;
                        var subject=Map(Member(notice,"subject"));
                        string type=Val(subject,"type"),url=Val(subject,"url");
                        string prefix="https://api.github.com/"+path+(type=="PullRequest"?"/pulls/":"/issues/");
                        string target="https://github.com/notifications";
                        if((type=="PullRequest" || type=="Issue") && url!=null && url.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) {
                            int id;
                            if(Int32.TryParse(url.Substring(prefix.Length),out id) && id>0)target=web+(type=="PullRequest"?"/pull/":"/issues/")+id;
                        }
                        result.Add(Val(subject,"title"),(GitHubValues.Bool(notice,"unread")?"읽지 않음":"읽음")+" · "+reason+" · "+Date(notice,"updated_at"),target);
                    }
                    break;
                case "gh-readme":
                    var readme=Map(await Get(path+"/readme"+(refQuery.Length>0?"?"+refQuery:""),token));
                    string encoded=Val(readme,"content");
                    string readmeLink=Val(readme,"html_url");
                    result.Add(Val(readme,"name"),branch??"기본 브랜치",GitHubUrl(readmeLink)?readmeLink:null);
                    if(Val(readme,"encoding")=="base64" && encoded!=null && encoded.Length<=1500000)
                        result.body=DeveloperResult.Limit(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)),30000);
                    else result.summary="GitHub에서 README 전문을 열 수 있습니다.";
                    break;
                case "gh-actions":case "gh-running":case "gh-failed":
                    string statusFilter=action=="gh-running"?"&status=in_progress":action=="gh-failed"?"&status=failure":"";
                    var runs=Map(await Get(path+"/actions/runs?"+PerPage+statusFilter+byBranch,token));
                    foreach(var run in Rows(Member(runs,"workflow_runs"))) {
                        string id=Num(run,"id");
                        result.Add(Val(run,"display_title")??Val(run,"name"),State(run)+" · "+Val(run,"head_branch")+" · "+Date(run,"updated_at"),id!=null?web+"/actions/runs/"+id:null);
                    }
                    break;
                case "gh-artifacts":
                    var artifacts=Map(await Get(path+"/actions/artifacts?"+PerPage,token));
                    foreach(var artifact in Rows(Member(artifacts,"artifacts"))) {
                        string run=Num(Map(Member(artifact,"workflow_run")),"id"),id=Num(artifact,"id");
                        bool expired=GitHubValues.Bool(artifact,"expired");
                        result.Add(Val(artifact,"name"),(expired?"만료됨":"보관 중")+" · "+(GitHubValues.Long(artifact,"size_in_bytes")/1048576.0).ToString("0.00",CultureInfo.InvariantCulture)+" MiB · "+Date(artifact,"created_at"),
                            run!=null && id!=null && !expired?web+"/actions/runs/"+run+"/artifacts/"+id:run!=null?web+"/actions/runs/"+run:web+"/actions");
                    }
                    break;
                case "gh-preview":case "gh-deployments":
                    if(action=="gh-preview" && preview!=null)result.Add("등록한 프리뷰","이 PC의 웹앱 설정",preview);
                    var deployments=Rows(await Get(path+"/deployments?per_page=6"+(ValidBranch(branch)?"&ref="+Esc(branch):""),token)).ToArray();
                    result.summary="최근 배포 최대 6건 · GitHub 배포 기록";
                    foreach(var deployment in deployments) {
                        string id=Num(deployment,"id");
                        if(id==null)continue;
                        var last=Rows(await Get(path+"/deployments/"+id+"/statuses?per_page=1",token)).FirstOrDefault();
                        string environment=Val(deployment,"environment")??"환경 이름 없음";
                        string environmentUrl=Val(last,"environment_url");
                        string address=web+"/deployments";
                        // An external deployment URL only becomes a shortcut when it matches the saved address.
                        if(action=="gh-preview" && preview!=null && preview==environmentUrl)address=preview;
                        result.Add(environment,State(last)+" · "+Val(deployment,"ref")+" · "+Date(deployment,"created_at"),address);
                    }
                    break;
                case "gh-releases":
                    foreach(var release in Rows(await Get(path+"/releases?"+PerPage,token))) {
                        string url=Val(release,"html_url");
                        result.Add(Val(release,"name")??Val(release,"tag_name"),Val(release,"tag_name")+" · "+(GitHubValues.Bool(release,"draft")?"초안":GitHubValues.Bool(release,"prerelease")?"사전 릴리스":"릴리스")+" · "+Date(release,"published_at"),GitHubUrl(url)?url:null);
                    }
                    break;
            }
            if(result.rows.Count==0 && result.body==null)result.summary+=" · 표시할 결과가 없습니다.";
            return result;
        }
    }
}
