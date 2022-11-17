using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EddiConfigService
{
    [JsonObject(MemberSerialization.OptOut), RelativePath(@"\trademonitor.json")]
    public class TradeMonitorConfiguration : Config
    {
        public int maxSearchDistanceFromStarLs { get; set; }
    }
}
