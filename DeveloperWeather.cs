using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MinaDesk {
    internal sealed class DeveloperWeather : IDisposable {
        static readonly Dictionary<string,string> Cities=new Dictionary<string,string> {
            {"서울","latitude=37.5665&longitude=126.9780"},{"인천","latitude=37.4563&longitude=126.7052"},
            {"수원","latitude=37.2636&longitude=127.0286"},{"대전","latitude=36.3504&longitude=127.3845"},
            {"대구","latitude=35.8714&longitude=128.6014"},{"광주","latitude=35.1595&longitude=126.8526"},
            {"부산","latitude=35.1796&longitude=129.0756"},{"제주","latitude=33.4996&longitude=126.5312"}
        };
        const string Forecast="https://api.open-meteo.com/v1/forecast?";
        const string Fields="&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=Asia%2FSeoul&forecast_days=7";
        readonly DeveloperPreferenceStore prefs;
        readonly Action changed;
        readonly HttpClient http=new HttpClient{Timeout=TimeSpan.FromSeconds(12)};
        readonly CancellationTokenSource stop=new CancellationTokenSource();
        object current,days;
        DateTimeOffset? readAt;
        DateTime nextRequest;
        string status;
        bool busy,disposed;
        string City {
            get {
                var city=prefs.Value.City;
                return city!=null && Cities.ContainsKey(city)?city:null;
            }
        }
        internal DeveloperWeather(DeveloperPreferenceStore prefs,Action changed){this.prefs=prefs;this.changed=changed;}
        internal object Snapshot() {
            return new {kind="widget-state",city=City,cities=Cities.Keys.ToArray(),weather=current,days,busy,
                status=status??(City==null?"도시를 선택하면 주간 예보를 조회합니다.":"날씨 새로고침을 눌러 예보를 확인하세요."),
                updated=readAt.HasValue?readAt.Value.ToLocalTime().ToString("MM-dd HH:mm"):null};
        }
        internal async Task Choose(string city) {
            if(busy || disposed)return;
            if(!String.IsNullOrEmpty(city) && !Cities.ContainsKey(city))return;
            prefs.Change(v=>v.City=String.IsNullOrEmpty(city)?null:city);
            current=null;days=null;readAt=null;status=null;nextRequest=DateTime.MinValue;changed();
            if(City!=null)await Refresh();
        }
        internal async Task Refresh() {
            string city=City;
            if(busy || disposed || city==null || DateTime.UtcNow<nextRequest)return;
            busy=true;status="예보 조회 중";changed();
            try {
                string address=Forecast+Cities[city]+Fields;
                using(var response=await http.GetAsync(address,stop.Token)) {
                    response.EnsureSuccessStatusCode();
                    var content=await response.Content.ReadAsStringAsync();
                    var data=LocalData.Decode<Dictionary<string,object>>(content);
                    var latest=Get(data,"current") as Dictionary<string,object>;
                    var daily=Get(data,"daily") as Dictionary<string,object>;
                    var dates=Get(daily,"time") as IList;
                    var codes=Get(daily,"weather_code") as IList;
                    var highs=Get(daily,"temperature_2m_max") as IList;
                    var lows=Get(daily,"temperature_2m_min") as IList;
                    if(latest==null || dates==null || codes==null || highs==null || lows==null)
                        throw new FormatException();
                    int count=new[]{dates.Count,codes.Count,highs.Count,lows.Count,7}.Min();
                    var parsed=Enumerable.Range(0,count).Select(i=>new {
                        date=FormatDate(dates[i]),description=Description(codes[i]),high=Temperature(highs[i]),low=Temperature(lows[i])
                    }).ToArray();
                    if(disposed)return;
                    current=new{temperature=Temperature(Get(latest,"temperature_2m")),description=Description(Get(latest,"weather_code"))};
                    days=parsed;readAt=DateTimeOffset.Now;nextRequest=DateTime.UtcNow.AddMinutes(5);
                    status="Open-Meteo 예보";
                }
            }catch(Exception) {
                if(!disposed){status=readAt.HasValue?"날씨 갱신 실패 · 마지막 예보 표시":"날씨를 불러오지 못했습니다. 잠시 후 다시 시도하세요.";nextRequest=DateTime.UtcNow.AddSeconds(30);}
            }finally{busy=false;if(!disposed)changed();}
        }
        static object Get(Dictionary<string,object> item,string key) {
            object value;return item!=null && item.TryGetValue(key,out value)?value:null;
        }
        static string FormatDate(object value) {
            DateTime date;
            return DateTime.TryParseExact(value as string,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date)?
                date.ToString("MM.dd (ddd)",CultureInfo.GetCultureInfo("ko-KR")):"—";
        }
        static string Temperature(object value) {
            double number;
            return value!=null && Double.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out number) &&
                !Double.IsNaN(number) && !Double.IsInfinity(number)?Math.Round(number).ToString("0",CultureInfo.InvariantCulture)+"°":"—";
        }
        static string Description(object value) {
            int code;
            if(value==null || !Int32.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),out code))return "확인 중";
            if(code==0)return "맑음";
            if(code<=3)return "구름";
            if(code<=48)return "안개";
            if(code>=95)return "뇌우";
            if((code>=71 && code<=77) || code>=85)return "눈";
            return "비";
        }
        public void Dispose(){disposed=true;stop.Cancel();http.Dispose();}
    }
}
