using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MinaDesk {
    internal sealed class MediaInfo {
        internal double Seconds;
        internal bool Image;
        internal DateTime? CapturedUtc;
    }
    internal static class MediaTools {
        internal static readonly HashSet<string> ImageExtensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };
        internal static readonly HashSet<string> VideoExtensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".mkv", ".m4v" };
        internal static bool Supported(string file) { string extension=Path.GetExtension(file);return ImageExtensions.Contains(extension)||VideoExtensions.Contains(extension); }
        internal static async Task<string> Run(string executable,string arguments,int seconds,CancellationToken cancel) {
            if(!File.Exists(executable))throw new FileNotFoundException("설정에서 ffmpeg.exe 위치를 지정하세요.");
            using(var process=new Process { StartInfo=new ProcessStartInfo(executable,arguments) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true } })
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
                timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
                process.Start();
                var output=process.StandardOutput.ReadToEndAsync();
                var error=process.StandardError.ReadToEndAsync();
                try {
                    while(!process.HasExited) await Task.Delay(150,timeout.Token);
                    string text=await output;
                    await error;
                    if(process.ExitCode!=0)throw new IOException("영상 도구 실행 실패 · 코덱과 파일을 확인하세요. 종료 코드 "+process.ExitCode);
                    return text;
                } catch {
                    try { if(!process.HasExited)process.Kill(); } catch(InvalidOperationException) {}
                    throw;
                }
            }
        }
        internal static async Task<MediaInfo> Probe(string ffmpeg,string file,CancellationToken cancel) {
            string probe=Path.Combine(Path.GetDirectoryName(ffmpeg??"")??"","ffprobe.exe");
            string text=await Run(probe,"-v error -show_format -show_streams -of json "+ProjectLauncher.Quote(file),45,cancel);
            var root=LocalData.Decode<Dictionary<string,object>>(text);
            var streams=((IEnumerable)root["streams"]).Cast<Dictionary<string,object>>().ToList();
            if(!streams.Any(s=>s.ContainsKey("codec_type")&&(string)s["codec_type"]=="video"))throw new IOException("영상 또는 사진 화면을 찾을 수 없습니다.");
            var format=(Dictionary<string,object>)root["format"];
            object duration;
            double length;
            Double.TryParse(format.TryGetValue("duration",out duration)?Convert.ToString(duration):"",NumberStyles.Float,CultureInfo.InvariantCulture,out length);
            bool image=ImageExtensions.Contains(Path.GetExtension(file));
            if(!image && (length<=0 || Double.IsInfinity(length) || Double.IsNaN(length)))throw new IOException("영상 재생 길이를 확인할 수 없습니다.");
            DateTimeOffset captured;
            DateTime? utc=null;
            object tags,creation;
            if(format.TryGetValue("tags",out tags) && ((Dictionary<string,object>)tags).TryGetValue("creation_time",out creation) && DateTimeOffset.TryParse(Convert.ToString(creation),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out captured))utc=captured.UtcDateTime;
            return new MediaInfo { Seconds=length,Image=image,CapturedUtc=utc };
        }
        internal static DateTime? FilenameTime(string name) {
            var match=Regex.Match(name,@"(?<!\d)(\d{8})[-_](\d{6})(?!\d)");
            DateTime value;
            return match.Success && DateTime.TryParseExact(match.Groups[1].Value+match.Groups[2].Value,"yyyyMMddHHmmss",CultureInfo.InvariantCulture,DateTimeStyles.AssumeLocal,out value)?(DateTime?)value.ToUniversalTime():null;
        }
        internal static async Task Render(SmartSettings settings,ArchiveJob job,IList<ArchiveItem> items,CancellationToken cancel) {
            string folder=Path.Combine(settings.Archive,"완성 영상");Directory.CreateDirectory(folder);
            string final=Path.Combine(folder,job.Id+".mp4");
            string temporary=Path.Combine(folder,job.Id+".part.mp4");
            string list=Path.Combine(folder,job.Id+".txt");
            var ordered=items.OrderBy(i=>i.CapturedUtc).ThenBy(i=>i.OriginalName,StringComparer.OrdinalIgnoreCase).ToList();
            if(ordered.Count==0)throw new IOException("편집할 원본이 없습니다.");
            if(job.Mode=="images10" && ordered.Any(i=>!i.Image))throw new IOException("하루 10분 사진 모드에는 사진 원본만 선택하세요.");
            if(job.Mode=="clips" && ordered.Any(i=>i.Image))throw new IOException("클립 모드에는 영상 원본만 선택하세요.");
            var lines=new List<string> { "ffconcat version 1.0" };
            foreach(var item in ordered) {
                // Archive paths are generated from GUIDs/hashes. Quote ffconcat paths separately from process arguments.
                lines.Add("file '"+item.Path.Replace("\\","/").Replace("'","'\\''")+"'");
                if(job.Mode=="images10")lines.Add("duration "+(600.0/ordered.Count).ToString("R",CultureInfo.InvariantCulture));
            }
            if(job.Mode=="images10")lines.Add(lines[lines.Count-2]);
            File.WriteAllLines(list,lines,new UTF8Encoding(false));
            string options=job.Mode=="images10"?" -t 600":"";
            try {
                await Run(settings.Ffmpeg,"-nostdin -hide_banner -loglevel error -y -f concat -safe 0 -i "+ProjectLauncher.Quote(list)+options+" -an -vf "+ProjectLauncher.Quote("scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,setsar=1")+" -r 24 -c:v libx264 -pix_fmt yuv420p -movflags +faststart "+ProjectLauncher.Quote(temporary),7200,cancel);
                var info=await Probe(settings.Ffmpeg,temporary,cancel);
                if(job.Mode=="images10" && Math.Abs(info.Seconds-600)>1)throw new IOException("완성 영상의 10분 길이 검사를 통과하지 못했습니다.");
                if(File.Exists(final))File.Replace(temporary,final,null);else File.Move(temporary,final);
                job.Output=final;
            } finally { if(File.Exists(list))File.Delete(list); }
        }
    }
}
