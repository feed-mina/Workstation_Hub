using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace MinaDesk {
    internal static class LocalData {
        internal static string Encode(object value) { return new JavaScriptSerializer { MaxJsonLength=32*1024*1024 }.Serialize(value); }
        internal static T Decode<T>(string value) { return new JavaScriptSerializer { MaxJsonLength=32*1024*1024 }.Deserialize<T>(value); }
        internal static T Read<T>(string file, Func<T> fallback) {
            return File.Exists(file) ? Decode<T>(File.ReadAllText(file, Encoding.UTF8)) : fallback();
        }
        internal static void Write(string file, object value) {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temporary=file+".tmp";
            File.WriteAllText(temporary, Encode(value), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(temporary, file, file+".bak");
            else File.Move(temporary, file);
        }
        internal static string Protect(string text) {
            return String.IsNullOrEmpty(text) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));
        }
        internal static string Unprotect(string text) {
            return String.IsNullOrEmpty(text) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(text), null, DataProtectionScope.CurrentUser));
        }
    }
    public sealed class RoomBinding {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Vacuum { get; set; }
        public string CleanScript { get; set; }
        public string Speaker { get; set; }
        public string Classic { get; set; }
        public string Pop { get; set; }
        public string Window { get; set; }
        public string Dryer { get; set; }
        public string Camera { get; set; }
        public RoomBinding() { Id=Guid.NewGuid().ToString("N"); Name="새 방"; }
    }
    public sealed class SmartSettings {
        public string HaAddress { get; set; }
        public string HaTokenProtected { get; set; }
        public string Inbox { get; set; }
        public string Archive { get; set; }
        public string Ffmpeg { get; set; }
        public bool WatchFolder { get; set; }
        public bool DailyJobs { get; set; }
        public bool AutoUpload { get; set; }
        public bool CaptureCamera { get; set; }
        public int RunHour { get; set; }
        public string VideoMode { get; set; }
        public string OAuthClientProtected { get; set; }
        public string OAuthTokensProtected { get; set; }
        public List<RoomBinding> Rooms { get; set; }
        public SmartSettings() {
            HaAddress="http://127.0.0.1:8123";
            RunHour=8;
            VideoMode="clips";
            Rooms=new List<RoomBinding> { new RoomBinding { Name="내 방" },new RoomBinding { Name="침실" },new RoomBinding { Name="거실" } };
        }
        internal static SmartSettings New(string data) {
            return new SmartSettings { Inbox=Path.Combine(data,"영상 가져오기"),Archive=Path.Combine(data,"영상 보관") };
        }
    }
    public sealed class ArchiveItem {
        public string Id { get; set; }
        public string Hash { get; set; }
        public string SourceKey { get; set; }
        public string RoomId { get; set; }
        public string OriginalName { get; set; }
        public string Path { get; set; }
        public string Day { get; set; }
        public DateTime AddedUtc { get; set; }
        public DateTime CapturedUtc { get; set; }
        public bool DateConfirmed { get; set; }
        public bool Image { get; set; }
        public double Seconds { get; set; }
        public long Bytes { get; set; }
        public string State { get; set; }
    }
    public sealed class ArchiveJob {
        public string Id { get; set; }
        public string RoomId { get; set; }
        public string Day { get; set; }
        public List<string> Inputs { get; set; }
        public string Mode { get; set; }
        public string Output { get; set; }
        public string State { get; set; }
        public string Detail { get; set; }
        public string SessionProtected { get; set; }
        public string VideoId { get; set; }
        public bool UploadStarted { get; set; }
        public int Attempts { get; set; }
        public DateTime NextAttemptUtc { get; set; }
        public DateTime CreatedUtc { get; set; }
        public ArchiveJob() { Inputs=new List<string>(); }
    }
}
