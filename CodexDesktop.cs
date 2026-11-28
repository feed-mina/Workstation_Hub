using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace MinaDesk {
    internal static class CodexDesktop {
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);

        static readonly string[] Search={"Search chats","채팅 검색","채팅 찾기"};
        static readonly string[] Tree={"File tree","Project files","Workspace files","Files","파일 트리","프로젝트 파일","파일"};
        static readonly string[] TreeCommands={"Toggle file tree","Show file tree","Open file tree","파일 트리 열기","파일 트리 전환","파일 트리 토글"};
        static readonly string[] Palette={"Search commands","Search commands…","Search commands...","명령 검색","명령 검색…","명령 검색..."};

        internal static Task<bool> OpenPanel(bool search,CancellationToken stop) {
            return Task.Run(()=>{
                var timer=Stopwatch.StartNew();
                IntPtr target=IntPtr.Zero;
                bool paletteOpened=false,queryEntered=false,invoked=false;
                while(timer.ElapsedMilliseconds<6500 && !stop.IsCancellationRequested) {
                    try {
                        IntPtr handle=GetForegroundWindow();uint pid;
                        GetWindowThreadProcessId(handle,out pid);
                        if(target!=IntPtr.Zero && handle!=target)return false;
                        if(handle!=IntPtr.Zero && DesktopSkillRunner.IsAppProcess((int)pid,"gpt")) {
                            target=handle;
                            var window=AutomationElement.FromHandle(handle);
                            object pattern;
                            if(!window.Current.IsEnabled || (window.TryGetCurrentPattern(WindowPattern.Pattern,out pattern) && ((WindowPattern)pattern).Current.IsModal))return false;
                            if(search?SearchOpen(window):TreeOpen(window))return true;
                            if(!invoked) {
                                var command=FindCommand(window,search?Search:TreeCommands);
                                if(command!=null) {
                                    if(stop.IsCancellationRequested || GetForegroundWindow()!=target)return false;
                                    // Do not retry an invocation if the provider loses the response.
                                    invoked=true;
                                    command.Invoke();
                                }else if(!paletteOpened) {
                                    if(stop.IsCancellationRequested || GetForegroundWindow()!=target)return false;
                                    paletteOpened=true;
                                    SendKeys.SendWait("^+p");
                                }else if(!queryEntered) {
                                    var focus=AutomationElement.FocusedElement;
                                    if(focus!=null && focus.Current.ProcessId==(int)pid && focus.Current.ControlType==ControlType.Edit &&
                                       Matches(focus.Current.Name,Palette) && focus.TryGetCurrentPattern(ValuePattern.Pattern,out pattern) &&
                                       !((ValuePattern)pattern).Current.IsReadOnly && GetForegroundWindow()==target) {
                                        queryEntered=true;
                                        ((ValuePattern)pattern).SetValue(search?"Search chats":"file tree");
                                    }
                                }
                            }
                        }
                    }catch(Exception ex) {
                        if(!DesktopSkillRunner.AutomationError(ex))throw;
                        if(invoked)return false;
                    }
                    if(stop.WaitHandle.WaitOne(120))break;
                }
                return false;
            },stop);
        }

        static bool Matches(string name,string[] accepted) {
            return accepted.Contains((name??"").Trim(),StringComparer.OrdinalIgnoreCase);
        }
        static bool SearchOpen(AutomationElement window) {
            return Named(window,Search).Any(item=>item.Current.ControlType==ControlType.Edit && item.Current.HasKeyboardFocus);
        }
        static bool TreeOpen(AutomationElement window) {
            return Named(window,Tree).Any(item=>item.Current.ControlType==ControlType.Tree ||
                ((item.Current.ControlType==ControlType.Pane || item.Current.ControlType==ControlType.Group) &&
                 item.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TreeItem))!=null));
        }
        static InvokePattern FindCommand(AutomationElement window,string[] names) {
            InvokePattern found=null;
            foreach(var item in Named(window,names)) {
                object invoke;
                if(!item.TryGetCurrentPattern(InvokePattern.Pattern,out invoke))continue;
                if(found!=null)return null;
                found=(InvokePattern)invoke;
            }
            return found;
        }
        static AutomationElement[] Named(AutomationElement window,string[] names) {
            var conditions=names.Select(name=>(Condition)new PropertyCondition(AutomationElement.NameProperty,name,PropertyConditionFlags.IgnoreCase)).ToArray();
            return window.FindAll(TreeScope.Descendants,new OrCondition(conditions)).Cast<AutomationElement>()
                .Where(item=>!item.Current.IsOffscreen && item.Current.IsEnabled).ToArray();
        }
    }
}
