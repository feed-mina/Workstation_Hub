using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MinaDesk {
    internal sealed class VerificationStep {
        internal string Title,Executable,Arguments,Display;
    }
    internal sealed class VerificationPlan {
        internal string Folder;
        internal readonly List<VerificationStep> Steps=new List<VerificationStep>();
    }
    internal static class DeveloperVerification {
        static VerificationStep Step(string title,string executable,string arguments,string display) {
            return new VerificationStep{Title=title,Executable=executable,Arguments=arguments,Display=display};
        }
        // The renderer supplies a check ID only. The project and predefined script names determine the command.
        internal static VerificationPlan Plan(string action,string folder) {
            if(String.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                throw new DirectoryNotFoundException("검증할 로컬 프로젝트 폴더를 연결하고 ‘이 PC’를 선택하세요.");
            var plan=new VerificationPlan{Folder=folder};
            bool all=action=="validate-all";
            bool type=all || action=="validate-types";
            bool test=all || action=="validate-test";
            bool build=all || action=="validate-build";
            if(!type && !test && !build)throw new ArgumentException("지원하지 않는 검증 항목입니다.");
            string manifest=Path.Combine(folder,"package.json");
            if(File.Exists(manifest)) {
                if(new FileInfo(manifest).Length>512000)throw new InvalidOperationException("package.json이 너무 큽니다.");
                var config=LocalData.Read<Dictionary<string,object>>(manifest,()=>null);
                object field;var scripts=config!=null && config.TryGetValue("scripts",out field)?field as Dictionary<string,object>:null;
                if(scripts!=null) {
                    string typeScript=new[]{"typecheck","type-check","check:types","tsc"}.FirstOrDefault(name=>HasScript(scripts,name));
                    string manager=Manager(folder,GitHubValues.Text(config,"packageManager"));
                    string systemShell=Path.Combine(Environment.SystemDirectory,"cmd.exe");
                    if(type && typeScript!=null)plan.Steps.Add(Script(manager,systemShell,typeScript,"타입 검사"));
                    if(test && HasScript(scripts,"test"))plan.Steps.Add(Script(manager,systemShell,"test","테스트"));
                    if(build && HasScript(scripts,"build"))plan.Steps.Add(Script(manager,systemShell,"build","빌드"));
                }
            }else if(File.Exists(Path.Combine(folder,"build.ps1")) && File.Exists(Path.Combine(folder,"Hub.cs")) &&
                File.Exists(Path.Combine(folder,"tests","test-skills.ps1"))) {
                string shell=Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe");
                const string flags="-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File ";
                if(test)plan.Steps.Add(Step("테스트",shell,flags+".\\tests\\test-skills.ps1","PowerShell "+flags+".\\tests\\test-skills.ps1"));
                if(build || (!all && type))plan.Steps.Add(Step(type && !build?"C# 컴파일":"빌드",shell,flags+".\\build.ps1","PowerShell "+flags+".\\build.ps1"));
            }else {
                var targets=Directory.EnumerateFiles(folder,"*.sln").Concat(Directory.EnumerateFiles(folder,"*.slnx"))
                    .Concat(Directory.EnumerateFiles(folder,"*.csproj")).ToArray();
                var solutions=targets.Where(p=>!p.EndsWith(".csproj",StringComparison.OrdinalIgnoreCase)).ToArray();
                string target=solutions.Length==1?solutions[0]:targets.Length==1?targets[0]:null;
                if(target!=null) {
                    string dotnet=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"dotnet","dotnet.exe");
                    string argument=ProjectLauncher.Quote(target)+" --no-restore";
                    if(test)plan.Steps.Add(Step("테스트",dotnet,"test "+argument,"dotnet test "+argument));
                    if(type || build)plan.Steps.Add(Step(build?"빌드":"C# 컴파일",dotnet,"build "+argument,"dotnet build "+argument));
                }
            }
            if(plan.Steps.Count==0)throw new InvalidOperationException("이 프로젝트에서 해당 검증을 찾지 못했습니다. package.json의 스크립트 또는 단일 .NET 프로젝트를 확인하세요.");
            return plan;
        }
        static bool HasScript(Dictionary<string,object> scripts,string name) {
            return !String.IsNullOrWhiteSpace(GitHubValues.Text(scripts,name));
        }
        static string Manager(string folder,string declared) {
            if(declared!=null) {
                string manager=declared.Split('@')[0];
                if(manager=="npm" || manager=="pnpm" || manager=="yarn")return manager;
                throw new InvalidOperationException("이 프로젝트는 "+manager+"을 사용합니다. 프로젝트 터미널에서 검증을 실행하세요.");
            }
            if(File.Exists(Path.Combine(folder,"pnpm-lock.yaml")))return "pnpm";
            if(File.Exists(Path.Combine(folder,"yarn.lock")))return "yarn";
            return "npm";
        }
        static VerificationStep Script(string manager,string shell,string script,string title) {
            string command=manager+".cmd run "+script;
            return Step(title,shell,"/d /c "+command,manager+" run "+script);
        }
        internal static async Task<DeveloperResult> Run(VerificationPlan plan,CancellationToken token) {
            var result=new DeveloperResult{title="로컬 검증",summary="선택한 프로젝트에서 실행"};
            var output=new StringBuilder();
            foreach(var step in plan.Steps) {
                token.ThrowIfCancellationRequested();
                output.AppendLine("> "+step.Display);
                int code=await Task.Run(()=>RunStep(plan.Folder,step,output,token),token);
                result.Add(step.Title,code==0?"통과":"실패 · 종료 코드 "+code);
                if(code!=0){result.summary="검증 실패 · 이후 단계 중지";break;}
            }
            if(result.rows.Count==plan.Steps.Count && !result.rows.Any(row=>row.detail.StartsWith("실패")))
                result.summary="검증 완료 · "+result.rows.Count+"단계 통과";
            result.body=output.ToString();
            return result;
        }
        static int RunStep(string folder,VerificationStep step,StringBuilder output,CancellationToken token) {
            var start=new ProcessStartInfo(step.Executable,step.Arguments) {
                WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true,
                RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true
            };
            using(var child=new Process{StartInfo=start})
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)) {
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                DataReceivedEventHandler append=(s,e)=>{
                    if(e.Data==null)return;
                    lock(output){
                        const int limit=24000;
                        if(output.Length+e.Data.Length>limit) {
                            int cut=Math.Min(output.Length,Math.Max(0,output.Length+Math.Min(4000,e.Data.Length)-limit));
                            if(cut>0)output.Remove(0,cut);
                        }
                        output.AppendLine(e.Data.Substring(0,Math.Min(4000,e.Data.Length)));
                    }
                };
                child.OutputDataReceived+=append;
                child.ErrorDataReceived+=append;
                child.Start();
                child.StandardInput.Close();
                child.BeginOutputReadLine();
                child.BeginErrorReadLine();
                using(timeout.Token.Register(()=>Stop(child)))child.WaitForExit();
                token.ThrowIfCancellationRequested();
                if(timeout.IsCancellationRequested)throw new TimeoutException("검증이 5분을 넘겨 실행을 중지했습니다.");
                return child.ExitCode;
            }
        }
        static void Stop(Process child) {
            try {
                if(child.HasExited)return;
                string killer=Path.Combine(Environment.SystemDirectory,"taskkill.exe");
                using(var process=Process.Start(new ProcessStartInfo(killer,"/PID "+child.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true}))
                    if(process!=null)process.WaitForExit(2000);
                if(!child.HasExited)child.Kill();
            }catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
        }
    }
}
