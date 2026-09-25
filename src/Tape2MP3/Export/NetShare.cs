using System.Runtime.InteropServices;

namespace Tape2MP3.Export;

/// <summary>Collega (se servono credenziali) e verifica le destinazioni di rete.</summary>
public static class NetShare
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string lpLocalName;
        public string lpRemoteName;
        public string lpComment;
        public string lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NETRESOURCE res, string password, string user, int flags);

    private const int RESOURCETYPE_DISK = 1;
    private const int ERROR_SESSION_CREDENTIAL_CONFLICT = 1219;
    private const int ERROR_ALREADY_ASSIGNED = 85;

    /// <summary>\\server\share\a\b → \\server\share</summary>
    public static string ShareRoot(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(@"\\")) return null;
        var parts = path.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        return $@"\\{parts[0]}\{parts[1]}";
    }

    /// <summary>
    /// Se la destinazione è UNC con utente impostato, apre la connessione.
    /// Poi verifica che la cartella sia scrivibile. Lancia eccezione con messaggio chiaro se no.
    /// </summary>
    public static void EnsureReady(Destination d, string fullFolder)
    {
        var root = ShareRoot(d.Path);
        if (root != null && !string.IsNullOrWhiteSpace(d.User))
        {
            var nr = new NETRESOURCE { dwType = RESOURCETYPE_DISK, lpRemoteName = root };
            int rc = WNetAddConnection2(ref nr, d.Password, d.User, 0);
            if (rc != 0 && rc != ERROR_SESSION_CREDENTIAL_CONFLICT && rc != ERROR_ALREADY_ASSIGNED)
                throw new IOException($"Impossibile collegarsi a {root} (errore Windows {rc}: {new System.ComponentModel.Win32Exception(rc).Message}).");
        }

        try
        {
            Directory.CreateDirectory(fullFolder);
            string probe = Path.Combine(fullFolder, ".tape2mp3_test_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new IOException($"La cartella di destinazione non è raggiungibile o non è scrivibile:\n{fullFolder}\n\n{ex.Message}", ex);
        }
    }
}
