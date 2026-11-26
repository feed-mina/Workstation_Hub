using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MinaDesk {
    internal sealed class AccountConnectionsDialog : Form {
        readonly BrowserAccounts accounts;
        readonly Action changed;
        readonly List<Action> refresh=new List<Action>();
        internal AccountConnectionsDialog(BrowserAccounts store,string selected,Action onChanged) {
            accounts=store;changed=onChanged;Text="재생·업로드 계정 연결 · Mini Workstation Hub";ClientSize=new Size(710,590);MinimumSize=new Size(520,510);StartPosition=FormStartPosition.CenterParent;Font=new Font(SystemFonts.MessageBoxFont.FontFamily,10);
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(14),RowCount=3,ColumnCount=1};
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(660,0),Text="재생용 Google · 업로드용 Google · 네이버 계정은 각각 분리합니다.\r\n연결한 전용 브라우저의 로그인 상태를 계속 사용하며, 프로젝트를 바꿔도 유지됩니다.",Margin=new Padding(0,0,0,12)});
            var tabs=new TabControl{Dock=DockStyle.Fill};
            foreach(string key in BrowserAccounts.Keys) {
                var tab=new TabPage(BrowserAccounts.Name(key)){Padding=new Padding(14)};tabs.TabPages.Add(tab);if(key==selected)tabs.SelectedTab=tab;
                BuildTab(tab,key);
            }
            root.Controls.Add(tabs);
            var bottom=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};var close=new Button{Text="닫기",AutoSize=true,DialogResult=DialogResult.Cancel};bottom.Controls.Add(close);root.Controls.Add(bottom);CancelButton=close;Controls.Add(root);
            RefreshAll();
        }
        void RefreshAll(){foreach(var update in refresh)update();}
        void BuildTab(TabPage tab,string key) {
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=9,AutoScroll=true};
            for(int i=0;i<9;i++)layout.RowStyles.Add(new RowStyle(i==7?SizeType.Percent:SizeType.AutoSize,i==7?100:0));tab.Controls.Add(layout);
            var state=new Label{AutoSize=true,MaximumSize=new Size(620,0),Font=new Font(Font,FontStyle.Bold),Margin=new Padding(0,0,0,10)};layout.Controls.Add(state);
            var provider=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=270};
            if(key=="media") {provider.Items.AddRange(new object[]{"YouTube","YouTube Music"});provider.SelectedIndex=accounts.Value.MediaProvider=="music"?1:0;provider.SelectedIndexChanged+=(s,e)=>Run(()=>{accounts.SetProvider(provider.SelectedIndex==1?"music":"youtube");changed();});layout.Controls.Add(provider);}
            else layout.Controls.Add(new Label{AutoSize=true,Text=key=="youtube_upload"?"유튜브 스튜디오 · 업로드 화면":"네이버 블로그 · 글쓰기 화면"});
            layout.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(620,0),Margin=new Padding(0,12,0,8),Text="① 전용 브라우저 열기 → ② 원하는 계정으로 직접 로그인 → ③ 아래에서 완료 확인\r\n비밀번호·인증번호를 Hub에 입력하지 않습니다. 로그인 정보는 이 PC의 전용 브라우저에 보관합니다."});
            var alias=new TextBox{Width=370,MaxLength=80,Text=accounts.Get(key).Alias??""};var aliasPanel=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};aliasPanel.Controls.Add(new Label{Text="계정 별칭 (선택)",AutoSize=true,Margin=new Padding(0,6,12,0)});aliasPanel.Controls.Add(alias);layout.Controls.Add(aliasPanel);
            var confirmed=new CheckBox{AutoSize=true,MaximumSize=new Size(620,0),Text="전용 브라우저에서 원하는 계정의 로그인을 확인했습니다.",Margin=new Padding(0,12,0,12)};layout.Controls.Add(confirmed);
            var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};
            var open=new Button{Text="전용 브라우저 열기",AutoSize=true};var finish=new Button{Text="로그인 확인 · 연결 완료",AutoSize=true};var disconnect=new Button{Text="계정 해제",AutoSize=true};actions.Controls.AddRange(new Control[]{open,finish,disconnect});layout.Controls.Add(actions);
            var status=new Label{AutoSize=true,MaximumSize=new Size(620,0),Margin=new Padding(0,10,0,8)};layout.Controls.Add(status);
            layout.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(620,0),Text=key=="media"?"연결 후 버튼은 해당 프로필의 재생 창에 재생·정지 요청을 보냅니다. 창이 없으면 선택한 서비스를 엽니다. 첫 재생할 영상·음악은 브라우저에서 선택하세요.":"버튼은 연결한 계정의 업로드·글쓰기 화면을 엽니다. 파일 선택과 게시 확인은 서비스 화면에서 직접 진행합니다."});
            var note=new Label{AutoSize=true,MaximumSize=new Size(620,0),Margin=new Padding(0,8,0,0),Text="계정 해제 시 Hub 연결은 즉시 끊어집니다. 열려 있는 전용 브라우저 창은 직접 닫아 주세요. 창이 종료되면 해당 프로필의 로그인 데이터를 정리합니다. 일반 브라우저 프로필에는 영향을 주지 않습니다."};layout.Controls.Add(note);
            Action update=()=>{var account=accounts.Get(key);state.Text=account.Connected?"연결됨 · "+(String.IsNullOrWhiteSpace(account.Alias)?BrowserAccounts.Name(key):account.Alias):account.ProfileId==null?"연결 전":"로그인 확인 대기";open.Enabled=!accounts.ReadOnly;finish.Enabled=!accounts.ReadOnly && account.ProfileId!=null && confirmed.Checked;disconnect.Enabled=!accounts.ReadOnly && account.ProfileId!=null;provider.Enabled=!accounts.ReadOnly;alias.Enabled=!accounts.ReadOnly;confirmed.Enabled=!accounts.ReadOnly;if(accounts.ReadOnly)status.Text=accounts.Notice;};
            refresh.Add(update);confirmed.CheckedChanged+=(s,e)=>update();
            open.Click+=(s,e)=>Run(()=>{var account=accounts.Get(key);if(account.ProfileId==null)accounts.Prepare(key,AccountBrowser.Detect());accounts.Open(key,true);status.Text="전용 브라우저에서 직접 로그인하세요. 로그인 후 이 화면에서 완료 확인을 눌러 주세요.";changed();RefreshAll();});
            finish.Click+=(s,e)=>Run(()=>{if(!confirmed.Checked)return;accounts.Confirm(key,alias.Text);status.Text="이 PC에서 연결한 계정 프로필을 계속 사용합니다. 서비스가 재로그인을 요청하면 같은 전용 창에서 로그인하세요.";changed();RefreshAll();});
            disconnect.Click+=(s,e)=>Run(()=>{accounts.Disconnect(key);confirmed.Checked=false;alias.Text="";int pending=accounts.Cleanup(AccountBrowser.Running);status.Text=pending>0?"연결 해제 완료 · 이전 전용 브라우저 창을 닫으면 로그인 데이터를 정리합니다.":"계정 해제와 전용 로그인 데이터 정리를 완료했습니다.";changed();RefreshAll();});
        }
        void Run(Action action){try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"계정 연결 확인",MessageBoxButtons.OK,MessageBoxIcon.Information);RefreshAll();}}
    }
}
