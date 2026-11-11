using System.Net;
using System.Text;
using System.Text.Json;

namespace NovaDown.Core;

/// <summary>
/// 本机控制 API（localhost HTTP 服务），浏览器扩展与 Agent 插件的通信中枢。
/// 鉴权：请求头 X-Nova-Token 必须等于设置中的 ApiToken。
/// CORS：允许扩展页面跨域调用（配合令牌，令牌为 GUID 不可猜测）。
/// </summary>
public sealed class ApiServer
{
    private readonly TaskManager _manager;
    private readonly AppSettings _settings;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true, // 插件端通常用小写字段名（url/referer）
    };

    public ApiServer(TaskManager manager, AppSettings settings)
    {
        _manager = manager;
        _settings = settings;
    }

    public bool IsRunning { get; private set; }

    public int BoundPort => _settings.ApiPort;

    public void Start()
    {
        if (IsRunning) return;

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_settings.ApiPort}/");
        _listener.Start(); // localhost 前缀无需管理员权限

        _cts = new CancellationTokenSource();
        IsRunning = true;
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener!.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                break; // 监听器被 Stop
            }
            _ = HandleAsync(ctx, ct);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var resp = ctx.Response;
        try
        {
            resp.Headers.Add("Access-Control-Allow-Origin", "*");
            resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Nova-Token");
            resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");

            if (ctx.Request.HttpMethod == "OPTIONS")
            {
                resp.StatusCode = 204;
                resp.Close();
                return;
            }

            if (ctx.Request.Headers["X-Nova-Token"] is not { } token || token != _settings.ApiToken)
            {
                await WriteAsync(resp, 401, new { error = "无效的访问令牌（X-Nova-Token）" }).ConfigureAwait(false);
                return;
            }

            var method = ctx.Request.HttpMethod;
            var path = ctx.Request.Url?.AbsolutePath ?? "/";

            if (method == "GET" && path == "/api/health")
            {
                await WriteAsync(resp, 200, new { name = "NovaDown", version = "0.2.0", status = "ok" }).ConfigureAwait(false);
            }
            else if (method == "GET" && path == "/api/extension-config")
            {
                await WriteAsync(resp, 200, new
                {
                    enabled = _settings.TakeoverEnabled,
                    minSizeBytes = _settings.TakeoverMinSizeBytes,
                    extensions = _settings.ExtensionList(),
                    fallbackOnError = _settings.FallbackOnError,
                }).ConfigureAwait(false);
            }
            else if (method == "POST" && path == "/api/tasks")
            {
                var body = await ReadJsonAsync<AddTaskRequest>(ctx.Request).ConfigureAwait(false);
                if (body?.Url is not { Length: > 0 } url)
                {
                    await WriteAsync(resp, 400, new { error = "缺少 url 字段" }).ConfigureAwait(false);
                    return;
                }

                Dictionary<string, string>? headers = null;
                if (!string.IsNullOrEmpty(body.Referer))
                    headers = new Dictionary<string, string> { ["Referer"] = body.Referer! };

                var job = await _manager.AddAsync(url, headers).ConfigureAwait(false);
                await WriteAsync(resp, 200, new { id = job.Id, fileName = job.FileName, totalBytes = job.Total }).ConfigureAwait(false);
            }
            else if (method == "GET" && path == "/api/tasks")
            {
                await WriteAsync(resp, 200, _manager.Snapshot().Select(TaskDto)).ConfigureAwait(false);
            }
            else if ((method == "POST" || method == "GET") && TryTaskAction(path, out var id, out var action))
            {
                // 同时接受 GET：http.sys 会拒绝无 Content-Length 的裸 POST（curl 等客户端常见）
                // 注意必须排在 /api/tasks/{id} 查询之前，否则 {id}/pause 会被当成 id
                var ok = action == "pause" ? _manager.Pause(id) : _manager.Resume(id);
                if (ok) await WriteAsync(resp, 200, new { ok = true }).ConfigureAwait(false);
                else await WriteAsync(resp, 409, new { error = "当前状态不允许该操作" }).ConfigureAwait(false);
            }
            else if (method == "GET" && path.StartsWith("/api/tasks/", StringComparison.Ordinal))
            {
                var taskId = path["/api/tasks/".Length..];
                var job = _manager.Get(taskId);
                if (job is null) await WriteAsync(resp, 404, new { error = "任务不存在" }).ConfigureAwait(false);
                else await WriteAsync(resp, 200, TaskDto(job)).ConfigureAwait(false);
            }
            else
            {
                await WriteAsync(resp, 404, new { error = "接口不存在" }).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            try
            {
                await WriteAsync(resp, 500, new { error = ex.Message }).ConfigureAwait(false);
            }
            catch
            {
                // 连接已断开
            }
        }
    }

    private static bool TryTaskAction(string path, out string id, out string action)
    {
        id = "";
        action = "";
        // /api/tasks/{id}/pause 或 /resume
        const string prefix = "/api/tasks/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var rest = path[prefix.Length..];
        var idx = rest.LastIndexOf('/');
        if (idx <= 0) return false;
        action = rest[(idx + 1)..];
        id = rest[..idx];
        return action is "pause" or "resume" && id.Length > 0;
    }

    private static object TaskDto(DownloadJob j) => new
    {
        id = j.Id,
        fileName = j.FileName,
        url = j.Url,
        state = j.State.ToString(),
        done = j.Done,
        total = j.Total,
        progress = j.Progress,
        error = j.ErrorMessage,
    };

    private static async Task<T?> ReadJsonAsync<T>(HttpListenerRequest request)
    {
        try
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var text = await reader.ReadToEndAsync().ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, Json);
        }
        catch
        {
            return default;
        }
    }

    private sealed record AddTaskRequest(string? Url, string? Referer);

    private static async Task WriteAsync(HttpListenerResponse resp, int status, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        resp.StatusCode = status;
        resp.ContentType = "application/json; charset=utf-8";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        resp.Close();
    }
}
