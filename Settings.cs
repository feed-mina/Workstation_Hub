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
        public Dictionary<string,PanelSettings> Panels {get;set;}
        public Settings(){ Panels=new Dictionary<string,PanelSettings>(); }
        internal static Settings Load(string file) {
            Settings value=null;
            try { value=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(file)); } catch(IOException){} catch(ArgumentException){} catch(InvalidOperationException){}
            if(value==null || value.Panels==null) value=new Settings();
            foreach(string id in Hub.Keys) if(!value.Panels.ContainsKey(id)||value.Panels[id]==null) value.Panels[id]=new PanelSettings();
            return value;
        }
        internal void Save(string file) {
            string temp=file+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this));
            if(File.Exists(file)) File.Replace(temp,file,null); else File.Move(temp,file);
        }
        internal static Rectangle Fit(Rectangle proposed) {
            var area=Screen.FromRectangle(proposed).WorkingArea;
            int w=Math.Min(area.Width,Math.Max(300,proposed.Width));
            int h=Math.Min(area.Height,Math.Max(210,proposed.Height));
            return new Rectangle(Math.Max(area.Left,Math.Min(proposed.X,area.Right-w)),Math.Max(area.Top,Math.Min(proposed.Y,area.Bottom-h)),w,h);
        }
        internal static Rectangle Initial(string key) {
            var a=Screen.PrimaryScreen.WorkingArea;
            int gap=9, cell=Math.Min(460,(a.Width-4*gap)/3);
            int left=a.Left+Math.Max(gap,(a.Width-(cell*3+2*gap))/2);
            int top=a.Top+gap;
            int tall=Math.Min(610,Math.Max(340,a.Height-260));
            int i=Array.IndexOf(Hub.Keys,key);
            if(a.Width<1000) {cell=Math.Max(310,(a.Width-gap*3)/2);left=a.Left+gap;tall=Math.Max(270,(a.Height-gap*3)/2);return Fit(new Rectangle(left+(i%2)*(cell+gap),top+(i/2)*(tall+gap),cell,tall));}
            return Fit(i==3 ? new Rectangle(left,top+tall+gap,3*cell+2*gap,Math.Min(240,a.Height-tall-3*gap)) : new Rectangle(left+i*(cell+gap),top,cell,tall));
        }
    }
}
