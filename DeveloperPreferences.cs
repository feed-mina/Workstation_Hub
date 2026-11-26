using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MinaDesk {
    public sealed class DeveloperPreferences {
        public int Version { get; set; }
        public string Model { get; set; }
        public string Effort { get; set; }
        public string City { get; set; }
        public Dictionary<string,string> Branches { get; set; }
        public Dictionary<string,string> Prompts { get; set; }
        public Dictionary<string,string> Threads { get; set; }
        public Dictionary<string,string> Links { get; set; }
        public Dictionary<string,string> Folders { get; set; }

        public DeveloperPreferences() {
            Version=1;
            Model="앱 기본 모델";
            Effort="앱 기본값";
            Branches=new Dictionary<string,string>();
            Prompts=new Dictionary<string,string>();
            Threads=new Dictionary<string,string>();
            Links=new Dictionary<string,string>();
            Folders=new Dictionary<string,string>();
        }
    }

    internal sealed class DeveloperPreferenceStore {
        internal static readonly string[] Models={"앱 기본 모델","GPT-6.2 Sol","GPT-6.2 Astra"};
        internal static readonly string[] Efforts={"앱 기본값","Low","Medium","High","Extra High"};
        internal static readonly Dictionary<string,string> LinkNames=new Dictionary<string,string> {
            {"web-work-cycle","Work-cycle"},{"web-nblog","NBlog"},{"web-kmovement","KMovement"},
            {"web-portfolio","포트폴리오"},{"web-ha","Home Assistant"},{"web-preview","배포 프리뷰"}
        };
        internal static readonly Dictionary<string,string> FolderNames=new Dictionary<string,string> {
            {"folder-work","작업 폴더"},{"folder-me","ME 문서"},{"folder-skills","스킬 폴더"},{"folder-build","빌드 결과"}
        };
        static readonly Dictionary<string,string> BuiltInLinks=new Dictionary<string,string> {
            {"web-work-cycle","https://work-cycle.evolvix.workers.dev/"},
            {"web-github","https://github.com/"},{"web-drive","https://drive.google.com/"},
            {"web-calendar","https://calendar.google.com/"},{"web-chatgpt","https://chatgpt.com/"},
            {"web-claude","https://claude.ai/"},{"web-youtube","https://www.youtube.com/"},{"web-naver","https://blog.naver.com/"},
            {"usage-codex","https://chatgpt.com/codex/settings/usage"},
            {"usage-claude","https://claude.ai/settings/usage"},
            {"codex-help","https://learn.chatgpt.com/docs/reference/commands"},
            {"weather-source","https://open-meteo.com/"}
        };

        readonly string path;
        internal DeveloperPreferences Value { get; private set; }
        internal string LoadNotice { get; private set; }
        internal bool ReadOnly { get; private set; }

        internal DeveloperPreferenceStore(string data) {
            path=Path.Combine(data,"developer-console.json");
            if(!File.Exists(path) && !File.Exists(path+".bak")){Value=new DeveloperPreferences();return;}
            try { Value=Read(path); }
            catch(Exception ex) {
                if(!StorageError(ex))throw;
                try {
                    Value=Read(path+".bak");
                    LoadNotice="개발자 설정을 백업에서 읽었습니다.";
                }catch(Exception backup) {
                    if(!StorageError(backup))throw;
                    Value=new DeveloperPreferences();
                    ReadOnly=true;
                    LoadNotice="개발자 설정을 읽을 수 없어 기존 파일을 보존했습니다.";
                }
            }
        }
        static bool StorageError(Exception ex) {
            return ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException;
        }
        static DeveloperPreferences Read(string file) {
            var value=LocalData.Read<DeveloperPreferences>(file,()=>null);
            if(value==null || value.Version!=1 || value.Branches==null || value.Prompts==null || value.Threads==null || value.Links==null || value.Folders==null)
                throw new InvalidDataException("개발자 설정 형식을 확인하세요.");
            return value;
        }
        internal void Change(Action<DeveloperPreferences> update) {
            if(ReadOnly)throw new InvalidOperationException(LoadNotice);
            var next=LocalData.Decode<DeveloperPreferences>(LocalData.Encode(Value));
            update(next);
            LocalData.Write(path,next);
            Value=next;
        }
        internal static string Lookup(Dictionary<string,string> values,string key) {
            string value;
            return key!=null && values.TryGetValue(key,out value)?value:null;
        }
        internal string Model { get {return Models.Contains(Value.Model)?Value.Model:Models[0];} }
        internal string Effort { get {return Efforts.Contains(Value.Effort)?Value.Effort:Efforts[0];} }
        internal string WebAddress(string key) {
            string stored=LinkNames.ContainsKey(key)?Lookup(Value.Links,key):null;
            if(!String.IsNullOrEmpty(stored)) {
                try{return CheckWeb(key,stored);}
                catch(ArgumentException){}
            }
            return Lookup(BuiltInLinks,key);
        }
        internal static string CheckWeb(string key,string entered) {
            if(String.IsNullOrWhiteSpace(entered))return null;
            Uri address;
            entered=entered.Trim();
            if(entered.Length>1500 || !Uri.TryCreate(entered,UriKind.Absolute,out address) || String.IsNullOrEmpty(address.Host) ||
               (address.Scheme!="https" && !(key=="web-ha" && address.Scheme=="http")) || address.UserInfo.Length>0)
                throw new ArgumentException("사용할 웹 주소를 https:// 형식으로 입력하세요. Home Assistant는 http://도 사용할 수 있습니다.");
            return address.AbsoluteUri;
        }
        internal static string ThreadId(string input) {
            input=(input??"").Trim();
            if(input.Length==0)return null;
            Uri link;
            if(Uri.TryCreate(input,UriKind.Absolute,out link) && link.Scheme=="codex" && link.Host=="threads" && link.Query.Length==0 && link.Fragment.Length==0)
                input=link.AbsolutePath.Trim('/');
            if(!Regex.IsMatch(input,@"\A[A-Za-z0-9_-]{8,100}\z"))
                throw new ArgumentException("Codex의 채팅 링크(codex://threads/…) 또는 채팅 ID를 입력하세요.");
            return input;
        }
    }
}
