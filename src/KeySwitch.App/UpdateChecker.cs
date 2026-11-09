using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using KeySwitch.Core;

namespace KeySwitch.App;

internal static class UpdateChecker
{
    internal const string ReleasesPage = "https://github.com/oover120/KeySwitch-GPT/releases/latest";
    internal static async Task<string?> CheckAsync(Version current)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KeySwitch", current.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.GetAsync("https://api.github.com/repos/oover120/KeySwitch-GPT/releases/latest");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var tag = json.RootElement.GetProperty("tag_name").GetString();
        return ReleaseVersion.IsNewer(tag, current) ? tag : null;
    }
}
