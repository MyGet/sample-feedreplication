using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace MyGet.Samples.FeedReplication.Providers
{
    public class NpmPackageProvider : IPackageProvider
    {
        private static readonly HttpClient HttpClient = new HttpClient(new RedirectAuthenticatedRequestHttpClientHandler());
        
        private static readonly TimeSpan PushTimeout = TimeSpan.FromMinutes(10);
        
        private readonly string _repositoryUrl;
        private readonly string _writeToken;
        private readonly string _readUsername;
        private readonly string _readPassword;

        public NpmPackageProvider(string repositoryUrl, string writeToken, string readUsername, string readPassword)
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

            var url = $"{_repositoryUrl}/-/all";
            Log.Logger.Verbose("Retrieving packages from url: {url}...", url);
            
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            await EnsureAuthenticated(request);
            var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            Log.Logger.Verbose("Retrieved packages from url: {url}.", url);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                using (var jsonStream = await response.Content.ReadAsStreamAsync())
                using (var streamReader = new StreamReader(jsonStream, Encoding.UTF8))
                using (var jsonTextReader = new JsonTextReader(streamReader))
                {
                    // We are only interested in properties
                    while (jsonTextReader.TokenType != JsonToken.PropertyName && jsonTextReader.Read())
                    {
                    }
                    
                    // Ok, for every property we have from now, read data as object
                    while (jsonTextReader.Read())
                    {
                        if (jsonTextReader.TokenType == JsonToken.StartObject)
                        {
                            var packageObject = JObject.ReadFrom(jsonTextReader);

                            var name = packageObject["name"];
                            var version = packageObject["version"];
                            var time = packageObject["time"] as JObject;
                            if (name != null && version != null)
                            {
                                var packageDefinition = new PackageDefinition
                                {
                                    PackageType = "npm",
                                    PackageIdentifier = name.ToString(),
                                    PackageVersion = version.ToString(),
                                    LastEdited = time?["modified"] != null ? DateTime.Parse(time["modified"].ToString()) : DateTime.MinValue,
                                    ContentUri = new Uri($"{_repositoryUrl}/{name}/-/{name}-{version}.tgz"),
                                    IsListed = true
                                };
                        
                                returnValue.Add(packageDefinition);
                            }
                        }
                    }
                }
            }
            else
            {
                Log.Logger.Error("Error retrieving packages from URL: {url}. Status: {statusCode} - {statusReason}.", url, response.StatusCode, response.ReasonPhrase);
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
            
            packageStream.Position = 0;

            string data = null;
            using (var streamCopy = (MemoryStream)await StreamUtilities.MakeMemoryCopy(packageStream))
            {
                data = Convert.ToBase64String(streamCopy.ToArray());
            }
            

            var uploadJson = new JObject();
            uploadJson.Add(new JProperty("_id", packageDefinition.PackageIdentifier + "@" + packageDefinition.PackageVersion));
            uploadJson.Add(new JProperty("name", packageDefinition.PackageIdentifier));
            uploadJson.Add(new JProperty("version", packageDefinition.PackageVersion));
            uploadJson.Add(new JProperty("dist", new JObject(
                new JProperty("shasum", ""),
                new JProperty("tarball", $"{_repositoryUrl}/{packageDefinition.PackageIdentifier}/-/{packageDefinition.PackageIdentifier}-{packageDefinition.PackageVersion}.tgz"))));

            uploadJson.Add(new JProperty("_attachments", new JObject(
                new JProperty(packageDefinition.PackageIdentifier + "-" + packageDefinition.PackageVersion + ".tgz", new JObject(
                    new JProperty("content_type", "application/octet-stream"),
                    new JProperty("data", data),
                    new JProperty("length", data.Length)
                )))));

            //uploadJson.Merge(npmPackage.PackageJson);

            var content = new StringContent( uploadJson.ToString());

            var request = new HttpRequestMessage(HttpMethod.Put, _repositoryUrl + "/" + packageDefinition.PackageIdentifier);
            request.Headers.Add("X-NuGet-ApiKey", _writeToken);
            request.Content = content;

            var response = await HttpClient.SendAsync(request);
            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error pushing {packageType} package {packageIdentifier}@{packageVersion}. Status: {statusCode} - {statusReason}.",
                    packageDefinition.PackageType, packageDefinition.PackageIdentifier, packageDefinition.PackageVersion,
                    response.StatusCode, response.ReasonPhrase, ex);
            }
        }
                
        public Task DeletePackage(PackageDefinition packageDefinition)
        {
            if (string.IsNullOrEmpty(_writeToken))
            {
                throw new Exception("Please provide an access token to be used during package delete.");
            }
            
            Log.Logger.Warning("Skipped deleting {packageType} package {packageIdentifier}@{packageVersion}: NPM registry does not support deletes.",
                packageDefinition.PackageType, packageDefinition.PackageIdentifier, packageDefinition.PackageVersion);

            return Task.CompletedTask;
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