using System.IO;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test de la priorité 2 « confort » (correctifs 17 à 19) : minuteur (format, bornes, ajouts, palette), sons de fin, météo et
/// « Détecter ma ville » (lecture des réponses seulement : rien n'est envoyé sur Internet, aucun son n'est joué).
/// </summary>
internal static partial class SelfTest
{
    private static void Comfort(List<string> lines)
    {
        // 19 : format h:mm:ss à partir d'une heure, mm:ss avant (secondes arrondies au-dessus, comme un compte à rebours).
        Check(lines, TimerService.Format(TimeSpan.Zero) == "00:00" && TimerService.Format(TimeSpan.FromMilliseconds(200)) == "00:01"
                     && TimerService.Format(TimeSpan.FromMinutes(5)) == "05:00" && TimerService.Format(new TimeSpan(0, 59, 59)) == "59:59"
                     && TimerService.Format(TimeSpan.FromHours(1)) == "1:00:00" && TimerService.Format(new TimeSpan(1, 30, 5)) == "1:30:05"
                     && TimerService.Format(new TimeSpan(23, 59, 59)) == "23:59:59" && TimerService.Format(TimeSpan.FromSeconds(-3)) == "00:00",
            "minuteur : « 05:00 », « 59:59 », puis « 1:00:00 », « 1:30:05 », « 23:59:59 » ; jamais négatif");
        Check(lines, !TimerService.HasHours(new TimeSpan(0, 59, 59)) && TimerService.HasHours(new TimeSpan(0, 0, 59, 59, 500)) && TimerService.HasHours(TimeSpan.FromHours(2)),
            "minuteur : le format à heures commence dès que « 1:00:00 » s'affiche");

        // 19 : bornes 1 s – 23:59:59 et ajouts (minutes ou secondes).
        Check(lines, TimerService.Clamp(TimeSpan.Zero) == TimeSpan.FromSeconds(1) && TimerService.Clamp(TimeSpan.FromSeconds(1)) == TimeSpan.FromSeconds(1)
                     && TimerService.Clamp(TimeSpan.FromHours(30)) == new TimeSpan(23, 59, 59) && TimerService.Clamp(new TimeSpan(2, 15, 7)) == new TimeSpan(2, 15, 7),
            "minuteur : durée gardée entre 1 s et 23:59:59 (plus de limite à 99 min)");
        var one = TimeSpan.FromSeconds(1);
        Check(lines, TimerService.Adjusted(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), one) == TimeSpan.FromSeconds(90)
                     && TimerService.Adjusted(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15), one) == TimeSpan.FromSeconds(45)
                     && TimerService.Adjusted(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(-1), one) == one
                     && TimerService.Adjusted(new TimeSpan(23, 59, 30), TimeSpan.FromMinutes(1), one) == new TimeSpan(23, 59, 59)
                     && TimerService.Adjusted(TimeSpan.FromHours(2), TimeSpan.FromMinutes(-1), one) == new TimeSpan(1, 59, 0)
                     && TimerService.Adjusted(TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(-1), TimeSpan.FromSeconds(5)) == TimeSpan.FromSeconds(5),
            "minuteur : + / − en minutes ou en secondes, sans passer sous 1 s (5 s s'il tourne) ni au-delà de 23:59:59");
        Check(lines, Views.CalendarTimerView.CustomStep(59, 1, 59) == 0 && Views.CalendarTimerView.CustomStep(0, -1, 59) == 59
                     && Views.CalendarTimerView.CustomStep(23, 1, 23) == 0 && Views.CalendarTimerView.CustomStep(0, -1, 23) == 23
                     && Views.CalendarTimerView.CustomStep(7, 1, 59) == 8,
            "minuteur « Perso » : compteurs h / min / s qui bouclent (59 → 00, 00 → 23 pour les heures)");

        // 19 : la palette accepte jusqu'à 24 h et la seconde (avant : bornée à 99 min par le minuteur).
        static TimeSpan? Parse(string text) => Palette.PaletteEngine.TryParseDuration(text, out var duration) ? duration : null;
        Check(lines, Parse("1h30") == new TimeSpan(1, 30, 0) && Parse("90 s") == TimeSpan.FromSeconds(90) && Parse("45s") == TimeSpan.FromSeconds(45)
                     && Parse("2 min 30 s") == new TimeSpan(0, 2, 30) && Parse("2:30") == new TimeSpan(0, 2, 30)
                     && Parse("1:30:15") == new TimeSpan(1, 30, 15) && Parse("150 min") == new TimeSpan(2, 30, 0) && Parse("10") == TimeSpan.FromMinutes(10)
                     && Parse("24h") == new TimeSpan(23, 59, 59) && Parse("25 h") == null && Parse("0") == null && Parse("99999999999 s") == null,
            "palette : « 1h30 », « 90 s » (plus lu 9 min), « 45s », « 1:30:15 », « 150 min » ; « 24h » → 23:59:59 ; au-delà, zéro ou nombre énorme refusés");

        // 19 : sons de fin — ceux de Windows qui existent vraiment, identifiants valables, réglages remis dans leurs bornes.
        var sounds = TimerSound.WindowsSounds();
        Check(lines, sounds.All(s => File.Exists(s.Path) && TimerSound.NormalizeId(s.Id, null) == s.Id) && sounds.Select(s => s.Id).Distinct().Count() == sounds.Count,
            $"sons du minuteur : {sounds.Count} alarmes / sonneries de Windows trouvées ({string.Join(", ", sounds.Take(3).Select(s => s.Name))}…), toutes lisibles");
        Check(lines, TimerSound.NormalizeId("windows:alarm03.WAV", null) == "windows:Alarm03.wav" && TimerSound.NormalizeId("windows:Alarm11.wav", null) == TimerSound.OpalineId
                     && TimerSound.NormalizeId("windows:..\\..\\x.wav", null) == TimerSound.OpalineId && TimerSound.NormalizeId(TimerSound.FileId, null) == TimerSound.OpalineId
                     && TimerSound.NormalizeId(TimerSound.FileId, @"C:\son.mp3") == TimerSound.FileId && TimerSound.NormalizeId(null, null) == TimerSound.OpalineId
                     && TimerSound.NameOf("windows:Ring10.wav", null) == Loc.F("timer.sound.ring", 10) && TimerSound.NameOf(TimerSound.FileId, @"C:\Mes sons\réveil.mp3") == "réveil.mp3",
            "sons du minuteur : identifiants contrôlés (pas de chemin glissé dans « windows: »), noms « Sonnerie 10 », fichier perso");
        var timer = new TimerSettings { Sound = "n'importe quoi", Volume = 7, CustomSeconds = 999999, CustomFile = "  " };
        timer.Normalize();
        var silent = new TimerSettings { Volume = double.NaN, CustomSeconds = 0 };
        silent.Normalize();
        var missing = TimerSound.PathOf(new TimerSettings { Sound = TimerSound.FileId, CustomFile = @"C:\n'existe pas\son.mp3" });
        Check(lines, timer.Sound == TimerSound.OpalineId && timer.Volume == 1 && timer.CustomSeconds == 86399 && timer.CustomFile == null
                     && silent.Volume == 0.8 && silent.CustomSeconds == 1 && (missing == null || File.Exists(missing)),
            "réglages du minuteur : son inconnu → son d'Opaline, volume 0–100 %, durée « Perso » 1 s – 23:59:59 ; fichier disparu → son d'Opaline");

        // 18 : actualisation de la météo réglable (5 / 10 / 15 / 30 / 60 min, 10 par défaut).
        int Refresh(int minutes)
        {
            var widgets = new WidgetSettings { WeatherRefreshMinutes = minutes };
            widgets.Normalize();
            return widgets.WeatherRefreshMinutes;
        }
        Check(lines, new WidgetSettings().WeatherRefreshMinutes == 10 && Refresh(7) == 5 && Refresh(8) == 10 && Refresh(0) == 5 && Refresh(45) == 30
                     && Refresh(999) == 60 && WidgetSettings.WeatherRefreshChoices.All(c => Refresh(c) == c),
            "météo : actualisation 10 min par défaut, valeur modifiée à la main ramenée au choix le plus proche");

        // 18 : « Détecter ma ville » — lecture des réponses de BigDataCloud et d'ipwho.is (rien n'est demandé sur Internet ici).
        Check(lines, CityDetector.CityFromReverse("""{"latitude":45.5,"longitude":-73.6,"city":"Montréal","locality":"Ville-Marie"}""") == "Montréal"
                     && CityDetector.CityFromReverse("""{"city":"","locality":"Saint-Sauveur"}""") == "Saint-Sauveur"
                     && CityDetector.CityFromReverse("""{"city":"","locality":""}""") == null,
            "ville : nom tiré de la position (ville, sinon localité, sinon rien)");
        var ip = CityDetector.PlaceFromIp("""{"ip":"203.0.113.7","success":true,"city":"Québec","latitude":46.81,"longitude":-71.21}""");
        Check(lines, ip is { City: "Québec", Latitude: 46.81, Longitude: -71.21, Source: CityDetector.Source.IpAddress }
                     && CityDetector.PlaceFromIp("""{"success":false,"message":"Reserved range"}""") == null
                     && CityDetector.PlaceFromIp("""{"success":true,"city":"","latitude":1,"longitude":2}""") == null,
            "ville : adresse IP lue (champs success, city, latitude, longitude), échec reconnu");
        var widgetsAfter = new WidgetSettings();
        CityDetector.Apply(widgetsAfter, new CityDetector.Place("Québec", 46.81, -71.21, CityDetector.Source.Windows));
        Check(lines, widgetsAfter is { City: "Québec", CityLatitude: 46.81, CityLongitude: -71.21, CityCoordinatesFor: "Québec" }
                     && new CityDetector.Outcome(null, CityDetector.Failure.Offline, true).Message == Loc.T("city.detect.offline")
                     && new CityDetector.Outcome(ip, CityDetector.Failure.None, true).Message == Loc.F("city.detect.found.ip_fallback", "Québec"),
            "ville : trouvée → nom + coordonnées rangés ; messages clairs (hors ligne, localisation refusée → adresse IP)");

        // 17 : libellés du réseau dans les deux langues.
        Check(lines, Loc.T("widgets.download") != "widgets.download" && Loc.T("widgets.upload") != "widgets.upload",
            $"réseau : « {Loc.T("widgets.download")} » et « {Loc.T("widgets.upload")} »");
    }
}
