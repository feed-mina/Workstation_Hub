using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MinaDesk {
    sealed class DeskWindow : Form {
        internal readonly string Key;
        internal bool Ready;
        internal WebView2 Browser {get;private set;}
        readonly Hub hub;
        readonly Uri address;
        Rectangle? beforeSkills;
        const string Host="workstation-hub.invalid";
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle,int attribute,ref int value,int size);
        internal DeskWindow(Hub context,string key,string title,Rectangle bounds){
            hub=context;Key=key;address=new Uri("https://"+Host+"/desk.html?panel="+key);
            Text=title+" · Mini Workstation Hub";Name="WorkstationHub-"+key;Icon=hub.Chick;
            StartPosition=FormStartPosition.Manual;ShowInTaskbar=true;FormBorderStyle=FormBorderStyle.Sizable;
            MinimizeBox=true;MaximizeBox=true;ApplyTheme(hub.Config.Theme);
            MinimumSize=new Size(280,153);Bounds=bounds;
            Shown+=(s,e)=>InitializeBrowser();
            ResizeEnd+=(s,e)=>RememberBounds();
            Move+=(s,e)=>RememberBounds();
            Resize+=(s,e)=>{RememberBounds();hub.RefreshSampling();};
            FormClosing+=(s,e)=>{if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;hub.ScheduleClose(this);}else if(e.CloseReason==CloseReason.WindowsShutDown)hub.ExitThread();};
        }
        void RememberBounds(){if(Visible&&WindowState==FormWindowState.Normal&&IsHandleCreated)hub.Remember(Key,Bounds);}
        internal void SkillLibrary(bool open) {
            if(Key!="macro" || WindowState!=FormWindowState.Normal)return;
            if(open && !beforeSkills.HasValue) {
                beforeSkills=Bounds;
                Bounds=Settings.Fit(new Rectangle(Left,Top,Math.Max(Width,1040),Math.Max(Height,810)));
            }else if(!open && beforeSkills.HasValue) {
                Bounds=Settings.Fit(beforeSkills.Value);beforeSkills=null;
            }
        }
        protected override void OnHandleCreated(EventArgs e){
            base.OnHandleCreated(e);
            ApplyTheme(hub.Config.Theme);
        }
        internal void ApplyTheme(string theme){
            bool neon=theme=="neon";
            BackColor=neon?Color.FromArgb(8,22,38):Color.FromArgb(15,46,38);
            if(Browser!=null)Browser.DefaultBackgroundColor=BackColor;
            if(!IsHandleCreated)return;
            int dark=1,cap=neon?0x003b2810:0x00334418,ink=neon?0x00fff7ee:0x00daf9eb;
            DwmSetWindowAttribute(Handle,20,ref dark,4);DwmSetWindowAttribute(Handle,35,ref cap,4);DwmSetWindowAttribute(Handle,36,ref ink,4);
        }
        async void InitializeBrowser(){
            var view=new WebView2{Dock=DockStyle.Fill,DefaultBackgroundColor=BackColor};Browser=view;
            Controls.Clear();Controls.Add(view);
            try{
                var env=await hub.EnvironmentTask();if(IsDisposed||view.IsDisposed)return;
                await view.EnsureCoreWebView2Async(env);if(IsDisposed||view.IsDisposed)return;
                var core=view.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping(Host,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui"),CoreWebView2HostResourceAccessKind.DenyCors);
                core.Settings.AreHostObjectsAllowed=false;core.Settings.AreDefaultContextMenusEnabled=false;
                core.Settings.AreDevToolsEnabled=false;core.Settings.IsStatusBarEnabled=false;
                core.Settings.IsZoomControlEnabled=true;core.Settings.AreBrowserAcceleratorKeysEnabled=false;
                core.NavigationStarting+=(s,e)=>{if(e.Uri!=address.AbsoluteUri)e.Cancel=true;else Ready=false;};
                core.NewWindowRequested+=(s,e)=>e.Handled=true;
                core.PermissionRequested+=(s,e)=>e.State=CoreWebView2PermissionState.Deny;
                core.DownloadStarting+=(s,e)=>e.Cancel=true;
                core.WebMessageReceived+=(s,e)=>{if(!IsDisposed&&e.Source==address.AbsoluteUri)hub.Receive(this,e.WebMessageAsJson);};
                core.ProcessFailed+=(s,e)=>{if(!IsDisposed&&(e.ProcessFailedKind==CoreWebView2ProcessFailedKind.BrowserProcessExited||e.ProcessFailedKind==CoreWebView2ProcessFailedKind.RenderProcessExited))BeginInvoke(new Action(()=>ShowError(view,"화면 프로세스가 종료됐습니다.")));};
                core.NavigationCompleted+=(s,e)=>{if(!e.IsSuccess&&!IsDisposed)BeginInvoke(new Action(()=>ShowError(view,"화면을 열 수 없습니다. "+e.WebErrorStatus)));};
                core.Navigate(address.AbsoluteUri);
            }catch(Exception ex){if(!IsDisposed)ShowError(view,ex.Message);}
        }
        void ShowError(WebView2 view,string detail){
            if(IsDisposed||view!=Browser)return;
            Ready=false;hub.RefreshSampling();Browser=null;view.Dispose();Controls.Clear();
            var info=new Label{Dock=DockStyle.Fill,Padding=new Padding(18),Text="화면을 준비할 수 없습니다.\nMicrosoft Edge WebView2 Runtime과 앱 폴더의 파일을 확인하세요.\n\n"+detail,ForeColor=Color.White};
            var retry=new Button{Text="다시 열기",Dock=DockStyle.Bottom};retry.Click+=(s,e)=>{info.Dispose();retry.Dispose();InitializeBrowser();};Controls.Add(info);Controls.Add(retry);
        }
        internal void Send(object message){
            if(!Ready||IsDisposed||Browser==null||Browser.CoreWebView2==null)return;
            try{Browser.CoreWebView2.PostWebMessageAsJson(hub.Json.Serialize(message));}catch(InvalidOperationException){Ready=false;}catch(COMException){Ready=false;}
        }
    }
}

