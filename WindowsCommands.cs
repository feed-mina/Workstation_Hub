using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MinaDesk {
    internal static class WindowsCommands {
        [DllImport("user32.dll",SetLastError=true)]static extern bool LockWorkStation();
        [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,Input[] input,int size);
        [StructLayout(LayoutKind.Sequential)]struct Input {internal uint type;internal InputUnion data;}
        [StructLayout(LayoutKind.Explicit)]struct InputUnion {
            [FieldOffset(0)]internal KeyboardInput keyboard;
            [FieldOffset(0)]internal MouseInput mouse;
        }
        [StructLayout(LayoutKind.Sequential)]struct KeyboardInput {internal ushort key,scan;internal uint flags,time;internal UIntPtr extra;}
        [StructLayout(LayoutKind.Sequential)]struct MouseInput {internal int x,y;internal uint data,flags,time;internal UIntPtr extra;}
        internal static void Lock() {if(!LockWorkStation())throw new Win32Exception(Marshal.GetLastWin32Error());}
        internal static void MediaKey() {
            var input=new Input[2];
            input[0].type=input[1].type=1;
            input[0].data.keyboard.key=input[1].data.keyboard.key=0xB3;
            input[1].data.keyboard.flags=2;
            if(SendInput(2,input,Marshal.SizeOf(typeof(Input)))!=2)throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        internal static string Screenshot(string root) {
            string folder=Path.Combine(root,DateTime.Now.ToString("yyyy-MM-dd"));Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,DateTime.Now.ToString("HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".png");
            var area=SystemInformation.VirtualScreen;
            using(var image=new Bitmap(area.Width,area.Height)) {
                using(var graphics=Graphics.FromImage(image))graphics.CopyFromScreen(area.Location,Point.Empty,area.Size);
                image.Save(path,ImageFormat.Png);
                try{Clipboard.SetImage(image);}catch(ExternalException){return "저장됨 · 클립보드가 사용 중입니다 · "+path;}
            }
            return "저장·클립보드 복사됨 · "+path;
        }
    }

    internal static class SpeakerVolume {
        internal static bool Read() {return Access(false);}
        internal static bool Toggle() {return Access(true);}
        static bool Access(bool toggle) {
            var enumerator=(IMMDeviceEnumerator)new DeviceEnumerator();IMMDevice device=null;object endpoint=null;
            try {
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0,1,out device));
                var iid=typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out endpoint));
                var volume=(IAudioEndpointVolume)endpoint;bool muted;
                Marshal.ThrowExceptionForHR(volume.GetMute(out muted));
                if(toggle){var context=Guid.Empty;Marshal.ThrowExceptionForHR(volume.SetMute(!muted,ref context));Marshal.ThrowExceptionForHR(volume.GetMute(out muted));}
                return muted;
            }finally{if(endpoint!=null)Marshal.ReleaseComObject(endpoint);if(device!=null)Marshal.ReleaseComObject(device);Marshal.ReleaseComObject(enumerator);}
        }
        [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]class DeviceEnumerator {}
        [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IMMDeviceEnumerator {
            [PreserveSig]int EnumAudioEndpoints(int flow,uint mask,out IntPtr devices);
            [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IMMDevice device);
        }
        [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IMMDevice {
            [PreserveSig]int Activate(ref Guid iid,uint context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)]out object result);
        }
        [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IAudioEndpointVolume {
            [PreserveSig]int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig]int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig]int GetChannelCount(out uint count);
            [PreserveSig]int SetMasterVolumeLevel(float level,ref Guid context);
            [PreserveSig]int SetMasterVolumeLevelScalar(float level,ref Guid context);
            [PreserveSig]int GetMasterVolumeLevel(out float level);
            [PreserveSig]int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig]int SetChannelVolumeLevel(uint channel,float level,ref Guid context);
            [PreserveSig]int SetChannelVolumeLevelScalar(uint channel,float level,ref Guid context);
            [PreserveSig]int GetChannelVolumeLevel(uint channel,out float level);
            [PreserveSig]int GetChannelVolumeLevelScalar(uint channel,out float level);
            [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool muted,ref Guid context);
            [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool muted);
        }
    }
}
