using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MinaDesk {
    internal sealed class ObsRecorder {
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        static string Hash(string value){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));}
        internal static Uri Address(string address) {
            Uri uri;
            if(!Uri.TryCreate(address,UriKind.Absolute,out uri)||(uri.Scheme!="ws"&&uri.Scheme!="wss")||!String.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("OBS 주소를 ws://127.0.0.1:4455 형식으로 입력하세요.");
            return uri;
        }
        async Task Send(ClientWebSocket socket,object message,CancellationToken token) {
            var bytes=Encoding.UTF8.GetBytes(json.Serialize(message));
            await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,token);
        }
        async Task<Dictionary<string,object>> Receive(ClientWebSocket socket,CancellationToken token) {
            using(var output=new MemoryStream()) {
                var buffer=new byte[4096];WebSocketReceiveResult result;
                do {
                    result=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),token);
                    if(result.MessageType!=WebSocketMessageType.Text)throw new IOException("OBS 연결이 닫혔습니다. 비밀번호와 WebSocket 서버 설정을 확인하세요.");
                    output.Write(buffer,0,result.Count);
                    if(output.Length>1048576)throw new IOException("OBS 응답이 너무 큽니다.");
                }while(!result.EndOfMessage);
                return json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(output.ToArray()));
            }
        }
        async Task<Dictionary<string,object>> Request(ClientWebSocket socket,string type,CancellationToken token) {
            string id=Guid.NewGuid().ToString("N");
            await Send(socket,new{op=6,d=new{requestType=type,requestId=id}},token);
            while(true) {
                var message=await Receive(socket,token);
                if(Convert.ToInt32(message["op"])!=7)continue;
                var data=(Dictionary<string,object>)message["d"];
                if((string)data["requestId"]!=id)continue;
                var status=(Dictionary<string,object>)data["requestStatus"];
                if(!(bool)status["result"])throw new IOException("OBS 명령 실패 · 코드 "+status["code"]);
                object response;
                return data.TryGetValue("responseData",out response)?(Dictionary<string,object>)response:new Dictionary<string,object>();
            }
        }
        internal async Task<bool> ReadOrToggle(MacroSettings settings,bool toggle) {
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            using(var socket=new ClientWebSocket()) {
                var token=timeout.Token;
                await socket.ConnectAsync(Address(settings.ObsAddress),token);
                var hello=await Receive(socket,token);
                if(Convert.ToInt32(hello["op"])!=0)throw new IOException("OBS WebSocket 5.x 서버가 필요합니다.");
                var data=(Dictionary<string,object>)hello["d"];
                var identify=new Dictionary<string,object>{{"rpcVersion",1},{"eventSubscriptions",0}};
                object auth;
                if(data.TryGetValue("authentication",out auth)) {
                    var challenge=(Dictionary<string,object>)auth;
                    identify["authentication"]=Hash(Hash(settings.Password()+(string)challenge["salt"])+(string)challenge["challenge"]);
                }
                await Send(socket,new{op=1,d=identify},token);
                var identified=await Receive(socket,token);
                if(Convert.ToInt32(identified["op"])!=2)throw new IOException("OBS 인증을 확인하세요.");
                var status=await Request(socket,"GetRecordStatus",token);
                if(toggle) {
                    bool expected=!(bool)status["outputActive"];
                    await Request(socket,expected?"StartRecord":"StopRecord",token);
                    do {
                        await Task.Delay(100,token);
                        status=await Request(socket,"GetRecordStatus",token);
                    }while((bool)status["outputActive"]!=expected);
                }
                return (bool)status["outputActive"];
            }
        }
    }
}
