using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MinaDesk {
    internal sealed class ArchiveEngine : IDisposable {
        readonly string journal,jobFile;
        readonly Dictionary<string,ArchiveItem> items=new Dictionary<string,ArchiveItem>();
        readonly HashSet<string> sources=new HashSet<string>();
        readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        readonly CancellationTokenSource stop=new CancellationTokenSource();
        readonly YouTubeArchive youtube;
        readonly Func<SmartSettings> settings;
        readonly Action changed,saveSettings;
        readonly Action<string> log;
        internal readonly List<ArchiveJob> Jobs;
        internal string Status="영상 수집 대기";
        bool disposed,processing;
        internal bool Busy { get { return processing || gate.CurrentCount==0; } }
        internal ArchiveEngine(string data,Func<SmartSettings> configuration,Action onChange,Action persistSettings,Action<string> report,YouTubeArchive uploader) {
            settings=configuration;changed=onChange;saveSettings=persistSettings;log=report;youtube=uploader;
            journal=Path.Combine(data,"archive-receipts.ndjson");jobFile=Path.Combine(data,"archive-jobs.json");
            Jobs=LocalData.Read(jobFile,()=>new List<ArchiveJob>());
            if(File.Exists(journal)) {
                var lines=File.ReadAllLines(journal);
                for(int index=0;index<lines.Length;index++) {
                    if(String.IsNullOrWhiteSpace(lines[index]))continue;
                    try {
                        var item=LocalData.Decode<ArchiveItem>(lines[index]);items[item.Id]=item;
                        if(!String.IsNullOrEmpty(item.SourceKey))sources.Add(item.SourceKey);
                    } catch {
                        if(index!=lines.Length-1)throw new IOException("영상 보관 기록이 손상됐습니다. 백업 기록을 확인하세요.");
                        // Preserve a crash-truncated last entry for recovery before continuing the journal.
                        File.Copy(journal,journal+".recovery-"+Guid.NewGuid().ToString("N"));
                        File.WriteAllLines(journal,lines.Take(index));
                    }
                }
            }
            foreach(var job in Jobs) {
                if(job.State=="rendering")job.State="queued";
                if(job.State=="uploading")job.State=String.IsNullOrEmpty(job.VideoId)?"upload-retry":"processing";
            }
        }
        internal IEnumerable<ArchiveItem> Items { get { return items.Values; } }
        void Receipt(ArchiveItem item) {
            File.AppendAllText(journal,LocalData.Encode(item)+Environment.NewLine);
            items[item.Id]=item;
            if(!String.IsNullOrEmpty(item.SourceKey))sources.Add(item.SourceKey);
        }
        void Persist() { LocalData.Write(jobFile,Jobs);changed(); }
        static string Digest(string file) {
            using(var input=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();
        }
        internal async Task<ArchiveItem> Import(string source,string room,DateTime? confirmed) {
            await gate.WaitAsync(stop.Token);
            try { return await ImportFile(source,room,confirmed,stop.Token); }
            finally { gate.Release();if(!disposed)changed(); }
        }
        async Task<ArchiveItem> ImportFile(string source,string room,DateTime? confirmed,CancellationToken cancel) {
            if(!MediaTools.Supported(source))throw new IOException("지원되는 영상 또는 사진 파일을 선택하세요.");
            var config=settings();
            var info=new FileInfo(source);
            string key=Path.GetFullPath(source).ToUpperInvariant()+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks;
            if(sources.Contains(key) && items.Values.Any(i=>i.SourceKey==key && File.Exists(i.Path)))return null;
            string folder=Path.Combine(config.Archive,"원본");Directory.CreateDirectory(folder);
            string temporary=Path.Combine(folder,Guid.NewGuid().ToString("N")+Path.GetExtension(source).ToLowerInvariant());
            try {
                await Task.Run(()=> {
                    using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
                    using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
                },cancel);
                string hash=await Task.Run(()=>Digest(temporary),cancel);
                var existing=items.Values.FirstOrDefault(i=>i.RoomId==room && i.Hash==hash && i.State!="사진 수집");
                if(existing!=null && File.Exists(existing.Path)) {
                    existing.SourceKey=key;Receipt(existing);return existing;
                }
                var media=await MediaTools.Probe(config.Ffmpeg,temporary,cancel);
                DateTime? captured=confirmed ?? media.CapturedUtc ?? MediaTools.FilenameTime(info.Name);
                string path=Path.Combine(folder,hash+Path.GetExtension(source).ToLowerInvariant());
                if(!File.Exists(path))File.Move(temporary,path);
                var item=new ArchiveItem { Id=Guid.NewGuid().ToString("N"),RoomId=room,Hash=hash,SourceKey=key,OriginalName=info.Name,Path=path,Bytes=info.Length,
                    AddedUtc=DateTime.UtcNow,CapturedUtc=captured??info.LastWriteTimeUtc,Day=(captured??info.LastWriteTimeUtc).ToLocalTime().ToString("yyyy-MM-dd"),DateConfirmed=captured.HasValue,
                    Image=media.Image,Seconds=media.Seconds,State=captured.HasValue?"원본 보관·길이 확인 완료":"촬영 날짜 확인 필요" };
                Receipt(item);log("원본 보관 · "+item.OriginalName);return item;
            } finally { if(File.Exists(temporary))File.Delete(temporary); }
        }
        internal async Task ConfirmDate(string id,DateTime date) {
            await gate.WaitAsync(stop.Token);
            try {
                var item=items[id];
                if(Jobs.Any(j=>j.Inputs.Contains(id)))throw new InvalidOperationException("이미 편집에 사용된 원본입니다.");
                item.CapturedUtc=date.ToUniversalTime();item.Day=date.ToString("yyyy-MM-dd");item.DateConfirmed=true;item.State="원본 보관·날짜 확인 완료";Receipt(item);
            } finally { gate.Release();changed(); }
        }
        internal async Task Capture(string room,byte[] bytes,DateTime time,CancellationToken cancel) {
            if(!await gate.WaitAsync(0,cancel))return;
            try {
                using(var input=new MemoryStream(bytes))using(var image=Image.FromStream(input,true,true)) {
                    if((long)image.Width*image.Height>64000000)throw new IOException("카메라 사진 크기를 확인하세요.");
                }
                string hash;
                using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
                string folder=Path.Combine(settings().Archive,"원본");Directory.CreateDirectory(folder);
                string path=Path.Combine(folder,hash+".jpg");
                if(!File.Exists(path)) {
                    string temporary=path+".part";File.WriteAllBytes(temporary,bytes);File.Move(temporary,path);
                }
                var item=new ArchiveItem { Id=Guid.NewGuid().ToString("N"),Hash=hash,RoomId=room,OriginalName="카메라 "+time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),Path=path,Day=time.ToLocalTime().ToString("yyyy-MM-dd"),CapturedUtc=time,AddedUtc=DateTime.UtcNow,DateConfirmed=true,Image=true,Bytes=bytes.Length,State="사진 수집" };
                Receipt(item);
            } finally { gate.Release();if(!disposed)changed(); }
        }
        internal async Task Tick() {
            if(disposed || processing || !await gate.WaitAsync(0))return;
            ArchiveJob selected=null;
            try {
                var config=settings();
                if(config.WatchFolder) {
                    Directory.CreateDirectory(config.Inbox);
                    foreach(var room in config.Rooms) {
                        string folder=Path.Combine(config.Inbox,room.Id);Directory.CreateDirectory(folder);
                        var files=Directory.EnumerateFiles(folder).Where(MediaTools.Supported);
                        if(room==config.Rooms.First())files=files.Concat(Directory.EnumerateFiles(config.Inbox).Where(MediaTools.Supported));
                        foreach(string source in files.Where(NewSource).Take(30)) {
                            try { await ImportFile(source,room.Id,null,stop.Token); }
                            catch(IOException) { Status="가져오기 대기 · 파일 전송 완료와 영상 도구 설정을 확인하세요."; }
                        }
                    }
                }
                if(config.DailyJobs && DateTime.Now.Hour>=config.RunHour) {
                    foreach(var group in items.Values.Where(i=>i.DateConfirmed && String.CompareOrdinal(i.Day,DateTime.Now.ToString("yyyy-MM-dd"))<0).GroupBy(i=>new {i.RoomId,i.Day})) {
                        var previous=Jobs.Where(j=>j.RoomId==group.Key.RoomId && j.Day==group.Key.Day).ToList();
                        if(previous.Count>0 && (previous.Any(j=>j.State!="done" && j.State!="ready") || group.Max(i=>i.AddedUtc)>DateTime.UtcNow.AddMinutes(-30)))continue;
                        if(group.Any(i=>!Jobs.Any(j=>j.Inputs.Contains(i.Id))))Queue(group.Key.RoomId,group.Key.Day,config.VideoMode,previous.Count>0);
                    }
                }
                selected=Jobs.FirstOrDefault(j=>new[]{"queued","upload-retry","processing","uploading"}.Contains(j.State) && j.NextAttemptUtc<=DateTime.UtcNow);
                if(selected!=null)processing=true;
                else if(Status.StartsWith("영상"))Status="영상 작업 대기";
            } catch(OperationCanceledException) {}
            catch(Exception ex) { Status="영상 처리 확인 필요 · "+ex.Message; }
            finally { gate.Release();if(!disposed)changed(); }
            if(selected!=null) {
                try { await ProcessJob(selected); }
                catch(OperationCanceledException) {}
                finally { processing=false;if(!disposed)changed(); }
            }
        }
        bool NewSource(string file) {
            var info=new FileInfo(file);
            return info.LastWriteTimeUtc<DateTime.UtcNow.AddSeconds(-15) && !sources.Contains(Path.GetFullPath(file).ToUpperInvariant()+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks);
        }
        internal ArchiveJob Queue(string room,string day,string mode,bool additional=false) {
            var existing=Jobs.FirstOrDefault(j=>j.RoomId==room && j.Day==day);
            if(existing!=null && !additional)return existing;
            var selected=items.Values.Where(i=>i.RoomId==room && i.Day==day && i.DateConfirmed && (!additional || !Jobs.Any(j=>j.Inputs.Contains(i.Id)))).ToList();
            if(selected.Count==0)throw new InvalidOperationException("촬영 날짜를 확인한 원본이 없습니다.");
            bool images=mode=="images10";
            if(selected.Any(i=>i.Image!=images))throw new InvalidOperationException("사진과 영상 원본을 나누어 보관 모드를 선택하세요.");
            var job=new ArchiveJob { Id=Guid.NewGuid().ToString("N"),RoomId=room,Day=day,Mode=mode,Inputs=selected.Select(i=>i.Id).ToList(),State="queued",Detail=images?"사진 "+selected.Count+"장 · 10분 편집 대기":"원래 길이의 클립 편집 대기",CreatedUtc=DateTime.UtcNow };
            Jobs.Add(job);Persist();return job;
        }
        internal async Task Prepare(string room,string day,bool additional=false) {
            await gate.WaitAsync(stop.Token);
            try { if(processing)throw new InvalidOperationException("영상 작업을 마친 뒤 편집을 준비하세요.");Queue(room,day,settings().VideoMode,additional); }
            finally { gate.Release();changed(); }
        }
        internal async Task Retry(string id) {
            await gate.WaitAsync(stop.Token);
            try {
                if(processing)throw new InvalidOperationException("현재 영상 작업이 끝난 뒤 다시 시도하세요.");
                var job=Jobs.Single(j=>j.Id==id);
                if(job.State=="rendering" || job.State=="uploading")throw new InvalidOperationException("이미 처리 중인 작업입니다.");
                if(job.State=="review")throw new InvalidOperationException("중복 게시 방지를 위해 Studio 확인이 필요한 작업입니다. 자동 재업로드가 중지됐습니다.");
                if(job.State=="done")return;
                job.State=!String.IsNullOrEmpty(job.VideoId)?"processing":File.Exists(job.Output)?"upload-retry":"queued";job.NextAttemptUtc=DateTime.MinValue;Persist();
            } finally { gate.Release(); }
        }
        internal async Task UploadNow(string id) {
            await gate.WaitAsync(stop.Token);
            try {
                if(processing)throw new InvalidOperationException("현재 영상 작업이 끝난 뒤 다시 시도하세요.");
                var job=Jobs.Single(j=>j.Id==id);
                if(job.State!="ready")throw new InvalidOperationException("편집을 완료한 영상을 선택하세요.");
                job.State="upload-retry";job.NextAttemptUtc=DateTime.MinValue;Persist();
            } finally { gate.Release(); }
        }
        internal async Task Resolve(string id,string videoId,bool allowNewUpload) {
            await gate.WaitAsync(stop.Token);
            try {
                if(processing)throw new InvalidOperationException("현재 영상 작업이 끝난 뒤 확인하세요.");
                var job=Jobs.Single(j=>j.Id==id);
                if(job.State!="review")throw new InvalidOperationException("확인이 필요한 작업을 선택하세요.");
                if(!String.IsNullOrEmpty(videoId)) {
                    if(!System.Text.RegularExpressions.Regex.IsMatch(videoId,@"^[a-zA-Z0-9_-]{11}$"))throw new ArgumentException("YouTube Studio의 영상 ID 11자를 입력하세요.");
                    job.VideoId=videoId;job.State="processing";job.Detail="기존 영상의 처리·비공개 상태 확인 중";
                } else if(allowNewUpload) {
                    job.VideoId=null;job.SessionProtected=null;job.UploadStarted=false;job.State="upload-retry";job.Detail="Studio 확인 후 새 업로드 대기";
                } else throw new InvalidOperationException("기존 영상 ID 또는 새 업로드 확인이 필요합니다.");
                job.NextAttemptUtc=DateTime.MinValue;Persist();
            } finally { gate.Release(); }
        }
        async Task ProcessJob(ArchiveJob job) {
            var config=settings();
            try {
                if(job.State=="queued") {
                    job.State="rendering";job.Detail="영상 편집 중";Persist();
                    await MediaTools.Render(config,job,job.Inputs.Select(id=>items[id]).ToList(),stop.Token);
                    job.State=config.AutoUpload?"upload-retry":"ready";job.Detail="편집 완료 · "+(config.AutoUpload?"비공개 업로드 대기":"업로드 확인 대기");Persist();
                }
                if(job.State=="upload-retry" || job.State=="uploading") {
                    job.State="uploading";job.Detail="유튜브 비공개 전송 중";Persist();
                    var room=config.Rooms.FirstOrDefault(r=>r.Id==job.RoomId);
                    await youtube.Upload(config,job,job.Day+" "+(room==null?"내 방":room.Name)+" 기록",Persist,saveSettings,stop.Token);
                }
                if(job.State=="processing")await youtube.Check(config,job,Persist,saveSettings,stop.Token);
                if(job.State=="done")log(job.Day+" · 유튜브 비공개 보관 완료");
            } catch(OperationCanceledException) { throw; }
            catch(Exception ex) {
                job.Attempts++;job.State=job.State=="rendering"?"error":!String.IsNullOrEmpty(job.VideoId)?"processing":"upload-retry";
                job.Detail=ex is System.Net.Http.HttpRequestException?"유튜브 연결·권한을 확인하세요.":ex.Message;
                job.NextAttemptUtc=DateTime.UtcNow.AddMinutes(Math.Min(240,Math.Pow(2,Math.Min(job.Attempts,8))));Persist();
            }
        }
        internal object Snapshot() {
            return new { status=Status,items=items.Values.Where(i=>i.State!="사진 수집").OrderBy(i=>i.DateConfirmed).ThenByDescending(i=>i.AddedUtc).Take(40).Select(i=>new { id=i.Id,roomId=i.RoomId,name=i.OriginalName,day=i.Day,dateConfirmed=i.DateConfirmed,state=i.State,bytes=i.Bytes,seconds=i.Seconds,image=i.Image }).ToArray(),
                jobs=Jobs.OrderByDescending(j=>j.CreatedUtc).Take(30).Select(j=>new { id=j.Id,roomId=j.RoomId,day=j.Day,state=j.State,detail=j.Detail,count=j.Inputs.Count,extra=items.Values.Count(i=>i.RoomId==j.RoomId && i.Day==j.Day && i.DateConfirmed && !Jobs.Any(other=>other.Inputs.Contains(i.Id))),videoId=j.State=="done"?j.VideoId:null }).ToArray(),
                photoCounts=items.Values.Where(i=>i.State=="사진 수집").GroupBy(i=>new{i.RoomId,i.Day}).Select(g=>new { roomId=g.Key.RoomId,day=g.Key.Day,count=g.Count(),longestGapSeconds=g.OrderBy(i=>i.CapturedUtc).Zip(g.OrderBy(i=>i.CapturedUtc).Skip(1),(a,b)=>(b.CapturedUtc-a.CapturedUtc).TotalSeconds).DefaultIfEmpty(0).Max() }).ToArray() };
        }
        public void Dispose() { disposed=true;stop.Cancel(); }
    }
}
