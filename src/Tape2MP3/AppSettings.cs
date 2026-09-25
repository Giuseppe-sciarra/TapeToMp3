using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tape2MP3;

public sealed class Destination
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string User { get; set; } = "";
    /// <summary>Password cifrata con DPAPI (legata all'utente Windows).</summary>
    public string PasswordEnc { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string Password
    {
        get
        {
            if (string.IsNullOrEmpty(PasswordEnc)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(PasswordEnc), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
        set
        {
            PasswordEnc = string.IsNullOrEmpty(value) ? "" :
                Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
        }
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Path : $"{Name}  —  {Path}";
}

public sealed class AppSettings
{
    public string DeviceId { get; set; }
    public bool Monitor { get; set; }

    public string Format { get; set; } = "mp3";   // mp3 | flac | wav | m4a
    public string Mp3Quality { get; set; } = "320"; // 128 | 192 | 256 | 320 | V0
    public bool SplitTracks { get; set; } = true;

    public List<Destination> Destinations { get; set; } = new();
    public int LastDestination { get; set; }
    public string LastSubfolder { get; set; } = "";
    public string LastBaseName { get; set; } = "Cassetta";
    public bool OpenFolderAfterExport { get; set; } = true;

    // --- analisi buchi (adattiva sul fruscio della cassetta)
    /// <summary>Quanti dB sopra il fruscio si considera ancora "silenzio".</summary>
    public double GapMarginDb { get; set; } = 6;
    /// <summary>Durata minima di un buco da segnalare.</summary>
    public double MinGapSec { get; set; } = 0.7;
    /// <summary>Da questa durata in su il buco è classificato come pausa fra brani.</summary>
    public double MinSilenceSec { get; set; } = 1.5;
    /// <summary>"Dividi sulle pause" non crea brani più corti di così.</summary>
    public double MinTrackSec { get; set; } = 30;
    /// <summary>Segnala anche i cali improvvisi dentro la musica (dropout del nastro).</summary>
    public bool DetectDrops { get; set; } = true;

    // --- pausa automatica (disattivata: con il fruscio delle cassette non è affidabile)
    public bool AutoPause { get; set; } = false;
    public double AutoPauseSec { get; set; } = 60;
    public double SilenceDb { get; set; } = -50;

    public int WinX { get; set; } = -1;
    public int WinY { get; set; } = -1;
    public int WinW { get; set; } = 1280;
    public int WinH { get; set; } = 800;
    public bool WinMax { get; set; }

    public static string Dir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tape2MP3");
    public static string WorkDir => System.IO.Path.Combine(Dir, "Registrazioni");
    private static string FilePath => System.IO.Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opts) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
        }
        catch { }
    }
}
