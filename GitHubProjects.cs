using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MinaDesk {
    public sealed class GitHubRepository {
        public long Id { get; set; }
        public string Name { get; set; }
        public string FullName { get; set; }
        public string Url { get; set; }
        public bool Private { get; set; }
        internal static bool Valid(GitHubRepository r) {
            return r!=null && r.Id>0 && !String.IsNullOrWhiteSpace(r.Name) && ValidName(r.FullName) && r.Url=="https://github.com/"+r.FullName;
        }
        internal static bool ValidName(string name) { return name!=null && Regex.IsMatch(name,@"\A[A-Za-z0-9-]+/[A-Za-z0-9_.-]+\z"); }
        internal static GitHubRepository From(Dictionary<string,object> item) {
            var r=new GitHubRepository{Id=GitHubValues.Long(item,"id"),Name=GitHubValues.Text(item,"name"),FullName=GitHubValues.Text(item,"full_name"),Url=GitHubValues.Text(item,"html_url"),Private=GitHubValues.Bool(item,"private")};
            if(!Valid(r))throw new InvalidOperationException("GitHub 저장소 응답을 확인할 수 없습니다.");return r;
        }
    }
    internal static class GitHubValues {
        internal static string Text(Dictionary<string,object> o,string k) { object v;return o!=null && o.TryGetValue(k,out v)?v as string:null; }
        internal static long Long(Dictionary<string,object> o,string k) { object v;long n;return o!=null && o.TryGetValue(k,out v) && Int64.TryParse(Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture),out n)?n:0; }
        internal static bool Bool(Dictionary<string,object> o,string k) { object v;return o!=null && o.TryGetValue(k,out v) && v is bool && (bool)v; }
    }
    internal sealed class GitHubResponse {
        internal int Status;
        internal string Json;
        internal void RequireSuccess() {
            if(Status>=200 && Status<300)return;
            if(Status==401)throw new InvalidOperationException("GitHub 로그인이 만료됐습니다. 다시 로그인하세요.");
            if(Status==403)throw new InvalidOperationException("GitHub 권한·조직 SSO 승인 또는 API 사용 한도를 확인하세요.");
            if(Status==404)throw new InvalidOperationException("저장소가 없거나 접근 권한이 없습니다. 조직·Private 저장소 권한을 확인하세요.");
            if(Status==409 || Status==422)throw new InvalidOperationException("다른 PC에서 설정이 변경됐습니다. 다시 동기화해 충돌을 확인하세요.");
            throw new InvalidOperationException("GitHub 연결에 실패했습니다. 네트워크와 로그인을 확인하고 다시 시도하세요.");
        }
    }
    internal interface IGitHubApi { Task<GitHubResponse> Send(string path,string method,object body,CancellationToken stop); }
    internal sealed class CommandOutput { internal int Code;internal string Output,Error; }
    // All arguments are native-owned. No shell, tokens, commands or API URLs come from WebView.
    internal static class GitHubCommand {
        internal static string Quote(string value) { return "\""+Regex.Replace(value??"",@"(\\*)\""","$1$1\\\"")+Regex.Match(value??"",@"\\*$").Value+"\""; }
        internal static Task<CommandOutput> Run(string executable,string[] args,string config,string input,Action<string> line,CancellationToken stop,int timeout) {
            return Task.Run(async ()=>{
                var start=new ProcessStartInfo(executable,String.Join(" ",args.Select(Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                foreach(string key in new[]{"GH_TOKEN","GITHUB_TOKEN","GH_ENTERPRISE_TOKEN","GITHUB_ENTERPRISE_TOKEN","GH_HOST","GH_BROWSER","BROWSER","GH_DEBUG","GH_FORCE_TTY","GH_REPO","GH_HTTP_UNIX_SOCKET"})start.EnvironmentVariables.Remove(key);
                start.EnvironmentVariables["GH_CONFIG_DIR"]=config;start.EnvironmentVariables["GH_PROMPT_DISABLED"]="1";start.EnvironmentVariables["GH_NO_UPDATE_NOTIFIER"]="1";start.EnvironmentVariables["GH_NO_EXTENSION_UPDATE_NOTIFIER"]="1";start.EnvironmentVariables["NO_COLOR"]="1";
                using(var limit=CancellationTokenSource.CreateLinkedTokenSource(stop))using(var process=new Process{StartInfo=start}) {
                    limit.CancelAfter(timeout);var error=new StringBuilder();
                    process.ErrorDataReceived+=(s,e)=>{if(e.Data==null)return;lock(error){if(error.Length<262144)error.AppendLine(e.Data);}if(line!=null)line(e.Data);};
                    process.Start();process.BeginErrorReadLine();
                    using(limit.Token.Register(()=>{try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}})) {
                        var output=process.StandardOutput.ReadToEndAsync();
                        if(input!=null)await process.StandardInput.WriteAsync(input);
                        process.StandardInput.Close();string text=await output;process.WaitForExit();
                        limit.Token.ThrowIfCancellationRequested();
                        if(text.Length>16*1024*1024)throw new InvalidOperationException("GitHub 응답이 너무 큽니다.");
                        return new CommandOutput{Code=process.ExitCode,Output=text,Error=error.ToString()};
                    }
                }
            },stop);
        }
    }
    internal sealed class GitHubProjects : IGitHubApi {
        readonly string config,exe;
        internal long AccountId { get; private set; }
        internal string LoginName { get; private set; }
        internal List<GitHubRepository> Repositories { get; private set; }
        internal bool Available { get { return File.Exists(exe); } }
        internal bool HasSession { get { return File.Exists(Path.Combine(config,"hosts.yml")); } }
        internal GitHubProjects(string data) {
            config=Path.Combine(data,"github-auth");exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tools","github","bin","gh.exe");Repositories=new List<GitHubRepository>();
        }
        void RequireSecureStorage() {
            string hosts=Path.Combine(config,"hosts.yml");
            if(File.Exists(hosts) && Regex.IsMatch(File.ReadAllText(hosts),@"(?m)^\s*(oauth_token|refresh_token):")) {
                File.Delete(hosts);AccountId=0;LoginName=null;
                throw new InvalidOperationException("Windows 자격 증명 저장소에 로그인 정보를 보관하지 못했습니다. 평문 저장은 중지했습니다. Windows 자격 증명 서비스를 확인한 뒤 다시 로그인하세요.");
            }
        }
        internal async Task Login(Action<string> code,CancellationToken stop) {
            if(!Available)throw new FileNotFoundException("GitHub 로그인 구성 요소가 없습니다. 앱을 다시 설치하세요.");
            Directory.CreateDirectory(config);
            int opened=0;
            try {
                var result=await GitHubCommand.Run(exe,new[]{"auth","login","--hostname","github.com","--web","--git-protocol","https","--skip-ssh-key","--clipboard=false"},config,null,line=>{
                    var found=Regex.Match(line,@"\b[A-Z0-9]{4}-[A-Z0-9]{4}\b");
                    if(found.Success){code(found.Value);if(Interlocked.Exchange(ref opened,1)==0)try{OpenDevicePage();}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}}
                },stop,900000);
                RequireSecureStorage();
                if(result.Code!=0)throw new InvalidOperationException("GitHub 로그인이 취소됐거나 완료되지 않았습니다. 다시 시도하세요.");
                await User(stop);
            }finally{RequireSecureStorage();}
        }
        internal static void OpenDevicePage(){using(var page=Process.Start(new ProcessStartInfo("https://github.com/login/device"){UseShellExecute=true})){} }
        internal async Task User(CancellationToken stop) {
            RequireSecureStorage();var response=await Send("user","GET",null,stop);response.RequireSuccess();var user=LocalData.Decode<Dictionary<string,object>>(response.Json);
            long id=GitHubValues.Long(user,"id");string login=GitHubValues.Text(user,"login");
            if(id<=0 || !Regex.IsMatch(login??"",@"\A[A-Za-z0-9-]+\z"))throw new InvalidOperationException("GitHub 계정을 확인하지 못했습니다.");
            if(AccountId!=id)Repositories.Clear();AccountId=id;LoginName=login;
        }
        internal async Task List(CancellationToken stop) {
            if(AccountId<=0)await User(stop);
            Repositories=await ReadRepositories(this,stop);
        }
        internal static async Task<List<GitHubRepository>> ReadRepositories(IGitHubApi api,CancellationToken stop) {
            var found=new Dictionary<long,GitHubRepository>();
            for(int page=1;page<=10000;page++) {
                var response=await api.Send("user/repos?visibility=all&affiliation=owner,collaborator,organization_member&sort=full_name&per_page=100&page="+page,"GET",null,stop);response.RequireSuccess();
                var rows=LocalData.Decode<List<Dictionary<string,object>>>(response.Json);
                if(rows==null)throw new InvalidOperationException("저장소 목록 응답을 확인하세요.");
                foreach(var row in rows){var repo=GitHubRepository.From(row);found[repo.Id]=repo;}
                if(rows.Count<100)return found.Values.OrderBy(x=>x.FullName,StringComparer.OrdinalIgnoreCase).ToList();
            }
            throw new InvalidOperationException("저장소 목록의 페이지 한도를 초과했습니다. 목록을 모두 받지 못해 등록을 중지했습니다.");
        }
        public async Task<GitHubResponse> Send(string path,string method,object body,CancellationToken stop) {
            if(!Available)throw new FileNotFoundException("GitHub 로그인 구성 요소가 없습니다. 앱을 다시 설치하세요.");
            if(!HasSession)return new GitHubResponse{Status=401,Json="{}"};
            if(path.StartsWith("/") || path.Contains(":") || path.Split('?')[0].Split('/').Any(part=>part==".." || part==".") || (method!="GET" && method!="POST" && method!="PUT"))throw new ArgumentException("지원하지 않는 GitHub 요청입니다.");
            RequireSecureStorage();
            var args=new List<string>{"api","--hostname","github.com","--method",method,"-H","Accept: application/vnd.github+json","-H","X-GitHub-Api-Version: 2022-11-28",path};
            if(body!=null){args.Add("--input");args.Add("-");}
            var r=await GitHubCommand.Run(exe,args.ToArray(),config,body==null?null:LocalData.Encode(body),null,stop,45000);
            var match=Regex.Match(r.Error??"",@"HTTP\s+(\d{3})");
            return new GitHubResponse{Status=r.Code==0?200:match.Success?Int32.Parse(match.Groups[1].Value):r.Error!=null && r.Error.IndexOf("auth login",StringComparison.OrdinalIgnoreCase)>=0?401:0,Json=r.Output};
        }
        internal Task Logout(CancellationToken stop) {
            // gh's Windows keyring is also used by the user's standalone CLI. Disconnect only Hub's metadata.
            // Never revoke or remove a credential that may still be used by the standalone GitHub CLI.
            stop.ThrowIfCancellationRequested();string hosts=Path.Combine(config,"hosts.yml");if(File.Exists(hosts))File.Delete(hosts);
            AccountId=0;LoginName=null;Repositories.Clear();return Task.FromResult(0);
        }
        internal static string ParseOrigin(string origin) {
            origin=(origin??"").Trim();
            var m=Regex.Match(origin,@"\A(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)([A-Za-z0-9-]+/[A-Za-z0-9_.-]+?)(?:\.git)?/?\z",RegexOptions.IgnoreCase);
            if(!m.Success)throw new InvalidOperationException("폴더의 origin이 GitHub 저장소가 아닙니다. 같은 저장소를 복제한 폴더를 선택하세요.");return m.Groups[1].Value;
        }
        internal static async Task<string> LocalOrigin(string folder,CancellationToken stop) {
            string git=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Git","cmd","git.exe");
            if(!File.Exists(git))throw new FileNotFoundException("로컬 연결 확인에는 Git for Windows가 필요합니다.");
            var r=await GitHubCommand.Run(git,new[]{"-C",folder,"config","--get","remote.origin.url"},Path.Combine(Path.GetTempPath(),"hub-git-unused"),null,null,stop,15000);
            if(r.Code!=0)throw new InvalidOperationException("이 폴더에 Git origin이 없습니다. 같은 저장소를 복제한 폴더를 선택하세요.");return ParseOrigin(r.Output);
        }
    }
}
