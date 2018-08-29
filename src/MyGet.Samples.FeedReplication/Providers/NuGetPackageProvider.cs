using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Serilog;

namespace MyGet.Samples.FeedReplication.Providers
{
    public class NuGetPackageProvider : IPackageProvider
    {
        private static readonly HttpClient HttpClient = new HttpClient(new RedirectAuthenticatedRequestHttpClientHandler());
        
        private static readonly XNamespace AtomNamespace = "http://www.w3.org/2005/Atom";
        private static readonly XNamespace DataServicesNamespace = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        private static readonly XNamespace MetadataNamespace = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
        
        private static readonly TimeSpan PushTimeout = TimeSpan.FromMinutes(10);
        
        private readonly string _repositoryUrl;
        private readonly string _writeToken;
        private readonly string _readUsername;
        private readonly string _readPassword;

        public NuGetPackageProvider(string repositoryUrl, string writeToken, string readUsername, string readPassword)
        {
            _repositoryUrl = repositoryUrl.TrimEnd('/');
            _writeToken = writeToken;
            _readUsername = readUsername;
            _readPassword = readPassword;
        }
        
        public async Task<ICollection<PackageDefinition>> GetPackages(DateTime since)
        {
            Log.Logger.Verbose("Getting packages since: {since}", since);
            
            var returnValue = new List<PackageDefinition>();
            
            // Fetch first page
            var result = await GetPackagesFromUrl(
                $"{_repositoryUrl}/Packages?$select=Id,Version,LastEdited,Published&$orderby=LastEdited%20desc&$filter=LastEdited%20gt%20datetime%27{since:s}%27");

            returnValue.AddRange(result.Item2);
            
            // While there are more pages, fetch more pages
            while (!string.IsNullOrEmpty(result.Item1))
            {
                Log.Logger.Verbose("Following OData continuation URL: {url}.", result.Item1);
                
                result = await GetPackagesFromUrl(result.Item1);
             
                returnValue.AddRange(result.Item2);   
            }

            return returnValue;
        }

        public async Task<Stream> GetPackageStream(PackageDefinition packageDefinition)
        {
            Log.Logger.Verbose("Getting package stream from URL: {url}.", packageDefinition.ContentUri);
            
            var request = new HttpRequestMessage(HttpMethod.Get, packageDefinition.ContentUri);
            await EnsureAuthenticated(request);
            var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStreamAsync();
        }

        public async Task PushPackage(PackageDefinition packageDefinition, Stream packageStream)
        {
            if (string.IsNullOrEmpty(_writeToken))
            {
                throw new Exception("Please provide an access token to be used during package push.");
            }

            var fallbackActions = new Func<Task<HttpResponseMessage>>[]
            {
                async () => await PushPackageMultipartAsync(_writeToken, packageStream, PushTimeout, HttpMethod.Put),
                async () => await PushPackageMultipartAsync(_writeToken, packageStream, PushTimeout, HttpMethod.Post),
                async () => await PushPackageAsRequestBodyAsync(_writeToken, packageStream, PushTimeout, HttpMethod.Put),
                async () => await PushPackageAsRequestBodyAsync(_writeToken, packageStream, PushTimeout, HttpMethod.Post)
            };

            for (var i = 0; i < fallbackActions.Length; i++)
            {
                try
                {
                    packageStream.Position = 0;
                    
                    var response = await fallbackActions[i]();

                    if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
                    {
                        // Unlist package?
                        if (!packageDefinition.IsListed)
                        {
                            Log.Logger.Verbose(
                                "Unlisting {packageType} package {packageIdentifier}@{packageVersion} on destination...",
                                packageDefinition.PackageType, packageDefinition.PackageIdentifier, packageDefinition.PackageVersion);
                            
                            await DeletePackage(packageDefinition, hardDelete: false);
                            
                            Log.Logger.Information(
                                "Unlisted {packageType} package {packageIdentifier}@{packageVersion} on destination.",
                                packageDefinition.PackageType, packageDefinition.PackageIdentifier, packageDefinition.PackageVersion);
                        }
                        
                        return;
                    }
                    else
                    {
                        // Retry other method if needed, but log if last retry method fails
                        if (i >= fallbackActions.Length - 1)
                        {
                            if (response?.Content != null)
                            {
                                var responseContent = await response.Content.ReadAsStringAsync();
                                if (!string.IsNullOrEmpty(responseContent))
                                {
                                    Log.Logger.Verbose("Response content: {responseContent}", responseContent);
                                }
                            }
                            
                            try
                            {
                                response.EnsureSuccessStatusCode();
                            }
                            catch (Exception ex)
                            {
                                Log.Logger.Error(
                                    "Error pushing {packageType} package {packageIdentifier}@{packageVersion}. Status: {statusCode} - {statusReason}. {Exception}",
                                    packageDefinition.PackageType, packageDefinition.PackageIdentifier,
                                    packageDefinition.PackageVersion,
                                    response.StatusCode, response.ReasonPhrase, ex);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Other error occurred. Retry other method if needed, but throw if last retry fails
                    if (i >= fallbackActions.Length - 1)
                    {
                        Log.Logger.Error(
                            "Error pushing {packageType} package {packageIdentifier}@{packageVersion}. {Exception}",
                            packageDefinition.PackageType, packageDefinition.PackageIdentifier,
                            packageDefinition.PackageVersion, ex);
                    }
                }
            }
        }
                
        public async Task DeletePackage(PackageDefinition packageDefinition)
        {
            await DeletePackage(packageDefinition, hardDelete: true);
        }

        private async Task<Tuple<string, ICollection<PackageDefinition>>> GetPackagesFromUrl(string url)
        {
            Log.Logger.Verbose("Retrieving packages from url: {url}...", url);
            
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            await EnsureAuthenticated(request);
            var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            Log.Logger.Verbose("Retrieved packages from url: {url}.", url);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                string continuationUrl = null;
                var returnValue = new List<PackageDefinition>();
                
                using (var odataStream = await response.Content.ReadAsStreamAsync())
                {
                    var odata = XDocument.Load(odataStream);
                    
                    // Parse entries
                    foreach (var entryElement in odata.Root.Elements(AtomNamespace + "entry"))
                    {
                        var propertiesElement = entryElement.Element(MetadataNamespace + "properties");
                        var contentElement = entryElement.Element(AtomNamespace + "content");

                        var published = DateTime.Parse(propertiesElement.Element(DataServicesNamespace + "Published").Value);
                        
                        var packageDefinition = new PackageDefinition
                        {
                            PackageType = "nuget",
                            PackageIdentifier = propertiesElement.Element(DataServicesNamespace + "Id").Value,
                            PackageVersion = propertiesElement.Element(DataServicesNamespace + "Version").Value,
                            LastEdited = DateTime.Parse(propertiesElement.Element(DataServicesNamespace + "LastEdited").Value),
                            ContentUri = new Uri(contentElement.Attribute("src").Value),
                            IsListed = published.Year != 1900 && published.Year != 1970
                        };
                        
                        returnValue.Add(packageDefinition);
                    }
                    
                    // Parse continuations
                    foreach (var linkElement in odata.Root.Elements(AtomNamespace + "link"))
                    {
                        var linkRel = linkElement.Attribute("rel");
                        if (linkRel != null && linkRel.Value == "next")
                        {
                            continuationUrl = linkElement.Attribute("href").Value;
                            break;
                        }
                    }
                }
                
                return new Tuple<string, ICollection<PackageDefinition>>(continuationUrl, returnValue);
            }
            else
            {
                Log.Logger.Error("Error retrieving packages from URL: {url}. Status: {statusCode} - {statusReason}.", url, response.StatusCode, response.ReasonPhrase);
                
                return new Tuple<string, ICollection<PackageDefinition>>(null, new List<PackageDefinition>());
            }
        }

        private async Task<HttpResponseMessage> PushPackageMultipartAsync(string accessToken, Stream packageStream, TimeSpan timeout, HttpMethod httpMethod)
        {
            // NOTE: For some reason, Microsoft decided StreamContent should dispose the stream, so work with a copy as we do not want to dispose here.
            using (var streamCopy = await StreamUtilities.MakeMemoryCopy(packageStream))
            {
                var content = new MultipartFormDataContent();
                content.Add(new StreamContent(streamCopy), "package", "package.nupkg");
            
                var request = new HttpRequestMessage(httpMethod, _repositoryUrl + "/package");
                request.Headers.Add("X-NuGet-ApiKey", accessToken);
                request.Content = content;

                return await HttpClient.SendAsync(request);
            }
        }
        
        private async Task<HttpResponseMessage> PushPackageAsRequestBodyAsync(string accessToken, Stream packageStream, TimeSpan timeout, HttpMethod httpMethod)
        {
            // NOTE: For some reason, Microsoft decided StreamContent should dispose the stream, so work with a copy as we do not want to dispose here.
            using (var streamCopy = await StreamUtilities.MakeMemoryCopy(packageStream))
            {
                var content = new StreamContent(streamCopy);

                var request = new HttpRequestMessage(httpMethod, _repositoryUrl + "/package");
                request.Headers.Add("X-NuGet-ApiKey", accessToken);
                request.Content = content;

                return await HttpClient.SendAsync(request);
            }
        }
        
        private async Task DeletePackage(PackageDefinition packageDefinition, bool hardDelete)
        {
            if (string.IsNullOrEmpty(_writeToken))
            {
                throw new Exception("Please provide an access token to be used during package delete.");
            }
            
            var request = new HttpRequestMessage(HttpMethod.Delete, _repositoryUrl + "/package/" + packageDefinition.PackageIdentifier + "/" + packageDefinition.PackageVersion + (hardDelete ? "?hardDelete=true" : ""));
            request.Headers.Add("X-NuGet-ApiKey", _writeToken);

            var response = await HttpClient.SendAsync(request);
            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error deleting/unlisting {packageType} package {packageIdentifier}@{packageVersion}. Status: {statusCode} - {statusReason}.",
                    packageDefinition.PackageType, packageDefinition.PackageIdentifier, packageDefinition.PackageVersion,
                    response.StatusCode, response.ReasonPhrase, ex);
            }
        }

        private Task EnsureAuthenticated(HttpRequestMessage request)
        {
            if (!string.IsNullOrEmpty(_readUsername) && !string.IsNullOrEmpty(_readPassword))
            {
                Log.Logger.Debug("Adding basic authentication headers to request: " + request.RequestUri);
                
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", 
                    Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_readUsername}:{_readPassword}")));
            }

            return Task.CompletedTask;
        }

        public override string ToString()
        {
            return _repositoryUrl;
        }
    }
}