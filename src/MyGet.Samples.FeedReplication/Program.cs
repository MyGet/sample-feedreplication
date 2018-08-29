using System;
using System.IO;
using System.Threading.Tasks;
using MyGet.Samples.FeedReplication.Configuration;
using MyGet.Samples.FeedReplication.Providers;
using Newtonsoft.Json;
using Serilog;

namespace MyGet.Samples.FeedReplication
{
    // TODO: Retries on HTTP calls
    class Program
    {
        public static async Task Main(string[] args)
        {
            // Setup logging
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.LiterateConsole()
                .WriteTo.RollingFile("log-{Date}.txt", 
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Message}\r\n{Exception}", retainedFileCountLimit: 7)
                .CreateLogger();
            
            // Load configuration
            Log.Logger.Information("Loading configuration...");
            var configuration = JsonConvert.DeserializeObject<ReplicationConfiguration>(
                File.ReadAllText("config.json"));
            Log.Logger.Information("Loaded configuration.");
            
            // Start replication tasks
            Log.Logger.Information("Starting replication tasks...");
            foreach (var replicationPair in configuration.ReplicationPairs)
            {
                // Validate replicator
                if (!replicationPair.Destination.Url.Contains("myget.org")
                    && !replicationPair.Destination.Url.Contains("localhost:1196"))
                {
                    Log.Logger.Error("Replication pair {description} destination URL should be on MyGet.org.", replicationPair.Description);
                    continue;
                }
                
                // Build replicator
                Replicator replicator = null;
                
                switch (replicationPair.Type.ToLowerInvariant())
                {
                    case "nuget":
                        replicator = new Replicator(
                            new NuGetPackageProvider(replicationPair.Source.Url, replicationPair.Source.Token, replicationPair.Source.Username, replicationPair.Source.Password),
                            new NuGetPackageProvider(replicationPair.Destination.Url, replicationPair.Destination.Token, replicationPair.Destination.Username, replicationPair.Destination.Password),
                            replicationPair.AllowDeleteFromDestination);
                        break;
                    case "npm":
                        replicator = new Replicator(
                            new NpmPackageProvider(replicationPair.Source.Url, replicationPair.Source.Token, replicationPair.Source.Username, replicationPair.Source.Password),
                            new NpmPackageProvider(replicationPair.Destination.Url, replicationPair.Destination.Token, replicationPair.Destination.Username, replicationPair.Destination.Password),
                            replicationPair.AllowDeleteFromDestination);
                        break;
                    default:
                        Log.Logger.Error("Unknown type {type} for replication pair {description}.", replicationPair.Type, replicationPair.Description);
                        break;
                }

                // Run replicator
                if (replicator != null)
                {
                    Log.Logger.Information("Starting replication task for replication pair: {description}...", replicationPair.Description, replicationPair);
                    await replicator.PerformReplication();
                    Log.Logger.Information("Finished replication task for replication pair: {description}.", replicationPair.Description, replicationPair);
                }
            }
            Log.Logger.Information("Finished replication tasks.");
        }
    }
}