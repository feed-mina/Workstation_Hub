using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MinaDesk;

class SmartHomeTests {
    sealed class Handler : HttpMessageHandler {
        internal Func<HttpRequestMessage,Task<HttpResponseMessage>> Run;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) { return Run(request); }
    }
    static int passed;
    static void Check(bool condition,string name) { if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);passed++; }
    static HttpResponseMessage Json(string text,int status=200) { return new HttpResponseMessage((HttpStatusCode)status){Content=new StringContent(text,Encoding.UTF8,"application/json")}; }
    static async Task Reject(Func<Task> run,string name) { bool rejected=false;try { await run(); }catch(InvalidOperationException) { rejected=true; }catch(ArgumentException) { rejected=true; }Check(rejected,name); }
    static async Task Ha() {
        int calls=0;bool offline=false;string posted=null,path=null;
        var handler=new Handler { Run=async request=> {
            Check(request.Headers.Authorization.Scheme=="Bearer","HA bearer header");
            if(offline)return Json("{}",503);
            if(request.Method==HttpMethod.Post) { calls++;posted=await request.Content.ReadAsStringAsync();path=request.RequestUri.AbsolutePath;return Json("[]"); }
            if(request.RequestUri.AbsolutePath.EndsWith("states"))return Json("[{\"entity_id\":\"vacuum.robot\",\"state\":\"docked\",\"attributes\":{\"supported_features\":8212}},{\"entity_id\":\"script.bedroom\",\"state\":\"off\",\"attributes\":{}},{\"entity_id\":\"media_player.nest\",\"state\":\"idle\",\"attributes\":{\"supported_features\":4608}},{\"entity_id\":\"binary_sensor.window\",\"state\":\"unknown\",\"attributes\":{}}]");
            return Json("[{\"domain\":\"vacuum\",\"services\":{\"start\":{},\"pause\":{},\"return_to_base\":{}}},{\"domain\":\"script\",\"services\":{\"turn_on\":{}}},{\"domain\":\"music_assistant\",\"services\":{\"play_media\":{}}}]");
        }};
        var settings=new SmartSettings { HaAddress="http://127.0.0.1:8123",HaTokenProtected=LocalData.Protect("test-token") };
        var room=new RoomBinding { Vacuum="vacuum.robot",CleanScript="script.bedroom",Speaker="media_player.nest",Classic="library://playlist/1" };
        using(var ha=new HomeAssistantClient(handler)) {
            await ha.Refresh(settings,CancellationToken.None);
            Check(ha.Can(room.Vacuum,8192)&&ha.ScriptReady(room.CleanScript),"capabilities from HA");
            Check(!ha.Find("binary_sensor.window").Available,"unknown window is unavailable");
            await ha.Execute(settings,room,"room-clean",CancellationToken.None);
            Check(path=="/api/services/script/turn_on"&&posted.Contains("script.bedroom"),"room uses mapped script");
            await ha.Execute(settings,room,"classic",CancellationToken.None);
            Check(path=="/api/services/music_assistant/play_media"&&posted.Contains("library://playlist/1")&&posted.Contains("playlist"),"music uses configured playlist");
            ha.UpdatedUtc=DateTime.UtcNow.AddMinutes(-1);
            await Reject(()=>ha.Execute(settings,room,"start",CancellationToken.None),"stale state blocks commands");
            Check(!ha.ScriptReady(room.CleanScript),"stale scripts disabled");
            offline=true;await ha.Refresh(settings,CancellationToken.None);
            Check(!ha.UpdatedUtc.HasValue&&!ha.Can(room.Vacuum,8192),"disconnect disables cached controls");
            await Reject(()=>ha.Execute(settings,room,"shell",CancellationToken.None),"unlisted action rejected");
            Check(calls==2,"no unintended command after failures");
        }
    }
    static SmartSettings Google() { return new SmartSettings { OAuthTokensProtected=LocalData.Protect(LocalData.Encode(new GoogleTokens { Access="test",Refresh="test-refresh",ExpiresUtc=DateTime.UtcNow.AddHours(1) })) }; }
    static async Task Youtube(string folder) {
        string video=Path.Combine(folder,"upload.mp4");File.WriteAllBytes(video,new byte[9000000]);
        int posts=0,chunks=0;long offset=0;bool verified=false;
        var handler=new Handler { Run=async request=> {
            if(request.Method==HttpMethod.Post) {
                posts++;var body=await request.Content.ReadAsStringAsync();Check(body.Contains("\"privacyStatus\":\"private\""),"upload always requests private");
                var response=Json("{}");response.Headers.Location=new Uri("https://www.googleapis.com/upload/youtube/v3/videos?upload_id=test");return response;
            }
            if(request.Method==HttpMethod.Get) { verified=true;return Json("{\"items\":[{\"status\":{\"privacyStatus\":\"private\"},\"processingDetails\":{\"processingStatus\":\"succeeded\"}}]}"); }
            var range=request.Content.Headers.ContentRange;
            if(!range.HasRange) { var response=Json("{}",308);if(offset>0)response.Headers.TryAddWithoutValidation("Range","bytes=0-"+(offset-1));return response; }
            Check(range.From==offset,"chunk resumes at server offset");chunks++;offset=range.To.Value+1;
            if(chunks==1)throw new HttpRequestException("connection lost after server accepted chunk");
            return Json("{\"id\":\"AbCdEfGhI12\"}");
        }};
        using(var youtube=new YouTubeArchive(handler)) {
            var job=new ArchiveJob { Output=video };var config=Google();bool interrupted=false;
            try { await youtube.Upload(config,job,"test",()=>{},()=>{},CancellationToken.None); }catch(HttpRequestException) { interrupted=true; }
            Check(interrupted&&job.UploadStarted&&!String.IsNullOrEmpty(job.SessionProtected),"session persisted before interrupted transfer");
            job=LocalData.Decode<ArchiveJob>(LocalData.Encode(job));
            await youtube.Upload(config,job,"test",()=>{},()=>{},CancellationToken.None);
            Check(posts==1&&chunks==2&&job.State=="processing"&&job.VideoId=="AbCdEfGhI12","restart resumes without creating duplicate video");
            await youtube.Check(config,job,()=>{},()=>{},CancellationToken.None);
            Check(verified&&job.State=="done","done requires successful processing and private status");
        }
        int attempts=0;
        using(var youtube=new YouTubeArchive(new Handler { Run=request=> { attempts++;return Task.FromResult(Json("{}",410)); } })) {
            var job=new ArchiveJob { Output=video,UploadStarted=true,SessionProtected=LocalData.Protect("https://www.googleapis.com/upload/youtube/v3/videos?upload_id=expired") };
            await youtube.Upload(Google(),job,"test",()=>{},()=>{},CancellationToken.None);
            Check(job.State=="review"&&attempts==1,"expired session waits for Studio review");
        }
        using(var youtube=new YouTubeArchive(new Handler { Run=request=>Task.FromResult(Json("{\"items\":[{\"status\":{\"privacyStatus\":\"public\"},\"processingDetails\":{\"processingStatus\":\"succeeded\"}}]}")) })) {
            var job=new ArchiveJob { VideoId="AbCdEfGhI12" };await youtube.Check(Google(),job,()=>{},()=>{},CancellationToken.None);
            Check(job.State=="review","public video never marked safely archived");
        }
    }
    static async Task Archive(string folder,string ffmpeg) {
        string data=Path.Combine(folder,"archive-test");Directory.CreateDirectory(data);
        var config=SmartSettings.New(data);config.Ffmpeg=ffmpeg;
        var room=config.Rooms[0];byte[] photo;
        using(var bitmap=new Bitmap(16,16))using(var stream=new MemoryStream()) { bitmap.Save(stream,ImageFormat.Jpeg);photo=stream.ToArray(); }
        using(var youtube=new YouTubeArchive()) {
            using(var archive=new ArchiveEngine(data,()=>config,()=>{},()=>{},message=>{},youtube)) {
                await archive.Capture(room.Id,photo,DateTime.UtcNow.AddSeconds(-6),CancellationToken.None);
                await archive.Capture(room.Id,photo,DateTime.UtcNow,CancellationToken.None);
                Check(archive.Items.Count()==2&&archive.Items.Select(i=>i.Path).Distinct().Count()==1,"identical snapshots retain two capture times and one original");
                var job=archive.Queue(room.Id,DateTime.Now.ToString("yyyy-MM-dd"),"images10");
                Check(job.Inputs.Count==2,"photo queue preserves chronological frame receipts");
                await Reject(()=>archive.ConfirmDate(job.Inputs[0],DateTime.Now),"used original date cannot be changed");
            }
            using(var recovered=new ArchiveEngine(data,()=>config,()=>{},()=>{},message=>{},youtube)) {
                Check(recovered.Items.Count()==2&&recovered.Jobs.Count==1,"archive journal and jobs recover after restart");
            }
            File.AppendAllText(Path.Combine(data,"archive-receipts.ndjson"),"{truncated");
            using(var recovered=new ArchiveEngine(data,()=>config,()=>{},()=>{},message=>{},youtube)) {
                Check(recovered.Items.Count()==2&&Directory.GetFiles(data,"*.recovery-*").Length==1,"crash-truncated journal is backed up and recovered");
            }
            if(String.IsNullOrEmpty(ffmpeg))return;
            string clip=Path.Combine(folder,"20261123_120000.mp4");
            await MediaTools.Run(ffmpeg,"-hide_banner -loglevel error -y -f lavfi -i color=c=green:s=320x240:d=1 -c:v libx264 "+ProjectLauncher.Quote(clip),60,CancellationToken.None);
            using(var archive=new ArchiveEngine(data,()=>config,()=>{},()=>{},message=>{},youtube)) {
                var item=await archive.Import(clip,room.Id,null);
                Check(item.DateConfirmed&&item.Day=="2026-11-23"&&item.Seconds>0,"real clip validated and capture filename parsed");
                string duplicate=Path.Combine(folder,"again.mp4");File.Copy(clip,duplicate);
                var repeat=await archive.Import(duplicate,room.Id,null);
                Check(item.Id==repeat.Id,"renamed source is deduplicated by content hash");
                archive.Jobs.First(j=>j.Mode=="images10").State="ready";
                await archive.Prepare(room.Id,item.Day);await archive.Tick();
                var job=archive.Jobs.Single(j=>j.Day==item.Day);
                Check(job.State=="ready"&&File.Exists(job.Output),"real clip render completes without automatic upload");
                Check(File.Exists(clip)&&File.Exists(item.Path),"source and archived original preserved");
                var photoJob=archive.Jobs.First(j=>j.Mode=="images10");
                await MediaTools.Render(config,photoJob,archive.Items.Where(i=>photoJob.Inputs.Contains(i.Id)).ToList(),CancellationToken.None);
                var result=await MediaTools.Probe(ffmpeg,photoJob.Output,CancellationToken.None);
                Check(Math.Abs(result.Seconds-600)<1,"real photo render passes 600-second probe");
            }
        }
    }
    static int Main(string[] args) {
        string folder=Path.Combine(Path.GetTempPath(),"Hub-smart-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try { Ha().GetAwaiter().GetResult();Youtube(folder).GetAwaiter().GetResult();Archive(folder,args.Length>0?args[0]:null).GetAwaiter().GetResult();Console.WriteLine("RESULT "+passed+" checks passed");return 0; }
        catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
