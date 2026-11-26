using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace MinaDesk {
    public sealed class ServiceAccount {
        public string ProfileId { get; set; }
        public string Browser { get; set; }
        public string Alias { get; set; }
        public bool Connected { get; set; }
    }
    public sealed class BrowserAccountSettings {
        public int Version { get; set; }
        public string MediaProvider { get; set; }
        public Dictionary<string,ServiceAccount> Accounts { get; set; }
        public List<string> RetiredProfiles { get; set; }
        public BrowserAccountSettings(){Version=1;MediaProvider="youtube";Accounts=new Dictionary<string,ServiceAccount>();RetiredProfiles=new List<string>();}
    }
    internal sealed class BrowserAccounts {
        internal static readonly string[] Keys={"media","youtube_upload","naver_blog_upload"};
        readonly string file,profiles;
        readonly Func<string,bool> browserAllowed;
        internal BrowserAccountSettings Value { get; private set; }
        internal bool ReadOnly { get; private set; }
        internal string Notice { get; private set; }
        internal BrowserAccounts(string data,Func<string,bool> allowed=null) {
            browserAllowed=allowed??AccountBrowser.Allowed;
            file=Path.Combine(data,"browser-accounts.json");profiles=Path.GetFullPath(Path.Combine(data,"AccountProfiles"));
            if(!File.Exists(file)){Value=Validate(new BrowserAccountSettings());return;}
            try{Value=Load(file);}catch(Exception ex){
                if(!StorageError(ex))throw;
                try{Value=Load(file+".bak");File.Copy(file,file+".unreadable-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));Notice="계정 연결 설정을 이전 백업에서 복구했습니다.";}
                catch(Exception backup){if(!StorageError(backup))throw;Value=Validate(new BrowserAccountSettings());ReadOnly=true;Notice="계정 설정을 읽지 못해 기존 파일을 보존했습니다. 저장을 중지했습니다.";}
            }
        }
        static bool StorageError(Exception ex){return ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException;}
        static bool ProfileId(string id){Guid result;return id!=null && id.Length==32 && Guid.TryParseExact(id,"N",out result);}
        static BrowserAccountSettings Validate(BrowserAccountSettings value) {
            if(value==null || value.Version!=1 || value.Accounts==null || value.RetiredProfiles==null || value.RetiredProfiles.Any(id=>!ProfileId(id)) || (value.MediaProvider!="youtube" && value.MediaProvider!="music"))throw new InvalidOperationException("계정 연결 설정 형식을 확인하세요.");
            foreach(var pair in value.Accounts)if(!Keys.Contains(pair.Key) || pair.Value==null || (pair.Value.ProfileId!=null && !ProfileId(pair.Value.ProfileId)) || (pair.Value.Connected && pair.Value.ProfileId==null) || (pair.Value.Alias??"").Length>80)throw new InvalidOperationException("계정 연결 설정 형식을 확인하세요.");
            foreach(string key in Keys)if(!value.Accounts.ContainsKey(key))value.Accounts[key]=new ServiceAccount();
            var active=value.Accounts.Values.Where(a=>a.ProfileId!=null).Select(a=>a.ProfileId).ToArray();
            if(active.Distinct().Count()!=active.Length || value.RetiredProfiles.Intersect(active).Any())throw new InvalidOperationException("서로 다른 버튼의 계정 프로필이 겹칩니다.");return value;
        }
        BrowserAccountSettings Load(string path){
            var value=Validate(LocalData.Read<BrowserAccountSettings>(path,()=>null));
            foreach(string key in Keys){var account=value.Accounts[key];if(account.ProfileId==null)continue;string profile=ProfilePath(account.ProfileId);
                if(File.Exists(Path.Combine(profile,".hub-disconnected")) || (account.Connected && !Directory.Exists(profile))){
                    if(Directory.Exists(profile) && !value.RetiredProfiles.Contains(account.ProfileId))value.RetiredProfiles.Add(account.ProfileId);
                    value.Accounts[key]=new ServiceAccount();
                }
            }
            return value;
        }
        void Change(Action<BrowserAccountSettings> change) {
            if(ReadOnly)throw new InvalidOperationException(Notice);
            var next=LocalData.Decode<BrowserAccountSettings>(LocalData.Encode(Value));change(next);Validate(next);LocalData.Write(file,next);Value=next;
        }
        internal static string Name(string key){return key=="media"?"재생용 YouTube·Music":key=="youtube_upload"?"유튜브 업로드":"네이버 블로그";}
        internal ServiceAccount Get(string key){if(!Keys.Contains(key))throw new ArgumentException("계정 종류를 선택하세요.");return Value.Accounts[key];}
        internal bool Connected(string key){return Get(key).Connected;}
        internal string ProfilePath(string id) {
            if(!ProfileId(id))throw new ArgumentException("프로필 식별자를 확인하세요.");
            string path=Path.GetFullPath(Path.Combine(profiles,id));
            if(!path.StartsWith(profiles+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("프로필 경로를 확인하세요.");return path;
        }
        internal void SetProvider(string provider){if(provider!="youtube" && provider!="music")throw new ArgumentException("YouTube 또는 YouTube Music을 선택하세요.");Change(next=>next.MediaProvider=provider);}
        internal static string Address(string key,string provider) {
            if(key=="media")return provider=="music"?"https://music.youtube.com/":"https://www.youtube.com/";
            if(key=="youtube_upload")return "https://www.youtube.com/upload";
            if(key=="naver_blog_upload")return "https://blog.naver.com/GoBlogWrite.naver";
            throw new ArgumentException("지원하지 않는 서비스입니다.");
        }
        internal void Prepare(string key,string browser) {
            Get(key);if(!browserAllowed(browser))throw new FileNotFoundException("Chrome 또는 Edge를 설치한 뒤 다시 연결하세요.");
            Change(next=>{var account=next.Accounts[key];if(account.ProfileId==null)account.ProfileId=Guid.NewGuid().ToString("N");account.Browser=browser;});
        }
        internal void Confirm(string key,string alias) {
            var account=Get(key);alias=(alias??"").Trim();
            if(account.ProfileId==null || !Directory.Exists(ProfilePath(account.ProfileId)) || !browserAllowed(account.Browser))throw new InvalidOperationException("먼저 전용 브라우저를 열고 로그인하세요.");
            if(alias.Length>80)throw new ArgumentException("계정 별칭은 80자까지 입력하세요.");
            Change(next=>{next.Accounts[key].Connected=true;next.Accounts[key].Alias=alias;});
        }
        internal void Disconnect(string key) {
            var current=Get(key);
            if(ReadOnly)throw new InvalidOperationException(Notice);
            if(current.ProfileId!=null){string path=ProfilePath(current.ProfileId);Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,".hub-disconnected"),"disconnected");}
            Change(next=>{var account=next.Accounts[key];if(account.ProfileId!=null && !next.RetiredProfiles.Contains(account.ProfileId))next.RetiredProfiles.Add(account.ProfileId);next.Accounts[key]=new ServiceAccount();});
        }
        internal void Open(string key,bool allowPending) {
            var account=Get(key);
            if(!allowPending && !account.Connected)throw new InvalidOperationException("계정을 먼저 연결하세요.");
            if(account.ProfileId==null)throw new InvalidOperationException("로그인용 브라우저를 먼저 준비하세요.");
            AccountBrowser.Open(account.Browser,ProfilePath(account.ProfileId),Address(key,Value.MediaProvider));
        }
        internal string Media() {
            var account=Get("media");if(!account.Connected)throw new InvalidOperationException("재생용 계정을 먼저 연결하세요.");
            string profile=ProfilePath(account.ProfileId);
            if(AccountBrowser.TryMedia(profile,Value.MediaProvider))return (Value.MediaProvider=="music"?"YouTube Music":"YouTube")+" · 연결한 재생 창에 재생/정지 요청 전달됨";
            Open("media",false);return "재생용 계정 화면 열기 요청됨 · 브라우저에서 영상·음악을 선택한 뒤 재생/정지 버튼을 다시 누르세요.";
        }
        // Disconnect immediately retires the profile. Never reuse it, even while its browser is still open.
        // Only erase retired browser data after its own browser process has exited. Other profiles are untouched.
        internal int Cleanup(Func<string,bool> isRunning) {
            if(ReadOnly)return Value.RetiredProfiles.Count;var removed=new List<string>();
            if(Directory.Exists(profiles) && (File.GetAttributes(profiles)&FileAttributes.ReparsePoint)!=0)return Value.RetiredProfiles.Count;
            foreach(string id in Value.RetiredProfiles.ToArray()) {
                string path=ProfilePath(id);if(isRunning(path))continue;
                try{if(Directory.Exists(path))DeleteTree(path);removed.Add(id);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
            if(removed.Count>0)Change(next=>next.RetiredProfiles.RemoveAll(removed.Contains));return Value.RetiredProfiles.Count;
        }
        static void DeleteTree(string path) {
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0){Directory.Delete(path);return;}
            foreach(string child in Directory.GetFileSystemEntries(path)) {
                var attributes=File.GetAttributes(child);
                if((attributes&FileAttributes.Directory)!=0)DeleteTree(child);
                else {if((attributes&FileAttributes.ReadOnly)!=0)File.SetAttributes(child,attributes&~FileAttributes.ReadOnly);File.Delete(child);}
            }
            Directory.Delete(path);
        }
    }
    internal static class AccountBrowser {
        [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindow callback,IntPtr parameter);
        delegate bool EnumWindow(IntPtr window,IntPtr parameter);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int maximum);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder text,int maximum);
        [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wparam,IntPtr lparam,uint flags,uint timeout,out IntPtr result);
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CommandLineToArgvW(string command,out int count);
        [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr memory);
        internal static string[] Candidates(){return new[]{
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google","Chrome","Application","chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Google","Chrome","Application","chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Google","Chrome","Application","chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","Edge","Application","msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Microsoft","Edge","Application","msedge.exe")};}
        internal static bool Allowed(string path){return path!=null && Candidates().Any(p=>String.Equals(p,path,StringComparison.OrdinalIgnoreCase)) && File.Exists(path);}
        internal static string Detect(){return Candidates().FirstOrDefault(File.Exists);}
        internal static string Arguments(string profile,string address){return "--user-data-dir="+ProjectLauncher.Quote(profile)+" --no-first-run --no-default-browser-check --new-window "+ProjectLauncher.Quote(address);}
        internal static void Open(string executable,string profile,string address) {
            if(!Allowed(executable))throw new FileNotFoundException("연결한 Chrome·Edge를 찾을 수 없습니다. 계정을 다시 연결하세요.");
            Uri uri;if(!Uri.TryCreate(address,UriKind.Absolute,out uri) || uri.Scheme!="https" || !new[]{"www.youtube.com","music.youtube.com","blog.naver.com"}.Contains(uri.Host))throw new ArgumentException("지원하지 않는 서비스 주소입니다.");
            Directory.CreateDirectory(profile);
            using(var process=Process.Start(new ProcessStartInfo(executable,Arguments(profile,address)){UseShellExecute=false})) {if(process==null)throw new IOException("브라우저를 열지 못했습니다.");}
        }
        internal static bool MatchesProfile(string command,string profile) {
            if(String.IsNullOrEmpty(command))return false;int count;IntPtr memory=CommandLineToArgvW(command,out count);if(memory==IntPtr.Zero)return false;
            try{for(int i=0;i<count;i++){string arg=Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory,i*IntPtr.Size));const string key="--user-data-dir=";if(arg!=null && arg.StartsWith(key,StringComparison.OrdinalIgnoreCase))return String.Equals(Path.GetFullPath(arg.Substring(key.Length)).TrimEnd('\\'),Path.GetFullPath(profile).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);}return false;}finally{LocalFree(memory);}
        }
        static HashSet<uint> ProfileProcesses(string profile) {
            var ids=new HashSet<uint>();
            using(var query=new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='chrome.exe' OR Name='msedge.exe'"))
            using(var results=query.Get())foreach(ManagementObject process in results)using(process){if(MatchesProfile(process["CommandLine"] as string,profile))ids.Add(Convert.ToUInt32(process["ProcessId"]));}
            return ids;
        }
        internal static bool Running(string profile){try{return ProfileProcesses(profile).Count>0;}catch{return true;}}
        internal static bool MediaTitle(string title,string provider){return !String.IsNullOrWhiteSpace(title) && (provider=="music"?title.IndexOf("YouTube Music",StringComparison.OrdinalIgnoreCase)>=0:title.IndexOf("YouTube",StringComparison.OrdinalIgnoreCase)>=0 && title.IndexOf("YouTube Music",StringComparison.OrdinalIgnoreCase)<0);}
        internal static bool TryMedia(string profile,string provider) {
            HashSet<uint> ids;try{ids=ProfileProcesses(profile);}catch{return false;}
            var windows=new List<IntPtr>();
            EnumWindows((window,unused)=>{uint pid;GetWindowThreadProcessId(window,out pid);if(!ids.Contains(pid) || !IsWindowVisible(window))return true;
                var title=new StringBuilder(1024);var kind=new StringBuilder(100);GetWindowText(window,title,title.Capacity);GetClassName(window,kind,kind.Capacity);
                if(kind.ToString()=="Chrome_WidgetWin_1" && MediaTitle(title.ToString(),provider))windows.Add(window);return true;},IntPtr.Zero);
            if(windows.Count!=1)return false;IntPtr result;
            // Target only the connected profile's single matching playback window. No system-wide media key.
            return SendMessageTimeout(windows[0],0x0319,windows[0],new IntPtr(14<<16),2,1000,out result)!=IntPtr.Zero;
        }
    }
}
