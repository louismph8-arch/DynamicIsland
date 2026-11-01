using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// Entrées « Ne pas déranger » (7F), les mêmes partout : tuile de l'onglet Contrôles et menu de l'icône Opaline. En tête l'état
/// (« Encore 25 min »), puis les durées (la durée en cours est cochée), puis « Arrêter » s'il est actif.
/// </summary>
public static class FocusMenu
{
    public static List<object> Items()
    {
        var items = new List<object>();
        bool on = FocusService.DoNotDisturb;
        if (on)
        {
            items.Add(new MenuItem { Header = FocusService.Describe(DateTime.Now), IsEnabled = false, FontWeight = FontWeights.SemiBold });
            items.Add(new Separator());
        }
        foreach (var choice in FocusService.Choices)
        {
            var item = new MenuItem { Header = FocusService.Label(choice) };
            var picked = choice;
            item.Click += (_, _) => FocusService.TurnOn(picked);
            items.Add(item);
        }
        if (on)
        {
            items.Add(new Separator());
            var stop = new MenuItem { Header = Loc.T("dnd.stop") };
            stop.Click += (_, _) => FocusService.Stop();
            items.Add(stop);
        }
        return items;
    }

    /// <summary>Sous-menu « Ne pas déranger » (icône de notification), coché quand il est actif.</summary>
    public static MenuItem Submenu()
    {
        var root = new MenuItem { Header = Loc.T("controls.dnd"), IsCheckable = false, IsChecked = FocusService.DoNotDisturb };
        foreach (var item in Items()) root.Items.Add(item);
        return root;
    }
}
