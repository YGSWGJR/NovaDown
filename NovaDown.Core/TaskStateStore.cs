using System.Text.Encodings.Web;
using System.Text.Json;

namespace NovaDown.Core;

/// <summary>把任务状态以 JSON 落盘，供断点续传恢复。</summary>
public sealed class TaskStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _lock = new();

    /// <summary>状态文件所在目录。</summary>
    public string Directory { get; }

    public TaskStateStore(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    private static string StatePathFor(string filePath) => filePath + ".novadown.json";

    public void Save(TaskRecord record)
    {
        lock (_lock)
        {
            using var fs = new FileStream(StatePathFor(record.FilePath), FileMode.Create, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(fs, record, JsonOptions);
        }
    }

    public TaskRecord? Load(string filePath)
    {
        var path = StatePathFor(filePath);
        if (!File.Exists(path)) return null;

        lock (_lock)
        {
            using var fs = File.OpenRead(path);
            return JsonSerializer.Deserialize<TaskRecord>(fs, JsonOptions);
        }
    }

    public void Delete(string filePath)
    {
        lock (_lock)
        {
            var path = StatePathFor(filePath);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
