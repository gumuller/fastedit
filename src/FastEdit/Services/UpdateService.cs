using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FastEdit.Models;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class UpdateService : IUpdateService
{
    private static readonly HttpClient _httpClient = CreateHttpClient();
    private const string ReleasesUrl = "https://api.github.com/repos/gumuller/fastedit/releases/latest";

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "FastEdit-UpdateChecker");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        return client;
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync(ReleasesUrl);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            var tagName = json.GetProperty("tag_name").GetString() ?? "";
            var latestVersion = ParseVersion(tagName);
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

            if (latestVersion <= currentVersion)
                return null;

            var downloadUrl = "";
            if (json.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
            {
                downloadUrl = assets[0].GetProperty("browser_download_url").GetString() ?? "";
            }

            return new UpdateInfo
            {
                Version = tagName,
                ReleaseNotes = json.GetProperty("body").GetString() ?? "",
                DownloadUrl = downloadUrl,
                HtmlUrl = json.GetProperty("html_url").GetString() ?? ""
            };
        }
        catch
        {
            return null;
        }
    }

    private static Version ParseVersion(string tag)
    {
        var versionString = tag.TrimStart('v', 'V');
        return Version.TryParse(versionString, out var version) ? version : new Version(0, 0, 0);
    }
}
