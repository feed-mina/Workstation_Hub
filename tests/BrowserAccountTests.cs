using System;
using System.IO;
using System.Linq;
using MinaDesk;

static class BrowserAccountTests {
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static void Throws(Action run,string message){bool failed=false;try{run();}catch{failed=true;}Check(failed,message);}
    internal static int Run(string root) {
        Directory.CreateDirectory(root);Func<string,bool> allow=b=>b=="fixture-browser";
        var store=new BrowserAccounts(root,allow);Check(BrowserAccounts.Keys.All(k=>!store.Connected(k)),"new accounts are disconnected");
        foreach(string key in BrowserAccounts.Keys){store.Prepare(key,"fixture-browser");string path=store.ProfilePath(store.Get(key).ProfileId);Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"fixture-cookie"),key);store.Confirm(key,key+" nickname");}
        var ids=BrowserAccounts.Keys.Select(k=>store.Get(k).ProfileId).ToArray();
        Check(ids.Distinct().Count()==3,"playback, YouTube upload and Naver use separate profile identities");
        store.SetProvider("music");var reopened=new BrowserAccounts(root,allow);
        Check(BrowserAccounts.Keys.All(reopened.Connected) && reopened.Value.MediaProvider=="music","linked state and media provider survive restart");
        Check(BrowserAccounts.Keys.Select(k=>reopened.Get(k).ProfileId).SequenceEqual(ids),"restart never generates a different account profile");
        Check(reopened.Get("media").Alias=="media nickname","account nickname persists");
        Check(BrowserAccounts.Address("media","music")=="https://music.youtube.com/" && BrowserAccounts.Address("media","youtube")=="https://www.youtube.com/","media provider route");
        Check(BrowserAccounts.Address("youtube_upload","music")=="https://www.youtube.com/upload" && BrowserAccounts.Address("naver_blog_upload","youtube")=="https://blog.naver.com/GoBlogWrite.naver","fixed upload and writing routes");
        Throws(()=>BrowserAccounts.Address("shell","youtube"),"unknown service cannot launch");
        Throws(()=>reopened.Prepare("media","cmd.exe"),"arbitrary executable refused");
        Throws(()=>reopened.SetProvider("other"),"unknown media provider refused");
        Throws(()=>reopened.ProfilePath("../outside"),"profile path traversal refused");
        string original=reopened.Get("media").ProfileId,oldPath=reopened.ProfilePath(original);reopened.Disconnect("media");
        Check(!reopened.Connected("media") && reopened.Get("media").ProfileId==null,"disconnect immediately removes active mapping");
        Check(reopened.Connected("youtube_upload") && reopened.Connected("naver_blog_upload"),"disconnect playback leaves upload and Naver connected");
        Check(reopened.Cleanup(path=>true)==1 && File.Exists(Path.Combine(oldPath,"fixture-cookie")),"running profile is retired without deleting live data");
        reopened.Prepare("media","fixture-browser");Check(reopened.Get("media").ProfileId!=original,"reconnect never reuses logged-out profile");
        Check(reopened.Cleanup(path=>false)==0 && !Directory.Exists(oldPath),"retired credentials removed after browser exit");
        Check(File.Exists(Path.Combine(reopened.ProfilePath(reopened.Get("youtube_upload").ProfileId),"fixture-cookie")),"cleanup preserves other service credentials");
        Throws(()=>reopened.Confirm("media","not logged in"),"completion requires opened profile directory");
        string sample=Path.Combine(root,"profile with spaces");
        string command="chrome.exe "+AccountBrowser.Arguments(sample,"https://music.youtube.com/");
        Check(AccountBrowser.MatchesProfile(command,sample),"native command quoting binds exact profile including spaces");
        Check(!AccountBrowser.MatchesProfile(command,sample+"-other"),"neighboring profile cannot receive media commands");
        Check(!AccountBrowser.MatchesProfile("chrome.exe --profile-directory=Default",sample),"regular Chrome profile cannot match Hub account");
        Check(AccountBrowser.MediaTitle("Song - YouTube Music","music") && !AccountBrowser.MediaTitle("Song - YouTube Music","youtube"),"provider-specific playback window selection");
        Check(!AccountBrowser.MediaTitle("Other browser page","youtube") && !AccountBrowser.MediaTitle(null,"music"),"unrelated or unknown browser windows rejected");
        Console.WriteLine("INFO · profile-specific process lookup: "+(AccountBrowser.Running(Path.Combine(root,"not-running-profile"))?"unavailable (service-open fallback)":"available"));
        string folder=Path.Combine(root,"project A");Directory.CreateDirectory(folder);
        Check(ProjectLauncher.ToolFolder(folder,root)==folder,"connected project directory wins over global default");
        Check(ProjectLauncher.ToolFolder(null,root)==root,"cloud-only project uses normal terminal default");
        Check(ProjectLauncher.ToolFolder(null,Path.Combine(root,"missing"))==Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"missing global default falls back to user home");
        Throws(()=>ProjectLauncher.ToolFolder(Path.Combine(root,"moved-project"),root),"missing connected project never silently opens unrelated folder");
        string file=Path.Combine(root,"browser-accounts.json");File.WriteAllText(file,"broken");var recovered=new BrowserAccounts(root,allow);
        Check(!recovered.ReadOnly && recovered.Connected("youtube_upload"),"damaged account settings recover valid backup");
        string corrupt=Path.Combine(root,"corrupt");Directory.CreateDirectory(corrupt);File.WriteAllText(Path.Combine(corrupt,"browser-accounts.json"),"broken");var guarded=new BrowserAccounts(corrupt,allow);
        Check(guarded.ReadOnly,"unrecoverable credential metadata preserved read-only");
        Throws(()=>guarded.Prepare("media","fixture-browser"),"unrecoverable metadata cannot be overwritten");
        string revokedRoot=Path.Combine(root,"revoked-backup");var linked=new BrowserAccounts(revokedRoot,allow);linked.Prepare("media","fixture-browser");Directory.CreateDirectory(linked.ProfilePath(linked.Get("media").ProfileId));linked.Confirm("media","old account");
        string accountFile=Path.Combine(revokedRoot,"browser-accounts.json"),oldMetadata=File.ReadAllText(accountFile);linked.Disconnect("media");File.WriteAllText(accountFile,"broken");File.WriteAllText(accountFile+".bak",oldMetadata);
        Check(!new BrowserAccounts(revokedRoot,allow).Connected("media"),"backup recovery cannot reconnect an explicitly disconnected browser profile");
        Console.WriteLine("PASS · "+checks+" account/tool checks: separate persistent sessions, disconnect/cleanup, profile targeting and project folder routing");return checks;
    }
}
