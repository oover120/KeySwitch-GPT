using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using KeySwitch.Hooks;

namespace KeySwitch.App;

internal sealed class DiagnosticLog(string directory) : IDisposable
{
    private readonly Channel<WordTrace> queue = Channel.CreateUnbounded<WordTrace>(
        new UnboundedChannelOptions { SingleReader = true });
    private Task? writer;

    public void Start() => writer ??= Task.Run(WriteAsync);
    public void Enqueue(WordTrace trace) => queue.Writer.TryWrite(trace);

    private async Task WriteAsync()
    {
        var path = Path.Combine(directory, "debug.jsonl");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await foreach (var trace in queue.Reader.ReadAllAsync())
        {
            try
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, JsonSerializer.Serialize(trace, options) + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        queue.Writer.TryComplete();
        writer?.Wait(TimeSpan.FromSeconds(2));
    }
}
