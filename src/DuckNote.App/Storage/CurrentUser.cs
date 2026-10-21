using System.Runtime.InteropServices;
using System.Text;

namespace DuckNote.App.Storage;

/// <summary>Chi sta usando DuckNote: il nome che Windows mostra, l'account e il computer.</summary>
public static partial class CurrentUser
{
    private const int NameDisplay = 3;

    /// <summary>Il nome completo dell'account (Mario Rossi), se Windows lo conosce; se no il nome di accesso.</summary>
    public static string DisplayName
    {
        get
        {
            try
            {
                uint size = 256;
                StringBuilder name = new((int)size);

                if (OperatingSystem.IsWindows() && GetUserNameEx(NameDisplay, name, ref size) && name.Length > 0)
                {
                    return name.ToString();
                }
            }
            catch (Exception problem) when (problem is DllNotFoundException or EntryPointNotFoundException)
            {
                // un account locale senza nome completo, o un sistema senza secur32: resta il nome di accesso
            }

            return Environment.UserName;
        }
    }

    /// <summary>Account e computer, come si scrivono: utente su PC-UFFICIO.</summary>
    public static string Where =>
        string.Equals(Environment.UserDomainName, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            ? $"{Environment.UserName} su {Environment.MachineName}"
            : $"{Environment.UserDomainName}\\{Environment.UserName} su {Environment.MachineName}";

    private const int Ducks = 26;

    /// <summary>
    /// La papera dell'avatar, vestita in modo buffo: una per lettera iniziale del nome (A il
    /// cowboy, Z il detective; vedi assets/make_avatars.py), cosi' ogni persona ha sempre la
    /// sua. Cifre e lettere accentate cadono comunque su una papera.
    /// </summary>
    public static Uri DuckFor(string name)
    {
        char first = name.Trim().Normalize(NormalizationForm.FormD).FirstOrDefault(char.IsLetterOrDigit);
        char upper = char.ToUpperInvariant(first);
        int index = upper is >= 'A' and <= 'Z' ? upper - 'A' : first % Ducks;
        return new Uri($"pack://application:,,,/Assets/avatars/duck{index + 1:00}.png");
    }

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserNameEx(int nameFormat, StringBuilder buffer, ref uint size);
}
