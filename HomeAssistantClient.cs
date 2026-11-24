using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MinaDesk {
    internal sealed class HaEntity {
        internal string Id, Name, State;
        internal int Features;
        internal Dictionary<string,object> Attributes;
        internal bool Available { get { return State!="unavailable" && State!="unknown"; } }
    }
    internal sealed class HomeAssistantClient : IDisposable {
        readonly HttpClient client;
        internal Dictionary<string,HaEntity> Entities=new Dictionary<string,HaEntity>();
        internal HashSet<string> Services=new HashSet<string>();
        internal string Status="Home Assistant 연결 전";
        internal DateTime? UpdatedUtc;
        internal HomeAssistantClient() : this(new HttpClientHandler { AllowAutoRedirect=false }) {}
        internal HomeAssistantClient(HttpMessageHandler handler) { client=new HttpClient(handler) { Timeout=TimeSpan.FromSeconds(8) }; }
        internal static Uri BaseAddress(string address) {
            Uri uri;
            if(!Uri.TryCreate(address,UriKind.Absolute,out uri) || (uri.Scheme!="http" && uri.Scheme!="https") || uri.UserInfo.Length>0 || uri.Query.Length>0 || uri.Fragment.Length>0)
                throw new ArgumentException("Home Assistant 주소를 http://집주소:8123 형식으로 입력하세요.");
            return new Uri(address.TrimEnd('/')+"/");
        }
        internal async Task<object> Request(SmartSettings settings,string path,object data,CancellationToken token) {
            using(var request=new HttpRequestMessage(data==null?HttpMethod.Get:HttpMethod.Post,new Uri(BaseAddress(settings.HaAddress),"api/"+path))) {
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",LocalData.Unprotect(settings.HaTokenProtected));
                if(data!=null) request.Content=new StringContent(LocalData.Encode(data),Encoding.UTF8,"application/json");
                using(var response=await client.SendAsync(request,token)) {
                    if(response.StatusCode==HttpStatusCode.Unauthorized) throw new InvalidOperationException("Home Assistant 연결 토큰을 확인하세요.");
                    if(!response.IsSuccessStatusCode) throw new InvalidOperationException("Home Assistant 응답 오류 · "+(int)response.StatusCode);
                    return LocalData.Decode<object>(await response.Content.ReadAsStringAsync());
                }
            }
        }
        internal async Task Refresh(SmartSettings settings,CancellationToken token) {
            if(String.IsNullOrEmpty(settings.HaTokenProtected)) { Status="설정에서 Home Assistant를 연결하세요."; UpdatedUtc=null; return; }
            try {
                var states=(IEnumerable)await Request(settings,"states",null,token);
                var entities=new Dictionary<string,HaEntity>();
                foreach(Dictionary<string,object> state in states) {
                    var attributes=(Dictionary<string,object>)state["attributes"];
                    string id=(string)state["entity_id"];
                    object name,features;
                    entities[id]=new HaEntity { Id=id,State=(string)state["state"],Attributes=attributes,
                        Name=attributes.TryGetValue("friendly_name",out name)?Convert.ToString(name):id,
                        Features=attributes.TryGetValue("supported_features",out features)?Convert.ToInt32(features):0 };
                }
                var services=new HashSet<string>();
                foreach(Dictionary<string,object> domain in (IEnumerable)await Request(settings,"services",null,token))
                    foreach(string name in ((Dictionary<string,object>)domain["services"]).Keys) services.Add(domain["domain"]+"."+name);
                Entities=entities; Services=services; UpdatedUtc=DateTime.UtcNow; Status="Home Assistant 연결됨";
            } catch(OperationCanceledException) { throw; }
            catch(Exception ex) { Status=ex is HttpRequestException?"Home Assistant에 연결할 수 없습니다.":ex.Message; UpdatedUtc=null; }
        }
        internal HaEntity Find(string id) { HaEntity entity; return id!=null && Entities.TryGetValue(id,out entity)?entity:null; }
        internal object View(string id) {
            var entity=Find(id);
            return entity==null?null:new { id=entity.Id,name=entity.Name,state=entity.State,available=UpdatedUtc.HasValue&&entity.Available };
        }
        internal bool Can(string id,int feature) {
            var entity=Find(id);
            return UpdatedUtc.HasValue && DateTime.UtcNow-UpdatedUtc.Value<TimeSpan.FromSeconds(20) && entity!=null && entity.Available && (entity.Features & feature)!=0;
        }
        internal bool ScriptReady(string id) { var entity=Find(id); return UpdatedUtc.HasValue && DateTime.UtcNow-UpdatedUtc.Value<TimeSpan.FromSeconds(20) && entity!=null && entity.Available && id.StartsWith("script.") && Services.Contains("script.turn_on"); }
        internal async Task<byte[]> Camera(SmartSettings settings,string entity,CancellationToken token) {
            var device=Find(entity);
            if(!UpdatedUtc.HasValue || device==null || !device.Available || !Regex.IsMatch(entity,@"^camera\.[a-z0-9_]+$"))throw new InvalidOperationException("사진 수집용 카메라를 연결하세요.");
            using(var request=new HttpRequestMessage(HttpMethod.Get,new Uri(BaseAddress(settings.HaAddress),"api/camera_proxy/"+entity))) {
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",LocalData.Unprotect(settings.HaTokenProtected));
                using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token)) {
                    response.EnsureSuccessStatusCode();
                    if(response.Content.Headers.ContentLength>20*1024*1024)throw new IOException("카메라 사진이 너무 큽니다.");
                    using(var input=await response.Content.ReadAsStreamAsync())
                    using(var output=new System.IO.MemoryStream()) {
                        var buffer=new byte[65536];int count;
                        while((count=await input.ReadAsync(buffer,0,buffer.Length,token))>0) {
                            output.Write(buffer,0,count);
                            if(output.Length>20*1024*1024)throw new IOException("카메라 사진이 너무 큽니다.");
                        }
                        return output.ToArray();
                    }
                }
            }
        }
        internal async Task Execute(SmartSettings settings,RoomBinding room,string action,CancellationToken token) {
            string service,entity;
            var data=new Dictionary<string,object>();
            switch(action) {
                case "start": service="vacuum.start";entity=room.Vacuum;if(!Can(entity,8192))throw new InvalidOperationException("청소 시작 지원과 기기 연결을 확인하세요.");break;
                case "pause": service="vacuum.pause";entity=room.Vacuum;if(!Can(entity,4))throw new InvalidOperationException("일시정지 지원과 기기 연결을 확인하세요.");break;
                case "return": service="vacuum.return_to_base";entity=room.Vacuum;if(!Can(entity,16))throw new InvalidOperationException("복귀 지원과 기기 연결을 확인하세요.");break;
                case "room-clean": service="script.turn_on";entity=room.CleanScript;if(!ScriptReady(entity))throw new InvalidOperationException("이 방의 청소 스크립트를 먼저 연결하세요.");break;
                case "classic": case "pop":
                    service="music_assistant.play_media";entity=room.Speaker;
                    if(!Can(entity,512))throw new InvalidOperationException("음악 재생 장치 연결을 확인하세요.");
                    string playlist=action=="classic"?room.Classic:room.Pop;
                    if(String.IsNullOrWhiteSpace(playlist))throw new InvalidOperationException("재생목록을 설정에서 연결하세요.");
                    data["media_id"]=playlist;data["media_type"]="playlist";data["enqueue"]="replace";break;
                case "music-stop": service="media_player.media_stop";entity=room.Speaker;if(!Can(entity,4096))throw new InvalidOperationException("이 장치의 정지 지원과 연결을 확인하세요.");break;
                default: throw new ArgumentException("지원하지 않는 집 명령입니다.");
            }
            if(!Services.Contains(service) || !Regex.IsMatch(entity??"",@"^[a-z_]+\.[a-z0-9_]+$")) throw new InvalidOperationException("Home Assistant에서 해당 동작을 사용할 수 없습니다.");
            data["entity_id"]=entity;
            await Request(settings,"services/"+service.Replace('.', '/'),data,token);
        }
        public void Dispose() { client.Dispose(); }
    }
}
