using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MinaDesk {
    internal sealed class SmartHome : IDisposable {
        readonly string configFile;
        readonly Action changed;
        readonly Action<string,string> log;
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=6000 };
        readonly CancellationTokenSource stop=new CancellationTokenSource();
        readonly YouTubeArchive youtube=new YouTubeArchive();
        HomeAssistantClient ha=new HomeAssistantClient();
        ArchiveEngine archive;
        SmartSettings config;
        bool polling,configuring,disposed,executing;
        string error="";
        internal string DashboardAddress {
            get {
                try {return String.IsNullOrWhiteSpace(config.HaTokenProtected)?null:HomeAssistantClient.BaseAddress(config.HaAddress).AbsoluteUri;}
                catch(ArgumentException){return null;}catch(UriFormatException){return null;}
            }
        }
        internal SmartHome(string data,Action stateChanged,Action<string,string> writeLog) {
            changed=stateChanged;log=writeLog;configFile=Path.Combine(data,"smart-home.json");
            try { config=LocalData.Read(configFile,()=>SmartSettings.New(data)); }
            catch { config=SmartSettings.New(data);error="스마트홈 설정을 읽지 못했습니다. 설정을 확인하세요."; }
            try { archive=new ArchiveEngine(data,()=>config,changed,Save,message=>log("영상 보관",message),youtube); }
            catch { error="영상 보관 기록을 읽지 못했습니다. 원본과 기록 백업을 확인하세요."; }
            timer.Tick+=async(s,e)=>await Tick();timer.Start();
        }
        void Save() { LocalData.Write(configFile,config); }
        async Task Tick() {
            if(disposed||configuring||polling)return;
            polling=true;
            try {
                var current=ha;var configuration=config;
                await current.Refresh(configuration,stop.Token);
                if(disposed || current!=ha || configuring)return;
                if(config.CaptureCamera && archive!=null) {
                    foreach(var room in config.Rooms.Where(r=>!String.IsNullOrEmpty(r.Camera))) {
                        try { await archive.Capture(room.Id,await ha.Camera(config,room.Camera,stop.Token),DateTime.UtcNow,stop.Token); }
                        catch(OperationCanceledException) { throw; }
                        catch { error="카메라 사진 수집에 빈 구간이 생겼습니다. 연결 상태를 확인하세요."; }
                    }
                }
                if(archive!=null) {
                    // A long render/upload must not stop HA state refresh or the independent capture timer.
                    var work=archive.Tick();
                    if(work.IsCompleted)await work;
                }
            } catch(OperationCanceledException) {}
            catch { error="스마트홈 연결과 영상 설정을 확인하세요."; }
            finally { polling=false;if(!disposed)changed(); }
        }
        internal object Snapshot() {
            return new {
                status=ha.Status,updatedUtc=ha.UpdatedUtc.HasValue?ha.UpdatedUtc.Value.ToString("o"):null,error,
                connected=ha.UpdatedUtc.HasValue,busy=executing,archive=archive==null?null:archive.Snapshot(),
                watch=config.WatchFolder,daily=config.DailyJobs,autoUpload=config.AutoUpload,capture=config.CaptureCamera,
                youtubeConnected=!String.IsNullOrEmpty(config.OAuthTokensProtected),
                rooms=config.Rooms.Select(r=>new {
                    id=r.Id,name=r.Name,vacuum=ha.View(r.Vacuum),speaker=ha.View(r.Speaker),window=ha.View(r.Window),dryer=ha.View(r.Dryer),camera=ha.View(r.Camera),
                    start=ha.Can(r.Vacuum,8192),pause=ha.Can(r.Vacuum,4),home=ha.Can(r.Vacuum,16),roomClean=ha.ScriptReady(r.CleanScript),
                    classic=ha.Can(r.Speaker,512)&&ha.Services.Contains("music_assistant.play_media")&&!String.IsNullOrEmpty(r.Classic),
                    pop=ha.Can(r.Speaker,512)&&ha.Services.Contains("music_assistant.play_media")&&!String.IsNullOrEmpty(r.Pop),stopMusic=ha.Can(r.Speaker,4096)
                }).ToArray()
            };
        }
        internal void Configure(Form owner) {
            if(configuring || executing)return;
            if(polling) { MessageBox.Show(owner,"기기 상태 확인이 끝난 뒤 설정을 다시 열어 주세요.","연결 확인 중");return; }
            if(archive!=null && archive.Busy) { MessageBox.Show(owner,"영상 작업이 끝난 뒤 설정을 바꿔 주세요.","영상 작업 중");return; }
            configuring=true;
            try {
                using(var dialog=new SmartSettingsDialog(config,youtube)) {
                    if(dialog.ShowDialog(owner)!=DialogResult.OK)return;
                    LocalData.Write(configFile,dialog.Value);config=dialog.Value;
                    var previous=ha;ha=new HomeAssistantClient();previous.Dispose();error="";
                }
            } catch(Exception ex) { MessageBox.Show(owner,ex.Message,"스마트홈 설정"); }
            finally { configuring=false;if(!disposed)changed(); }
        }
        internal async Task<string> Execute(Form owner,string action,string roomId,string id) {
            if(configuring || executing)throw new InvalidOperationException("현재 요청을 마친 뒤 다시 눌러 주세요.");
            executing=true;changed();
            try { return await ExecuteOne(owner,action,roomId,id); }
            finally { executing=false;if(!disposed)changed(); }
        }
        async Task<string> ExecuteOne(Form owner,string action,string roomId,string id) {
            var room=config.Rooms.FirstOrDefault(r=>r.Id==roomId);
            if(room==null)throw new InvalidOperationException("설정에서 방을 선택하세요.");
            if(new[]{"import","date","prepare","prepare-extra","retry","upload","resolve-upload","open-job"}.Contains(action) && archive==null)throw new IOException(error);
            if(new[]{"retry","upload","resolve-upload","open-job","prepare-extra"}.Contains(action) && !archive.Jobs.Any(j=>j.Id==id && j.RoomId==roomId))throw new ArgumentException("선택한 방의 영상 작업을 확인하세요.");
            if(action=="import") {
                if(archive==null)throw new IOException(error);
                using(var files=new OpenFileDialog { Multiselect=true,Filter="영상·사진|*.mp4;*.mov;*.mkv;*.m4v;*.jpg;*.jpeg;*.png" }) {
                    if(files.ShowDialog(owner)!=DialogResult.OK)return "가져오기 취소";
                    int count=0;
                    foreach(string file in files.FileNames) { await archive.Import(file,room.Id,null);count++; }
                    return count+"개 원본 확인 · 날짜가 없으면 촬영 날짜를 지정하세요.";
                }
            }
            if(action=="date") {
                var item=archive.Items.Single(i=>i.Id==id && i.RoomId==roomId);
                using(var form=new Form { Text="촬영 시각 확인",Width=430,Height=190,StartPosition=FormStartPosition.CenterParent }) {
                    var date=new DateTimePicker { Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd HH:mm:ss",Value=item.CapturedUtc.ToLocalTime(),Dock=DockStyle.Top };
                    var label=new Label { Text=item.OriginalName+"\n실제 촬영 날짜와 시각을 확인하세요.",Dock=DockStyle.Top,Height=50 };
                    var confirm=new Button { Text="이 촬영 시각으로 확인",Dock=DockStyle.Bottom,DialogResult=DialogResult.OK };
                    form.Controls.Add(date);form.Controls.Add(label);form.Controls.Add(confirm);
                    if(form.ShowDialog(owner)==DialogResult.OK)await archive.ConfirmDate(item.Id,date.Value);
                }
                return "촬영 시각 확인을 마쳤습니다.";
            }
            if(action=="prepare" || action=="prepare-extra") {
                if(action=="prepare-extra")id=archive.Jobs.Single(j=>j.Id==id).Day;
                DateTime day;
                if(!DateTime.TryParseExact(id,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out day))throw new ArgumentException("편집할 촬영 날짜를 선택하세요.");
                if(config.VideoMode=="images10" && MessageBox.Show(owner,"수집된 사진으로 10분 영상을 만듭니다. 장면이 적거나 누락된 시간에는 같은 사진이 반복됩니다. 편집할까요?","사진 편집 확인",MessageBoxButtons.YesNo)!=DialogResult.Yes)return "편집 취소";
                await archive.Prepare(room.Id,id,action=="prepare-extra");return "날짜별 영상 편집을 준비했습니다.";
            }
            if(action=="retry") { await archive.Retry(id);return "작업 재시도 예약됨"; }
            if(action=="resolve-upload") {
                using(var form=new Form { Text="중복 업로드 방지 · Studio 결과 확인",ClientSize=new System.Drawing.Size(530,245),StartPosition=FormStartPosition.CenterParent }) {
                    var note=new Label { Text="YouTube Studio에서 이 날짜의 영상을 확인하세요.\n이미 있으면 영상 주소의 ID 11자를 입력합니다.\n새 업로드는 기존 영상이 없음을 확인한 경우에만 선택하세요.",Dock=DockStyle.Top,Height=85 };
                    var video=new TextBox { Dock=DockStyle.Top,MaxLength=11 };
                    var fresh=new CheckBox { Text="Studio에서 기존 영상이 없음을 확인했습니다 · 새로 올리기",Dock=DockStyle.Top,Height=50 };
                    var studio=new Button { Text="YouTube Studio 열기",Dock=DockStyle.Top,Height=30 };studio.Click+=(s,e)=>Process.Start(new ProcessStartInfo("https://studio.youtube.com/"){UseShellExecute=true});
                    var confirm=new Button { Text="확인 결과 적용",Dock=DockStyle.Bottom,DialogResult=DialogResult.OK };
                    form.Controls.Add(fresh);form.Controls.Add(video);form.Controls.Add(studio);form.Controls.Add(note);form.Controls.Add(confirm);
                    if(form.ShowDialog(owner)!=DialogResult.OK)return "업로드 결과 확인 취소";
                    await archive.Resolve(id,video.Text.Trim(),fresh.Checked);
                }
                return "업로드 확인 결과를 적용했습니다.";
            }
            if(action=="upload") {
                if(MessageBox.Show(owner,"이 완성 영상을 연결한 유튜브 계정에 비공개로 올릴까요?","유튜브 업로드",MessageBoxButtons.YesNo)!=DialogResult.Yes)return "업로드 취소";
                await archive.UploadNow(id);return "비공개 업로드 예약됨";
            }
            if(action=="open-inbox" || action=="open-archive") {
                string folder=action=="open-inbox"?Path.Combine(config.Inbox,room.Id):config.Archive;
                Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder) { UseShellExecute=true });return "영상 폴더 열림";
            }
            if(action=="open-job") {
                var job=archive.Jobs.Single(j=>j.Id==id && j.RoomId==roomId);
                if(job.State=="done" && !String.IsNullOrEmpty(job.VideoId))Process.Start(new ProcessStartInfo("https://www.youtube.com/watch?v="+job.VideoId) { UseShellExecute=true });
                else if(File.Exists(job.Output))Process.Start(new ProcessStartInfo(job.Output) { UseShellExecute=true });
                else throw new IOException("아직 완성 영상이 없습니다.");
                return "완성 영상 열림";
            }
            await ha.Execute(config,room,action,stop.Token);
            await ha.Refresh(config,stop.Token);
            return room.Name+" · "+action+" 요청을 보냈습니다.";
        }
        public void Dispose() {
            disposed=true;timer.Dispose();stop.Cancel();ha.Dispose();if(archive!=null)archive.Dispose();youtube.Dispose();
        }
    }
}
