using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace MinaDesk {
    internal sealed class TemperatureReading {
        public double celsius {get;set;}
        public string sensor {get;set;}
        public string source {get;set;}
    }

    internal static class TemperatureSensors {
        const string MonitorNamespace=@"root\LibreHardwareMonitor";

        internal static void Read(out TemperatureReading cpu,out TemperatureReading gpu) {
            cpu=null;gpu=null;
            var adapters=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            try {
                GpuTelemetry.Read("SELECT Identifier,HardwareType FROM Hardware",row=>{
                    string id=row["Identifier"] as string,type=row["HardwareType"] as string;
                    if(!String.IsNullOrEmpty(id) && !String.IsNullOrEmpty(type))adapters[id]=type;
                },MonitorNamespace);
                var samples=new List<MonitorReading>();
                GpuTelemetry.Read("SELECT Parent,Name,Value FROM Sensor WHERE SensorType = 'Temperature'",row=>{
                    string type,parent=row["Parent"] as string,name=row["Name"] as string;
                    if(parent==null || name==null || !adapters.TryGetValue(parent,out type) || row["Value"]==null)return;
                    double number;
                    if(!Double.TryParse(Convert.ToString(row["Value"],CultureInfo.InvariantCulture),
                        NumberStyles.Float,CultureInfo.InvariantCulture,out number))return;
                    var sample=Identify(type,name,number);
                    if(sample!=null)samples.Add(sample);
                },MonitorNamespace);
                cpu=Pick(samples,true);
                gpu=Pick(samples,false);
            }catch(ManagementException){}catch(COMException){}catch(UnauthorizedAccessException){}
            if(gpu==null)gpu=WindowsGpuTemperature.Read();
        }
        internal sealed class MonitorReading {
            internal bool Cpu;
            internal int Rank;
            internal TemperatureReading Value;
        }
        internal static MonitorReading Identify(string type,string name,double value) {
            if(String.IsNullOrWhiteSpace(name) || !Valid(value))return null;
            bool cpu=String.Equals(type,"Cpu",StringComparison.OrdinalIgnoreCase);
            bool gpu=type!=null && type.StartsWith("Gpu",StringComparison.OrdinalIgnoreCase);
            if(!cpu && !gpu)return null;
            string key=name.Trim().ToLowerInvariant();
            int rank=0;
            string sensor=null;
            if(cpu) {
                if(key=="cpu package" || key=="package"){rank=3;sensor="CPU 패키지";}
                else if(key=="core (tctl/tdie)" || key=="cpu (tctl/tdie)" || key=="cpu die (average)"){
                    rank=2;sensor=key=="cpu die (average)"?"CPU 다이 평균":"CPU Tctl/Tdie";
                }else if(key.StartsWith("core #",StringComparison.Ordinal) && !key.Contains("distance")){
                    rank=1;sensor="CPU 코어 최고 온도";
                }
            }else {
                if(key=="gpu core" || key=="gpu temperature" || key=="gpu"){rank=2;sensor="GPU 코어";}
                else if(key=="gpu hot spot" || key=="gpu hotspot"){rank=1;sensor="GPU 핫스팟";}
            }
            return rank==0?null:new MonitorReading {Cpu=cpu,Rank=rank,Value=new TemperatureReading {
                celsius=value,sensor=sensor,source="LibreHardwareMonitor"
            }};
        }
        static TemperatureReading Pick(IEnumerable<MonitorReading> samples,bool cpu) {
            return samples.Where(item=>item.Cpu==cpu).OrderByDescending(item=>item.Rank)
                .ThenByDescending(item=>item.Value.celsius).Select(item=>item.Value).FirstOrDefault();
        }
        internal static bool Valid(double value) {return value>0 && value<=150;}
    }

    internal static class WindowsGpuTemperature {
        // KMTQAITYPE_PHYSICALADAPTERCOUNT and KMTQAITYPE_ADAPTERPERFDATA in the Windows SDK.
        const int AdapterCount=30,AdapterPerformance=62;
        [StructLayout(LayoutKind.Sequential)] struct AdapterList {internal uint Count;internal IntPtr Adapters;}
        [StructLayout(LayoutKind.Sequential)] struct Adapter {
            internal uint Handle,LuidLow;
            internal int LuidHigh;
            internal uint Sources;
            internal int PrecisePresent;
        }
        [StructLayout(LayoutKind.Sequential)] struct AdapterQuery {
            internal uint Handle;
            internal int Type;
            internal IntPtr Data;
            internal uint Size;
        }
        [StructLayout(LayoutKind.Sequential)] struct Performance {
            internal uint Index;
            internal ulong MemoryFrequency,MaxMemoryFrequency,MaxMemoryFrequencyOc,MemoryBandwidth,PcieBandwidth;
            internal uint FanRpm,Power,Temperature;
            internal byte PowerStateOverride;
        }
        [DllImport("gdi32.dll")] static extern int D3DKMTEnumAdapters2(ref AdapterList args);
        [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref AdapterQuery args);
        [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref uint adapter);

        internal static TemperatureReading Read() {
            const int maxAdapters=64;
            double? hottest=null;
            int adapterSize=Marshal.SizeOf(typeof(Adapter)),performanceSize=Marshal.SizeOf(typeof(Performance));
            var list=new AdapterList{Count=maxAdapters,Adapters=Marshal.AllocHGlobal(maxAdapters*adapterSize)};
            IntPtr data=Marshal.AllocHGlobal(performanceSize);
            try {
                if(D3DKMTEnumAdapters2(ref list)!=0 || list.Count>maxAdapters)return null;
                for(int i=0;i<list.Count;i++){
                    uint handle=((Adapter)Marshal.PtrToStructure(IntPtr.Add(list.Adapters,i*adapterSize),typeof(Adapter))).Handle;
                    if(handle==0)continue;
                    try {
                        var query=new AdapterQuery{Handle=handle,Type=AdapterCount,Data=data,Size=sizeof(uint)};
                        Marshal.WriteInt32(data,0);
                        int count=D3DKMTQueryAdapterInfo(ref query)==0?Math.Max(1,Math.Min(16,Marshal.ReadInt32(data))):1;
                        query.Type=AdapterPerformance;query.Size=(uint)performanceSize;
                        for(uint index=0;index<count;index++){
                            Marshal.StructureToPtr(new Performance{Index=index},data,false);
                            if(D3DKMTQueryAdapterInfo(ref query)!=0)continue;
                            var measured=(Performance)Marshal.PtrToStructure(data,typeof(Performance));
                            double degrees=measured.Temperature/10.0;
                            if(TemperatureSensors.Valid(degrees) && (!hottest.HasValue || hottest.Value<degrees))hottest=degrees;
                        }
                    }finally{D3DKMTCloseAdapter(ref handle);}
                }
            }catch(EntryPointNotFoundException){}catch(DllNotFoundException){}
            finally{Marshal.FreeHGlobal(data);Marshal.FreeHGlobal(list.Adapters);}
            return hottest.HasValue?new TemperatureReading{celsius=hottest.Value,sensor="그래픽 어댑터 최고 온도",source="Windows 그래픽 드라이버"}:null;
        }
    }
}
