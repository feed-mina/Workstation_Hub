using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MinaDesk {
    internal sealed class CodexModelInfo {
        public string id {get;set;}
        public string title {get;set;}
        public string work {get;set;}
        public string tradeoff {get;set;}
        public int? context {get;set;}
        public bool listed {get;set;}
        public bool latest {get;set;}
    }

    internal sealed class CodexModelComparison : IDisposable {
        internal const string Guide="https://learn.chatgpt.com/docs/models";
        internal const string Release="https://openai.com/index/introducing-gpt-6-2-astra-sol/";
        const string BuiltInDate="2026-11-27";
        static readonly Regex Id=new Regex(@"\Agpt-(?<major>[0-9]{1,2})(?:\.(?<minor>[0-9]{1,2}))?(?:-(?<tier>[a-z]{2,20})(?:-[a-z0-9]+){0,2})?\z",RegexOptions.CultureInvariant);
        static readonly Regex Details=new Regex(@"<ModelDetails(?=\s)(?<fields>.*?)\s*/>",RegexOptions.Singleline|RegexOptions.CultureInvariant);
        static readonly Regex Slug=new Regex(@"\bslug\s*=\s*""(?<id>[a-z0-9.-]{4,60})""",RegexOptions.CultureInvariant);
        static readonly Regex DefaultContext=new Regex(@"title\s*:\s*""Default context in Codex""\s*,\s*value\s*:\s*""(?<tokens>[1-9][0-9, ]{2,10})\s+tokens""",RegexOptions.CultureInvariant|RegexOptions.IgnoreCase);
        static readonly Dictionary<string,CodexModelInfo> Baseline=new[] {
            Model("gpt-6.2-astra","여러 서비스에 걸친 장애 추적, 복잡한 코드 수정과 검증","깊은 추론에 적합합니다. 표준 속도에서 6.2 Sol보다 토큰당 크레딧을 더 사용합니다.",272000),
            Model("gpt-6.2-sol","일상 코딩, 코드 편집, 문서 작성, 여러 자료 요약","일상 작업에 적합합니다. 표준 속도에서 6.2 Astra보다 토큰당 크레딧을 적게 사용합니다.",500000),
            Model("gpt-6.1-sol","코드와 문서를 오가는 반복 작업과 장시간 작업","이전 Sol 계열로, 긴 문맥을 유지하는 작업의 비교 대상입니다.",500000),
            Model("gpt-6.1-terra","문서 작성, 화면·차트 해석, 앱에서 자료 확인","빠른 응답과 시각 자료 이해를 중심으로 설계됐습니다."),
            Model("gpt-6-astra","코드·앱·리서치를 아우르는 복잡한 작업","이전 Astra 계열의 심층 작업용 모델입니다."),
            Model("gpt-6-luna","범위가 명확한 반복 작업, 요약, 데이터 추출","간결하고 반복되는 작업에 효율적인 선택입니다.")
        }.ToDictionary(model=>model.id,StringComparer.Ordinal);

        readonly HttpClient http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,UseDefaultCredentials=false}){
            Timeout=TimeSpan.FromSeconds(9),MaxResponseContentBufferSize=1024*1024
        };
        readonly CancellationTokenSource stop=new CancellationTokenSource();
        CodexModelInfo[] models;
        DateTimeOffset? checkedAt;
        DateTimeOffset nextRead;
        string error;
        bool busy,disposed;

        internal CodexModelComparison(){models=Order(Baseline.Values.Select(row=>Copy(row,true,row.context)));}
        internal object Snapshot(string preferred) {
            return new {kind="model-comparison",models,busy,preferred,
                checkedAt=checkedAt.HasValue?checkedAt.Value.ToString("yyyy-MM-dd HH:mm"):null,builtInDate=BuiltInDate,error};
        }
        internal async Task Refresh(bool force,Action changed) {
            if(disposed || busy || DateTimeOffset.UtcNow<nextRead)return;
            if(!force && checkedAt.HasValue && DateTimeOffset.Now-checkedAt.Value<TimeSpan.FromMinutes(30))return;
            busy=true;error=null;changed();
            try {
                using(var response=await http.GetAsync(Guide+".md",stop.Token)){
                    response.EnsureSuccessStatusCode();
                    var parsed=Parse(await response.Content.ReadAsStringAsync());
                    if(disposed)return;
                    if(parsed.Length==0)throw new FormatException("공식 문서의 모델 표를 찾지 못했습니다.");
                    models=parsed;checkedAt=DateTimeOffset.Now;
                    nextRead=DateTimeOffset.UtcNow.AddSeconds(20);
                }
            }catch(Exception){
                if(!disposed){
                    error="공식 자료를 갱신하지 못했습니다. 마지막으로 확인한 비교 자료를 표시합니다.";
                    nextRead=DateTimeOffset.UtcNow.AddSeconds(15);
                }
            }finally{busy=false;if(!disposed)changed();}
        }

        internal static CodexModelInfo[] Parse(string markdown) {
            if(markdown==null || markdown.Length>1024*1024)return new CodexModelInfo[0];
            int start=markdown.IndexOf("## Recommended models",StringComparison.Ordinal);
            if(start<0)return new CodexModelInfo[0];
            int end=markdown.IndexOf("\n## ",start+1,StringComparison.Ordinal);
            string section=markdown.Substring(start,end<0?markdown.Length-start:end-start);
            var found=new Dictionary<string,CodexModelInfo>();
            foreach(Match item in Details.Matches(section)) {
                string fields=item.Groups["fields"].Value;
                string slug=Slug.Match(fields).Groups["id"].Value;
                if(!Id.IsMatch(slug) || found.ContainsKey(slug))continue;
                CodexModelInfo known;Baseline.TryGetValue(slug,out known);
                int context;
                string tokens=DefaultContext.Match(fields).Groups["tokens"].Value.Replace(",","").Replace(" ","");
                int? window=Int32.TryParse(tokens,NumberStyles.None,CultureInfo.InvariantCulture,out context) && context>=1000 && context<=50000000?
                    context:known==null?null:known.context;
                found[slug]=known==null?Model(slug,"공식 모델 안내에 등록된 모델입니다.","작업 용도와 요금은 공식 자료에서 확인할 수 있습니다.",window):Copy(known,true,window);
            }
            if(found.Count<2)return new CodexModelInfo[0];
            var early=Baseline.Values.Where(old=>!found.ContainsKey(old.id)).Select(old=>Copy(old,false,old.context));
            return Order(found.Values.Concat(early));
        }
        static CodexModelInfo[] Order(IEnumerable<CodexModelInfo> source) {
            var sorted=source.OrderByDescending(item=>item.listed).ThenByDescending(item=>Generation(item.id))
                .ThenBy(item=>Tier(item.id)).ThenBy(item=>item.id).Take(16).ToArray();
            int newest=Generation(sorted[0].id);
            foreach(var item in sorted)item.latest=item.listed && Generation(item.id)==newest;
            return sorted;
        }
        static int Generation(string id) {
            var match=Id.Match(id);int minor;
            Int32.TryParse(match.Groups["minor"].Value,out minor);
            return Int32.Parse(match.Groups["major"].Value,CultureInfo.InvariantCulture)*100+minor;
        }
        static int Tier(string id) {
            int rank=Array.IndexOf(new[]{"astra","sol","terra","luna","codex"},Id.Match(id).Groups["tier"].Value);
            return rank<0?5:rank;
        }
        static CodexModelInfo Model(string id,string work,string tradeoff,int? context=null) {
            string label=String.Join(" ",id.Split('-').Skip(2).Select(part=>CultureInfo.InvariantCulture.TextInfo.ToTitleCase(part)));
            return new CodexModelInfo{id=id,title=("GPT-"+id.Split('-')[1]+" "+label).TrimEnd(),work=work,tradeoff=tradeoff,context=context,listed=true};
        }
        static CodexModelInfo Copy(CodexModelInfo item,bool listed,int? context) {
            return new CodexModelInfo{id=item.id,title=item.title,work=item.work,tradeoff=item.tradeoff,context=context,listed=listed};
        }
        public void Dispose(){disposed=true;stop.Cancel();http.Dispose();}
    }
}
