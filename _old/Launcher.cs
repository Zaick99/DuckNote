using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

static class Launcher
{
    const string ScriptName = "DuckNote.ps1";

    const string ScriptHash = "@@SCRIPT_SHA256@@";

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DuckNote", "app");
            Directory.CreateDirectory(dir);
            string script = Path.Combine(dir, ScriptName);

            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ScriptName))
            {
                if (s == null) throw new InvalidOperationException("script non incorporato");
                WriteScript(s, script);
            }

            string actual = HashOf(script);
            if (!ScriptHash.StartsWith("@@") && !string.Equals(actual, ScriptHash, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Lo script estratto non corrisponde a quello incorporato: DuckNote non parte.\n\n" +
                    "atteso:   " + ScriptHash + "\n" +
                    "trovato:  " + actual + "\n\n" +
                    "Riscarica l'eseguibile dalla release e confronta la sua impronta con quella pubblicata.",
                    "DuckNote", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }

            string exe = FindPwsh();
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = dir
            };
            string sta = exe.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase) ? " -Sta" : "";
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass" + sta + " -File \"" + script + "\"";
            foreach (string a in args) psi.Arguments += " " + a;

            Process.Start(psi);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Avvio di DuckNote non riuscito.\n\n" + ex.Message,
                "DuckNote", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    static string HashOf(string path)
    {
        using (FileStream f = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
        {
            return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }
    }

    static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    static void WriteScript(Stream src, string path)
    {
        using (FileStream f = File.Create(path))
        {
            byte[] head = new byte[3];
            int n = src.Read(head, 0, 3);
            bool hasBom = n == 3 && head[0] == Utf8Bom[0] && head[1] == Utf8Bom[1] && head[2] == Utf8Bom[2];
            if (!hasBom) f.Write(Utf8Bom, 0, Utf8Bom.Length);
            if (n > 0) f.Write(head, 0, n);
            src.CopyTo(f);
        }
    }

    static string FindPwsh()
    {
        foreach (string candidate in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "pwsh.exe")
        })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                            "WindowsPowerShell", "v1.0", "powershell.exe");
    }
}
