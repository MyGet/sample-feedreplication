using System.Collections.Generic;
using Newtonsoft.Json;

namespace MyGet.Samples.FeedReplication.Configuration
{
    public class ReplicationConfiguration
    {
        public ReplicationConfiguration()
        {
            ReplicationPairs = new List<ReplicationPair>();
        }
        
        [JsonProperty("replicationPairs")]
        public List<ReplicationPair> ReplicationPairs { get; set; }
    }
}