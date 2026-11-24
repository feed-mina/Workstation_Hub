using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MinaDesk {
    sealed class Telemetry {
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
        [StructLayout(LayoutKind.Sequential)] struct Memory {public uint Length,Load; public ulong Total,Free,PageTotal,PageFree,VirtualTotal,VirtualFree,Extended;}
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool GlobalMemoryStatusEx(ref Memory value);
        long lastTick; ulong lastTotal,lastIdle;
        Dictionary<string,long[]> lastNics=new Dictionary<string,long[]>();
        public void Reset(){lastTick=0;lastTotal=0;lastNics.Clear();}
        public object Sample() {
            double? cpu=null, ram=null, usedGb=null,totalGb=null,driveFree=null,driveTotal=null,down=null,up=null;
            ulong idle,k,u;
            if(GetSystemTimes(out idle,out k,out u)){
                ulong total=k+u;
                if(lastTotal>0 && total>lastTotal && idle>=lastIdle) cpu=Math.Max(0,Math.Min(100,100.0*(1.0-(double)(idle-lastIdle)/(total-lastTotal))));
                lastTotal=total;lastIdle=idle;
            }
            var mem=new Memory{Length=(uint)Marshal.SizeOf(typeof(Memory))};
            if(GlobalMemoryStatusEx(ref mem)&&mem.Total>0){totalGb=mem.Total/1073741824.0;usedGb=(mem.Total-mem.Free)/1073741824.0;ram=100.0*(mem.Total-mem.Free)/mem.Total;}
            string volume=Path.GetPathRoot(Environment.SystemDirectory);
            try {var drive=new DriveInfo(volume);driveFree=drive.AvailableFreeSpace/1073741824.0;driveTotal=drive.TotalSize/1073741824.0;} catch(IOException){} catch(UnauthorizedAccessException){}
            long now=Stopwatch.GetTimestamp(); double elapsed=(now-lastTick)/(double)Stopwatch.Frequency;
            var next=new Dictionary<string,long[]>();long inbound=0,outbound=0;bool matched=false;
            try {foreach(var n in NetworkInterface.GetAllNetworkInterfaces()) {
                if(n.OperationalStatus!=OperationalStatus.Up || n.NetworkInterfaceType==NetworkInterfaceType.Loopback || n.NetworkInterfaceType==NetworkInterfaceType.Tunnel)continue;
                try {var s=n.GetIPStatistics();var v=new long[]{s.BytesReceived,s.BytesSent};next[n.Id]=v;long[] old;
                    if(lastNics.TryGetValue(n.Id,out old)&&v[0]>=old[0]&&v[1]>=old[1]){inbound+=v[0]-old[0];outbound+=v[1]-old[1];matched=true;}
                } catch(NetworkInformationException){}
            }}catch(NetworkInformationException){}
            if(lastTick!=0 && matched && elapsed>0 && elapsed<8){down=inbound*8/elapsed/1000000;up=outbound*8/elapsed/1000000;}
            lastNics=next;lastTick=now;
            return new {kind="sample",time=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),cpu,ram,usedGb,totalGb,driveFree,driveTotal,volume,down,up};
        }
    }
}

