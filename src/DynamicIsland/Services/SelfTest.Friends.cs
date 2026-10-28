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
