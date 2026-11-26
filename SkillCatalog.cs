using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MinaDesk {
    internal sealed class InstalledSkill {
        internal string Id,Name,Description,Path,Source;
    }
    internal sealed class SkillCatalogResult {
        internal readonly List<InstalledSkill> Items=new List<InstalledSkill>();
        internal readonly List<string> Warnings=new List<string>();
    }
    internal static class SkillCatalog {
        internal static SkillCatalogResult Scan(string user,string project) {
            var result=new SkillCatalogResult();
            var roots=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            Action<string,string> add=(path,source)=>{if(!roots.ContainsKey(path))roots.Add(path,source);};
            add(System.IO.Path.Combine(user,".codex","skills"),"내 Codex 스킬");
            add(System.IO.Path.Combine(user,".codex","skills",".system"),"Codex 기본 스킬");
            add(System.IO.Path.Combine(user,".agents","skills"),"내 공용 스킬");
            add(System.IO.Path.Combine(user,".claude","skills"),"내 Claude 스킬");
            if(!String.IsNullOrEmpty(project) && Directory.Exists(project)) {
                var cursor=new DirectoryInfo(project);
                while(cursor!=null) {
                    foreach(string kind in new[]{".agents",".codex",".claude"})add(System.IO.Path.Combine(cursor.FullName,kind,"skills"),"프로젝트 · "+kind);
                    if(Directory.Exists(System.IO.Path.Combine(cursor.FullName,".git")) || File.Exists(System.IO.Path.Combine(cursor.FullName,".git")))break;
                    // With no Git root, only this project is in scope.
                    if(!HasGitAncestor(cursor))break;
                    cursor=cursor.Parent;
                }
            }
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var root in roots) {
                try {
                    if(!Directory.Exists(root.Key))continue;
                    foreach(var directory in Directory.GetDirectories(root.Key)) {
                        string file=System.IO.Path.Combine(directory,"SKILL.md");
                        if(!File.Exists(file) || !seen.Add(System.IO.Path.GetFullPath(file)))continue;
                        try { result.Items.Add(Read(file,root.Value)); }
                        catch(Exception ex) { if(!Expected(ex))throw;result.Warnings.Add("일부 스킬 파일을 읽지 못했습니다."); }
                    }
                }catch(Exception ex) { if(!Expected(ex))throw;result.Warnings.Add("일부 스킬 폴더를 읽지 못했습니다."); }
            }
            result.Items.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.Name,b.Name));return result;
        }
        static bool HasGitAncestor(DirectoryInfo directory) {
            for(var parent=directory.Parent;parent!=null;parent=parent.Parent)
                if(Directory.Exists(System.IO.Path.Combine(parent.FullName,".git")) || File.Exists(System.IO.Path.Combine(parent.FullName,".git")))return true;
            return false;
        }
        static bool Expected(Exception ex) { return ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException; }
        internal static InstalledSkill Read(string path,string source) {
            if(new FileInfo(path).Length>1024*1024)throw new IOException("스킬 파일이 너무 큽니다.");
            string text=File.ReadAllText(path,Encoding.UTF8);
            string name=Header(text,"name"),description=Header(text,"description");
            if(String.IsNullOrWhiteSpace(name))name=new DirectoryInfo(System.IO.Path.GetDirectoryName(path)).Name;
            if(String.IsNullOrWhiteSpace(description))description="설치된 SKILL.md의 지침을 프로젝트에서 사용합니다.";
            string full=System.IO.Path.GetFullPath(path),id;
            using(var sha=SHA256.Create())id=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(full.ToUpperInvariant()))).Replace("-","").ToLowerInvariant();
            return new InstalledSkill{Id=id,Name=Limit(name,160),Description=Limit(description,1200),Path=full,Source=source};
        }
        internal static string Header(string text,string field) {
            var front=Regex.Match(text,@"\A\uFEFF?---\s*\r?\n(?<body>[\s\S]*?)\r?\n---(?:\s|$)");
            if(!front.Success)return "";
            var match=Regex.Match(front.Groups["body"].Value,@"(?m)^"+Regex.Escape(field)+@":[ \t]*(?<value>[^\r\n]*)(?<continuation>(?:\r?\n[ \t]+[^\r\n]*)*)");
            if(!match.Success)return "";
            string value=match.Groups["value"].Value.Trim();
            if(value==">" || value=="|" || value==">-" || value=="|-")value=Regex.Replace(match.Groups["continuation"].Value.Trim(),@"\s+"," ");
            else if(value.Length>=2 && value[0]=='"' && value[value.Length-1]=='"') {
                try{value=LocalData.Decode<string>(value);}catch(ArgumentException){value=value.Substring(1,value.Length-2);}
            }else if(value.Length>=2 && value[0]=='\'' && value[value.Length-1]=='\'')value=value.Substring(1,value.Length-2).Replace("''","'");
            return value;
        }
        static string Limit(string text,int size) { return text.Length>size?text.Substring(0,size):text; }
    }
}

