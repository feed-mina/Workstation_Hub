using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MinaDesk {
    internal sealed class MacroSettingsDialog : Form {
        internal MacroSettings Value {get;private set;}
        readonly TextBox screenshots=new TextBox(),folder=new TextBox(),code=new TextBox(),address=new TextBox(),password=new TextBox();
        readonly MacroSettings original;
        internal MacroSettingsDialog(MacroSettings settings) {
            original=settings;Text="매크로 설정 · Mini Workstation Hub";StartPosition=FormStartPosition.CenterParent;
            AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(570,340);MinimumSize=Size;
            screenshots.Text=settings.ScreenshotFolder;folder.Text=settings.WorkingFolder;code.Text=settings.CodePath;address.Text=settings.ObsAddress;password.UseSystemPasswordChar=true;
            var table=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=3,RowCount=7};
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,60));
            Controls.Add(table);
            Add(table,0,"스크린샷 폴더",screenshots,()=>PickFolder(screenshots));
            Add(table,1,"터미널 기본 폴더",folder,()=>PickFolder(folder));
            Add(table,2,"VS Code 실행 파일",code,()=>{using(var dialog=new OpenFileDialog{Filter="VS Code (Code.exe)|Code.exe",CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK)code.Text=dialog.FileName;});
            Add(table,3,"OBS 서버 주소",address,null);Add(table,4,"OBS 비밀번호",password,null);
            var note=new Label{Text="OBS에서 도구 → WebSocket 서버 설정을 켜세요.\n비밀번호를 비워두면 기존 값을 유지합니다.",AutoSize=true,Dock=DockStyle.Fill};table.Controls.Add(note,0,5);table.SetColumnSpan(note,3);
            var actions=new FlowLayoutPanel{FlowDirection=FlowDirection.RightToLeft,Dock=DockStyle.Fill};
            var save=new Button{Text="저장",AutoSize=true};save.Click+=(s,e)=>Save();
            var cancel=new Button{Text="취소",DialogResult=DialogResult.Cancel};
            var clear=new Button{Text="OBS 암호 지우기",AutoSize=true};clear.Click+=(s,e)=>{password.Clear();password.Tag=true;};
            actions.Controls.Add(save);actions.Controls.Add(cancel);actions.Controls.Add(clear);table.Controls.Add(actions,0,6);table.SetColumnSpan(actions,3);AcceptButton=save;CancelButton=cancel;
        }
        void Add(TableLayoutPanel table,int row,string label,TextBox field,Action browse) {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute,36));table.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left},0,row);field.Dock=DockStyle.Fill;table.Controls.Add(field,1,row);
            if(browse!=null){var button=new Button{Text="찾기",Dock=DockStyle.Fill};button.Click+=(s,e)=>browse();table.Controls.Add(button,2,row);}
        }
        void PickFolder(TextBox field){using(var dialog=new FolderBrowserDialog{SelectedPath=field.Text})if(dialog.ShowDialog(this)==DialogResult.OK)field.Text=dialog.SelectedPath;}
        void Save() {
            try {
                ObsRecorder.Address(address.Text.Trim());
                if(!Path.IsPathRooted(screenshots.Text)||!Directory.Exists(folder.Text))throw new ArgumentException("저장 폴더는 전체 경로로, 작업 폴더는 현재 있는 폴더로 지정하세요.");
                if(code.Text.Trim().Length>0&&(!File.Exists(code.Text.Trim())||!String.Equals(Path.GetExtension(code.Text.Trim()),".exe",StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("VS Code 실행 파일을 확인하세요.");
                var value=new MacroSettings{ScreenshotFolder=screenshots.Text.Trim(),WorkingFolder=folder.Text.Trim(),CodePath=code.Text.Trim(),ObsAddress=address.Text.Trim(),Projects=original.Projects,ObsPasswordProtected=original.ObsPasswordProtected};
                if(password.Text.Length>0||password.Tag!=null)value.SetPassword(password.Text);
                Value=value;DialogResult=DialogResult.OK;Close();
            }catch(Exception ex){MessageBox.Show(this,ex.Message,"설정 확인",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        }
    }
}
