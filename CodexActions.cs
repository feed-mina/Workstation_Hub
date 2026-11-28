using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MinaDesk {
    internal static class CodexActions {
        internal const string Workflow="workstation-hub-codex.yml";
        internal const string DefaultRunner="workstation-hub-actions";

        internal static string RunnerName(string configured,string login) {
            string name=(configured??"").Trim();
            if(name.Length==0)return String.IsNullOrEmpty(login)?null:login+"/"+DefaultRunner;
            if(name.Length>140 || !GitHubRepository.ValidName(name) || name.EndsWith("/.",StringComparison.Ordinal) ||
                name.EndsWith("/..",StringComparison.Ordinal))
                throw new ArgumentException("Actions 실행 저장소를 소유자/저장소 형식으로 입력하세요.");
            return name;
        }

        internal static async Task<DeveloperResult> Submit(IGitHubApi api,GitHubRepository project,string branch,string runnerName,
            string prompt,string modelLabel,string effortLabel,CancellationToken token) {
            string target=DeveloperGitHub.Route(project);
            if(!DeveloperGitHub.ValidBranch(branch) || branch.StartsWith("-",StringComparison.Ordinal))
                throw new InvalidOperationException("Actions에서 실행할 GitHub 브랜치를 선택하세요.");
            if(String.IsNullOrWhiteSpace(prompt) || prompt.Length>6000 || prompt.Contains("\0"))
                throw new ArgumentException("Actions에 보낼 요청문을 확인하세요.");
            string runner=RunnerName(runnerName,null);
            if(runner==null)throw new InvalidOperationException("실행 설정에서 비공개 Actions 저장소를 선택하세요.");
            string model=CodexLocal.ModelId(modelLabel)??"gpt-6.2-sol";
            string effort=CodexLocal.EffortId(effortLabel)??"medium";

            var metadata=await Get(api,"repos/"+runner,token);
            var destination=GitHubRepository.From(metadata);
            if(!destination.Private || !String.Equals(runner,destination.FullName,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("요청문을 보내려면 이름이 일치하는 비공개 Actions 저장소가 필요합니다.");
            string workflowBranch=GitHubValues.Text(metadata,"default_branch");
            if(!DeveloperGitHub.ValidBranch(workflowBranch))
                throw new InvalidOperationException("Actions 저장소의 기본 브랜치를 확인할 수 없습니다.");
            var revision=await Get(api,target+"/branches/"+Uri.EscapeDataString(branch),token);
            object commit;
            string sha=revision.TryGetValue("commit",out commit)?GitHubValues.Text(commit as Dictionary<string,object>,"sha"):null;
            if(!String.Equals(GitHubValues.Text(revision,"name"),branch,StringComparison.Ordinal) ||
                !Regex.IsMatch(sha??"",@"\A[0-9a-f]{40}\z"))
                throw new InvalidOperationException("선택한 브랜치의 커밋을 확인할 수 없습니다.");

            string requestId=Guid.NewGuid().ToString("N");
            string workflowUrl=destination.Url+"/actions/workflows/"+Workflow;
            var payload=new { @ref=workflowBranch,return_run_details=true,inputs=new {
                target=project.FullName,branch,revision=sha,private_repository=project.Private?"true":"false",model,effort,prompt,request_id=requestId
            }};
            GitHubResponse response;
            token.ThrowIfCancellationRequested();
            try {
                response=await api.Send("repos/"+destination.FullName+"/actions/workflows/"+Workflow+"/dispatches","POST",payload,token);
            }catch(OperationCanceledException) {
                return Unconfirmed(project,branch,sha,model,effort,requestId,workflowUrl);
            }
            if(response.Status==0 || response.Status>=500)
                return Unconfirmed(project,branch,sha,model,effort,requestId,workflowUrl);
            if(response.Status==404)
                throw new InvalidOperationException("비공개 실행 저장소의 기본 브랜치에 Hub Actions 워크플로를 설치하세요.");
            if(response.Status==422)
                throw new InvalidOperationException("Actions 워크플로의 입력 형식이나 실행 가능 상태를 확인하세요.");
            response.RequireSuccess();

            long run=0;
            if(!String.IsNullOrWhiteSpace(response.Json)) {
                try {run=GitHubValues.Long(LocalData.Decode<Dictionary<string,object>>(response.Json),"workflow_run_id");}
                catch(ArgumentException){}
                catch(InvalidOperationException){}
            }
            var result=Result("GitHub Actions 실행 접수",project,branch,sha,model,effort);
            string runUrl=run>0?destination.Url+"/actions/runs/"+run.ToString(CultureInfo.InvariantCulture):workflowUrl;
            result.Add(run>0?"실행 #"+run:"실행 목록", "요청 ID "+requestId,runUrl);
            return result;
        }

        static DeveloperResult Unconfirmed(GitHubRepository project,string branch,string sha,string model,string effort,string requestId,string url) {
            var result=Result("GitHub Actions 접수 확인 필요",project,branch,sha,model,effort);
            result.Add("실행 목록 확인","요청 ID "+requestId+" · 같은 요청을 다시 보내기 전에 실행 목록을 확인하세요.",url);
            return result;
        }

        static DeveloperResult Result(string title,GitHubRepository project,string branch,string sha,string model,string effort) {
            return new DeveloperResult{title=title,summary=project.FullName+" · "+branch+" · "+sha.Substring(0,8)+" · "+model+" · "+effort};
        }

        static async Task<Dictionary<string,object>> Get(IGitHubApi api,string path,CancellationToken token) {
            var response=await api.Send(path,"GET",null,token);
            response.RequireSuccess();
            var result=LocalData.Decode<Dictionary<string,object>>(response.Json);
            if(result==null)throw new InvalidOperationException("GitHub 응답을 확인할 수 없습니다.");
            return result;
        }
    }
}
