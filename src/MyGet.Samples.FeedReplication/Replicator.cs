using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MyGet.Samples.FeedReplication.Providers;
using Serilog;
using Serilog.Core;

namespace MyGet.Samples.FeedReplication
{
    public class Replicator
    {
        private const int ConcurrentTasksLimit = 8;   
        
        private readonly IPackageProvider _sourceProvider;
        private readonly IPackageProvider _destinationProvider;
        private readonly bool _allowDeleteFromDestination;

        public Replicator(IPackageProvider sourceProvider, IPackageProvider destinationProvider, bool allowDeleteFromDestination)
        {
            _sourceProvider = sourceProvider;
            _destinationProvider = destinationProvider;
            _allowDeleteFromDestination = allowDeleteFromDestination;
        }
        
        public async Task PerformReplication()
        {
            Log.Logger.Verbose("Concurrent task limit used for replication: {limit}.", ConcurrentTasksLimit);
            var throttler = new SemaphoreSlim(ConcurrentTasksLimit);
            
            Log.Logger.Information("Fetching packages from providers...");
            var fetchTasks = new[]
            {
                _sourceProvider.GetPackages(since: DateTime.MinValue /*cursor.Value*/),
                _destinationProvider.GetPackages(since: DateTime.MinValue)
            };

            await Task.WhenAll(fetchTasks);
            Log.Logger.Information("Fetched packages from providers.");

            var sourcePackages = fetchTasks[0].Result;
            var destinationPackages = fetchTasks[1].Result;

            var packagesToMirror = sourcePackages.Except(destinationPackages, PackageDefinition.FullComparer).ToList();
            var packagesToDelete = destinationPackages.Except(sourcePackages, PackageDefinition.IdentityComparer).ToList();
            
            Log.Logger.Information("# of packages on source: {numberOfPackages}", sourcePackages.Count);
            Log.Logger.Information("# of packages on destination: {numberOfPackages}", destinationPackages.Count);
            Log.Logger.Information("# of packages to replicate from source to destination: {numberOfPackages}", packagesToMirror.Count);
            Log.Logger.Information("# of packages to remove from destination: {numberOfPackages}", packagesToDelete.Count);

            // 1. Mirror packages from source that are not in destination
            var mirrorTasks = new List<Task>();
            foreach (var packageDefinition in packagesToMirror.OrderBy(p => p.PackageIdentifier).ThenBy(p => p.PackageVersion))
            {
                mirrorTasks.Add(Task.Factory.StartNew(async () =>
                {
                    try
                    {
                        await throttler.WaitAsync();

                        Log.Logger.Verbose(
                            "Replicating {packageType} package {packageIdentifier}@{packageVersion} from source to destination...",
                            packageDefinition.PackageType, packageDefinition.PackageIdentifier, packageDefinition.PackageVersion);

                        using (var packageStream = await StreamUtilities.MakeSeekable(
                            await _sourceProvider.GetPackageStream(packageDefinition)))
                        {
                            await _destinationProvider.PushPackage(packageDefinition, packageStream);
                        }

                        Log.Logger.Information(
                            "Replicated {packageType} package {packageIdentifier}@{packageVersion} from source to destination.",
                            packageDefinition.PackageType, packageDefinition.PackageIdentifier,
                            packageDefinition.PackageVersion);
                    }
                    catch (HttpRequestException requestException)
                    {
                        if (requestException.Message.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Log.Logger.Error(
                                "Replicating {packageType} package {packageIdentifier}@{packageVersion} failed: source returned 404 status code.",
                                 packageDefinition.PackageType, packageDefinition.PackageIdentifier, packageDefinition.PackageVersion);
                        }
                    }
                    finally
                    {
                        throttler.Release();
                    }
                }).Unwrap());
            }

            await Task.WhenAll(mirrorTasks);
            
            // 2. Delete packages from destination that are not in source
            if (_allowDeleteFromDestination)
            {
                var deleteTasks = new List<Task>();
                foreach (var packageDefinition in packagesToDelete.OrderBy(p => p.PackageIdentifier).ThenBy(p => p.PackageVersion))
                {
                    deleteTasks.Add(Task.Factory.StartNew(async () =>
                    {
                        try
                        {
                            await throttler.WaitAsync();

                            Log.Logger.Verbose(
                                "Deleting {packageType} package {packageIdentifier}@{packageVersion} from destination...",
                                    packageDefinition.PackageType, packageDefinition.PackageIdentifier, packageDefinition.PackageVersion);

                            await _destinationProvider.DeletePackage(packageDefinition);

                            Log.Logger.Information(
                                "Deleted {packageType} package {packageIdentifier}@{packageVersion} from destination.",
                                    packageDefinition.PackageType, packageDefinition.PackageIdentifier, packageDefinition.PackageVersion);
                        }
                        finally
                        {
                            throttler.Release();
                        }
                    }).Unwrap());
                }

                await Task.WhenAll(deleteTasks);
            }
            else if (packagesToDelete.Count > 0)
            {
                Log.Logger.Information("Skip deleting packages from destination.");
            }
        }
    }
}