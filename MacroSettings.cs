using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace MinaDesk {
    public sealed class MacroSettings {
        public string ScreenshotFolder {get;set;}
        public string WorkingFolder {get;set;}
        public string CodePath {get;set;}
        public string ObsAddress {get;set;}
        public string ObsPasswordProtected {get;set;}
        public List<string> Projects {get;set;}
        public MacroSettings() {
            ScreenshotFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),"허브");
            WorkingFolder=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            ObsAddress="ws://127.0.0.1:4455";
            Projects=new List<string>();
        }
        internal static MacroSettings Load(string file) {
            try {
                var value=new JavaScriptSerializer().Deserialize<MacroSettings>(File.ReadAllText(file));
                if(value!=null) {
                    if(value.Projects==null)value.Projects=new List<string>();
                    return value;
                }
            }catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}
            return new MacroSettings();
        }
        internal string Password() {
            if(String.IsNullOrEmpty(ObsPasswordProtected))return "";
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ObsPasswordProtected),null,DataProtectionScope.CurrentUser));
        }
        internal void SetPassword(string value) {
            ObsPasswordProtected=String.IsNullOrEmpty(value)?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser));
        }
        internal void Save(string file) {
            string temp=file+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this));
            if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
        }
    }
}
