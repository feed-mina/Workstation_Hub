using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Timer=System.Windows.Forms.Timer;

namespace MinaDesk {
    sealed class Hub : ApplicationContext {
        internal static readonly string[] Keys={"macro","pc","home","log"};
        internal static readonly string[] Titles={"매크로 패드","PC 성능","스마트홈","작업 기록"};
        static readonly HashSet<string> MacroActions=new HashSet<string>{"work_start","work_end","volume_mute","vscode","terminal","record","media","screenshot","focus","light_toggle","ac_toggle","pc_lock","youtube_upload","naver_blog_upload"};
        internal readonly Dictionary<string,DeskWindow> Windows=new Dictionary<string,DeskWindow>();
        internal readonly Dictionary<string,ToolStripMenuItem> Toggles=new Dictionary<string,ToolStripMenuItem>();
        internal readonly Settings Config;
        internal readonly Icon Chick;
        internal readonly NotifyIcon Tray;
        internal readonly string DataDir;
        internal readonly JavaScriptSerializer Json=new JavaScriptSerializer();
        readonly string file; readonly ContextMenuStrip menu;
        readonly Timer save=new Timer{Interval=350}, sample=new Timer{Interval=1500}, instanceTimer=new Timer{Interval=500};
        readonly Telemetry telemetry=new Telemetry();
        readonly MacroCommands commands;
        readonly ProjectSkills skills;
        readonly SmartHome smart;
        readonly WorkHistory history;
        readonly Timer historyTimer=new Timer{Interval=5000};
        readonly Timer projectSyncTimer=new Timer{Interval=60000};
        WorkEntry[] sharedHistory=new WorkEntry[0];
        string publishedId, historyStatus="이 PC", historyError;
        bool historySyncing;
        readonly HashSet<string> active=new HashSet<string>();
        readonly Dictionary<string,object> studio=new Dictionary<string,object>{{"lamp",true},{"brightness",80},{"bar",true},{"kelvin",4500},{"ac",false},{"target",23},{"presence",true},{"door",false}};
        string mode="대기";bool ending;
        System.Threading.Tasks.Task<CoreWebView2Environment> environment;
        internal System.Threading.Tasks.Task<CoreWebView2Environment> EnvironmentTask(){
            if(environment==null || environment.IsFaulted)environment=CoreWebView2Environment.CreateAsync(null,Path.Combine(DataDir,"WebView2"));
            return environment;
        }
        internal Hub(string data,EventWaitHandle activate) {
            CoreWebView2Environment.SetLoaderDllFolderPath(AppDomain.CurrentDomain.BaseDirectory);
            DataDir=data;Directory.CreateDirectory(data);
            file=Path.Combine(data,"windows.json");Config=Settings.Load(file);
            try { history=new WorkHistory(data); }
            catch (Exception ex) { if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)) throw;
                historyError="작업 기록 파일을 열 수 없습니다.";
            }
            commands=new MacroCommands(data,()=>{if(!ending)Broadcast();});
            skills=new ProjectSkills(data,()=>{if(!ending)Broadcast();},AddLog);
            commands.ProjectFolder=()=>skills.ToolFolder;
            commands.HasProject=()=>skills.HasProject;
            smart=new SmartHome(data,()=>{if(!ending)Broadcast();},AddLog);
            Chick=new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"chick.ico"));
            menu=new ContextMenuStrip{ShowCheckMargin=true,ShowImageMargin=false};
            var label=menu.Items.Add("MINA DESK   ·   기능 켜기 / 끄기");label.Enabled=false;
            menu.Items.Add(new ToolStripSeparator());
            for(int i=0;i<Keys.Length;i++){
                string key=Keys[i];var item=new ToolStripMenuItem(Titles[i]){Checked=Config.Panels[key].Enabled,Name=key};
                item.Click+=(s,e)=>Enable(key,!Config.Panels[key].Enabled);menu.Items.Add(item);Toggles[key]=item;
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("켜진 창 모두 복원",null,(s,e)=>Restore(false));
            menu.Items.Add("창 위치 정리",null,(s,e)=>ResetPositions());
            menu.Items.Add("매크로 설정",null,(s,e)=>{DeskWindow owner;if(Windows.TryGetValue("macro",out owner))commands.Configure(owner);else{Enable("macro",true);commands.Configure(Windows["macro"]);}});
            menu.Items.Add("스마트홈 연결",null,(s,e)=>{if(!Config.Panels["home"].Enabled)Enable("home",true);smart.Configure(Windows["home"]);});
            menu.Items.Add("작업실 종료",null,(s,e)=>ExitThread());
            Tray=new NotifyIcon{Icon=Chick,Text="Mini Workstation Hub · 병아리 메뉴",ContextMenuStrip=menu,Visible=true};
            Tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left)menu.Show(Cursor.Position);};
            save.Tick+=(s,e)=>{save.Stop();Save();};
            sample.Tick+=(s,e)=>{DeskWindow win;if(Windows.TryGetValue("pc",out win)&&win.Ready)win.Send(telemetry.Sample());};
            if(activate!=null){instanceTimer.Tick+=(s,e)=>{if(activate.WaitOne(0))Restore(true);};instanceTimer.Start();}
            foreach(var key in Keys)if(Config.Panels[key].Enabled)Open(key);
            AddLog("작업실","앱을 시작했습니다.");
            historyTimer.Tick+=(s,e)=>SyncHistory();historyTimer.Start();SyncHistory();
            projectSyncTimer.Tick+=async(s,e)=>await skills.SyncBackground();projectSyncTimer.Start();
        }
        internal void Remember(string key,Rectangle bounds) {if(ending)return;Config.Panels[key].Remember(bounds);save.Stop();save.Start();}
        void Save(){try{Config.Save(file);}catch(IOException){Tray.Text="Mini Workstation Hub · 창 설정 저장 실패";}catch(UnauthorizedAccessException){Tray.Text="Mini Workstation Hub · 창 설정 저장 권한 확인";}}
        internal void Ready(DeskWindow win) {win.Send(Snapshot());if(win.Key=="log")SendHistory();RefreshSampling();if(win.Key=="macro")RefreshSkills();}
        async void RefreshSkills(){await skills.Refresh();}
        void Open(string key){
            var entry=Config.Panels[key];
            var rect=entry.Width>0 && entry.Height>0?Settings.Fit(entry.Bounds()):Settings.Initial(key);
            var window=new DeskWindow(this,key,Titles[Array.IndexOf(Keys,key)],rect);
            Windows.Add(key,window);window.Show();
        }
        internal void Enable(string key,bool on) {
            if(ending || Config.Panels[key].Enabled==on)return;
            Config.Panels[key].Enabled=on;Toggles[key].Checked=on;
            if(on)Open(key);
            else {DeskWindow win;if(Windows.TryGetValue(key,out win)){
                if(win.WindowState==FormWindowState.Normal)Config.Panels[key].Remember(win.Bounds);
                Windows.Remove(key);win.Dispose();
            }}
            Save();RefreshSampling();AddLog("기능",Titles[Array.IndexOf(Keys,key)]+(on?" 켜짐":" 꺼짐"));Broadcast();
        }
        internal void Restore(bool recoverEmpty) {
            if(Windows.Count==0 && recoverEmpty)Enable("macro",true);
            foreach(var w in Windows.Values){if(w.WindowState==FormWindowState.Minimized)w.WindowState=FormWindowState.Normal;w.Show();w.Activate();}
        }
        void ResetPositions(){foreach(string key in Keys){var r=Settings.Initial(key);Config.Panels[key].Remember(r);DeskWindow w;if(Windows.TryGetValue(key,out w)){w.WindowState=FormWindowState.Normal;w.Bounds=r;}}Save();}
        internal void RefreshSampling(){
            DeskWindow win;
            commands.Sampling(!ending&&Windows.TryGetValue("macro",out win)&&win.Ready&&win.WindowState!=FormWindowState.Minimized);
            bool should=!ending&&Windows.TryGetValue("pc",out win)&&win.Ready&&win.WindowState!=FormWindowState.Minimized;
            if(should==sample.Enabled)return;
            telemetry.Reset();
            if(should){sample.Start();Windows["pc"].Send(telemetry.Sample());}else sample.Stop();
        }
        object Snapshot(){return new {kind="state",version=typeof(Hub).Assembly.GetName().Version.ToString(3),mode,home=studio,smart=smart.Snapshot(),macro=commands.Snapshot(),skills=skills.Snapshot(),active=active.ToArray(),enabled=Config.Panels.ToDictionary(p=>p.Key,p=>p.Value.Enabled)};}
        void Broadcast(){var state=Snapshot();foreach(var w in Windows.Values)w.Send(state);}
        void AddLog(string source,string detail){
            if(history==null)return;
            try { history.Add(source,detail);historyError=null; }
            catch (Exception ex) { if (!(ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)) throw; historyError="이 PC에 기록을 저장하지 못했습니다."; }
            SendHistory();
        }
        void SendHistory(){
            DeskWindow win;
            if(!Windows.TryGetValue("log",out win)||!win.Ready)return;
            var local=history==null?new WorkEntry[0]:history.Snapshot();
            var rows=WorkHistory.Recent(local.Concat(sharedHistory));
            win.Send(new {kind="history",status=historyError??historyStatus,
                items=rows.Select(row=>new {id=row.Id,time=row.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),pc=row.Pc,source=row.Source,detail=row.Detail}).ToArray()});
        }
        async void SyncHistory(){
            string folder=Config.HistoryShare;
            if(ending||historySyncing||history==null||String.IsNullOrEmpty(folder))return;
            historySyncing=true;
            var local=history.Snapshot();
            string previous=publishedId;
            try {
                var remote=await System.Threading.Tasks.Task.Run(()=>history.Sync(folder,local,previous));
                if(ending||folder!=Config.HistoryShare)return;
                sharedHistory=remote;publishedId=local.Select(row=>row.Id).LastOrDefault();
                historyStatus="공유 연결됨";
            } catch (Exception ex) { if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)) throw;
                if(folder==Config.HistoryShare)historyStatus="공유 연결 실패 · 이 PC에는 계속 저장";
            } finally { historySyncing=false;if(!ending)SendHistory(); }
        }
        void ConfigureHistory(DeskWindow owner){
            using(var dialog=new Form{Text="작업 기록 공유",ClientSize=new Size(520,147),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}){
                var hint=new Label{Left=12,Top=12,Width=494,Height=55,
                    Text="두 PC에 같은 네트워크 공유 폴더를 입력하세요. 기록 내용과 PC 이름이 공유됩니다. 비워 두면 이 PC에만 저장합니다."};
                var input=new TextBox{Left=12,Top=68,Width=496,Text=Config.HistoryShare??""};
                var apply=new Button{Text="저장",Left=348,Top=104,DialogResult=DialogResult.OK};
                var cancel=new Button{Text="취소",Left=431,Top=104,DialogResult=DialogResult.Cancel};
                dialog.Controls.AddRange(new Control[]{hint,input,apply,cancel});dialog.AcceptButton=apply;dialog.CancelButton=cancel;
                if(dialog.ShowDialog(owner)!=DialogResult.OK)return;
                var folder=input.Text.Trim();
                Uri address;
                if(folder.Length>0&&(!folder.StartsWith(@"\\")||!Uri.TryCreate(folder,UriKind.Absolute,out address)||!address.IsUnc||address.Segments.Length<2||folder.StartsWith(@"\\?\")||folder.StartsWith(@"\\.\"))){
                    MessageBox.Show(owner,@"Windows 공유 폴더 주소를 입력하세요. 예: \\서버\공유폴더","작업 기록 공유");return;
                }
                if(folder==Config.HistoryShare)return;
                Config.HistoryShare=folder;sharedHistory=new WorkEntry[0];publishedId=null;
                historyStatus=folder.Length==0?"이 PC":"공유 연결 확인 중";
                Save();SendHistory();SyncHistory();
            }
        }
        static string StringValue(Dictionary<string,object> m,string k){object obj;return m.TryGetValue(k,out obj)?obj as string:null;}
        internal async void Receive(DeskWindow sender,string data){
            DeskWindow current;
            if(data.Length>16384||!Windows.TryGetValue(sender.Key,out current)||current!=sender)return;
            Dictionary<string,object> msg;
            try{msg=Json.Deserialize<Dictionary<string,object>>(data);}catch(ArgumentException){return;}catch(InvalidOperationException){return;}
            if(msg==null)return;
            string type=StringValue(msg,"type"),id=StringValue(msg,"id");
            if(id=="mic_mute")id="volume_mute";
            if(type=="ready"){sender.Ready=true;Ready(sender);return;}
            if(type=="skills-close" && sender.Key=="macro") { sender.SkillLibrary(false);return; }
            if(type=="projects-open" && sender.Key=="macro"){sender.SkillLibrary(true);return;}
            if(type=="github-device-page" && sender.Key=="macro"){await skills.Handle(sender,type,msg);return;}
            if(sender.Key=="macro" && (type=="skills-open" || type=="skills-refresh" || type=="skill-project-add" || type=="skill-project-select" || type=="skill-start-apps" || type=="skill-save" || type=="skill-remove" || type=="skill-run" || type=="skill-button-options" || type=="github-login" || type=="github-cancel" || type=="github-refresh" || type=="github-logout" || type=="github-share-connect" || type=="github-sync" || type=="github-register" || type=="project-config" || type=="project-bind" || type=="project-conflict" || type=="project-open-app")) {
                if(type=="skills-open")sender.SkillLibrary(true);
                await skills.Handle(sender,type,msg);if(!ending)sender.Send(new{kind="skills-result",action=type});return;
            }
            if(type=="history-settings"&&sender.Key=="log"){ConfigureHistory(sender);return;}
            if(type=="macro-settings"&&sender.Key=="macro"){commands.Configure(sender);return;}
            if(type=="account-settings"&&sender.Key=="macro"){commands.ConfigureAccounts(sender);return;}
            if(type=="smart-settings"&&sender.Key=="home"){smart.Configure(sender);return;}
            if(type=="smart"&&sender.Key=="home") {
                string result;
                try { result=await smart.Execute(sender,StringValue(msg,"action"),StringValue(msg,"roomId"),StringValue(msg,"itemId")); }
                catch(Exception ex) { result="스마트홈 확인 필요 · "+ex.Message; }
                if(!ending) { AddLog("스마트홈",result);Broadcast();sender.Send(new{kind="notice",text=result}); }
                return;
            }
            string text=null;
            if(type=="macro"&&sender.Key=="macro"&&MacroActions.Contains(id)){
                if(MacroCommands.Supported.Contains(id)) {
                    text=await commands.Execute(id,sender);
                    if(ending)return;
                    AddLog("PC 명령",text);Broadcast();sender.Send(new{kind="notice",text});return;
                }
                bool linked=id=="light_toggle"||id=="ac_toggle";
                if(linked&&!Config.Panels["home"].Enabled){sender.Send(new {kind="notice",text="트레이에서 스마트홈을 먼저 켜세요."});return;}
                if(id=="work_start"||id=="work_end"){
                    bool on=id=="work_start";mode=on?"업무":"퇴근";
                    if(on){text=skills.StartApps();AddLog("매크로","업무 시작 · 선택한 AI 앱 열기 요청");Broadcast();sender.Send(new {kind="notice",text});return;}
                    AddLog("매크로","퇴근 · PC 잠금 요청");
                    text=await commands.Execute("pc_lock",sender);
                    if(!ending){AddLog("PC 명령",text);Broadcast();sender.Send(new {kind="notice",text});}
                    return;
                }else if(id=="light_toggle"){bool on=!((bool)studio["lamp"]||(bool)studio["bar"]);studio["lamp"]=studio["bar"]=on;if(on&&(int)studio["brightness"]==0)studio["brightness"]=65;}
                else if(id=="ac_toggle")studio["ac"]=!(bool)studio["ac"];
                else if(id=="focus"){if(!active.Remove(id))active.Add(id);}
                text=(StringValue(msg,"label")??id);if(text.Length>40)text=text.Substring(0,40);text+=" · 시연";
            }else if(type=="home"&&sender.Key=="home"){
                if(id=="lamp"||id=="bar"||id=="ac"||id=="presence"||id=="door"){studio[id]=!(bool)studio[id];if(id=="lamp"&&(bool)studio[id]&&(int)studio["brightness"]==0)studio["brightness"]=65;}
                else{
                    object val;if(!msg.TryGetValue("value",out val)||!(val is int))return;int n=(int)val;
                    if(id=="brightness"&&n>=0&&n<=100){studio[id]=n;studio["lamp"]=n>0;}
                    else if(id=="kelvin"&&(n==3000||n==4500||n==6500)){studio[id]=n;studio["bar"]=true;}
                    else if(id=="target"&&n>=18&&n<=28){studio[id]=n;studio["ac"]=true;}else return;
                }
                var names=new Dictionary<string,string>{{"lamp","스탠드"},{"bar","라이트바"},{"ac","냉방"},{"presence","재실"},{"door","문 열림"},{"brightness","밝기"},{"kelvin","색온도"},{"target","목표 온도"}};
                var value=studio[id];text=names[id]+" · "+(value is bool?((bool)value?"켜짐":"꺼짐"):value.ToString())+" (시연)";
            }else return;
            AddLog(sender.Key=="macro"?"매크로":"스마트홈",text);Broadcast();sender.Send(new {kind="notice",text});
        }
        internal void ScheduleClose(DeskWindow w){w.BeginInvoke(new Action(()=>{if(!ending&&Windows.ContainsKey(w.Key)&&Windows[w.Key]==w)Enable(w.Key,false);}));}
        protected override void ExitThreadCore(){
            if(!ending){ending=true;projectSyncTimer.Stop();projectSyncTimer.Dispose();historyTimer.Stop();historyTimer.Dispose();save.Stop();sample.Stop();instanceTimer.Stop();Save();Tray.Visible=false;
                foreach(var w in Windows.Values.ToArray())w.Dispose();Windows.Clear();
                skills.Dispose();smart.Dispose();commands.Dispose();Tray.Dispose();menu.Dispose();save.Dispose();sample.Dispose();instanceTimer.Dispose();Chick.Dispose();}
            base.ExitThreadCore();
        }
    }
}

