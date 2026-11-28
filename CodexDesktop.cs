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
        static readonly string[] SearchCommands={"Search chats","Search chats…","Search chats...","Switch chat…","Switch chat...","채팅 검색","채팅 검색…","채팅 전환…","채팅 찾기"};
        static readonly string[] Tree={"File tree","Project files","Workspace files","Files","파일 트리","프로젝트 파일","파일"};
        static readonly string[] TreeControls={"Choose file tree root","파일 트리 루트 선택","Filter files","파일 필터"};
        static readonly string[] TreeCommands={"Toggle file tree","Show file tree","Open file tree","Toggle navigation panel","탐색 패널 표시/숨기기",
            "파일 트리 열기","파일 트리 전환","파일 트리 토글"};
        static readonly string[] Palette={"Search commands","Search commands…","Search commands...","Search ChatGPT","명령 검색","명령 검색…","명령 검색...","ChatGPT 검색"};

        internal static Task<bool> OpenPanel(bool search,CancellationToken stop) {
            return Task.Run(()=>{
                var timer=Stopwatch.StartNew();
                IntPtr target=IntPtr.Zero;
                bool paletteOpened=false,invoked=false;
                string query=null;
                long queryAt=0;
                int queries=0;
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
                                var command=FindCommand(window,search?SearchCommands:TreeCommands);
                                if(command!=null) {
                                    if(stop.IsCancellationRequested || GetForegroundWindow()!=target)return false;
                                    // Do not retry an invocation if the provider loses the response.
                                    invoked=true;
                                    command.Invoke();
                                }else if(!paletteOpened) {
                                    if(stop.IsCancellationRequested || GetForegroundWindow()!=target)return false;
                                    paletteOpened=true;
                                    SendKeys.SendWait("^+p");
                                }else if(queries<(search?1:2) && timer.ElapsedMilliseconds-queryAt>=400) {
                                    var focus=AutomationElement.FocusedElement;
                                    if(focus!=null && focus.Current.ProcessId==(int)pid && focus.Current.ControlType==ControlType.Edit &&
                                       Matches(focus.Current.Name,Palette) && focus.TryGetCurrentPattern(ValuePattern.Pattern,out pattern) &&
                                       !((ValuePattern)pattern).Current.IsReadOnly && GetForegroundWindow()==target) {
                                        var field=(ValuePattern)pattern;
                                        if(queries==1 && field.Current.Value!=query)return false;
                                        string label=focus.Current.Name;
                                        bool korean=label.Any(c=>c>='\uac00' && c<='\ud7a3');
                                        query=search?(korean?"채팅":"chat"):queries==0?
                                            (korean?"탐색 패널":"navigation panel"):(korean?"파일 트리":"file tree");
                                        queries++;
                                        queryAt=timer.ElapsedMilliseconds;
                                        field.SetValue(query);
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
            return Named(window,TreeControls).Any(item=>item.Current.ControlType==ControlType.Button || item.Current.ControlType==ControlType.ComboBox || item.Current.ControlType==ControlType.Edit)
                || Named(window,Tree).Any(item=>item.Current.ControlType==ControlType.Tree ||
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
