using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

public class GithubRepository
{
    public string name { get; set; } = string.Empty;
}

class Program
{
    static Uri BaseAdress { get; set; } = new Uri("https://api.github.com");
    static ProductInfoHeaderValue UserAgent { get; set; } = new ProductInfoHeaderValue("useragent", "1.0");
    static int PerPage { get; set; } = 100;

    public static async Task<int> Main(string[] args)
    {
        var githubtoken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(githubtoken))
        {
            Console.WriteLine("Missing environment variable: GITHUB_TOKEN");
            return 1;
        }

        if (args.Length != 2)
        {
            Console.WriteLine("Usage: getsboms <organization> <folder>");
            return 1;
        }

        var organization = args[0];
        var folder = args[1];

        var result = await GetSBOMS(organization, githubtoken, folder);

        return result;
    }

    static async Task<int> GetSBOMS(string organization, string githubtoken, string folder)
    {
        var startTime = Stopwatch.GetTimestamp();

        using var client = new HttpClient();
        client.BaseAddress = BaseAdress;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", githubtoken);
        client.DefaultRequestHeaders.UserAgent.Add(UserAgent);

        var reponames = await GetAllRepositories(client, organization);

        Console.WriteLine($"Got {reponames.Count} repos.");

        await SaveSBOMS(client, organization, reponames, folder);

        Console.WriteLine($"Saved {Directory.GetFiles("sboms").Length} sboms in {Stopwatch.GetElapsedTime(startTime)}");

        return 0;
    }

    static async Task SaveSBOMS(HttpClient client, string organization, List<string> reponames, string folder)
    {
        if (Directory.Exists(folder))
        {
            Console.WriteLine($"Deleting folder: '{folder}'");
            Directory.Delete(folder, recursive: true);
        }
        Console.WriteLine($"Creating folder: '{folder}'");
        Directory.CreateDirectory(folder);

        await Task.WhenAll(reponames.Select(reponame => SaveSBOM(client, organization, reponame, folder)));
    }

    static async Task SaveSBOM(HttpClient client, string organization, string reponame, string folder)
    {
        var address = $"repos/{organization}/{reponame}/dependency-graph/sbom";

        Console.WriteLine($"Getting sbom: '{address}'");

        var content = string.Empty;
        try
        {
            var response = await client.GetAsync(address);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Got StatusCode: {response.StatusCode}");
                return;
            }
            content = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Get '{address}'");
            Console.WriteLine($"Result: >>>{content}<<<");
            Console.WriteLine($"Exception: >>>{ex.ToString()}<<<");
            return;
        }

        if (content.Length > 0)
        {
            var jsonelement = JsonSerializer.Deserialize<JsonElement>(content);
            var pretty = JsonSerializer.Serialize(jsonelement, new JsonSerializerOptions { WriteIndented = true });
            var filename = Path.Combine(folder, $"{reponame}.json");

            Console.WriteLine($"Saving json to: '{filename}'");
            File.WriteAllText(filename, pretty);
        }
    }

    static async Task<List<string>> GetAllRepositories(HttpClient client, string organization)
    {
        var allrepos = new List<string>();

        var address = $"orgs/{organization}/repos?per_page={PerPage}";
        while (address != string.Empty)
        {
            Console.WriteLine($"Getting repos: '{address}'");

            var content = string.Empty;
            try
            {
                var response = await client.GetAsync(address);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Get '{address}', StatusCode: {response.StatusCode}");
                }
                content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Result: >>>{content}<<<");
                }
                address = GetNextLink(response.Headers);

                var jsonarray = JsonSerializer.Deserialize<GithubRepository[]>(content) ?? new GithubRepository[] { };

                allrepos.AddRange(jsonarray.Select(repo => repo.name));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Get '{address}'");
                Console.WriteLine($"Result: >>>{content}<<<");
                Console.WriteLine($"Exception: >>>{ex.ToString()}<<<");
                continue;
            }
        }

        return allrepos;
    }

    static string GetNextLink(HttpResponseHeaders headers)
    {
        if (headers.Contains("Link"))
        {
            var links = headers.GetValues("Link").SelectMany(l => l.Split(',')).ToArray();
            foreach (var link in links)
            {
                var parts = link.Split(';');
                if (parts.Length == 2 && parts[0].Trim().StartsWith('<') && parts[0].Trim().EndsWith('>') && parts[1].Trim() == "rel=\"next\"")
                {
                    var url = parts[0].Trim().Substring(1, parts[0].Trim().Length - 2);
                    return url;
                }
            }
        }

        return string.Empty;
    }
}
