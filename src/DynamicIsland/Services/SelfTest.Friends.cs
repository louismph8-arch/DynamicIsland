using DynamicIsland.Services.Friends;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test des amis en ligne (6D-4). Le vrai compte et le vrai serveur de l'utilisateur ne sont jamais touchés. Si la
/// variable OPALINE_AMIS_ESSAI donne l'adresse d'un serveur d'essai (server/amis/test-local.mjs --serve 8799), deux faux PC
/// s'y inscrivent et deviennent amis, de bout en bout par le vrai client d'Opaline.
/// </summary>
internal static partial class SelfTest
{
    private static void Friends(List<string> lines)
    {
        Check(lines, FriendsService.PrettyName("VALORANT-Win64-Shipping") == "Valorant" && FriendsService.PrettyName("dofus") == "Dofus"
                     && FriendsService.PrettyName("RocketLeague") == "Rocketleague" && FriendsService.PrettyName("cs2") == "Cs2",
            "nom du jeu lisible (« VALORANT-Win64-Shipping » → « Valorant »)");
        Check(lines, FriendsApi.ValidServer("https://opaline-amis.exemple.workers.dev") && FriendsApi.ValidServer("http://127.0.0.1:8799")
                     && !FriendsApi.ValidServer("http://exemple.com") && !FriendsApi.ValidServer("ftp://x") && !FriendsApi.ValidServer(""),
            "adresse du serveur : https (ou http seulement sur ce PC)");
        Check(lines, FriendsApi.FormatCode("ABCDEFGH") == "ABCD-EFGH", "code d'ami affiché « ABCD-EFGH »");

        // Messages rapides (correctif 24) : la liste fermée d'Opaline est la même que celle du serveur, et chacun a son texte.
        string worker;
        using (var stream = typeof(FriendsService).Assembly.GetManifestResourceStream("DynamicIsland.Friends.worker.js"))
        using (var reader = new System.IO.StreamReader(stream!))
            worker = reader.ReadToEnd();
        var serverList = "const QUICK_MESSAGES = [" + string.Join(", ", QuickMessages.Ids.Select(id => $"\"{id}\"")) + "];";
        Check(lines, worker.Contains(serverList) && worker.Contains("const VERSION = 3;")
                     && QuickMessages.Ids.All(id => QuickMessages.Text(id) != "friends.message." + id),
            "messages rapides : même liste fermée que le serveur (version 3), un texte pour chacun");
        var cleaned = BugReport.CleanSettings("""{"Friends":{"Name":"Louis","Server":"https://a.workers.dev","Muted":["MAXX2345"],"Blocked":[{"Code":"TROL7777","Name":"Troll"}]}}""");
        Check(lines, !cleaned.Contains("Louis") && !cleaned.Contains("workers.dev") && !cleaned.Contains("MAXX2345") && !cleaned.Contains("Troll"),
            "rapport de bug : ni pseudo, ni serveur, ni amis coupés ou bloqués");

        var server = Environment.GetEnvironmentVariable("OPALINE_AMIS_ESSAI");
        if (string.IsNullOrEmpty(server))
        {
            lines.Add("(bout en bout sauté : pas de serveur d'essai, voir server/amis/LISEZMOI.md)");
            return;
        }
        var louis = FriendsApi.RegisterAsync(server, "Louis (essai)").GetAwaiter().GetResult();
        var max = FriendsApi.RegisterAsync(server, "Max (essai)").GetAwaiter().GetResult();
        FriendsApi.UpdateMeAsync(louis, "Louis (essai)", "game", "Valorant", invisible: false).GetAwaiter().GetResult();
        var refused = FriendsApi.AddAsync(louis, "ZZZZ-2222").GetAwaiter().GetResult();
        FriendsApi.AddAsync(louis, FriendsApi.FormatCode(max.Id)).GetAwaiter().GetResult();
        var pending = FriendsApi.ListAsync(max).GetAwaiter().GetResult();
        FriendsApi.AddAsync(max, louis.Id).GetAwaiter().GetResult();
        var mutual = FriendsApi.ListAsync(max).GetAwaiter().GetResult();
        FriendsApi.UpdateMeAsync(louis, "Louis (essai)", "online", null, invisible: true).GetAwaiter().GetResult();
        var hidden = FriendsApi.ListAsync(max).GetAwaiter().GetResult();

        // Version 3 : « depuis », message rapide, blocage (correctifs 23-24)
        var sent = FriendsApi.SendMessageAsync(max, louis.Id, "play").GetAwaiter().GetResult();
        var freeText = FriendsApi.SendMessageAsync(max, louis.Id, "viens sur mon serveur").GetAwaiter().GetResult();
        var inbox = FriendsApi.ReadMessagesAsync(louis).GetAwaiter().GetResult();
        var emptied = FriendsApi.ReadMessagesAsync(louis).GetAwaiter().GetResult();
        Check(lines, mutual.Extended && mutual.Friends[0].Since != null && sent == null && freeText == "message"
                     && inbox is [{ Message: "play", Name: "Max (essai)" }] && inbox[0].Code == max.Id && emptied.Count == 0,
            "version 3 : « joue à … depuis », message « On joue ? » reçu puis effacé, texte libre refusé");
        FriendsApi.BlockAsync(louis, max.Id).GetAwaiter().GetResult();
        var blockedSide = FriendsApi.ListAsync(louis).GetAwaiter().GetResult();
        var otherSide = FriendsApi.ListAsync(max).GetAwaiter().GetResult();
        var askAgain = FriendsApi.AddAsync(max, louis.Id).GetAwaiter().GetResult();
        var sendBlocked = FriendsApi.SendMessageAsync(max, louis.Id, "play").GetAwaiter().GetResult();
        FriendsApi.UnblockAsync(louis, max.Id).GetAwaiter().GetResult();
        var unblocked = FriendsApi.ListAsync(louis).GetAwaiter().GetResult();
        Check(lines, blockedSide.Blocked.Any(b => b.Code == max.Id) && blockedSide.Friends.Count == 0 && otherSide.Friends.Count == 0
                     && askAgain == "unknown" && sendBlocked == "not_friend" && unblocked.Blocked.Count == 0,
            "blocage : plus d'amitié ni de présence, nouvelle demande et message refusés, déblocage");

        FriendsApi.ForgetAsync(louis).GetAwaiter().GetResult();
        FriendsApi.ForgetAsync(max).GetAwaiter().GetResult();
        bool forgotten;
        try
        {
            FriendsApi.ListAsync(louis).GetAwaiter().GetResult();
            forgotten = false;
        }
        catch (FriendsApi.AuthException)
        {
            forgotten = true;
        }
        Check(lines, refused == "unknown" && pending.Incoming.Count == 1 && pending.Friends.Count == 0
                     && mutual.Friends is [{ Name: "Louis (essai)", Status: "game", Game: "Valorant" }]
                     && hidden.Friends is [{ Status: "offline" }] && forgotten,
            $"bout en bout sur {server} : demande, amis mutuels « joue à Valorant », invisible, comptes d'essai effacés");
    }
}
