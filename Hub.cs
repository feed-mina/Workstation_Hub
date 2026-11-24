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
        static readonly HashSet<string> MacroActions=new HashSet<string>{"work_start","work_end","mic_mute","vscode","terminal","record","media","screenshot","focus","light_toggle","ac_toggle","pc_lock"};
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
        readonly Queue<object> logs=new Queue<object>();
        readonly HashSet<string> active=new HashSet<string>();
        readonly Dictionary<string,object> studio=new Dictionary<string,object>{{"lamp",true},{"brightness",80},{"bar",true},{"kelvin",4500},{"ac",false},{"target",23},{"presence",true},{"door",false}};
        string mode="대기";long sequence;bool ending;
        System.Threading.Tasks.Task<CoreWebView2Environment> environment;
        internal System.Threading.Tasks.Task<CoreWebView2Environment> EnvironmentTask(){
            if(environment==null || environment.IsFaulted)environment=CoreWebView2Environment.CreateAsync(null,Path.Combine(DataDir,"WebView2"));
            return environment;
        }
        internal Hub(string data,EventWaitHandle activate) {
            CoreWebView2Environment.SetLoaderDllFolderPath(AppDomain.CurrentDomain.BaseDirectory);
            DataDir=data;Directory.CreateDirectory(data);
            file=Path.Combine(data,"windows.json");Config=Settings.Load(file);
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
            menu.Items.Add("작업실 종료",null,(s,e)=>ExitThread());
            Tray=new NotifyIcon{Icon=Chick,Text="Mini Workstation Hub · 병아리 메뉴",ContextMenuStrip=menu,Visible=true};
            Tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left)menu.Show(Cursor.Position);};
            save.Tick+=(s,e)=>{save.Stop();Save();};
            sample.Tick+=(s,e)=>{DeskWindow win;if(Windows.TryGetValue("pc",out win)&&win.Ready)win.Send(telemetry.Sample());};
            if(activate!=null){instanceTimer.Tick+=(s,e)=>{if(activate.WaitOne(0))Restore(true);};instanceTimer.Start();}
            foreach(var key in Keys)if(Config.Panels[key].Enabled)Open(key);
            AddLog("작업실","앱을 시작했습니다.");
        }
        internal void Remember(string key,Rectangle bounds) {if(ending)return;Config.Panels[key].Remember(bounds);save.Stop();save.Start();}
        void Save(){try{Config.Save(file);}catch(IOException){Tray.Text="Mini Workstation Hub · 창 설정 저장 실패";}catch(UnauthorizedAccessException){Tray.Text="Mini Workstation Hub · 창 설정 저장 권한 확인";}}
        internal void Ready(DeskWindow win) {win.Send(Snapshot());if(win.Key=="log")win.Send(new {kind="history",items=logs.ToArray()});RefreshSampling();}
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
            if(key=="log"&&!on)logs.Clear();
            Save();RefreshSampling();AddLog("기능",Titles[Array.IndexOf(Keys,key)]+(on?" 켜짐":" 꺼짐"));Broadcast();
        }
        internal void Restore(bool recoverEmpty) {
            if(Windows.Count==0 && recoverEmpty)Enable("macro",true);
            foreach(var w in Windows.Values){if(w.WindowState==FormWindowState.Minimized)w.WindowState=FormWindowState.Normal;w.Show();w.Activate();}
        }
        void ResetPositions(){foreach(string key in Keys){var r=Settings.Initial(key);Config.Panels[key].Remember(r);DeskWindow w;if(Windows.TryGetValue(key,out w)){w.WindowState=FormWindowState.Normal;w.Bounds=r;}}Save();}
        internal void RefreshSampling(){
            DeskWindow win;
            bool should=!ending&&Windows.TryGetValue("pc",out win)&&win.Ready&&win.WindowState!=FormWindowState.Minimized;
            if(should==sample.Enabled)return;
            telemetry.Reset();
            if(should){sample.Start();Windows["pc"].Send(telemetry.Sample());}else sample.Stop();
        }
        object Snapshot(){return new {kind="state",mode,home=studio,active=active.ToArray(),enabled=Config.Panels.ToDictionary(p=>p.Key,p=>p.Value.Enabled)};}
        void Broadcast(){var state=Snapshot();foreach(var w in Windows.Values)w.Send(state);}
        void AddLog(string source,string detail){
            if(!Config.Panels["log"].Enabled)return;
            var entry=new {id=++sequence,time=DateTime.Now.ToString("HH:mm:ss"),source,detail};
            logs.Enqueue(entry);while(logs.Count>60)logs.Dequeue();
            DeskWindow w;if(Windows.TryGetValue("log",out w))w.Send(new {kind="entry",item=entry});
        }
        static string StringValue(Dictionary<string,object> m,string k){object obj;return m.TryGetValue(k,out obj)?obj as string:null;}
        internal void Receive(DeskWindow sender,string data){
            DeskWindow current;
            if(data.Length>4096||!Windows.TryGetValue(sender.Key,out current)||current!=sender)return;
            Dictionary<string,object> msg;
            try{msg=Json.Deserialize<Dictionary<string,object>>(data);}catch(ArgumentException){return;}catch(InvalidOperationException){return;}
            if(msg==null)return;
            string type=StringValue(msg,"type"),id=StringValue(msg,"id");
            if(type=="ready"){sender.Ready=true;Ready(sender);return;}
            if(type=="clear"&&sender.Key=="log"){logs.Clear();sender.Send(new {kind="history",items=logs.ToArray()});return;}
            string text=null;
            if(type=="macro"&&sender.Key=="macro"&&MacroActions.Contains(id)){
                bool linked=id=="work_start"||id=="work_end"||id=="light_toggle"||id=="ac_toggle";
                if(linked&&!Config.Panels["home"].Enabled){sender.Send(new {kind="notice",text="트레이에서 스마트홈을 먼저 켜세요."});return;}
                if(id=="work_start"||id=="work_end"){
                    bool on=id=="work_start";mode=on?"업무":"퇴근";studio["lamp"]=studio["bar"]=studio["ac"]=on;
                    if(on){studio["brightness"]=85;studio["kelvin"]=4500;studio["target"]=23;}else{active.Remove("record");active.Remove("media");}
                }else if(id=="light_toggle"){bool on=!((bool)studio["lamp"]||(bool)studio["bar"]);studio["lamp"]=studio["bar"]=on;if(on&&(int)studio["brightness"]==0)studio["brightness"]=65;}
                else if(id=="ac_toggle")studio["ac"]=!(bool)studio["ac"];
                else if(id=="mic_mute"||id=="focus"||id=="record"||id=="media"){if(!active.Remove(id))active.Add(id);}
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
            if(!ending){ending=true;save.Stop();sample.Stop();instanceTimer.Stop();Save();Tray.Visible=false;
                foreach(var w in Windows.Values.ToArray())w.Dispose();Windows.Clear();
                Tray.Dispose();menu.Dispose();save.Dispose();sample.Dispose();instanceTimer.Dispose();Chick.Dispose();}
            base.ExitThreadCore();
        }
    }
}
