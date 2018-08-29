using System;
using System.Collections.Generic;

namespace MyGet.Samples.FeedReplication
{
    public class PackageDefinition
    {
        public string PackageType { get; set; }
        public string PackageIdentifier { get; set; }
        public string PackageVersion { get; set; }

        public DateTime LastEdited { get; set; }

        public Uri ContentUri { get; set; }
        
        public bool IsListed { get; set; }

        private sealed class PackageDefinitionFullEqualityComparer : IEqualityComparer<PackageDefinition>
        {
            public bool Equals(PackageDefinition x, PackageDefinition y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return string.Equals(x.PackageType, y.PackageType, StringComparison.OrdinalIgnoreCase) && string.Equals(x.PackageIdentifier, y.PackageIdentifier, StringComparison.OrdinalIgnoreCase) && string.Equals(x.PackageVersion, y.PackageVersion, StringComparison.OrdinalIgnoreCase) && x.IsListed == y.IsListed;
            }

            public int GetHashCode(PackageDefinition obj)
            {
                unchecked
                {
                    var hashCode = (obj.PackageType != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PackageType) : 0);
                    hashCode = (hashCode * 397) ^ (obj.PackageIdentifier != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PackageIdentifier) : 0);
                    hashCode = (hashCode * 397) ^ (obj.PackageVersion != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PackageVersion) : 0);
                    hashCode = (hashCode * 397) ^ obj.IsListed.GetHashCode();
                    return hashCode;
                }
            }
        }
        
        private sealed class PackageDefinitionIdentityEqualityComparer : IEqualityComparer<PackageDefinition>
        {
            public bool Equals(PackageDefinition x, PackageDefinition y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return string.Equals(x.PackageType, y.PackageType, StringComparison.OrdinalIgnoreCase) && string.Equals(x.PackageIdentifier, y.PackageIdentifier, StringComparison.OrdinalIgnoreCase) && string.Equals(x.PackageVersion, y.PackageVersion, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(PackageDefinition obj)
            {
                unchecked
                {
                    var hashCode = (obj.PackageType != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PackageType) : 0);
                    hashCode = (hashCode * 397) ^ (obj.PackageIdentifier != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PackageIdentifier) : 0);
                    hashCode = (hashCode * 397) ^ (obj.PackageVersion != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PackageVersion) : 0);
                    return hashCode;
                }
            }
        }

        public static IEqualityComparer<PackageDefinition> FullComparer { get; } = new PackageDefinitionFullEqualityComparer();
        public static IEqualityComparer<PackageDefinition> IdentityComparer { get; } = new PackageDefinitionIdentityEqualityComparer();
    }
}