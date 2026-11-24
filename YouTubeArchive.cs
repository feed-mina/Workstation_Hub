using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MinaDesk {
    public sealed class GoogleTokens {
        public string Access { get; set; }
        public string Refresh { get; set; }
        public DateTime ExpiresUtc { get; set; }
    }
    internal sealed class YouTubeArchive : IDisposable {
        readonly HttpClient http;
        internal YouTubeArchive() : this(new HttpClientHandler { AllowAutoRedirect=false }) {}
        internal YouTubeArchive(HttpMessageHandler handler) { http=new HttpClient(handler) { Timeout=TimeSpan.FromMinutes(3) }; }
        internal static Dictionary<string,object> ImportClient(string file) {
            var root=LocalData.Decode<Dictionary<string,object>>(File.ReadAllText(file));object installed;
            if(!root.TryGetValue("installed",out installed))throw new ArgumentException("Google의 데스크톱 앱 OAuth 클라이언트 JSON을 선택하세요.");
            var data=(Dictionary<string,object>)installed;
            if(!Convert.ToString(data["client_id"]).EndsWith(".apps.googleusercontent.com",StringComparison.Ordinal))throw new ArgumentException("OAuth 클라이언트 ID를 확인하세요.");
            return data;
        }
        static string RandomKey() {
            using(var random=RandomNumberGenerator.Create()) { var bytes=new byte[32];random.GetBytes(bytes);return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_'); }
        }
        static string Challenge(string verifier) {
            using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+','-').Replace('/','_');
        }
        internal async Task Authorize(SmartSettings settings,CancellationToken cancel) {
            var client=LocalData.Decode<Dictionary<string,object>>(LocalData.Unprotect(settings.OAuthClientProtected));
            string verifier=RandomKey(),state=RandomKey();
            var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start();
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
                timeout.CancelAfter(TimeSpan.FromMinutes(4));
                using(timeout.Token.Register(()=>listener.Stop())) {
                    try {
                        int port=((IPEndPoint)listener.LocalEndpoint).Port;
                        string redirect="http://127.0.0.1:"+port+"/";
                        string url="https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id="+Uri.EscapeDataString((string)client["client_id"])+"&redirect_uri="+Uri.EscapeDataString(redirect)+"&scope="+Uri.EscapeDataString("https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube.readonly")+"&access_type=offline&prompt=consent&state="+state+"&code_challenge="+Challenge(verifier)+"&code_challenge_method=S256";
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute=true });
                        string code=null;
                        while(code==null) {
                            using(var connection=await listener.AcceptTcpClientAsync())
                            using(timeout.Token.Register(()=>connection.Close()))
                            using(var stream=connection.GetStream()) {
                                var reader=new StreamReader(stream,Encoding.ASCII,false,4096,true);
                                string line=await reader.ReadLineAsync();
                                if(line==null || line.Length>8192)continue;
                                string[] request=line.Split(' ');
                                Uri callback;
                                if(request.Length!=3 || request[0]!="GET" || !Uri.TryCreate(redirect.TrimEnd('/')+request[1],UriKind.Absolute,out callback))continue;
                                var query=callback.Query.TrimStart('?').Split('&').Select(p=>p.Split(new[]{'='},2)).Where(p=>p.Length==2).ToDictionary(p=>WebUtility.UrlDecode(p[0]),p=>WebUtility.UrlDecode(p[1]));
                                string received;
                                bool valid=query.TryGetValue("state",out received) && received==state;
                                if(valid && query.ContainsKey("code"))code=query["code"];
                                string message=code!=null?"연결 응답을 받았습니다. Hub로 돌아가세요.":"연결을 완료하지 못했습니다. Hub에서 다시 시도하세요.";
                                byte[] body=Encoding.UTF8.GetBytes(message);
                                byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 "+(code!=null?"200 OK":"400 Bad Request")+"\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n");
                                await stream.WriteAsync(header,0,header.Length,timeout.Token);await stream.WriteAsync(body,0,body.Length,timeout.Token);
                                if(valid && query.ContainsKey("error"))throw new InvalidOperationException("Google 연결을 취소했습니다.");
                            }
                        }
                        var values=ClientFields(client);
                        values["grant_type"]="authorization_code";values["code"]=code;values["redirect_uri"]=redirect;values["code_verifier"]=verifier;
                        settings.OAuthTokensProtected=LocalData.Protect(LocalData.Encode(await Exchange(values,timeout.Token)));
                    } catch(SocketException) { timeout.Token.ThrowIfCancellationRequested();throw; }
                    finally { listener.Stop(); }
                }
            }
        }
        static Dictionary<string,string> ClientFields(Dictionary<string,object> client) {
            var data=new Dictionary<string,string> { {"client_id",Convert.ToString(client["client_id"])} };object secret;
            if(client.TryGetValue("client_secret",out secret))data["client_secret"]=Convert.ToString(secret);
            return data;
        }
        async Task<GoogleTokens> Exchange(Dictionary<string,string> fields,CancellationToken cancel) {
            using(var response=await http.PostAsync("https://oauth2.googleapis.com/token",new FormUrlEncodedContent(fields),cancel)) {
                if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Google 연결을 갱신할 수 없습니다. 계정을 다시 연결하세요.");
                var data=LocalData.Decode<Dictionary<string,object>>(await response.Content.ReadAsStringAsync());object refresh;
                return new GoogleTokens { Access=Convert.ToString(data["access_token"]),Refresh=data.TryGetValue("refresh_token",out refresh)?Convert.ToString(refresh):null,ExpiresUtc=DateTime.UtcNow.AddSeconds(Convert.ToDouble(data["expires_in"])-60) };
            }
        }
        async Task<string> Access(SmartSettings settings,Action save,CancellationToken cancel) {
            if(String.IsNullOrEmpty(settings.OAuthTokensProtected))throw new InvalidOperationException("설정에서 유튜브 계정을 연결하세요.");
            var tokens=LocalData.Decode<GoogleTokens>(LocalData.Unprotect(settings.OAuthTokensProtected));
            if(tokens.ExpiresUtc<=DateTime.UtcNow) {
                var fields=ClientFields(LocalData.Decode<Dictionary<string,object>>(LocalData.Unprotect(settings.OAuthClientProtected)));
                fields["grant_type"]="refresh_token";fields["refresh_token"]=tokens.Refresh;
                var updated=await Exchange(fields,cancel);updated.Refresh=tokens.Refresh;
                tokens=updated;settings.OAuthTokensProtected=LocalData.Protect(LocalData.Encode(tokens));save();
            }
            return tokens.Access;
        }
        static Uri SessionAddress(string address) {
            Uri uri;
            if(!Uri.TryCreate(address,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.UserInfo.Length>0||(uri.Host!="www.googleapis.com"&&uri.Host!="youtube.googleapis.com")||!uri.AbsolutePath.StartsWith("/upload/",StringComparison.Ordinal))throw new InvalidOperationException("업로드 세션 주소를 확인할 수 없습니다.");
            return uri;
        }
        async Task<HttpResponseMessage> Send(HttpRequestMessage request,SmartSettings settings,Action save,CancellationToken cancel) {
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",await Access(settings,save,cancel));
            return await http.SendAsync(request,cancel);
        }
        static long Offset(HttpResponseMessage response) {
            IEnumerable<string> values;
            if(!response.Headers.TryGetValues("Range",out values))return 0;
            var match=Regex.Match(values.First(),@"bytes=0-(\d+)$");
            if(!match.Success)throw new IOException("업로드 진행 위치를 읽을 수 없습니다.");
            return Int64.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture)+1;
        }
        async Task<bool> Completed(HttpResponseMessage response,ArchiveJob job,Action persist) {
            if(response.StatusCode!=HttpStatusCode.OK && response.StatusCode!=HttpStatusCode.Created)return false;
            var data=LocalData.Decode<Dictionary<string,object>>(await response.Content.ReadAsStringAsync());
            string id=Convert.ToString(data["id"]);
            if(!Regex.IsMatch(id,@"^[a-zA-Z0-9_-]{11}$"))throw new IOException("업로드 결과의 영상 번호를 확인하세요.");
            job.VideoId=id;job.State="processing";job.Detail="유튜브 처리·비공개 확인 중";persist();return true;
        }
        internal async Task Upload(SmartSettings settings,ArchiveJob job,string title,Action persist,Action save,CancellationToken cancel) {
            long size=new FileInfo(job.Output).Length;
            if(String.IsNullOrEmpty(job.SessionProtected)) {
                if(job.UploadStarted) { job.State="review";job.Detail="이전 업로드 결과를 YouTube Studio에서 확인하세요.";persist();return; }
                var metadata=new { snippet=new { title=title.Length>95?title.Substring(0,95):title,categoryId="22",description="Mini Workstation Hub 개인 영상 보관" },status=new { privacyStatus="private",selfDeclaredMadeForKids=false } };
                using(var request=new HttpRequestMessage(HttpMethod.Post,"https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status&notifySubscribers=false")) {
                    request.Content=new StringContent(LocalData.Encode(metadata),Encoding.UTF8,"application/json");
                    request.Headers.Add("X-Upload-Content-Length",size.ToString(CultureInfo.InvariantCulture));request.Headers.Add("X-Upload-Content-Type","video/mp4");
                    using(var response=await Send(request,settings,save,cancel)) {
                        response.EnsureSuccessStatusCode();
                        job.SessionProtected=LocalData.Protect(SessionAddress(response.Headers.Location.AbsoluteUri).AbsoluteUri);
                        job.UploadStarted=true;persist();
                    }
                }
            }
            Uri session=SessionAddress(LocalData.Unprotect(job.SessionProtected));
            long offset;
            using(var request=new HttpRequestMessage(HttpMethod.Put,session)) {
                request.Content=new ByteArrayContent(new byte[0]);request.Content.Headers.ContentRange=new ContentRangeHeaderValue(size);
                using(var response=await Send(request,settings,save,cancel)) {
                    if(await Completed(response,job,persist))return;
                    if(response.StatusCode==HttpStatusCode.NotFound || response.StatusCode==HttpStatusCode.Gone) { job.State="review";job.Detail="업로드 세션 만료 · 중복 방지를 위해 Studio 확인이 필요합니다.";persist();return; }
                    if((int)response.StatusCode!=308)throw new IOException("업로드 진행 조회 실패 · "+(int)response.StatusCode);
                    offset=Offset(response);
                }
            }
            using(var file=File.OpenRead(job.Output)) {
                if(offset<0||offset>size)throw new IOException("업로드 위치가 파일 범위를 벗어났습니다.");
                file.Position=offset;
                var buffer=new byte[8*1024*1024];
                while(offset<size) {
                    int needed=(int)Math.Min(buffer.Length,size-offset),read=0;
                    while(read<needed) { int count=await file.ReadAsync(buffer,read,needed-read,cancel);if(count==0)throw new EndOfStreamException();read+=count; }
                    using(var request=new HttpRequestMessage(HttpMethod.Put,session)) {
                        request.Content=new ByteArrayContent(buffer,0,read);request.Content.Headers.ContentType=new MediaTypeHeaderValue("video/mp4");request.Content.Headers.ContentRange=new ContentRangeHeaderValue(offset,offset+read-1,size);
                        using(var response=await Send(request,settings,save,cancel)) {
                            if(await Completed(response,job,persist))return;
                            if((int)response.StatusCode!=308)throw new IOException("영상 전송 실패 · "+(int)response.StatusCode);
                            long next=Offset(response);
                            if(next<=offset||next>offset+read)throw new IOException("영상 전송 진행 위치를 다시 확인해야 합니다.");
                            offset=next;file.Position=offset;
                        }
                    }
                }
            }
            throw new IOException("유튜브의 영상 처리 결과를 다시 확인해야 합니다.");
        }
        internal async Task Check(SmartSettings settings,ArchiveJob job,Action persist,Action save,CancellationToken cancel) {
            using(var request=new HttpRequestMessage(HttpMethod.Get,"https://www.googleapis.com/youtube/v3/videos?part=status,processingDetails&id="+Uri.EscapeDataString(job.VideoId)))
            using(var response=await Send(request,settings,save,cancel)) {
                response.EnsureSuccessStatusCode();
                var data=LocalData.Decode<Dictionary<string,object>>(await response.Content.ReadAsStringAsync());
                var items=((IEnumerable)data["items"]).Cast<Dictionary<string,object>>().ToList();
                if(items.Count==0)throw new IOException("유튜브에서 영상 상태를 아직 확인하지 못했습니다.");
                var status=(Dictionary<string,object>)items[0]["status"];object details;
                string processing=items[0].TryGetValue("processingDetails",out details)?Convert.ToString(((Dictionary<string,object>)details)["processingStatus"]):"processing";
                if(Convert.ToString(status["privacyStatus"])!="private") { job.State="review";job.Detail="유튜브 영상의 비공개 설정을 확인하세요."; }
                else if(processing=="succeeded") { job.State="done";job.Detail="비공개 보관 완료"; }
                else if(processing=="failed"||processing=="terminated") { job.State="review";job.Detail="유튜브 영상 처리 실패 · Studio 확인 필요"; }
                else { job.State="processing";job.Detail="유튜브 처리·비공개 확인 중";job.NextAttemptUtc=DateTime.UtcNow.AddMinutes(2); }
                persist();
            }
        }
        public void Dispose() { http.Dispose(); }
    }
}
