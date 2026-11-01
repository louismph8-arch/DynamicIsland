using System.Text.Json.Nodes;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test de la musique et du vocal dans l'île (demandés pendant la refonte) : barre de position (calculs purs), volume de
/// l'appli qui joue (correspondance des sessions + vraie lecture SANS rien régler), commande RPC SET_USER_VOICE_SETTINGS (forme
/// exacte, rien n'est envoyé à Discord).
/// </summary>
internal static partial class SelfTest
{
    private static void MediaControls(List<string> lines)
    {
        var song = TimeSpan.FromSeconds(222);
        Check(lines, SeekMath.At(0, 300, song) == TimeSpan.Zero && SeekMath.At(150, 300, song) == TimeSpan.FromSeconds(111)
                     && SeekMath.At(-20, 300, song) == TimeSpan.Zero && SeekMath.At(400, 300, song) == song
                     && SeekMath.At(10, 0, song) == TimeSpan.Zero && SeekMath.At(10, 300, TimeSpan.Zero) == TimeSpan.Zero,
            "barre de position : clic → temps (milieu = 1:51 sur 3:42, bornée aux deux bouts, barre ou morceau vides = 0)");

        Check(lines, SeekMath.Wheel(TimeSpan.FromSeconds(77), +1, song) == TimeSpan.FromSeconds(82)
                     && SeekMath.Wheel(TimeSpan.FromSeconds(77), -3, song) == TimeSpan.FromSeconds(72)
                     && SeekMath.Wheel(TimeSpan.FromSeconds(2), -1, song) == TimeSpan.Zero
                     && SeekMath.Wheel(TimeSpan.FromSeconds(219), +1, song) == TimeSpan.FromSeconds(221)
                     && SeekMath.Wheel(TimeSpan.FromSeconds(77), 0, song) == TimeSpan.FromSeconds(77),
            "barre de position : molette ±5 s par cran, sans sortir du morceau (pas pile à la fin : le lecteur passerait au suivant)");

        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var hold = now + SeekMath.Hold;
        Check(lines, !SeekMath.Accept(now, hold) && !SeekMath.Accept(now.AddSeconds(1.4), hold) && SeekMath.Accept(now.AddSeconds(1.5), hold)
                     && SeekMath.Accept(now, DateTimeOffset.MinValue) && SeekMath.Hold == TimeSpan.FromSeconds(1.5)
                     && SeekMath.Fraction(TimeSpan.FromSeconds(111), song) == 0.5 && SeekMath.Fraction(song * 2, song) == 1,
            "barre de position : la position du lecteur est ignorée 1,5 s après un changement (pas de saut en arrière), puis reprise");

        // Volume de l'appli : à quelle session audio correspond la session média (Spotify classique, Spotify du Store, navigateur).
        Check(lines, AppVolumeService.Matches("Spotify.exe", "spotify", "")
                     && !AppVolumeService.Matches("Spotify.exe", "chrome", "")
                     && AppVolumeService.Matches("Chrome", "chrome", "")
                     && AppVolumeService.Matches("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "spotify",
                         @"{0.0.0.00000000}.{x}|\Device\HarddiskVolume3\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.250.0.0_x64__zpdnekdrzrea0\Spotify.exe%b{y}")
                     && AppVolumeService.Matches("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "spotify", "")
                     && !AppVolumeService.Matches("Contoso.Player_abc!App", "app", ""),
            "volume de l'appli : Spotify (exe et Store), navigateur reconnus ; « !App » ne sert jamais de nom de processus");

        // Vraie lecture du volume de Spotify (s'il a une session audio) : deux lectures, rien n'est réglé entre les deux.
        var volume = new AppVolumeService();
        bool found = volume.TryRead("Spotify.exe", out double level, out bool muted);
        bool again = volume.TryRead("Spotify.exe", out double level2, out bool muted2);
        Check(lines, found == again && (!found || (level is >= 0 and <= 1 && Math.Abs(level - level2) < 0.0001 && muted == muted2)),
            found ? $"volume de l'appli : Spotify trouvé ({Math.Round(level * 100)} %{(muted ? ", muet" : "")}), lu sans rien changer"
                  : "volume de l'appli : pas de session audio de Spotify en ce moment (message « introuvable » affiché à la place du curseur)");

        // Discord : forme exacte de la commande (doc RPC : user_id, mute et volume 0–200 facultatifs).
        string Json(JsonObject o) => o.ToJsonString();
        Check(lines, Json(DiscordVoice.UserVoiceCommand("42", true, null)) == """{"cmd":"SET_USER_VOICE_SETTINGS","args":{"user_id":"42","mute":true}}"""
                     && Json(DiscordVoice.UserVoiceCommand("42", null, 150)) == """{"cmd":"SET_USER_VOICE_SETTINGS","args":{"user_id":"42","volume":150}}"""
                     && Json(DiscordVoice.UserVoiceCommand("42", false, 100)) == """{"cmd":"SET_USER_VOICE_SETTINGS","args":{"user_id":"42","mute":false,"volume":100}}""",
            "vocal : commande SET_USER_VOICE_SETTINGS { user_id, mute, volume } (seulement les champs changés)");
        Check(lines, Json(DiscordVoice.UserVoiceCommand("42", null, 260)).Contains("\"volume\":200") && Json(DiscordVoice.UserVoiceCommand("42", null, -5)).Contains("\"volume\":0")
                     && DiscordVoice.ClampUserVolume(99.6) == 100 && DiscordVoice.ClampUserVolume(double.NaN) == DiscordVoice.DefaultUserVolume,
            "vocal : volume d'une personne borné à 0–200 (entier), 100 par défaut");
        var member = new VoiceMember("2", "Max", "", false, false, false);
        Check(lines, member.Volume == 100 && !member.LocalMuted && (member with { LocalMuted = true }).LocalMuted,
            "vocal : une personne arrive à 100 %, non coupée pour moi");
    }
}
