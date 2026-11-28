using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MinaDesk {
    internal sealed class CodexSubmission {
        internal string ThreadId,Model,Effort;
        // The turn remains attached to its server while it is running.
        internal Task<bool> Completion;
    }

    internal static class CodexLocal {
        internal static string ModelId(string model) {
            switch(model) {
                case "GPT-6.2 Sol":return "gpt-6.2-sol";
                case "GPT-6.2 Astra":return "gpt-6.2-astra";
                default:return null;
            }
        }
        internal static string EffortId(string effort) {
            switch(effort) {
                case "Low":return "low";
                case "Medium":return "medium";
                case "High":return "high";
                case "Extra High":return "xhigh";
                default:return null;
            }
        }

        internal static Task<CodexSubmission> Submit(string executable,string folder,string prompt,string model,string effort,CancellationToken stop) {
            if(String.IsNullOrEmpty(executable) || !File.Exists(executable))throw new InvalidOperationException("공식 Codex를 설치하고 로그인하세요.");
            if(!Directory.Exists(folder))throw new DirectoryNotFoundException("Codex에 연결할 로컬 프로젝트 폴더를 확인하세요.");
            if(String.IsNullOrWhiteSpace(prompt))throw new ArgumentException("보낼 요청문을 입력하세요.");
            var accepted=new TaskCompletionSource<CodexSubmission>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task.Run(async ()=>{
                bool started=false;
                try {
                    var info=new ProcessStartInfo(executable,"app-server --listen stdio://"){
                        WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true,
                        RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                        StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
                    };
                    using(var process=new Process{StartInfo=info})
                    using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop)) {
                        timeout.CancelAfter(TimeSpan.FromSeconds(35));
                        process.ErrorDataReceived+=(sender,line)=>{};
                        process.Start();
                        process.BeginErrorReadLine();
                        using(timeout.Token.Register(()=>Stop(process))) try {
                            var server=new Connection(process);
                            await server.Request("initialize",new {clientInfo=new {name="workstation_hub",title="Workstation Hub",version="1"}});
                            await server.Send(new {method="initialized"});
                            var config=new Dictionary<string,object>();
                            if(effort!=null)config.Add("model_reasoning_effort",effort);
                            var thread=await server.Request("thread/start",new {cwd=folder,model,config,ephemeral=false});
                            string activeModel=Text(thread,"model"),activeEffort=Text(thread,"reasoningEffort");
                            if(String.IsNullOrEmpty(activeModel) || (model!=null && model!=activeModel) || (effort!=null && effort!=activeEffort))
                                throw new InvalidOperationException("Codex가 선택한 모델 또는 추론 강도를 확인해 주지 않아 전송을 중단했습니다.");
                            string id=DeveloperPreferenceStore.ThreadId(Text(Field(thread,"thread") as Dictionary<string,object>,"id"));
                            if(String.IsNullOrEmpty(id))throw new InvalidOperationException("Codex 대화 ID를 확인할 수 없습니다.");
                            timeout.Token.ThrowIfCancellationRequested();
                            var submission=new CodexSubmission{ThreadId=id,Model=activeModel,Effort=activeEffort,Completion=finished.Task};
                            // After writing a start request, a lost reply is an unknown outcome. Do not retry.
                            started=true;
                            var turn=await server.Request("turn/start",new {threadId=id,
                                input=new[]{new {type="text",text=prompt}},model=activeModel,effort=activeEffort});
                            var actual=Field(turn,"turn") as Dictionary<string,object>;
                            string turnId=Text(actual,"id");
                            if(String.IsNullOrEmpty(turnId))throw new InvalidOperationException("Codex 전송 결과를 확인하지 못했습니다. 앱에서 대화를 확인하세요.");
                            timeout.CancelAfter(Timeout.Infinite);
                            accepted.TrySetResult(submission);
                            finished.TrySetResult(await server.WaitForTurn(id,turnId));
                        } finally {Stop(process);}
                    }
                }catch(Exception ex) {
                    var failure=started?new InvalidOperationException("Codex 전송 응답을 확인하지 못했습니다. 중복 전송을 피하려면 Codex 앱에서 먼저 확인하세요.",ex)
                        :new InvalidOperationException(ex is InvalidOperationException?ex.Message:"Codex에 연결하지 못했습니다. 앱의 로그인과 버전을 확인하세요.",ex);
                    accepted.TrySetException(failure);
                    finished.TrySetResult(false);
                }
            });
            return accepted.Task;
        }

        static void Stop(Process process) {
            try {if(!process.HasExited)process.Kill();}
            catch(InvalidOperationException){}
            catch(Win32Exception){}
        }
        static object Field(Dictionary<string,object> row,string key) {
            object value;
            return row!=null && row.TryGetValue(key,out value)?value:null;
        }
        static string Text(Dictionary<string,object> row,string key) {return Field(row,key) as string;}

        sealed class Connection {
            readonly StreamReader reader;
            readonly StreamWriter writer;
            int nextId;
            internal Connection(Process process) {
                reader=process.StandardOutput;
                // Write UTF-8 independently of the Windows console ANSI code page.
                writer=new StreamWriter(process.StandardInput.BaseStream,new UTF8Encoding(false)){AutoFlush=true};
            }
            internal Task Send(object message) {return writer.WriteLineAsync(LocalData.Encode(message));}
            async Task<Dictionary<string,object>> Read() {
                string line=await reader.ReadLineAsync();
                if(line==null)throw new IOException("Codex 연결이 종료되었습니다.");
                return LocalData.Decode<Dictionary<string,object>>(line);
            }
            async Task HandleServerRequest(Dictionary<string,object> message) {
                object id=Field(message,"id");
                string method=Text(message,"method");
                if(id==null || method==null)return;
                // This connection never grants app-server permission requests on the user's behalf.
                if(method=="item/commandExecution/requestApproval" || method=="item/fileChange/requestApproval")
                    await Send(new {id,result=new {decision="decline"}});
                else await Send(new {id,error=new {code=-32601,message="Action requires the Codex app."}});
            }
            internal async Task<Dictionary<string,object>> Request(string method,object parameters) {
                int id=++nextId;
                await Send(new {id,method,@params=parameters});
                while(true) {
                    var reply=await Read();
                    if(!Equals(Field(reply,"id"),id) || Text(reply,"method")!=null) {
                        await HandleServerRequest(reply);
                        continue;
                    }
                    var result=Field(reply,"result") as Dictionary<string,object>;
                    if(result!=null)return result;
                    var error=Field(reply,"error") as Dictionary<string,object>;
                    throw new InvalidOperationException(Text(error,"message")??"Codex 요청 처리에 실패했습니다.");
                }
            }
            internal async Task<bool> WaitForTurn(string thread,string turn) {
                while(true) {
                    var message=await Read();
                    if(Text(message,"method")=="turn/completed") {
                        var data=Field(message,"params") as Dictionary<string,object>;
                        var completed=Field(data,"turn") as Dictionary<string,object>;
                        if(Text(data,"threadId")==thread && Text(completed,"id")==turn)return Text(completed,"status")=="completed";
                    }
                    await HandleServerRequest(message);
                }
            }
        }
    }
}
