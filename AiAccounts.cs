using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MinaDesk {
    internal enum AiLoginStatus { Unknown, MissingCli, SignedOut, Account, OtherAuth }

    internal sealed class AiAccounts : IDisposable {
        internal const string CodexGuide="https://learn.chatgpt.com/docs/auth";
        internal const string ClaudeGuide="https://code.claude.com/docs/en/authentication";
        static readonly string[] Providers={"gpt","claude"};
        readonly DeveloperPreferenceStore store;
        readonly string workingDirectory;
        readonly CancellationTokenSource stop=new CancellationTokenSource();
        readonly Dictionary<string,AiLoginStatus> statuses=new Dictionary<string,AiLoginStatus>();
        readonly Dictionary<string,string> messages=new Dictionary<string,string>();
        readonly HashSet<string> checking=new HashSet<string>(),loggingIn=new HashSet<string>();
        bool disposed;

        internal AiAccounts(DeveloperPreferenceStore store,string workingDirectory) {
            this.store=store;this.workingDirectory=workingDirectory;
        }
        bool Linked(string provider){
            bool linked;return store.Value.AiLinks.TryGetValue(provider,out linked) && linked;
        }
        AiLoginStatus Status(string provider){
            AiLoginStatus value;return statuses.TryGetValue(provider,out value)?value:AiLoginStatus.Unknown;
        }
        static string StatusText(AiLoginStatus status) {
            switch(status){
                case AiLoginStatus.Account:return "계정 로그인 확인";
                case AiLoginStatus.SignedOut:return "로그인 필요";
                case AiLoginStatus.OtherAuth:return "API 키 또는 별도 공급자 사용 중";
                case AiLoginStatus.MissingCli:return "CLI 설치 확인 필요";
                default:return "로그인 확인 대기";
            }
        }
        internal object Snapshot(){
            return new {kind="ai-account-state",readOnly=store.ReadOnly,providers=Providers.Select(provider=>{
                var status=Status(provider);
                string text;
                messages.TryGetValue(provider,out text);
                bool linked=Linked(provider);
                return new {id=provider,title=provider=="gpt"?"GPT · Codex":"Claude Code",status=status.ToString(),statusText=StatusText(status),
                    linked,connected=linked && status==AiLoginStatus.Account,busy=checking.Contains(provider),login= loggingIn.Contains(provider),message=text};
            }).ToArray()};
        }
        internal async Task Handle(string action,string provider,Action changed) {
            if(disposed)return;
            if(action=="refresh"){
                await Task.WhenAll(Providers.Select(key=>Refresh(key,changed)));
                return;
            }
            if(!Providers.Contains(provider))return;
            if(action=="desktop"){ProjectExecution.OpenRoot(provider,"code");return;}
            if(action=="guide"){Open(provider=="gpt"?CodexGuide:ClaudeGuide);return;}
            if(action=="setup"){Open(provider=="gpt"?"https://developers.openai.com/codex/cli":"https://code.claude.com/docs/en/setup");return;}
            if(action=="unlink"){
                store.Change(next=>next.AiLinks.Remove(provider));
                messages[provider]="허브 연동을 해제했습니다. 공식 앱과 CLI의 로그인은 유지됩니다.";
                changed();return;
            }
            if(action!="link" || checking.Contains(provider) || loggingIn.Contains(provider) || store.ReadOnly)return;
            await Refresh(provider,changed);
            if(disposed)return;
            var status=Status(provider);
            if(status==AiLoginStatus.Account){SaveLink(provider,changed);return;}
            if(status==AiLoginStatus.MissingCli){
                ProjectExecution.OpenRoot(provider,"code");
                messages[provider]="데스크톱 앱에서 로그인하세요. 허브에서 로그인 상태를 확인하려면 공식 CLI가 필요합니다.";
                changed();return;
            }
            await Login(provider,changed);
        }
        void SaveLink(string provider,Action changed) {
            store.Change(next=>next.AiLinks[provider]=true);
            messages[provider]="이 Windows 사용자의 CLI 계정을 허브에 연결했습니다.";
            changed();
        }
        async Task Refresh(string provider,Action changed) {
            if(disposed || !checking.Add(provider))return;
            changed();
            try {
                statuses[provider]=await Task.Run(()=>CheckStatus(provider),stop.Token);
            }catch(OperationCanceledException){}
            finally {checking.Remove(provider);if(!disposed)changed();}
        }
        AiLoginStatus CheckStatus(string provider) {
            string executable=AiCli.Find(provider);
            if(executable==null)return AiLoginStatus.MissingCli;
            var info=new ProcessStartInfo(executable,provider=="gpt"?"login status":"auth status"){
                UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=workingDirectory,
                RedirectStandardError=true,RedirectStandardOutput=true,
                StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
            };
            try {
                using(var proc=Process.Start(info)){
                    if(proc==null)return AiLoginStatus.Unknown;
                    var stdout=proc.StandardOutput.ReadToEndAsync();
                    var stderr=proc.StandardError.ReadToEndAsync();
                    if(!proc.WaitForExit(6500)){
                        try{proc.Kill();}catch(InvalidOperationException){}catch(Win32Exception){}
                        return AiLoginStatus.Unknown;
                    }
                    if(!Task.WaitAll(new[]{stdout,stderr},1000))return AiLoginStatus.Unknown;
                    return Interpret(provider,proc.ExitCode,stdout.Result,stderr.Result);
                }
            }catch(Win32Exception){return AiLoginStatus.Unknown;}
            catch(InvalidOperationException){return AiLoginStatus.Unknown;}
            catch(AggregateException){return AiLoginStatus.Unknown;}
        }
        internal static AiLoginStatus Interpret(string provider,int exit,string output,string error) {
            output=output??"";error=error??"";
            if(output.Length>24000 || error.Length>24000)return AiLoginStatus.Unknown;
            if(provider=="gpt"){
                string message=output+"\n"+error;
                if(exit==0 && message.IndexOf("Logged in using ChatGPT",StringComparison.OrdinalIgnoreCase)>=0)return AiLoginStatus.Account;
                if(exit==0 && message.IndexOf("Logged in",StringComparison.OrdinalIgnoreCase)>=0)return AiLoginStatus.OtherAuth;
                if(exit==1 && message.IndexOf("Not logged in",StringComparison.OrdinalIgnoreCase)>=0)return AiLoginStatus.SignedOut;
                return AiLoginStatus.Unknown;
            }
            if(provider!="claude" || (exit!=0 && exit!=1))return AiLoginStatus.Unknown;
            try {
                var report=LocalData.Decode<Dictionary<string,object>>(output);
                object logged;
                if(report==null || !report.TryGetValue("loggedIn",out logged) || !(logged is bool))return AiLoginStatus.Unknown;
                if(!(bool)logged)return AiLoginStatus.SignedOut;
                if(exit!=0)return AiLoginStatus.Unknown;
                string method=GitHubValues.Text(report,"authMethod");
                return method=="claude.ai"?AiLoginStatus.Account:
                    String.IsNullOrEmpty(method) || method=="none"?AiLoginStatus.Unknown:AiLoginStatus.OtherAuth;
            }catch(ArgumentException){return AiLoginStatus.Unknown;}
            catch(InvalidOperationException){return AiLoginStatus.Unknown;}
        }
        async Task Login(string provider,Action changed) {
            string exe=AiCli.Find(provider);
            if(exe==null)return;
            loggingIn.Add(provider);
            messages[provider]="공식 로그인 창에서 계정을 선택해 주세요. 완료 후 허브에서 상태를 확인합니다.";
            changed();
            try {
                using(var proc=Process.Start(new ProcessStartInfo(exe,provider=="gpt"?"login":"auth login"){
                    UseShellExecute=true,WorkingDirectory=workingDirectory,WindowStyle=ProcessWindowStyle.Normal
                })){
                    if(proc==null)throw new InvalidOperationException("공식 로그인 프로그램을 시작할 수 없습니다.");
                    await Task.Run(()=>{while(!stop.IsCancellationRequested && !proc.WaitForExit(1000)){}},stop.Token);
                }
                if(disposed)return;
                await Refresh(provider,changed);
                if(disposed)return;
                if(Status(provider)==AiLoginStatus.Account)SaveLink(provider,changed);
                else messages[provider]="공식 로그인 완료가 확인되지 않았습니다. 로그인 창을 확인한 뒤 상태를 새로고침하세요.";
            }catch(OperationCanceledException){}
            finally {loggingIn.Remove(provider);if(!disposed)changed();}
        }
        static void Open(string address){
            using(var opened=Process.Start(new ProcessStartInfo(address){UseShellExecute=true})){}
        }
        public void Dispose(){disposed=true;stop.Cancel();}
    }

    internal static class AiCli {
        const string CodexFamily="OpenAI.Codex_2p2nqsd0c76g0";
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
        static extern int GetPackagesByPackageFamily(string family,ref uint count,IntPtr names,ref uint size,IntPtr buffer);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
        static extern int GetPackagePathByFullName(string name,ref uint size,StringBuilder path);

        internal static string Find(string provider) {
            if(provider!="gpt" && provider!="claude")return null;
            if(provider=="gpt"){
                string installed=PackagedCodex();
                if(installed!=null)return installed;
            }
            string executable=provider=="gpt"?"codex.exe":"claude.exe";
            string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var folders=new List<string>{
                Path.Combine(home,".local","bin"),Path.Combine(local,"Microsoft","WinGet","Links")
            };
            if(provider=="claude"){
                const string package="Anthropic.ClaudeCode_Microsoft.Winget.Source_8wekyb3d8bbwe";
                folders.Add(Path.Combine(local,"Microsoft","WinGet","Packages",package));
                folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WinGet","Packages",package));
            }
            folders.AddRange((Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator));
            foreach(string item in folders){
                var folder=item.Trim().Trim('"');
                if(folder.Length<3 || !Char.IsLetter(folder[0]) || folder[1]!=':' || (folder[2]!='\\' && folder[2]!='/'))continue;
                try{
                    string full=Path.GetFullPath(Path.Combine(folder,executable));
                    if(File.Exists(full))return full;
                }catch(ArgumentException){}catch(NotSupportedException){}catch(PathTooLongException){}
            }
            return null;
        }
        static string PackagedCodex(){
            const int more=122;
            uint count=0,size=0;
            if(GetPackagesByPackageFamily(CodexFamily,ref count,IntPtr.Zero,ref size,IntPtr.Zero)!=more ||
                count==0 || count>64 || size>32768)return null;
            IntPtr names=Marshal.AllocHGlobal((int)count*IntPtr.Size);
            IntPtr text=Marshal.AllocHGlobal((int)size*2);
            try{
                if(GetPackagesByPackageFamily(CodexFamily,ref count,names,ref size,text)!=0)return null;
                var candidates=new List<KeyValuePair<Version,string>>();
                for(int i=0;i<count;i++){
                    string package=Marshal.PtrToStringUni(Marshal.ReadIntPtr(names,i*IntPtr.Size));
                    var fields=package==null?new string[0]:package.Split('_');
                    Version version;
                    if(fields.Length<3 || fields[2]!="x64" || !Version.TryParse(fields[1],out version))continue;
                    uint length=0;
                    if(GetPackagePathByFullName(package,ref length,null)!=more || length>32768 || length==0)continue;
                    var path=new StringBuilder((int)length);
                    if(GetPackagePathByFullName(package,ref length,path)!=0)continue;
                    string cli=Path.Combine(path.ToString(),"app","resources","codex.exe");
                    if(File.Exists(cli))candidates.Add(new KeyValuePair<Version,string>(version,cli));
                }
                return candidates.OrderByDescending(item=>item.Key).Select(item=>item.Value).FirstOrDefault();
            }finally{Marshal.FreeHGlobal(text);Marshal.FreeHGlobal(names);}
        }
    }
}
