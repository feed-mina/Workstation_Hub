using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MinaDesk {
    internal sealed class MacroCommands : IDisposable {
        internal static readonly HashSet<string> Supported=new HashSet<string>{"pc_lock","media","volume_mute","screenshot","terminal","vscode","record","youtube_upload","naver_blog_upload"};
        readonly string file;
        readonly Action changed;
        internal Func<string> ProjectFolder;
        internal Func<bool> HasProject;
        readonly Timer timer=new Timer{Interval=2500};
        readonly ObsRecorder obs=new ObsRecorder();
        readonly BrowserAccounts accounts;
        DateTime nextAccountCleanup;
        readonly HashSet<string> busy=new HashSet<string>();
        MacroSettings settings;
        bool disposed,obsBusy;bool? muted,recording;
        internal MacroCommands(string data,Action stateChanged) {
            file=Path.Combine(data,"macros.json");settings=MacroSettings.Load(file);changed=stateChanged;accounts=new BrowserAccounts(data);
            timer.Tick+=async(s,e)=>await Refresh();
        }
        internal object Snapshot(){return new{muted,recording,busy=busy.ToArray(),accounts=new{mediaProvider=accounts.Value.MediaProvider,connections=BrowserAccounts.Keys.Select(key=>new{key,connected=accounts.Connected(key),alias=accounts.Get(key).Alias}).ToArray()}};}
        internal void ConfigureAccounts(Form owner,string selected=null){using(var dialog=new AccountConnectionsDialog(accounts,selected??"media",changed))dialog.ShowDialog(owner);}
        internal void Sampling(bool enabled){if(disposed)return;if(enabled)timer.Start();else timer.Stop();}
        internal void Configure(Form owner) {
            using(var dialog=new MacroSettingsDialog(settings)) {
                if(dialog.ShowDialog(owner)!=DialogResult.OK)return;
                try{dialog.Value.Save(file);settings=dialog.Value;recording=null;changed();}
                catch(Exception ex){MessageBox.Show(owner,"설정을 저장하지 못했습니다. "+ex.Message);}
            }
        }
        internal async Task Refresh() {
            if(disposed)return;
            if(DateTime.UtcNow>=nextAccountCleanup){nextAccountCleanup=DateTime.UtcNow.AddMinutes(1);try{accounts.Cleanup(AccountBrowser.Running);}catch(IOException){}catch(UnauthorizedAccessException){}}
            try{muted=SpeakerVolume.Read();}catch{muted=null;}
            changed();
            if(obsBusy)return;
            obsBusy=true;
            try{recording=await obs.ReadOrToggle(settings,false);}catch{recording=null;}
            finally{obsBusy=false;}
            if(!disposed)changed();
        }
        internal async Task<string> Execute(string id,Form owner) {
            if(!busy.Add(id))return "이미 실행 중입니다.";
            changed();
            try {
                switch(id) {
                    case "pc_lock":WindowsCommands.Lock();return "PC 잠금 · 요청됨";
                    case "media":
                        if(!accounts.Connected("media")){ConfigureAccounts(owner,"media");return accounts.Connected("media")?"재생용 계정 연결 완료 · 재생/정지 버튼을 다시 누르세요.":"재생용 계정 연결이 필요합니다.";}
                        return accounts.Media();
                    case "youtube_upload":case "naver_blog_upload":
                        if(!accounts.Connected(id)){ConfigureAccounts(owner,id);return accounts.Connected(id)?BrowserAccounts.Name(id)+" 계정 연결 완료 · 버튼을 다시 눌러 화면을 여세요.":BrowserAccounts.Name(id)+" 계정 연결이 필요합니다.";}
                        accounts.Open(id,false);return BrowserAccounts.Name(id)+" · 연결한 계정의 "+(id=="youtube_upload"?"업로드":"글쓰기")+" 화면 열기 요청됨";
                    case "volume_mute":muted=SpeakerVolume.Toggle();return "볼륨 음소거 · "+(muted.Value?"켜짐":"꺼짐");
                    case "screenshot":return "스크린샷 · "+WindowsCommands.Screenshot(settings.ScreenshotFolder);
                    case "terminal":string folder=ProjectFolder==null?null:ProjectFolder();ProjectLauncher.Terminal(ProjectLauncher.ToolFolder(folder,settings.WorkingFolder));return folder==null?"터미널 · 기본 작업 폴더에서 Git Bash 또는 PowerShell 열기 요청됨":"터미널 · 현재 프로젝트에서 Git Bash 또는 PowerShell 열기 요청됨";
                    case "vscode":
                        string project=ProjectFolder==null?null:ProjectFolder();
                        if(project!=null){if(!Directory.Exists(project))throw new DirectoryNotFoundException("프로젝트 폴더를 다시 선택하세요.");ProjectLauncher.Code(settings,project);return "VS Code · 현재 프로젝트 열기 요청됨";}
                        if(HasProject!=null && HasProject()){ProjectLauncher.Code(settings,null);return "VS Code · 로컬 폴더가 연결되지 않아 앱만 열기 요청됨";}
                        return OpenCode(owner);
                    case "record":
                        if(obsBusy)return "OBS 상태 확인 중입니다. 잠시 뒤 다시 누르세요.";
                        obsBusy=true;
                        try {
                            recording=await obs.ReadOrToggle(settings,true);
                            return "OBS 녹화 · "+(recording.Value?"녹화 중":"정지됨");
                        }finally{obsBusy=false;}
                    default:throw new ArgumentException("지원하지 않는 명령입니다.");
                }
            }catch(Exception ex) {
                if(id=="record")recording=null;
                if(id=="volume_mute")muted=null;
                if(id=="record")return "OBS 녹화 실패 · OBS를 켜고 매크로 설정의 주소·비밀번호를 확인하세요. 녹화 상태를 다시 확인한 후 재시도하세요.";
                return "실행 실패 · "+ex.Message;
            }finally{busy.Remove(id);if(!disposed)changed();}
        }
        string OpenCode(Form owner) {
            var recent=ProjectLauncher.Recent(settings);
            using(var picker=new Form{Text="VS Code · 프로젝트 선택",Size=new Size(560,320),StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false})
            using(var list=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true})
            using(var actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42}) {
                foreach(string path in recent)list.Items.Add(path);
                if(list.Items.Count>0)list.SelectedIndex=0;
                string selected=null;
                Action accept=()=>{if(list.SelectedItem!=null){selected=(string)list.SelectedItem;picker.DialogResult=DialogResult.OK;}};
                var open=new Button{Text="선택 열기",AutoSize=true};open.Click+=(s,e)=>accept();list.DoubleClick+=(s,e)=>accept();
                var browse=new Button{Text="폴더 선택",AutoSize=true};browse.Click+=(s,e)=>{using(var folder=new FolderBrowserDialog{SelectedPath=settings.WorkingFolder})if(folder.ShowDialog(picker)==DialogResult.OK){selected=folder.SelectedPath;picker.DialogResult=DialogResult.OK;}};
                var workspace=new Button{Text="작업 영역 파일",AutoSize=true};workspace.Click+=(s,e)=>{using(var dialog=new OpenFileDialog{Filter="VS Code 작업 영역|*.code-workspace"})if(dialog.ShowDialog(picker)==DialogResult.OK){selected=dialog.FileName;picker.DialogResult=DialogResult.OK;}};
                var last=new Button{Text="VS Code 열기",AutoSize=true};last.Click+=(s,e)=>picker.DialogResult=DialogResult.OK;
                actions.Controls.AddRange(new Control[]{open,browse,workspace,last});picker.Controls.Add(list);picker.Controls.Add(actions);picker.AcceptButton=open;
                if(picker.ShowDialog(owner)!=DialogResult.OK)return "VS Code · 선택 취소";
                if(selected!=null&&!ProjectLauncher.IsProject(selected))throw new DirectoryNotFoundException("선택한 프로젝트를 찾을 수 없습니다.");
                ProjectLauncher.Code(settings,selected);
                if(selected==null)return "VS Code · 열기 요청됨";
                settings.Projects.RemoveAll(p=>String.Equals(p,selected,StringComparison.OrdinalIgnoreCase));settings.Projects.Insert(0,selected);settings.Projects=settings.Projects.Take(5).ToList();
                settings.WorkingFolder=Directory.Exists(selected)?selected:Path.GetDirectoryName(selected);
                try{settings.Save(file);}catch(IOException){return "VS Code · 열기 요청됨 · 최근 폴더 저장 실패";}
                return "VS Code · 열기 요청됨 · "+selected;
            }
        }
        public void Dispose(){disposed=true;timer.Dispose();}
    }
}

