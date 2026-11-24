using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MinaDesk {
    internal sealed class SmartSettingsDialog : Form {
        internal SmartSettings Value { get; private set; }
        readonly SmartSettings draft;
        readonly TextBox address=new TextBox(),token=new TextBox(),inbox=new TextBox(),archive=new TextBox(),ffmpeg=new TextBox();
        readonly CheckBox watch=new CheckBox { Text="앱 실행 중 폴더 자동 수집",AutoSize=true },daily=new CheckBox { Text="전날 원본을 매일 예약 편집",AutoSize=true },upload=new CheckBox { Text="완성 영상을 내 유튜브에 자동 비공개 업로드",AutoSize=true },capture=new CheckBox { Text="연결한 HA 카메라를 약 6초마다 사진으로 수집",AutoSize=true };
        readonly NumericUpDown hour=new NumericUpDown { Minimum=0,Maximum=23,Width=70 };
        readonly ComboBox mode=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=240 };
        readonly DataGridView rooms=new DataGridView { Dock=DockStyle.Fill,AutoGenerateColumns=false,AllowUserToAddRows=true,AllowUserToDeleteRows=true,RowHeadersWidth=30,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.AllCells };
        readonly Label connection=new Label { AutoSize=true },google=new Label { AutoSize=true };
        readonly YouTubeArchive youtube;
        readonly CancellationTokenSource closing=new CancellationTokenSource();
        bool authorizing;
        internal SmartSettingsDialog(SmartSettings settings,YouTubeArchive uploader) {
            draft=LocalData.Decode<SmartSettings>(LocalData.Encode(settings));youtube=uploader;
            Text="스마트홈 연결 · Mini Workstation Hub";ClientSize=new Size(900,630);MinimumSize=new Size(750,560);StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.Dpi;
            var tabs=new TabControl { Dock=DockStyle.Fill };
            Controls.Add(tabs);
            var home=new TabPage("집 연결");var files=new TabPage("영상 보관");var account=new TabPage("유튜브");
            tabs.TabPages.AddRange(new[]{home,new TabPage("방과 기기"),files,account});
            var connectionPanel=Panel(home);
            address.Text=draft.HaAddress;token.UseSystemPasswordChar=true;
            AddField(connectionPanel,"Home Assistant 주소",address);
            AddField(connectionPanel,"장기 액세스 토큰 · 비워두면 기존 값 유지",token);
            var test=new Button { Text="연결 확인 · 기기 목록 읽기",AutoSize=true };
            var devices=new TextBox { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Width=810,Height=270 };
            test.Click+=async(s,e)=> {
                test.Enabled=false;
                try {
                    ApplyConnection();
                    using(var client=new HomeAssistantClient()) {
                        await client.Refresh(draft,CancellationToken.None);connection.Text=client.Status;
                        devices.Text=String.Join(Environment.NewLine,client.Entities.Values.Where(d=>new[]{"vacuum","media_player","binary_sensor","sensor","script","camera"}.Contains(d.Id.Split('.')[0])).OrderBy(d=>d.Id).Select(d=>d.Name+"   ·   "+d.Id+"   ·   "+d.State));
                    }
                } catch(Exception ex) { connection.Text=ex.Message; }
                finally { test.Enabled=true; }
            };
            connectionPanel.Controls.Add(test);connectionPanel.Controls.Add(connection);connectionPanel.Controls.Add(devices);
            connectionPanel.Controls.Add(Note("HA 프로필의 보안 메뉴에서 만든 토큰을 이 입력란에 직접 붙여넣으세요. 토큰은 Windows 사용자 암호화로 로컬 저장합니다. 기기 목록을 참고해 다음 탭에 각 방의 대상을 연결합니다."));
            var binding=tabs.TabPages[1];
            rooms.DataSource=new System.ComponentModel.BindingList<RoomBinding>(draft.Rooms);
            foreach(var field in new[]{new[]{"Name","방 이름"},new[]{"Vacuum","청소기 entity_id"},new[]{"CleanScript","방 청소 script"},new[]{"Speaker","MA 스피커 entity_id"},new[]{"Classic","클래식 목록 URI"},new[]{"Pop","인기가요 목록 URI"},new[]{"Window","창문 binary_sensor"},new[]{"Dryer","건조기 sensor"},new[]{"Camera","수집용 camera"}})
                rooms.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName=field[0],HeaderText=field[1],MinimumWidth=100 });
            binding.Controls.Add(rooms);
            binding.Controls.Add(new Label { Dock=DockStyle.Top,Height=64,Padding=new Padding(8),Text="방 이름을 먼저 정하고 기기 ID를 연결하세요. 음악은 Music Assistant의 플레이어와 목록 URI를 사용합니다. 방 청소는 HA에서 검증한 script를 연결합니다. 연결한 카메라만 사진 수집 대상이 됩니다." });
            var filePanel=Panel(files);
            inbox.Text=draft.Inbox;archive.Text=draft.Archive;ffmpeg.Text=draft.Ffmpeg;
            AddPath(filePanel,"휴대폰 전송 앱이 파일을 넣는 폴더",inbox,false);
            AddPath(filePanel,"PC의 원본·완성 영상 보관 폴더",archive,false);
            AddPath(filePanel,"ffmpeg.exe · 같은 폴더의 ffprobe.exe도 필요",ffmpeg,true);
            mode.Items.AddRange(new object[]{"영상 클립 · 원래 길이로 연결","사진 원본 · 10분으로 편집"});mode.SelectedIndex=draft.VideoMode=="images10"?1:0;
            filePanel.Controls.Add(mode);
            watch.Checked=draft.WatchFolder;daily.Checked=draft.DailyJobs;upload.Checked=draft.AutoUpload;capture.Checked=draft.CaptureCamera;hour.Value=draft.RunHour;
            filePanel.Controls.Add(watch);filePanel.Controls.Add(capture);filePanel.Controls.Add(daily);
            var schedule=new FlowLayoutPanel { AutoSize=true,Width=810 };schedule.Controls.Add(new Label { Text="예약 시각 · 매일 현지 시각",AutoSize=true });schedule.Controls.Add(hour);schedule.Controls.Add(new Label { Text="시 이후",AutoSize=true });filePanel.Controls.Add(schedule);
            filePanel.Controls.Add(upload);
            filePanel.Controls.Add(Note("앱과 PC가 켜져 있을 때 동작합니다. 창을 최소화하거나 숨겨도 영상 작업은 계속됩니다. 앱 종료 중에는 작업을 쉬고 다음 실행에서 이어갑니다. 휴대폰 파일 정리는 PC의 원본 보관을 확인한 뒤 휴대폰에서 승인하세요. PhotoSync 등에서 위 전송 폴더를 대상으로 지정합니다. 사진이 적거나 수집이 끊긴 구간은 반복 프레임으로 표시됩니다."));
            var accountPanel=Panel(account);
            var import=new Button { Text="데스크톱 OAuth 클라이언트 JSON 선택",AutoSize=true };
            import.Click+=(s,e)=> {
                using(var dialog=new OpenFileDialog { Filter="Google OAuth JSON|*.json" }) {
                    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                    try { draft.OAuthClientProtected=LocalData.Protect(LocalData.Encode(YouTubeArchive.ImportClient(dialog.FileName)));draft.OAuthTokensProtected="";google.Text="클라이언트 선택됨 · 계정을 연결하세요."; }
                    catch(Exception ex) { MessageBox.Show(this,ex.Message,"Google 연결 확인"); }
                }
            };
            var connect=new Button { Text="Google에서 내 유튜브 연결",AutoSize=true };
            connect.Click+=async(s,e)=> {
                if(String.IsNullOrEmpty(draft.OAuthClientProtected)) { google.Text="OAuth 클라이언트 JSON을 먼저 선택하세요.";return; }
                connect.Enabled=false;import.Enabled=false;google.Text="기본 브라우저에서 직접 로그인·승인하세요.";
                authorizing=true;
                try { await youtube.Authorize(draft,closing.Token);if(!IsDisposed)google.Text="유튜브 연결됨 · 설정을 저장하세요."; }
                catch(Exception) { if(!IsDisposed)google.Text="연결을 마치지 못했습니다. 계정 승인과 API 설정을 확인하세요."; }
                finally { authorizing=false;if(!IsDisposed) { connect.Enabled=true;import.Enabled=true; } }
            };
            google.Text=String.IsNullOrEmpty(draft.OAuthTokensProtected)?"유튜브 연결 전":"유튜브 연결 정보 저장됨";
            accountPanel.Controls.Add(import);accountPanel.Controls.Add(connect);accountPanel.Controls.Add(google);
            accountPanel.Controls.Add(Note("YouTube Data API가 켜진 Google Cloud 프로젝트의 데스크톱 OAuth JSON을 사용합니다. 계정 연결은 기본 브라우저에서 사용자가 승인합니다. 업로드·내 영상 상태 확인 권한을 요청하며 비공개로 올립니다. 자동 업로드는 영상 보관 탭에서 별도로 켭니다. 연결 정보는 앱의 로컬 암호화 저장소에 보관합니다."));
            foreach(var link in new[]{new[]{"Hub 개인정보 안내","https://github.com/feed-mina/Workstation_Hub/blob/main/docs/privacy.md"},new[]{"YouTube 서비스 약관","https://www.youtube.com/t/terms"},new[]{"Google 개인정보처리방침","https://policies.google.com/privacy"}}) {
                string url=link[1];var label=new LinkLabel { Text=link[0],AutoSize=true };
                label.LinkClicked+=(s,e)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});accountPanel.Controls.Add(label);
            }
            var buttons=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=46,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(5) };
            var save=new Button { Text="설정 저장",AutoSize=true };save.Click+=(s,e)=>Save();
            var cancel=new Button { Text="취소",DialogResult=DialogResult.Cancel };buttons.Controls.Add(save);buttons.Controls.Add(cancel);Controls.Add(buttons);buttons.BringToFront();CancelButton=cancel;
        }
        static FlowLayoutPanel Panel(TabPage tab) {
            var panel=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(14) };tab.Controls.Add(panel);return panel;
        }
        static Label Note(string text) { return new Label { Text=text,AutoSize=true,MaximumSize=new Size(810,0),Margin=new Padding(3,12,3,12) }; }
        static void AddField(FlowLayoutPanel panel,string name,TextBox field) { panel.Controls.Add(new Label { Text=name,AutoSize=true });field.Width=810;panel.Controls.Add(field); }
        void AddPath(FlowLayoutPanel panel,string name,TextBox field,bool executable) {
            panel.Controls.Add(new Label { Text=name,AutoSize=true });
            var line=new FlowLayoutPanel { Width=815,Height=32 };field.Width=720;line.Controls.Add(field);
            var select=new Button { Text="찾기" };select.Click+=(s,e)=> {
                if(executable) { using(var dialog=new OpenFileDialog { Filter="FFmpeg|ffmpeg.exe" })if(dialog.ShowDialog(this)==DialogResult.OK)field.Text=dialog.FileName; }
                else { using(var dialog=new FolderBrowserDialog { SelectedPath=field.Text })if(dialog.ShowDialog(this)==DialogResult.OK)field.Text=dialog.SelectedPath; }
            };
            line.Controls.Add(select);panel.Controls.Add(line);
        }
        void ApplyConnection() {
            HomeAssistantClient.BaseAddress(address.Text.Trim());draft.HaAddress=address.Text.Trim();
            if(!String.IsNullOrWhiteSpace(token.Text))draft.HaTokenProtected=LocalData.Protect(token.Text.Trim());
        }
        void Save() {
            try {
                if(authorizing)throw new InvalidOperationException("Google 연결이 끝난 뒤 저장하세요.");
                rooms.EndEdit();ApplyConnection();
                string input=Path.GetFullPath(inbox.Text.Trim()).TrimEnd(Path.DirectorySeparatorChar);
                string output=Path.GetFullPath(archive.Text.Trim()).TrimEnd(Path.DirectorySeparatorChar);
                if(input.Equals(output,StringComparison.OrdinalIgnoreCase)||input.StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||output.StartsWith(input+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("전송 폴더와 보관 폴더는 서로 겹치지 않게 선택하세요.");
                if(draft.Rooms.Count==0||draft.Rooms.Any(r=>String.IsNullOrWhiteSpace(r.Name)||!System.Text.RegularExpressions.Regex.IsMatch(r.Id??"",@"^[a-f0-9]{32}$")))throw new ArgumentException("방 이름을 하나 이상 등록하세요.");
                if(!String.IsNullOrWhiteSpace(ffmpeg.Text) && (!File.Exists(ffmpeg.Text.Trim())||!File.Exists(Path.Combine(Path.GetDirectoryName(ffmpeg.Text.Trim()),"ffprobe.exe"))))throw new ArgumentException("ffmpeg.exe와 ffprobe.exe가 있는 폴더를 확인하세요.");
                draft.Inbox=input;draft.Archive=output;draft.Ffmpeg=ffmpeg.Text.Trim();draft.WatchFolder=watch.Checked;draft.DailyJobs=daily.Checked;draft.AutoUpload=upload.Checked;draft.CaptureCamera=capture.Checked;draft.RunHour=(int)hour.Value;draft.VideoMode=mode.SelectedIndex==1?"images10":"clips";
                if((draft.WatchFolder||draft.DailyJobs) && String.IsNullOrEmpty(draft.Ffmpeg))throw new ArgumentException("자동 영상 작업을 켜기 전에 FFmpeg 위치를 지정하세요.");
                if(draft.AutoUpload && String.IsNullOrEmpty(draft.OAuthTokensProtected))throw new ArgumentException("유튜브 계정을 연결한 후 자동 업로드를 켜세요.");
                if(draft.CaptureCamera && (String.IsNullOrEmpty(draft.HaTokenProtected)||!draft.Rooms.Any(r=>!String.IsNullOrWhiteSpace(r.Camera))))throw new ArgumentException("사진 수집을 켜기 전에 HA와 수집할 카메라를 연결하세요.");
                if(draft.CaptureCamera && draft.DailyJobs && draft.VideoMode!="images10")throw new ArgumentException("카메라 사진의 예약 편집은 사진 원본 · 10분 모드를 선택하세요.");
                Value=draft;DialogResult=DialogResult.OK;Close();
            } catch(Exception ex) { MessageBox.Show(this,ex.Message,"설정 확인",MessageBoxButtons.OK,MessageBoxIcon.Information); }
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { closing.Cancel();base.OnFormClosed(e); }
    }
}
