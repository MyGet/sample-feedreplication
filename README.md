# MyGet Samples - Feed replication

Sample application providing feed replication for NuGet and NPM feeds hosted on MyGet.org.

## Requirements

The application is a .NET Core 2.0 console application that can be run on-demand or as a scheduled task.

## Configuration

There is a `config.json` file in which replication pairs can be added.

Replication supports NuGet V2 (not V3!) and npm feeds, on MyGet or on other package management servers.

For example:

```
{
  "replicationPairs": [
    {
      "description": "Replicate feed to first server (NuGet)",
      "type": "nuget",
      "source": {
        "url": "https://www.myget.org/F/test-source/api/v2",
        "token": "api-key-goes-here",
        "username": "my-username",
        "password": "my-password"
      },
      "destination": {
        "url": "https://www.myget.org/F/test-destination/api/v2",
        "token": "api-key-goes-here",
        "username": "my-username",
        "password": "my-password"
      },
      "allowDeleteFromDestination": false
    }
  ]
}
```

The above sample will:

* Replicate NuGet packages from the feed `https://www.myget.org/F/test-source/api/v2` to the feed `https://www.myget.org/F/test-destination/api/v2`
* Never delete destination packages (hen packages are removed from source, these will remain in destination)
* Use the configured username/password and access token (API key)

## Disclaimer

This sample is provided as-is. Please us with caution (especially the `allowDeleteFromDestination` option). Test the application on a test environment when unsure.