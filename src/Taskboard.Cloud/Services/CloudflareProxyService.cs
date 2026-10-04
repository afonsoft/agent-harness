using System.Net.Http.Json;
using Taskboard.Application.Contracts.AiChat;

namespace Taskboard.Cloud.Services;

public interface ICloudflareProxyService
{
    Task<CloudflareD1Result> ExecuteD1QueryAsync(string sql, CancellationToken ct = default);
    Task<CloudflareR2Result> UploadToR2Async(string key, Stream content, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadFromR2Async(string key, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}

public sealed record CloudflareD1Result(
    bool Success,
    IReadOnlyList<Dictionary<string, object?>>? Results = null,
    string? Error = null);

public sealed record CloudflareR2Result(
    bool Success,
    string? Key = null,
    string? Error = null);

public sealed class CloudflareProxyService : ICloudflareProxyService
{
    private readonly HttpClient _httpClient;
    private readonly string _accountId;
    private readonly string _databaseId;

    public CloudflareProxyService(HttpClient httpClient, string accountId, string databaseId, string apiToken)
    {
        _httpClient = httpClient;
        _accountId = accountId;
        _databaseId = databaseId;

        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);
    }

    public async Task<CloudflareD1Result> ExecuteD1QueryAsync(string sql, CancellationToken ct = default)
    {
        try
        {
            var request = new { sql };
            var response = await _httpClient.PostAsJsonAsync(
                $"https://api.cloudflare.com/client/v4/accounts/{_accountId}/d1/database/{_databaseId}/query",
                request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                return new CloudflareD1Result(false, Error: error);
            }

            var result = await response.Content.ReadFromJsonAsync<CloudflareD1Response>(cancellationToken: ct);
            return new CloudflareD1Result(
                result?.Result?.Success ?? false,
                result?.Result?.Results,
                result?.Result?.Error?.Message);
        }
        catch (Exception ex)
        {
            return new CloudflareD1Result(false, Error: ex.Message);
        }
    }

    public async Task<CloudflareR2Result> UploadToR2Async(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        try
        {
            var requestContent = new MultipartFormDataContent();
            var streamContent = new StreamContent(content);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            requestContent.Add(streamContent, "file", key);

            var response = await _httpClient.PostAsync(
                $"https://api.cloudflare.com/client/v4/accounts/{_accountId}/r2/buckets/{_databaseId}/objects",
                requestContent, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                return new CloudflareR2Result(false, Error: error);
            }

            var result = await response.Content.ReadFromJsonAsync<CloudflareR2Response>(cancellationToken: ct);
            return new CloudflareR2Result(result?.Result?.Success ?? false, result?.Result?.Key);
        }
        catch (Exception ex)
        {
            return new CloudflareR2Result(false, Error: ex.Message);
        }
    }

    public async Task<Stream> DownloadFromR2Async(string key, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(
            $"https://api.cloudflare.com/client/v4/accounts/{_accountId}/r2/buckets/{_databaseId}/objects/{key}", ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Failed to download from R2: {response.StatusCode}");
        }

        return await response.Content.ReadAsStreamAsync(ct);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await ExecuteD1QueryAsync("SELECT 1", ct);
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    private sealed record CloudflareD1Response(
        bool Success,
        CloudflareD1ResultData? Result);

    private sealed record CloudflareD1ResultData(
        bool Success,
        IReadOnlyList<Dictionary<string, object?>>? Results,
        CloudflareD1Error? Error);

    private sealed record CloudflareD1Error(
        int Code,
        string Message);

    private sealed record CloudflareR2Response(
        bool Success,
        CloudflareR2ResultData? Result);

    private sealed record CloudflareR2ResultData(
        bool Success,
        string? Key);
}