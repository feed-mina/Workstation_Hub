using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace MinaDesk {
    internal sealed class SkillRunResult {
        internal bool Sent;
        internal string Text;
    }
    internal static class DesktopSkillRunner {
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr handle,out uint processId);
        internal static string AppName(string target) { return target=="gpt"?"GPT":"Claude"; }
        internal static string Prompt(SkillProject project,SkillButton button) {
            return "이 프로젝트에서 '"+button.SkillName+"' 스킬을 사용해 주세요.\n"+
                "프로젝트 폴더: "+project.Folder+"\n"+
                "스킬 파일: "+button.SkillPath+"\n"+
                "먼저 위 SKILL.md를 읽고 지침과 연결된 자료를 따라 주세요. 상대 경로는 스킬 파일 폴더를 기준으로 해석해 주세요.\n"+
                "요청: "+(String.IsNullOrWhiteSpace(button.Instruction)?"이 프로젝트에 스킬을 적용해 주세요. 필요한 입력이 부족하면 먼저 저에게 질문해 주세요.":button.Instruction);
        }
        internal static string Link(string target,string folder,string prompt) {
            if(!SkillWorkspaces.IsTarget(target))throw new ArgumentException("지원하지 않는 AI 앱입니다.");
            string link=target=="gpt"?"codex://threads/new?path="+Uri.EscapeDataString(folder):"claude://code/new?folder="+Uri.EscapeDataString(folder);
            if(!String.IsNullOrEmpty(prompt))link+=(target=="gpt"?"&prompt=":"&q=")+Uri.EscapeDataString(prompt);
            if(link.Length>30000)throw new ArgumentException("요청문이 너무 깁니다. 스킬 버튼에 저장한 요청을 줄여 주세요.");
            return link;
        }
        internal static void Open(string target,string folder) { Launch(Link(target,folder,null)); }
        static void Launch(string link) {
            using(var process=Process.Start(new ProcessStartInfo(link){UseShellExecute=true})) { }
        }
        internal static async Task<SkillRunResult> Run(SkillProject project,SkillButton button,CancellationToken stop) {
            string prompt=Prompt(project,button),app=AppName(button.Target);
            Launch(Link(button.Target,project.Folder,prompt));
            // Official links prepare a composer. Send only after reading back that exact draft.
            // No blind Enter, clipboard replacement, focus stealing or permission-dialog clicks.
            return await Task.Run(()=>{
                var watch=Stopwatch.StartNew();
                while(watch.ElapsedMilliseconds<12000 && !stop.IsCancellationRequested) {
                    try {
                        IntPtr handle=GetForegroundWindow();uint pid;GetWindowThreadProcessId(handle,out pid);
                        if(handle!=IntPtr.Zero && IsAppProcess((int)pid,button.Target)) {
                            var window=AutomationElement.FromHandle(handle);
                            object pattern;
                            if(window.TryGetCurrentPattern(WindowPattern.Pattern,out pattern) && ((WindowPattern)pattern).Current.IsModal)break;
                            var composer=FindComposer(window,prompt);
                            if(composer!=null) {
                                var send=FindSend(composer);
                                // Re-read immediately before the single send attempt.
                                if(send!=null && SameDraft(ReadDraft(composer),prompt)) {
                                    if(stop.IsCancellationRequested || GetForegroundWindow()!=handle || !window.Current.IsEnabled)break;
                                    try {
                                        ((InvokePattern)send.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                                        return new SkillRunResult{Sent=true,Text=app+"에 스킬 요청을 전송했습니다. 진행 상황은 해당 앱에서 확인하세요."};
                                    }catch(Exception ex) {
                                        if(!AutomationError(ex))throw;
                                        return new SkillRunResult{Text=app+"의 전송 결과를 확인하지 못했습니다. 중복 전송하지 않고 멈췄습니다. 앱에서 확인하세요."};
                                    }
                                }
                            }
                        }
                    }catch(Exception ex) { if(!AutomationError(ex))throw; }
                    if(stop.WaitHandle.WaitOne(250))break;
                }
                return new SkillRunResult{Text=app+"에 요청문 열기를 요청했습니다. 앱의 프로젝트 접근 확인 또는 전송 버튼을 직접 확인하세요. 자동 전송은 확인되지 않았습니다."};
            });
        }
        internal static bool IsAppProcess(int id,string target) {
            try {
                using(var process=Process.GetProcessById(id)) {
                    string name=process.ProcessName;
                    return target=="claude"?String.Equals(name,"Claude",StringComparison.OrdinalIgnoreCase):
                        String.Equals(name,"ChatGPT",StringComparison.OrdinalIgnoreCase) || String.Equals(name,"Codex",StringComparison.OrdinalIgnoreCase);
                }
            }catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
        }
        static bool AutomationError(Exception ex) {
            return ex is ElementNotAvailableException || ex is ElementNotEnabledException || ex is InvalidOperationException || ex is COMException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception;
        }
        internal static bool SameDraft(string actual,string expected) {
            return actual!=null && expected!=null && Normalize(actual)==Normalize(expected);
        }
        static string Normalize(string text) { return Regex.Replace(text.Replace("\u200b",""),@"\s+"," ").Trim(); }
        internal static bool IsSendName(string name) {
            return new[]{"Send","Send message","Send prompt","Submit message","전송","보내기","메시지 보내기","메시지 전송","프롬프트 보내기"}.Contains((name??"").Trim(),StringComparer.OrdinalIgnoreCase);
        }
        static string ReadDraft(AutomationElement edit) {
            if(edit.Current.IsOffscreen || !edit.Current.IsEnabled)return null;
            object pattern;
            if(edit.TryGetCurrentPattern(ValuePattern.Pattern,out pattern)) {
                var value=(ValuePattern)pattern;
                return value.Current.IsReadOnly?null:value.Current.Value;
            }
            if(edit.TryGetCurrentPattern(TextPattern.Pattern,out pattern)) {
                var text=(TextPattern)pattern;
                object readOnly=text.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                if(readOnly is bool && !(bool)readOnly)return text.DocumentRange.GetText(-1);
            }
            return null;
        }
        internal static AutomationElement FindComposer(AutomationElement window,string prompt) {
            var matches=window.FindAll(TreeScope.Descendants,new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Document)));
            AutomationElement found=null;
            foreach(AutomationElement element in matches)if(SameDraft(ReadDraft(element),prompt)) {
                if(found!=null)return null; // Ambiguous drafts: leave the user in control.
                found=element;
            }
            return found;
        }
        internal static AutomationElement FindSend(AutomationElement composer) {
            var parent=composer;
            for(int level=0;level<4;level++) {
                parent=TreeWalker.ControlViewWalker.GetParent(parent);
                if(parent==null || parent.Current.ControlType==ControlType.Window)return null;
                var buttons=parent.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
                AutomationElement found=null;
                foreach(AutomationElement button in buttons) {
                    object invoke;
                    if(!button.Current.IsOffscreen && button.Current.IsEnabled && IsSendName(button.Current.Name) && button.TryGetCurrentPattern(InvokePattern.Pattern,out invoke)) {
                        if(found!=null)return null;
                        found=button;
                    }
                }
                if(found!=null)return found;
            }
            return null;
        }
    }
}

