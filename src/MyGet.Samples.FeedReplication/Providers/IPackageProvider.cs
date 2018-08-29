using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MyGet.Samples.FeedReplication.Providers
{
    public interface IPackageProvider
    {
        Task<ICollection<PackageDefinition>> GetPackages(DateTime since);

        Task<Stream> GetPackageStream(PackageDefinition packageDefinition);
        
        Task PushPackage(PackageDefinition packageDefinition, Stream packageStream);

        Task DeletePackage(PackageDefinition packageDefinition);
    }
}