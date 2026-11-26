using System;
using System.IO;
using System.Linq;
using MinaDesk;

static class ProjectSkillTests {
    static int checks;
    static void Check(bool condition,string message) { if(!condition)throw new Exception(message);checks++; }
    static string Skill(string root,string name,string body) {
        string folder=Path.Combine(root,name);Directory.CreateDirectory(folder);
        string file=Path.Combine(folder,"SKILL.md");File.WriteAllText(file,body);return file;
    }
    static void Throws(Action action,string message) { bool failed=false;try{action();}catch(Exception){failed=true;}Check(failed,message); }
    [STAThread]static int Main(string[] args) {
        try {
            string root=args[0];Directory.CreateDirectory(root);
            string user=Path.Combine(root,"fake-user"),repo=Path.Combine(root,"repo"),nested=Path.Combine(repo,"src","nested"),other=Path.Combine(root,"other-project");
            Directory.CreateDirectory(Path.Combine(repo,".git"));Directory.CreateDirectory(nested);Directory.CreateDirectory(other);
            Skill(Path.Combine(user,".codex","skills"),"fix-guide","---\nname: fix-guide\ndescription: >\n  쉬운 수정 설명\n  HTML 보고서\n---\nBody");
            Skill(Path.Combine(user,".codex","skills",".system"),"system-skill","---\nname: \"system-skill\"\ndescription: 'System skill'\n---\nBody");
            Skill(Path.Combine(user,".claude","skills"),"claude-skill","---\nname: claude-skill\ndescription: Claude\n---\nBody");
            Skill(Path.Combine(repo,".agents","skills"),"repo-skill","---\nname: repo-skill\ndescription: Repository\n---\nBody");
            Skill(Path.Combine(nested,".agents","skills"),"nested-skill","---\nname: nested-skill\ndescription: Nested\n---\nBody");
            Skill(Path.Combine(other,".agents","skills"),"other-skill","---\nname: other-skill\ndescription: Other\n---\nBody");
            Skill(Path.Combine(user,".codex","plugins","cache","old"),"cached-only","---\nname: cached-only\n---\nBody");
            var catalog=SkillCatalog.Scan(user,nested);
            Check(catalog.Items.Count==5,"personal + system + ancestor + project skills");
            Check(!catalog.Items.Any(s=>s.Name=="other-skill" || s.Name=="cached-only"),"no other project or stale plugin cache");
            Check(catalog.Items.Single(s=>s.Name=="fix-guide").Description=="쉬운 수정 설명 HTML 보고서","folded metadata");
            Check(SkillCatalog.Scan(user,other).Items.Count==4,"isolated second project catalog");
            string data=Path.Combine(root,"profile");var store=new SkillWorkspaces(data);string a=store.AddProject(nested),b=store.AddProject(other);
            Check(a!=b,"unique projects");store.Select(a);
            var skill=catalog.Items.Single(s=>s.Name=="fix-guide");
            store.SaveButton(a,null,skill,"수정 가이드","gpt","한국어 HTML로 설명해 줘");
            string id=store.Current.Buttons[0].Id;
            store.SaveButton(a,id,skill,"가이드 수정","claude","현재 변경 사항");
            Check(store.Current.Buttons.Count==1 && store.Current.Buttons[0].Target=="claude","edit does not duplicate");
            store.Select(b);Check(store.Current.Buttons.Count==0,"buttons are project scoped");
            store.SaveButton(b,null,skill,"두 번째","gpt","");
            var reopened=new SkillWorkspaces(data);Check(reopened.Current.Id==b && reopened.Current.Buttons.Count==1,"restart restores selected project and buttons");
            reopened.Select(a);Check(reopened.Current.Buttons[0].Label=="가이드 수정","other project preserved");
            string again=reopened.AddProject(nested);Check(again==a && reopened.Value.Projects.Count==2,"same folder is not duplicated");
            reopened.Change(next=>next.StartApps="claude");Check(new SkillWorkspaces(data).Value.StartApps=="claude","last AI selection persists");
            for(int i=0;i<24;i++)reopened.SaveButton(a,null,skill,"버튼 "+i,"gpt","");
            Check(reopened.Current.Buttons.Count==25,"more than twelve skill buttons");
            Throws(()=>reopened.SaveButton(a,null,skill,"잘못된 앱","shell",""),"targets allowlisted");
            Throws(()=>reopened.SaveButton(a,"unknown",skill,"실종 버튼","gpt",""),"unknown id cannot append");
            Throws(()=>reopened.Select("unknown"),"unknown project rejected");
            string before=LocalData.Encode(reopened.Value);
            Directory.CreateDirectory(Path.Combine(data,"skill-workspaces.json.tmp"));
            Throws(()=>reopened.Change(next=>next.StartApps="gpt"),"disk failure exposed");
            Check(LocalData.Encode(reopened.Value)==before,"failed save preserves memory");
            Directory.Delete(Path.Combine(data,"skill-workspaces.json.tmp"));
            reopened.RemoveButton(a,id);Check(reopened.Current.Buttons.Count==24,"remove only selected button");
            string json=Path.Combine(data,"skill-workspaces.json");File.WriteAllText(json,"broken");
            var recovered=new SkillWorkspaces(data);Check(!recovered.ReadOnly && recovered.Current!=null && recovered.LoadNotice!=null,"backup recovery");
            Check(Directory.GetFiles(data,"*.unreadable-*").Length==1,"corrupt original preserved");
            File.WriteAllText(json+".bak","also broken");
            var broken=new SkillWorkspaces(data);Check(broken.ReadOnly,"unrecoverable profile read only");
            Throws(()=>broken.AddProject(nested),"cannot overwrite corrupt profile");Check(File.ReadAllText(json)=="broken","corrupt file unchanged");
            var project=new SkillProject{Folder=Path.Combine(root,"한글 & folder")};
            var button=new SkillButton{SkillName=skill.Name,SkillPath=skill.Path,Instruction="줄1\n&question? #한글"};
            string prompt=DesktopSkillRunner.Prompt(project,button);
            foreach(string target in new[]{"gpt","claude"}) {
                var uri=new Uri(DesktopSkillRunner.Link(target,project.Folder,prompt));
                Check(uri.Scheme==(target=="gpt"?"codex":"claude"),"official desktop protocol");
                var query=System.Web.HttpUtility.ParseQueryString(uri.Query);
                Check(query[target=="gpt"?"path":"folder"]==project.Folder,"folder escaped without parameter injection");
                Check(query[target=="gpt"?"prompt":"q"]==prompt,"prompt round trip");
            }
            Throws(()=>DesktopSkillRunner.Link("shell",repo,"x"),"no arbitrary protocol");
            Throws(()=>DesktopSkillRunner.Link("gpt",repo,new string('한',4000)),"long deep link rejected before launch");
            Check(DesktopSkillRunner.SameDraft("hello\r\nworld","hello\nworld"),"platform newline normalization");
            Check(!DesktopSkillRunner.SameDraft("hello world extra","hello world"),"changed draft cannot send");
            Check(!DesktopSkillRunner.SameDraft(null,"hello"),"unreadable draft cannot send");
            Check(DesktopSkillRunner.IsSendName("전송") && !DesktopSkillRunner.IsSendName("Approve") && !DesktopSkillRunner.IsSendName("Allow folder access"),"permission controls cannot match Send");
            checks+=CloudProjectTests.Run(Path.Combine(root,"cloud"));
            checks+=BrowserAccountTests.Run(Path.Combine(root,"browser-accounts"));
            Console.WriteLine("PASS · "+checks+" checks: project persistence/isolation, recovery, catalog scope, desktop links, exact draft/send guards and GitHub projects");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}

