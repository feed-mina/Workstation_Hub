using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using MinaDesk;

class DesktopSmoke {
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr handle,uint command);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    static string data, result, shots;static Hub hub;
    static readonly List<string> passed=new List<string>();
    static void Check(bool b,string name){if(!b)throw new Exception(name);passed.Add(name);Report();}
    static void Report(){File.WriteAllLines(result,passed.ToArray());}
    static async Task Wait(Func<bool> pred,string name,int seconds=32){var watch=Stopwatch.StartNew();while(!pred()){if(watch.Elapsed.TotalSeconds>seconds)throw new TimeoutException(name+" · "+String.Join(" / ",hub.Windows.Select(p=>p.Key+":"+String.Join(",",p.Value.Controls.OfType<Label>().Select(l=>l.Text)))));await Task.Delay(100);}}
    static async Task<string> Js(string key,string script){return await hub.Windows[key].Browser.CoreWebView2.ExecuteScriptAsync(script);}
    static async Task Dom(string key,string test,string name,int seconds=8){var clock=Stopwatch.StartNew();while(await Js(key,test)!="true"){if(clock.Elapsed.TotalSeconds>seconds)throw new Exception(name+" · "+await Js(key,"document.body.innerText.slice(0,190)"));await Task.Delay(100);}Check(true,name);}
    static async Task Capture(string key,string name){using(var s=File.Create(Path.Combine(shots,name+".png")))await hub.Windows[key].Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,s);}
    static async Task<Rectangle> CheckWindow(string key){
        var window=hub.Windows[key];
        var peers=hub.Windows.Where(p=>p.Key!=key).ToDictionary(p=>p.Key,p=>p.Value.Bounds);
        var area=Screen.FromControl(window).WorkingArea;
        var start=window.Bounds;
        int dx=start.Right+10<=area.Right ? 10 : -10;
        int dy=start.Bottom+8<=area.Bottom ? 8 : -8;
        window.Bounds=Settings.Fit(new Rectangle(start.X+dx,start.Y+dy,start.Width-5,start.Height-3));
        await Task.Delay(450);
        var moved=window.Bounds;
        Check(moved.Location!=start.Location,key+" moved independently");
        SendMessage(window.Handle,0x112,(IntPtr)0xF020,IntPtr.Zero);
        await Wait(()=>IsIconic(window.Handle),key+" minimize");
        Check(hub.Config.Panels[key].Enabled&&hub.Toggles[key].Checked,key+" minimize keeps feature on");
        if(key=="pc"){
            string before=await Js(key,"chartHistory.at(-1).time");
            await Task.Delay(1800);
            Check(before==await Js(key,"chartHistory.at(-1).time"),"sampling paused while minimized");
        }
        SendMessage(window.Handle,0x112,(IntPtr)0xF120,IntPtr.Zero);
        await Wait(()=>!IsIconic(window.Handle)&&window.Bounds==moved,key+" native restore");
        Check(peers.All(p=>!IsIconic(hub.Windows[p.Key].Handle)&&hub.Windows[p.Key].Bounds==p.Value),key+" preserves other window bounds and states");
        return moved;
    }
    static async Task Run(){
        await Wait(()=>hub.Windows.Count==6&&hub.Windows.Values.All(form=>form.Ready),"six WebView2 panels",50);
        Check(hub.Windows.Values.All(form=>form.ShowInTaskbar&&form.MinimizeBox&&form.MaximizeBox&&GetWindow(form.Handle,4)==IntPtr.Zero),"six independent top-level taskbar windows");
        Check(hub.Tray.Visible&&hub.Tray.Icon!=null&&hub.Toggles.Count==6,"tray exposes all six windows");
        await Dom("macro","document.querySelectorAll('#keys .key').length===13&&!!document.querySelector('[data-key=skills]')","existing macro keys and skill entry");
        await Dom("pc","/^\\d+%$/.test(document.getElementById('cpu').innerText)&&document.querySelectorAll('#plot path').length===2","real CPU/RAM chart");
        await Dom("pc","document.querySelectorAll('.pc-sensors dd').length===4&&document.getElementById('cpu-temperature').textContent!=='—'","hardware panel shows driver or sensor availability",15);
        await Dom("dev","document.querySelectorAll('#dev-tabs button').length===5&&document.querySelectorAll('.dev-card').length===32&&!document.querySelector('.dev-card').disabled","five developer tabs and 32 initial Codex cards");
        await Js("dev","document.querySelector('[data-card=codex-model-comparison]').click()");
        await Dom("dev","document.querySelector('#model-dialog').open&&document.querySelectorAll('#model-comparison select').length===2&&document.querySelector('#model-comparison').innerText.includes('기본 콘텍스트')","model comparison shows two selectable models");
        await Js("dev","document.getElementById('model-close').click()");
        await Dom("widgets","document.querySelectorAll('#widget-dock button').length===9&&!!document.querySelector('#calendar-days [aria-current=date]')","clock, calendar and nine shortcuts");
        await Js("widgets","document.getElementById('calendar-next').click()");
        await Dom("widgets","!document.querySelector('#calendar-days [aria-current=date]')","calendar can navigate to next month");
        await Js("dev","document.getElementById('dev-tab-1').click();document.querySelector('[data-card=gh-code]').click()");
        await Dom("dev","document.getElementById('dev-result-summary').textContent.includes('GitHub 저장소가 연결된 프로젝트')","GitHub requires a repository instead of showing sample data");
        await Js("dev","document.getElementById('dev-tab-0').click();document.querySelector('[data-card=model]').click();document.querySelector('#dev-dialog-content select').value='GPT-6.2 Sol';document.querySelector('#dev-dialog-actions button').click()");
        await Dom("dev","document.getElementById('dev-context-line').textContent.includes('GPT-6.2 Sol')","model preference saved");
        await Js("dev","document.getElementById('dev-theme').click()");
        await Dom("widgets","document.body.classList.contains('neon')","theme reaches independent windows");
        foreach(string layout in new[]{"original","focus","extended"}) {
            hub.ChangeLayout(layout);
            await Wait(()=>hub.Windows.Count==(layout=="original"?4:6)&&hub.Windows.Values.All(form=>form.Ready),layout+" independent windows");
            var bounds=hub.Windows.Values.Select(form=>form.Bounds).ToArray();
            Check(!bounds.Where((rect,i)=>bounds.Skip(i+1).Any(other=>rect.IntersectsWith(other))).Any(),layout+" non-overlapping placement");
        }
        var moved=new Dictionary<string,Rectangle>();
        foreach(var key in Hub.Keys)moved[key]=await CheckWindow(key);
        await Js("dev","document.getElementById('dev-tab-2').click();document.querySelector('[data-card=tool-focus]').click()");
        await Dom("macro","document.querySelector('[data-key=focus]').getAttribute('aria-pressed')==='true'","developer tool and macro share focus state");
        await Dom("log","document.getElementById('history').textContent.includes('집중 표시')","event reaches log window");
        await Js("home","document.getElementById('open-room').click()");
        await Dom("home","document.querySelector('#room-stage canvas')!==null","native WebView 3D scene",15);
        await Js("home","document.getElementById('room-lamp').click();document.getElementById('close-room').click()");
        await Dom("home","document.getElementById('brightness').value==='0'&&!document.querySelector('#room-stage canvas')","3D toggle and cleanup");
        hub.Toggles["home"].PerformClick();await Task.Delay(120);Check(!hub.Windows.ContainsKey("home")&&!hub.Toggles["home"].Checked,"tray off closes feature");
        await Dom("macro","!document.querySelector('[data-key=work_start]').disabled","work start independent of home panel");
        hub.Toggles["home"].PerformClick();await Wait(()=>hub.Windows["home"].Ready,"tray reopen");
        await Dom("home","document.getElementById('brightness').value==='0'","tray on rebuilds view from shared state");
        await Js("macro","document.getElementById('edit').click();document.querySelector('[data-key=vscode]').click();document.getElementById('key-name').value='작업 앱';document.getElementById('apply').click();document.getElementById('edit').click()");
        await Dom("macro","document.querySelector('[data-key=vscode] b').textContent==='작업 앱'","key editor persists");
        await Capture("macro","native-macro");await Capture("pc","native-pc");await Capture("home","native-home");await Capture("log","native-log");
        hub.Windows["dev"].WindowState=FormWindowState.Maximized;
        await Js("dev","document.getElementById('dev-tab-0').click()");
        await Task.Delay(300);
        await Capture("dev","native-developer-console");
        hub.Windows["dev"].WindowState=FormWindowState.Normal;
        hub.Windows["widgets"].WindowState=FormWindowState.Maximized;
        await Task.Delay(300);
        await Capture("widgets","native-widgets");
        hub.Windows["widgets"].WindowState=FormWindowState.Normal;
        hub.Windows["log"].Close();await Wait(()=>!hub.Windows.ContainsKey("log"),"close via X");Check(!hub.Toggles["log"].Checked,"X disables feature in menu");
        foreach(var name in Hub.Keys){
            if(hub.Config.Panels[name].Enabled)hub.Toggles[name].PerformClick();
            Check(!hub.Windows.ContainsKey(name)&&!hub.Toggles[name].Checked,name+" menu off");
        }
        Check(hub.Windows.Count==0&&hub.Tray.Visible,"all off leaves tray alive");
        foreach(var name in Hub.Keys){
            hub.Toggles[name].PerformClick();
            await Wait(()=>hub.Windows.ContainsKey(name)&&hub.Windows[name].Ready,name+" menu on");
            Check(hub.Windows[name].Bounds==moved[name]&&hub.Toggles[name].Checked,name+" menu restores saved position");
            if(name!="pc")hub.Toggles[name].PerformClick();
        }
        var saved=Settings.Load(Path.Combine(data,"windows.json"));
        Check(saved.Panels["pc"].Enabled&&!saved.Panels["log"].Enabled&&saved.Panels["pc"].Bounds()==moved["pc"],"enabled choices and position written to disk");
        passed.Add("PASS");Report();
    }
    static async Task Reopen(){
        var saved=Settings.Load(Path.Combine(data,"windows.json"));
        await Wait(()=>hub.Windows.Count==1&&hub.Windows.ContainsKey("pc")&&hub.Windows["pc"].Ready,"second run restores enabled feature",50);
        Check(hub.Windows["pc"].Bounds==saved.Panels["pc"].Bounds()&&hub.Toggles["pc"].Checked&&!hub.Toggles["macro"].Checked,"new process restores saved bounds and enabled flags");
        hub.Toggles["macro"].PerformClick();await Wait(()=>hub.Windows["macro"].Ready,"reopen macro");
        await Dom("macro","document.querySelector('[data-key=vscode] b').textContent==='작업 앱'","new process restores edited key name");
        hub.Toggles["dev"].PerformClick();await Wait(()=>hub.Windows["dev"].Ready,"reopen developer control");
        await Dom("dev","document.body.classList.contains('neon')&&document.getElementById('dev-context-line').textContent.includes('GPT-6.2 Sol')","new process restores theme and developer preference");
        passed.Add("PASS");Report();
    }
    [STAThread]static int Main(string[] args){
        data=args[0];result=args[1];shots=args[2];Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        using(hub=new Hub(data,null)){
            var kickoff=new System.Windows.Forms.Timer{Interval=200};kickoff.Tick+=async(s,e)=>{kickoff.Stop();try{if(args.Length>3)await Reopen();else await Run();}catch(Exception ex){passed.Add("FAIL "+ex);Report();}finally{hub.ExitThread();}};kickoff.Start();Application.Run(hub);
        }
        return passed.Last()=="PASS"?0:1;
    }
}

