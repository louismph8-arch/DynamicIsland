using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace DynamicIsland.Services;

/// <summary>
/// Presse-papiers « sensible » : les formats que posent les gestionnaires de mots de passe pour dire à Windows (et à tout
/// historique) de ne PAS garder ce qui est copié. On les lit pour ne jamais garder ces copies dans notre historique, et on les
/// pose nous-mêmes quand on copie un mot de passe du coffre (ni l'historique de Windows Win+V, ni le cloud, ni le nôtre), puis
/// on l'efface après 30 s s'il est toujours là (numéro de séquence du presse-papiers inchangé = personne n'a copié autre chose).
/// </summary>
public static class SecureClipboard
{
    /// <summary>Présent = « ne pas surveiller » (aucune donnée utile).</summary>
    public const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";
    /// <summary>DWORD 0 = pas dans l'historique du presse-papiers de Windows.</summary>
    public const string HistoryFormat = "CanIncludeInClipboardHistory";
    /// <summary>DWORD 0 = pas de synchronisation dans le cloud.</summary>
    public const string CloudFormat = "CanUploadToCloudClipboard";

    public static readonly TimeSpan ClearDelay = TimeSpan.FromSeconds(30);

    /// <summary>Numéro de séquence après notre dernière écriture : l'historique ignore ce changement (c'est nous).</summary>
    public static uint OwnSequence { get; private set; }

    private static readonly SecretEraser Eraser = new(GetClipboardSequenceNumber, ClearIfStillOurs,
        action => Application.Current?.Dispatcher.BeginInvoke(action));

    public static uint Sequence => GetClipboardSequenceNumber();

    /// <summary>
    /// Décision pure (testée par --self-test) : <paramref name="has"/> dit si un format est là, <paramref name="dword"/> donne sa
    /// valeur (null = absent ; un format présent mais illisible doit renvoyer 0 : dans le doute, c'est sensible).
    /// </summary>
    public static bool IsSensitive(Func<string, bool> has, Func<string, uint?> dword) =>
        has(ExcludeFormat) || dword(HistoryFormat) == 0 || dword(CloudFormat) == 0;

    /// <summary>Le contenu actuel du presse-papiers est marqué sensible par l'appli qui l'a copié.</summary>
    public static bool CurrentIsSensitive() => IsSensitive(Has, ReadDword);

    /// <summary>Objet de données d'un mot de passe : texte + les trois formats d'exclusion (construit en mémoire, testable).</summary>
    public static DataObject SecretData(string text)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        data.SetData(ExcludeFormat, new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData(HistoryFormat, new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData(CloudFormat, new MemoryStream(BitConverter.GetBytes(0)));
        return data;
    }

    /// <summary>Copie un mot de passe (exclu des historiques) ; effacé après 30 s s'il est encore là et si l'option est active.</summary>
    public static bool SetSecret(string text, bool autoClear)
    {
        if (!TrySet(SecretData(text))) return false;
        if (autoClear) Eraser.Arm(OwnSequence, ClearDelay);
        else Eraser.Disarm();
        return true;
    }

    /// <summary>Copie ordinaire faite par l'app (identifiant, élément de l'historique) : l'historique ne la reprend pas.</summary>
    public static bool SetOwn(object data)
    {
        Eraser.Disarm(); // un autre contenu remplace le mot de passe : plus rien à effacer
        return TrySet(data);
    }

    /// <summary>Fermeture de l'app : un mot de passe du coffre encore dans le presse-papiers est effacé tout de suite.</summary>
    public static void EraseNow()
    {
        if (Eraser.IsArmed) ClearIfStillOurs();
        Eraser.Disarm();
    }

    private static bool TrySet(object data)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                OwnSequence = GetClipboardSequenceNumber();
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(30); // une autre appli tient le presse-papiers ouvert
            }
            catch (ExternalException)
            {
                Thread.Sleep(30);
            }
        }
        Log.Info("Presse-papiers : écriture impossible (occupé par une autre appli)");
        return false;
    }

    private static void ClearIfStillOurs()
    {
        if (GetClipboardSequenceNumber() != OwnSequence) return; // quelqu'un a copié autre chose : on n'y touche pas
        try
        {
            Clipboard.Clear();
            OwnSequence = GetClipboardSequenceNumber();
            Log.Info("Presse-papiers : mot de passe effacé");
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            Log.Info("Presse-papiers : effacement impossible (occupé)");
        }
    }

    private static bool Has(string format) => IsClipboardFormatAvailable(RegisterClipboardFormat(format));

    private static uint? ReadDword(string format)
    {
        uint id = RegisterClipboardFormat(format);
        if (!IsClipboardFormatAvailable(id)) return null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                Thread.Sleep(15);
                continue;
            }
            try
            {
                var handle = GetClipboardData(id);
                if (handle == IntPtr.Zero || GlobalSize(handle) < 4) return 0;
                var pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero) return 0;
                try
                {
                    return (uint)Marshal.ReadInt32(pointer);
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        return 0; // illisible : dans le doute, sensible
    }

    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
}

/// <summary>
/// Effacement différé d'un mot de passe : après le délai, efface seulement si le presse-papiers n'a pas changé depuis
/// (même numéro de séquence). Les fonctions sont injectées : l'auto-test l'essaie sans toucher au vrai presse-papiers.
/// </summary>
public sealed class SecretEraser(Func<uint> sequence, Action clear, Action<Action>? post = null)
{
    private readonly object _lock = new();
    private Timer? _timer;
    private uint _sequence;

    public bool IsArmed
    {
        get
        {
            lock (_lock) return _timer != null;
        }
    }

    public void Arm(uint sequenceAtCopy, TimeSpan delay)
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _sequence = sequenceAtCopy;
            _timer = new Timer(_ => Fire(), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Disarm()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Fire()
    {
        uint expected;
        lock (_lock)
        {
            if (_timer == null) return;
            _timer.Dispose();
            _timer = null;
            expected = _sequence;
        }
        void Run()
        {
            if (sequence() == expected) clear();
        }
        if (post != null) post(Run);
        else Run();
    }
}
