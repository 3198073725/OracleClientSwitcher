using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace OracleClientSwitcher;

internal static class TnsConnectivityService
{
    public static async Task<ExternalProcessResult> RunTnsPingAsync(OracleClientInfo client, string alias, string tnsDirectory, CancellationToken cancellationToken = default)
    {
        string[] candidates =
        {
            Path.Combine(client.PathDirectory, "tnsping.exe"),
            Path.Combine(client.OracleHome, "bin", "tnsping.exe")
        };
        string? executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null)
            return new ExternalProcessResult { Started = false, Error = "当前客户端未安装 tnsping.exe。完整客户端通常才包含此工具。" };

        var startInfo = new ProcessStartInfo(executable, alias)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Default,
            StandardErrorEncoding = Encoding.Default
        };
        startInfo.Environment["TNS_ADMIN"] = tnsDirectory;
        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) return new ExternalProcessResult { Started = false, Error = "tnsping 未能启动。" };
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return new ExternalProcessResult { Started = true, TimedOut = true, Error = "tnsping 在 15 秒内没有结束。" };
            }
            return new ExternalProcessResult { Started = true, ExitCode = process.ExitCode, Output = await outputTask, Error = await errorTask };
        }
        catch (Exception ex)
        {
            return new ExternalProcessResult { Started = false, Error = ex.Message };
        }
    }

    public static async Task<IReadOnlyList<TnsOperationResult>> TestEndpointsAsync(IEnumerable<TnsEndpoint> endpoints, int timeoutSeconds = 3)
    {
        var results = new List<TnsOperationResult>();
        foreach (TnsEndpoint endpoint in endpoints)
        {
            try
            {
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 30)));
                Stopwatch watch = Stopwatch.StartNew();
                await client.ConnectAsync(endpoint.Host, endpoint.Port, timeout.Token);
                watch.Stop();
                results.Add(new TnsOperationResult { Success = true, Message = $"{endpoint.Protocol}  {endpoint.Host}:{endpoint.Port}  可达（{watch.ElapsedMilliseconds} ms）" });
            }
            catch (OperationCanceledException)
            {
                results.Add(new TnsOperationResult { Success = false, Message = $"{endpoint.Protocol}  {endpoint.Host}:{endpoint.Port}  连接超时" });
            }
            catch (Exception ex)
            {
                results.Add(new TnsOperationResult { Success = false, Message = $"{endpoint.Protocol}  {endpoint.Host}:{endpoint.Port}  不可达：{ex.Message}" });
            }
        }
        return results;
    }
}
