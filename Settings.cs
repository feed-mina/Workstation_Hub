using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MinaDesk {
    public class PanelSettings {
        public bool Enabled {get;set;}
        public int X {get;set;} public int Y {get;set;}
        public int Width {get;set;} public int Height {get;set;}
        public PanelSettings() { Enabled=true; }
        public Rectangle Bounds() {return new Rectangle(X,Y,Width,Height);}
        public void Remember(Rectangle rect) {X=rect.X; Y=rect.Y; Width=rect.Width; Height=rect.Height;}
    }
    public class Settings {
        public string HistoryShare {get;set;}
        public string Layout {get;set;}
        public string Theme {get;set;}
        public Dictionary<string,PanelSettings> Panels {get;set;}
        public Settings(){ Panels=new Dictionary<string,PanelSettings>();Layout="extended";Theme="green"; }
        internal static Settings Load(string file) {
            Settings value=null;
            try { value=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(file)); } catch(IOException){} catch(ArgumentException){} catch(InvalidOperationException){}
            if(value==null || value.Panels==null) value=new Settings();
            if(!ValidLayout(value.Layout))value.Layout="extended";
            if(value.Theme!="neon")value.Theme="green";
            foreach(string id in Hub.Keys) if(!value.Panels.ContainsKey(id)||value.Panels[id]==null)
                value.Panels[id]=new PanelSettings{Enabled=value.Layout!="original" || !IsExtension(id)};
            return value;
        }
        internal void Save(string file) {
            string temp=file+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this));
            if(File.Exists(file)) File.Replace(temp,file,null); else File.Move(temp,file);
        }
        internal static Rectangle Fit(Rectangle proposed) {
            var area=Screen.FromRectangle(proposed).WorkingArea;
            int w=Math.Min(area.Width,Math.Max(280,proposed.Width));
            int h=Math.Min(area.Height,Math.Max(153,proposed.Height));
            return new Rectangle(Math.Max(area.Left,Math.Min(proposed.X,area.Right-w)),Math.Max(area.Top,Math.Min(proposed.Y,area.Bottom-h)),w,h);
        }
        internal static bool IsExtension(string key) { return key=="dev" || key=="widgets"; }
        internal static bool ValidLayout(string value) { return value=="extended" || value=="original" || value=="focus"; }
        internal static Rectangle Initial(string key,string layout="extended") {
            return Fit(LayoutBounds(Screen.PrimaryScreen.WorkingArea,key,layout));
        }
        internal static Rectangle LayoutBounds(Rectangle area,string key,string layout) {
            const int gap=8;
            bool compact=area.Width<1040;
            int columns=compact?2:3;
            int columnWidth=(area.Width-gap*(columns+1))/columns;
            int logHeight=Math.Max(153,Math.Min(215,area.Height/5));
            int usable=area.Height-logHeight-gap*4;
            int firstHeight=layout=="original"?usable+gap:usable*(layout=="focus"?61:36)/100;
            int secondHeight=usable-firstHeight;
            int left=area.Left+gap;
            int top=area.Top+gap;
            int second=top+firstHeight+gap;
            if(layout=="original" && IsExtension(key))
                return new Rectangle(left,top,key=="dev"?Math.Min(1050,area.Width-gap*2):columnWidth,Math.Min(700,area.Height-gap*2));
            if(compact && layout!="original") {
                string[] order=layout=="focus"?new[]{"macro","dev","pc","home","widgets","log"}:new[]{"macro","pc","home","widgets","dev","log"};
                int index=Array.IndexOf(order,key);
                int height=(area.Height-gap*4)/3;
                return new Rectangle(left+(index%2)*(columnWidth+gap),top+(index/2)*(height+gap),columnWidth,height);
            }
            if(key=="log")return new Rectangle(left,area.Bottom-logHeight-gap,area.Width-gap*2,logHeight);
            if(layout=="original" && compact) {
                int rowHeight=(usable-gap)/2;
                if(key=="home")return new Rectangle(left,top+rowHeight+gap,area.Width-gap*2,rowHeight);
                return new Rectangle(left+(key=="pc"?columnWidth+gap:0),top,columnWidth,rowHeight);
            }
            if(layout=="focus") {
                if(key=="macro")return new Rectangle(left,top,columnWidth,firstHeight);
                if(key=="dev")return new Rectangle(left+columnWidth+gap,top,area.Width-columnWidth-gap*3,firstHeight);
                return new Rectangle(left+(key=="pc"?0:key=="home"?1:2)*(columnWidth+gap),second,columnWidth,secondHeight);
            }
            if(key=="dev")return new Rectangle(left,second,columnWidth*2+gap,secondHeight);
            if(key=="widgets")return new Rectangle(left+(columns-1)*(columnWidth+gap),second,columnWidth,secondHeight);
            return new Rectangle(left+(key=="macro"?0:key=="pc"?1:2)*(columnWidth+gap),top,columnWidth,firstHeight);
        }
    }
}

