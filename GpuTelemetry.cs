using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;

namespace MinaDesk {
    internal static class GpuTelemetry {
        internal static object Sample() {
            double? load=null,dedicated=null,shared=null;
            try {
                var engines=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
                Read("SELECT Name,UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine",row=>{
                    string name=row["Name"] as string;
                    int start=name==null?-1:name.IndexOf("_luid_",StringComparison.OrdinalIgnoreCase);
                    if(start<0 || row["UtilizationPercentage"]==null)return;
                    string key=name.Substring(start);double existing;
                    engines.TryGetValue(key,out existing);
                    engines[key]=existing+Convert.ToDouble(row["UtilizationPercentage"],CultureInfo.InvariantCulture);
                });
                if(engines.Count>0)load=Math.Max(0,Math.Min(100,engines.Values.Max()));
            }catch(ManagementException){}catch(System.Runtime.InteropServices.COMException){}catch(UnauthorizedAccessException){}
            try {
                var adapters=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Read("SELECT Name,DedicatedUsage,SharedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory",row=>{
                    string name=row["Name"] as string;
                    if(String.IsNullOrEmpty(name) || name=="_Total" || !adapters.Add(name))return;
                    const double bytesPerGiB=1073741824.0;
                    if(row["DedicatedUsage"]!=null)dedicated=(dedicated??0)+Convert.ToDouble(row["DedicatedUsage"],CultureInfo.InvariantCulture)/bytesPerGiB;
                    if(row["SharedUsage"]!=null)shared=(shared??0)+Convert.ToDouble(row["SharedUsage"],CultureInfo.InvariantCulture)/bytesPerGiB;
                });
            }catch(ManagementException){}catch(System.Runtime.InteropServices.COMException){}catch(UnauthorizedAccessException){}
            return new {kind="gpu-sample",load,dedicated,shared,
                status=load.HasValue || dedicated.HasValue || shared.HasValue?
                    "Windows 성능 카운터 · 가장 바쁜 엔진과 어댑터 메모리 합계":"이 PC에서 사용할 수 있는 GPU 성능 카운터를 찾지 못했습니다."};
        }
        static void Read(string query,Action<ManagementBaseObject> row) {
            using(var search=new ManagementObjectSearcher(@"root\CIMV2",query,
                new EnumerationOptions{ReturnImmediately=true,Timeout=TimeSpan.FromSeconds(3)}))
            using(var values=search.Get())
            foreach(ManagementBaseObject value in values)using(value)row(value);
        }
    }
}
