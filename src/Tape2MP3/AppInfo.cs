using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Tape2MP3;

/// <summary>Versione del programma e controllo aggiornamenti sulle Release GitHub.</summary>
public static class AppInfo
{
    /// <summary>Repository GitHub da cui leggere l'ultima release (modificabile in settings.json → UpdateRepo).</summary>
    public const string DefaultRepo = "Giuseppe-TD/Tape2MP3";

    public static string Version
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
            {
                int plus = info.IndexOf('+');
                return plus > 0 ? info[..plus] : info;
            }
            return asm.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }

    /// <summary>Hash corto del commit (se la build lo contiene), utile per distinguere build dello stesso numero.</summary>
    public static string Commit
    {
        get
        {
            var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            int plus = info.IndexOf('+');
            if (plus < 0) return "";
            var h = info[(plus + 1)..];
            return h.Length > 7 ? h[..7] : h;
        }
    }

    public static DateTime BuildDate
    {
        get
        {
            try { return File.GetLastWriteTime(Environment.ProcessPath ?? Application.ExecutablePath); }
            catch { return DateTime.MinValue; }
        }
    }

    public static bool IsDevBuild => Version.StartsWith("0.0.");

    public sealed class UpdateInfo
    {
        public string LatestVersion;
        public string Url;
        public bool IsNewer;
    }

    /// <summary>Legge l'ultima release pubblicata. Ritorna null se il repo non è raggiungibile (offline, privato, nessuna release).</summary>
    public static async Task<UpdateInfo> CheckAsync(string repo, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Tape2MP3/" + Version);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest", ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : $"https://github.com/{repo}/releases/latest";
            var latest = tag.TrimStart('v', 'V');
            bool newer = System.Version.TryParse(latest, out var lv) && System.Version.TryParse(Version, out var cv) && lv > cv;
            return new UpdateInfo { LatestVersion = latest, Url = url, IsNewer = newer };
        }
        catch { return null; }
    }

    /// <summary>Versione di ffmpeg incluso (prima riga di "ffmpeg -version").</summary>
    public static string FfmpegVersion(string ffmpeg)
    {
        if (ffmpeg == null) return "non trovato";
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(ffmpeg, "-hide_banner -version")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using var p = System.Diagnostics.Process.Start(psi);
            var line = p.StandardOutput.ReadLine() ?? "";
            p.WaitForExit(3000);
            var parts = line.Split(' ');
            return parts.Length >= 3 ? parts[2] : line;
        }
        catch { return "errore"; }
    }
}
