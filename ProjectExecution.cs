using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MinaDesk {
    internal static class ProjectExecution {
        internal static string Prompt(SkillProject project,SkillButton button) {
            if(project==null)throw new ArgumentNullException("project");
            string text="프로젝트: "+project.Name+"\r\n";
            if(project.Repository!=null)text+="GitHub 저장소: "+project.Repository.Url+"\r\n";
            text+="작업 방식: "+(project.TaskKind=="conversation"?"자료 연결 후 대화·검토. 코드 실행이나 변경은 먼저 확인해 주세요.":"코딩·PR 작업")+"\r\n";
            text+="실행 위치: "+(project.Location=="cloud"?"클라우드. 이 저장소의 원격 환경과 접근 권한을 먼저 확인해 주세요.":"이 PC의 로컬 폴더: "+project.Folder)+"\r\n";
            if(button!=null) {
                text+="사용할 스킬: "+button.SkillName+"\r\n";
                if(!String.IsNullOrEmpty(button.Reference))text+="저장소 안의 스킬 문서: "+button.Reference+". 해당 파일을 읽고 연결된 지침을 따라 주세요.\r\n";
                else text+="이 환경에 해당 스킬이 설치돼 있는지 확인해 주세요. 로컬 PC의 개인 스킬 경로는 클라우드에서 읽을 수 있다고 가정하지 마세요. 자료가 없으면 먼저 요청해 주세요.\r\n";
                text+="요청: "+(String.IsNullOrWhiteSpace(button.Instruction)?"이 프로젝트에 스킬을 적용해 주세요. 필요한 내용이 부족하면 먼저 질문해 주세요.":button.Instruction);
            }else text+="요청: 오늘 할 일과 이전 작업을 확인한 뒤 다음 작업을 함께 정하고 싶습니다.";
            return text;
        }
        internal static void OpenRoot(string target,string taskKind) {
            if(!SkillWorkspaces.IsTarget(target))throw new ArgumentException("AI 앱을 선택하세요.");
            string protocol=target=="claude"?"claude":taskKind=="conversation"?"chatgpt":"codex";
            using(var key=Registry.ClassesRoot.OpenSubKey(protocol))if(key==null)throw new InvalidOperationException((protocol=="codex"?"Codex":protocol=="chatgpt"?"ChatGPT":"Claude")+" 데스크톱 앱이 설치돼 있는지 확인하세요.");
            using(var opened=Process.Start(new ProcessStartInfo(protocol+"://"){UseShellExecute=true})){}
        }
        internal static void Show(IWin32Window owner,SkillProject project,SkillButton button,string target) {
            string prompt=Prompt(project,button);
            using(var dialog=new Form{Text="AI 작업 준비 · "+project.Name,ClientSize=new Size(760,610),MinimumSize=new Size(410,400),StartPosition=FormStartPosition.CenterParent}) {
                var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=5};
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var title=new Label{AutoSize=true,MaximumSize=new Size(700,0),Text=(project.Location=="cloud"?"클라우드":"이 PC")+" · "+(project.TaskKind=="conversation"?"대화·검토":"코딩·PR")+" · "+project.Name,Font=new Font(SystemFonts.MessageBoxFont.FontFamily,13,FontStyle.Bold)};
                var help=new Label{AutoSize=true,MaximumSize=new Size(700,0),Margin=new Padding(0,10,0,10),Text="1. 선택한 데스크톱 AI 앱을 엽니다.\r\n2. "+(project.Location=="cloud"?"앱에서 클라우드 환경과 같은 GitHub 저장소를 선택합니다.":"선택한 프로젝트 폴더와 자료 접근을 확인합니다.")+"\r\n3. 요청문을 복사해 붙여넣고 내용을 확인한 뒤 전송하세요.\r\nGitHub 등록만으로 AI 앱의 저장소 권한·클라우드 실행이 자동 설정되지는 않습니다."};
                var text=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Text=prompt,Font=new Font(SystemFonts.MessageBoxFont.FontFamily,11)};
                var status=new Label{AutoSize=true,MaximumSize=new Size(700,0),Margin=new Padding(0,8,0,8),Text="요청 준비됨 · 아직 AI에 전송하지 않았습니다."};
                var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};
                foreach(string app in target=="both"?new[]{"gpt","claude"}:new[]{target}) {
                    string chosen=app;var open=new Button{Text=(app=="gpt"?(project.TaskKind=="code"?"GPT · Codex":"GPT · ChatGPT"):"Claude")+" 열기",AutoSize=true};
                    open.Click+=(s,e)=>{try{OpenRoot(chosen,project.TaskKind);status.Text="앱 열기를 요청했습니다. 실행 위치·저장소를 앱에서 확인하세요.";}catch(Exception ex){status.Text=ex.Message;}};actions.Controls.Add(open);
                }
                var copy=new Button{Text="요청문 복사",AutoSize=true};copy.Click+=(s,e)=>{try{Clipboard.SetText(prompt);status.Text="요청문을 복사했습니다. AI 앱에 붙여넣어 확인하세요.";}catch(System.Runtime.InteropServices.ExternalException){status.Text="클립보드를 사용 중입니다. 잠시 후 다시 누르세요.";}};
                var close=new Button{Text="닫기",AutoSize=true,DialogResult=DialogResult.Cancel};actions.Controls.Add(copy);actions.Controls.Add(close);dialog.CancelButton=close;
                layout.Controls.Add(title);layout.Controls.Add(help);layout.Controls.Add(text);layout.Controls.Add(status);layout.Controls.Add(actions);dialog.Controls.Add(layout);
                if(owner==null)dialog.ShowDialog();else dialog.ShowDialog(owner);
            }
        }
    }
}
