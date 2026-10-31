using System.IO;
using System.Media;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Settings;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Son de fin du minuteur (correctif 19) : « Son d'Opaline » (le son « Exclamation » de Windows, comme avant), une alarme ou
/// une sonnerie de C:\Windows\Media (seulement celles qui existent sur ce PC), ou un fichier perso (.wav / .mp3). Joué par
/// MediaPlayer au volume choisi (repli : son système, au volume de Windows) ; l'alerte sonne au plus 3 fois et 1 minute, et se
/// tait dès qu'on l'acquitte. Rien ne tourne au repos : tout est créé au premier son.
/// </summary>
public static partial class TimerSound
{
    public const string OpalineId = "opaline";
    public const string FileId = "file";
    private const string WindowsPrefix = "windows:";
    private const int Rings = 3;
    private static readonly TimeSpan SystemSoundGap = TimeSpan.FromMilliseconds(1500); // son système : une sonnerie toutes les 1,5 s (comme avant)
    private static readonly TimeSpan FileGap = TimeSpan.FromMilliseconds(500);         // fichier : petite pause entre deux lectures
    private static readonly TimeSpan MaxRinging = TimeSpan.FromMinutes(1);              // un long .mp3 ne sonne pas plus d'une minute

    /// <summary>Un son proposé : identifiant enregistré, nom affiché, fichier.</summary>
    public sealed record Choice(string Id, string Name, string Path);

    [GeneratedRegex(@"^(?<kind>Alarm|Ring)(?<n>0[1-9]|10)\.wav$", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsFile();

    private static MediaPlayer? _player;
    private static DispatcherTimer? _gap, _limit;
    private static int _left;
    private static Func<bool>? _keepRinging;

    public static string MediaFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    /// <summary>Alarmes puis sonneries de Windows qui existent vraiment (Alarm01.wav … Alarm10.wav, Ring01.wav … Ring10.wav).</summary>
    public static IReadOnlyList<Choice> WindowsSounds()
    {
        var list = new List<Choice>();
        foreach (var kind in new[] { "Alarm", "Ring" })
            for (int i = 1; i <= 10; i++)
            {
                var file = $"{kind}{i:00}.wav";
                var path = Path.Combine(MediaFolder, file);
                if (File.Exists(path)) list.Add(new Choice(WindowsPrefix + file, NameOf(WindowsPrefix + file, null), path));
            }
        return list;
    }

    /// <summary>Identifiant valable (sinon le son d'Opaline) ; « file » seulement s'il y a un fichier perso.</summary>
    public static string NormalizeId(string? id, string? customFile) => id switch
    {
        FileId when !string.IsNullOrWhiteSpace(customFile) => FileId,
        { } windows when windows.StartsWith(WindowsPrefix, StringComparison.OrdinalIgnoreCase)
                         && WindowsFile().Match(windows[WindowsPrefix.Length..]) is { Success: true } match
            => WindowsPrefix + Capitalized(match.Groups["kind"].Value) + match.Groups["n"].Value + ".wav",
        _ => OpalineId,
    };

    /// <summary>« Son d'Opaline », « Alarme 3 », « Sonnerie 10 », ou le nom du fichier perso.</summary>
    public static string NameOf(string id, string? customFile)
    {
        if (id == FileId && customFile != null) return Path.GetFileName(customFile);
        if (id.StartsWith(WindowsPrefix, StringComparison.OrdinalIgnoreCase) && WindowsFile().Match(id[WindowsPrefix.Length..]) is { Success: true } match)
        {
            int number = int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
            return Loc.F(match.Groups["kind"].Value.Equals("Alarm", StringComparison.OrdinalIgnoreCase) ? "timer.sound.alarm" : "timer.sound.ring", number);
        }
        return Loc.T("timer.sound.opaline");
    }

    /// <summary>Fichier à jouer pour ces réglages ; null = son système (Opaline sans fichier, ou fichier disparu).</summary>
    public static string? PathOf(TimerSettings settings)
    {
        string? path = settings.Sound switch
        {
            FileId => settings.CustomFile,
            { } id when id.StartsWith(WindowsPrefix, StringComparison.OrdinalIgnoreCase) => Path.Combine(MediaFolder, id[WindowsPrefix.Length..]),
            _ => null,
        };
        if (path != null && File.Exists(path)) return path;
        return ExclamationFile(); // son d'Opaline, ou repli quand le fichier choisi n'existe plus
    }

    /// <summary>Le son « Exclamation » du jeu de sons de Windows (celui que jouait le minuteur), s'il a un fichier.</summary>
    private static string? ExclamationFile()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes\Apps\.Default\SystemExclamation\.Current");
            if (key?.GetValue(null) is not string value || value.Trim().Length == 0) return null;
            var path = Environment.ExpandEnvironmentVariables(value.Trim());
            if (!Path.IsPathRooted(path)) path = Path.Combine(MediaFolder, path);
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex)
        {
            Log.Error("Minuteur : son d'Opaline", ex);
            return null;
        }
    }

    /// <summary>Fin du minuteur : sonne (3 fois, 1 minute au plus) tant que <paramref name="keepRinging"/> est vrai.</summary>
    public static void Ring(Func<bool> keepRinging) => Start(Rings, keepRinging);

    /// <summary>Bouton « Tester » des paramètres : une fois, au volume choisi.</summary>
    public static void Test() => Start(1, null);

    /// <summary>Alerte acquittée (ou nouveau son) : on se tait tout de suite.</summary>
    public static void Stop()
    {
        _left = 0;
        _keepRinging = null;
        _gap?.Stop();
        _limit?.Stop();
        if (_player == null) return;
        _player.Stop();
        _player.Close();
    }

    private static void Start(int times, Func<bool>? keepRinging)
    {
        Stop();
        _left = times;
        _keepRinging = keepRinging;
        _limit ??= NewTimer(MaxRinging, Stop);
        _limit.Start();
        PlayNext();
    }

    private static void PlayNext()
    {
        _gap?.Stop();
        if (_left <= 0 || _keepRinging?.Invoke() == false)
        {
            Stop();
            return;
        }
        _left--;
        var settings = SettingsStore.Current.Timer;
        try
        {
            if (PathOf(settings) is { } path)
            {
                _player ??= NewPlayer();
                _player.Open(new Uri(path));
                _player.Volume = settings.Volume;
                _player.Play();
                return; // la suite à la fin du son (MediaEnded)
            }
        }
        catch (Exception ex)
        {
            Log.Error("Minuteur : lecture du son", ex);
        }
        SystemSounds.Exclamation.Play(); // pas de fichier lisible : le son de Windows, au volume du système
        After(SystemSoundGap);
    }

    private static MediaPlayer NewPlayer()
    {
        var player = new MediaPlayer();
        player.MediaEnded += (_, _) => After(FileGap);
        player.MediaFailed += (_, e) =>
        {
            Log.Error("Minuteur : son illisible", e.ErrorException);
            SystemSounds.Exclamation.Play(); // codec absent (Windows N…) ou fichier abîmé : on sonne quand même
            After(SystemSoundGap);
        };
        return player;
    }

    private static void After(TimeSpan delay)
    {
        if (_left <= 0)
        {
            _limit?.Stop();
            return;
        }
        _gap ??= NewTimer(delay, PlayNext);
        _gap.Interval = delay;
        _gap.Start();
    }

    private static DispatcherTimer NewTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Guarded.Run(tick, "Minuteur : son de fin");
        };
        return timer;
    }

    private static string Capitalized(string word) => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
}
