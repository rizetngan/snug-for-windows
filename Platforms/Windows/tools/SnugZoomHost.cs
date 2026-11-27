using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;

// Native Messaging stdio relay. It cannot launch commands or read web data.
// The server is restricted to the current Windows user.
internal static class SnugZoomHost
{
    private static void Relay(Stream input, Stream output)
    {
        var bytes = new byte[8192];
        int n;
        while ((n = input.Read(bytes, 0, bytes.Length)) > 0)
        {
            output.Write(bytes, 0, n);
            output.Flush();
        }
    }

    private static int Main()
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User.Value;
            using (var pipe = new NamedPipeClientStream(".", "Snug.Windows.ZoomBridge.v1." + sid,
                PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                pipe.Connect(2500);
                var reader = new Thread(delegate()
                {
                    try { Relay(Console.OpenStandardInput(), pipe); }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { }
                    finally { try { pipe.Dispose(); } catch { } }
                });
                reader.IsBackground = true;
                reader.Start();
                try { Relay(pipe, Console.OpenStandardOutput()); }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }
            return 0;
        }
        catch { return 1; }
    }
}