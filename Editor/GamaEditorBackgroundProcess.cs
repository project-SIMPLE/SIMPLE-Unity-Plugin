using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Wrapper non bloquant autour d'un <see cref="Process"/> : démarrage via cmd /c "script.bat",
/// capture asynchrone de stdout/stderr, kill propre. Utilisé pour lancer GAMA et/ou le middleware
/// en arrière-plan pendant que l'éditeur écoute la websocket.
/// </summary>
internal sealed class GamaEditorBackgroundProcess : IDisposable
{
    private Process process;
    private readonly StringBuilder log = new StringBuilder();
    private readonly object logLock = new object();
    private bool disposed;

    public string Name { get; private set; }
    public int ProcessId
    {
        get
        {
            try
            {
                return process != null ? process.Id : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            try
            {
                return process != null && !process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    public string LogSnapshot
    {
        get
        {
            lock (logLock)
            {
                return log.ToString();
            }
        }
    }

    public static GamaEditorBackgroundProcess StartCmdScript(
        string displayName,
        string scriptPath,
        string workingDirectory,
        IDictionary<string, string> environment,
        out string error,
        Action<string, bool> unityLogSink = null)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
        {
            error = "Script introuvable : " + scriptPath;
            return null;
        }

        System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ResolveCmdExe(),
            Arguments = "/c " + CmdQuote(scriptPath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? (Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory)
                : workingDirectory
        };

        if (environment != null)
        {
            foreach (KeyValuePair<string, string> kv in environment)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                psi.EnvironmentVariables[kv.Key] = kv.Value ?? string.Empty;
            }
        }

        GamaEditorBackgroundProcess wrapper = new GamaEditorBackgroundProcess
        {
            Name = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(scriptPath) : displayName
        };

        System.Diagnostics.Process p = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
        AttachStreamHandlers(wrapper, p, unityLogSink);

        try
        {
            if (!p.Start())
            {
                error = "Process.Start a retourné false.";
                return null;
            }

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            wrapper.process = p;
            return wrapper;
        }
        catch (Exception ex)
        {
            error = "Échec du démarrage : " + ex.Message;
            try { p.Dispose(); } catch { /* ignore */ }
            return null;
        }
    }

    public static GamaEditorBackgroundProcess StartCommand(
        string displayName,
        string fileName,
        string arguments,
        string workingDirectory,
        IDictionary<string, string> environment,
        out string error,
        Action<string, bool> unityLogSink = null)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            error = "Commande vide.";
            return null;
        }

        System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Environment.CurrentDirectory
                : workingDirectory
        };

        if (environment != null)
        {
            foreach (KeyValuePair<string, string> kv in environment)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                psi.EnvironmentVariables[kv.Key] = kv.Value ?? string.Empty;
            }
        }

        GamaEditorBackgroundProcess wrapper = new GamaEditorBackgroundProcess
        {
            Name = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(fileName) : displayName
        };

        System.Diagnostics.Process p = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
        AttachStreamHandlers(wrapper, p, unityLogSink);

        try
        {
            if (!p.Start())
            {
                error = "Process.Start a retourné false.";
                return null;
            }

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            wrapper.process = p;
            return wrapper;
        }
        catch (Exception ex)
        {
            error = "Échec du démarrage : " + ex.Message;
            try { p.Dispose(); } catch { /* ignore */ }
            return null;
        }
    }

    private static void AttachStreamHandlers(
        GamaEditorBackgroundProcess wrapper,
        System.Diagnostics.Process process,
        Action<string, bool> unityLogSink)
    {
        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data == null) return;
            lock (wrapper.logLock) wrapper.log.AppendLine("[" + wrapper.Name + "] " + e.Data);
            try
            {
                unityLogSink?.Invoke(e.Data, false);
            }
            catch
            {
                // ignore
            }
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data == null) return;
            lock (wrapper.logLock) wrapper.log.AppendLine("[" + wrapper.Name + "][stderr] " + e.Data);
            try
            {
                unityLogSink?.Invoke(e.Data, true);
            }
            catch
            {
                // ignore
            }
        };
    }

    public async Task StopAsync(int gracePeriodMs)
    {
        if (process == null) return;
        try
        {
            if (!process.HasExited)
            {
                try { process.CloseMainWindow(); } catch { /* ignore */ }

                int waited = 0;
                while (waited < gracePeriodMs && !process.HasExited)
                {
                    await Task.Delay(100);
                    waited += 100;
                }

                if (!process.HasExited)
                {
                    try { process.Kill(); } catch { /* ignore */ }
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (process != null)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(); } catch { /* ignore */ }
                }

                process.Dispose();
                process = null;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string ResolveCmdExe()
    {
        string comspec = Environment.GetEnvironmentVariable("COMSPEC");
        if (!string.IsNullOrWhiteSpace(comspec) && File.Exists(comspec)) return comspec;
        string systemCmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        return File.Exists(systemCmd) ? systemCmd : "cmd.exe";
    }

    private static string CmdQuote(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        return "\"" + arg.Replace("\"", "\"\"") + "\"";
    }
}
