using System.IO.Pipes;
using FullWidget.Core;

namespace FullWidget.Shell;

/// Lets a second launch talk to the running instance, e.g.
///   FullWidget.exe --cmd "add fullwidget.clock"
/// The pipe only accepts writes from the same user (default named-pipe security).
static class CommandChannel
{
    public static bool Send(string pipeName, string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(3000);
            using var writer = new StreamWriter(client);
            writer.Write(command);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not reach running instance: {ex.Message}");
            return false;
        }
    }

    public static void Listen(string pipeName, Action<string> onCommand)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    var cmd = reader.ReadToEnd().Trim();
                    if (cmd.Length > 0) onCommand(cmd);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Command channel: {ex.Message}");
                    Thread.Sleep(500);
                }
            }
        })
        { IsBackground = true, Name = "CommandChannel" };
        thread.Start();
    }
}
