using System;
using System.IO;
using System.Linq;
using MinaDesk;

static class HardwareAiTests {
    static int checks;
    static void Check(bool condition,string name) {
        if(!condition)throw new Exception(name);
        checks++;
    }
    internal static int Run(string root) {
        Check(TemperatureSensors.Identify("Cpu","CPU Package",51.5).Value.celsius==51.5,"CPU sensor precision");
        Check(TemperatureSensors.Identify("Cpu","Core #2",48).Value.sensor.Contains("코어"),"physical CPU core fallback is labeled");
        Check(TemperatureSensors.Identify("Cpu","Core #2 Distance to TjMax",48)==null,"distance-to-limit is excluded");
        Check(TemperatureSensors.Identify("GpuIntel","GPU Hot Spot",60).Value.sensor.Contains("핫스팟"),"hotspot keeps its own label");
        Check(TemperatureSensors.Identify("Motherboard","CPU Package",30)==null,"motherboard probes do not masquerade as CPU temperature");
        Check(TemperatureSensors.Identify("GpuIntel","GPU Core",0)==null &&
            TemperatureSensors.Identify("GpuIntel","GPU Core",Double.NaN)==null,"invalid driver values are excluded");

        string doc="## Recommended models\n"+
            "<ModelDetails slug=\"gpt-6.3-sol\" data={{features:[{title: \"Default context in Codex\", value:\"600,000 tokens\"}]}} />\n"+
            "<ModelDetails slug=\"gpt-6.2-astra\" data={{features:[{title: \"Default context in Codex\", value:\"272,000 tokens\"}]}} />\n"+
            "## Deprecated Codex models\n<ModelDetails slug=\"gpt-9-luna\" />";
        var models=CodexModelComparison.Parse(doc);
        Check(models[0].id=="gpt-6.3-sol" && models[0].latest && models[0].context==600000,"new official model and explicit default context");
        Check(models.Single(model=>model.id=="gpt-6.2-astra").listed&&!models.Single(model=>model.id=="gpt-6.2-astra").latest,"existing model stays available for comparison");
        Check(models.Single(model=>model.id=="gpt-6.1-sol").context==500000&&!models.Single(model=>model.id=="gpt-6.1-sol").listed,"historical snapshot remains labeled");
        Check(!models.Any(model=>model.id=="gpt-9-luna") && CodexModelComparison.Parse("other docs").Length==0,"unrelated and partial documents cannot replace the catalog");
        Check(AiAccounts.Interpret("gpt",0,"","Logged in using ChatGPT")==AiLoginStatus.Account,"Codex ChatGPT confirmation");
        Check(AiAccounts.Interpret("gpt",0,"Logged in using an API key","")==AiLoginStatus.OtherAuth,"API key does not count as a GPT account");
        Check(AiAccounts.Interpret("gpt",1,"Not logged in","")==AiLoginStatus.SignedOut,"signed-out Codex session");
        Check(AiAccounts.Interpret("claude",0,"{\"loggedIn\":true,\"authMethod\":\"claude.ai\"}","")==AiLoginStatus.Account,"Claude Code account confirmation");
        Check(AiAccounts.Interpret("claude",1,"{\"loggedIn\":false,\"authMethod\":\"none\"}","")==AiLoginStatus.SignedOut,"signed-out Claude Code session");
        Check(AiAccounts.Interpret("claude",0,"{\"loggedIn\":true,\"authMethod\":\"third_party\"}","")==AiLoginStatus.OtherAuth &&
            AiAccounts.Interpret("claude",1,"unrecognized text","")==AiLoginStatus.Unknown,"other providers and unknown output stay separate");

        Directory.CreateDirectory(root);
        var store=new DeveloperPreferenceStore(root);
        store.Change(next=>next.AiLinks["gpt"]=true);
        Check(new DeveloperPreferenceStore(root).Value.AiLinks["gpt"],"device linkage survives restart");
        store.Change(next=>next.AiLinks.Remove("gpt"));
        Check(!new DeveloperPreferenceStore(root).Value.AiLinks.ContainsKey("gpt"),"unlink removes only app association");
        Console.WriteLine("PASS · "+checks+" hardware, AI login and model comparison checks");
        return checks;
    }
}
