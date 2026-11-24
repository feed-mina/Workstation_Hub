using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MinaDesk {
    internal static class ProjectLauncher {
        // SQLite is included in supported Windows versions. Open VS Code's history read-only.
        [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_open_v2(byte[] file,out IntPtr db,int flags,IntPtr vfs);
        [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int length,out IntPtr statement,IntPtr tail);
        [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_step(IntPtr statement);
        [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_column_text(IntPtr statement,int column);
        [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_column_bytes(IntPtr statement,int column);
        [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_finalize(IntPtr statement);
        [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_close(IntPtr db);
        static byte[] Utf8(string value){return Encoding.UTF8.GetBytes(value+"\0");}
        static string History() {
            string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Code","User","globalStorage","state.vscdb");
            if(!File.Exists(file))return null;
            IntPtr db=IntPtr.Zero,statement=IntPtr.Zero;
            try {
                if(sqlite3_open_v2(Utf8(file),out db,1,IntPtr.Zero)!=0)return null;
                var query=Utf8("SELECT value FROM ItemTable WHERE key='history.recentlyOpenedPathsList'");
                if(sqlite3_prepare_v2(db,query,-1,out statement,IntPtr.Zero)!=0||sqlite3_step(statement)!=100)return null;
                int length=sqlite3_column_bytes(statement,0);
                if(length<1||length>1048576)return null;
                var bytes=new byte[length];Marshal.Copy(sqlite3_column_text(statement,0),bytes,0,length);
                return Encoding.UTF8.GetString(bytes);
            }finally{if(statement!=IntPtr.Zero)sqlite3_finalize(statement);if(db!=IntPtr.Zero)sqlite3_close(db);}
        }
        internal static bool IsProject(string path){return Directory.Exists(path)||(File.Exists(path)&&String.Equals(Path.GetExtension(path),".code-workspace",StringComparison.OrdinalIgnoreCase));}
        internal static List<string> Recent(MacroSettings settings) {
            var paths=settings.Projects.Where(IsProject).ToList();
            try {
                string history=History();
                if(history!=null) {
                    var root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(history);
                    foreach(Dictionary<string,object> entry in (IEnumerable)root["entries"]) {
                        object value;
                        if(!entry.TryGetValue("folderUri",out value)) {
                            object workspace;
                            if(!entry.TryGetValue("workspace",out workspace))continue;
                            if(!((Dictionary<string,object>)workspace).TryGetValue("configPath",out value))continue;
                        }
                        Uri uri;if(value is string&&Uri.TryCreate((string)value,UriKind.Absolute,out uri)&&uri.IsFile&&IsProject(uri.LocalPath))paths.Add(uri.LocalPath);
                    }
                }
            }catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}catch(DllNotFoundException){}catch(InvalidCastException){}catch(KeyNotFoundException){}
            return paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
        }
        internal static string Quote(string argument) {
            // Windows command line quoting, including trailing backslashes and embedded quotes.
            var result=new StringBuilder("\"");int slashes=0;
            foreach(char c in argument) {
                if(c=='\\'){slashes++;continue;}
                if(c=='"')result.Append('\\',slashes*2+1);else result.Append('\\',slashes);
                result.Append(c);slashes=0;
            }
            result.Append('\\',slashes*2);return result.Append('"').ToString();
        }
        internal static void Code(MacroSettings settings,string project) {
            string executable=settings.CodePath;
            if(String.IsNullOrWhiteSpace(executable)) {
                executable=new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Microsoft VS Code","Code.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Microsoft VS Code","Code.exe")}.FirstOrDefault(File.Exists);
            }
            if(String.IsNullOrEmpty(executable)||!File.Exists(executable))throw new FileNotFoundException("VS Code 설치 위치를 매크로 설정에서 지정하세요.");
            Start(executable,project==null?"":Quote(project),settings.WorkingFolder);
        }
        internal static void Terminal(string folder) {
            if(!Directory.Exists(folder))throw new DirectoryNotFoundException("작업 폴더를 매크로 설정에서 다시 선택하세요.");
            string terminal=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","WindowsApps","wt.exe");
            if(File.Exists(terminal))Start(terminal,"-d "+Quote(folder),folder);
            else Start(Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe"),"-NoLogo -NoExit",folder);
        }
        static void Start(string executable,string arguments,string folder) {
            var info=new ProcessStartInfo(executable,arguments){UseShellExecute=false};
            if(Directory.Exists(folder))info.WorkingDirectory=folder;
            using(var process=Process.Start(info)){if(process==null)throw new IOException("프로그램을 시작하지 못했습니다.");}
        }
    }
}
