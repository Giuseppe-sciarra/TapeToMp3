namespace Tape2MP3;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // una sola istanza: due programmi che registrano dallo stesso ingresso non servono
        using var mutex = new Mutex(true, "Tape2MP3_SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("Tape2MP3 è già aperto.", "Tape2MP3", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) =>
            MessageBox.Show(e.Exception.Message, "Errore imprevisto", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm());
    }
}
