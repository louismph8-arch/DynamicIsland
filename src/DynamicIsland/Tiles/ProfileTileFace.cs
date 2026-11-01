using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DynamicIsland.Animation;
using DynamicIsland.Controls;
using DynamicIsland.Design;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>
/// Tuile vivante « Profil » (refonte O6) : changer de profil en UN clic, comme les pastilles de l'onglet Contrôles
/// (<see cref="ProfileService.Switch"/> : applis du dock, couleur, fond d'écran du profil).
/// <list type="bullet">
/// <item>S : le profil actif (icône allumée) et un point par profil ; clic = profil suivant ;</item>
/// <item>W : une pastille par profil (icône Lucide + couleur du profil, 5 au plus, l'actif toujours dedans) ; l'active est remplie
/// de sa couleur ; clic sur une pastille = ce profil (petit saut de la pastille choisie).</item>
/// </list>
/// Rien ne tourne : relue chaque seconde par la grille (page visible) et redessinée seulement si un profil a changé.
/// </summary>
public sealed class ProfileTileFace : TileFace
{
    private readonly Grid _layout = new();
    private readonly StackPanel _top = new() { Orientation = Orientation.Horizontal };
    private readonly IconChip? _chip;
    private readonly TextBlock _line;
    private string _signature = "\0";
    private string? _pop;

    public ProfileTileFace(TileDefinition definition, TileSize size) : base(definition, size, null)
    {
        Child = _layout;
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (size == TileSize.S)
        {
            _chip = new IconChip
            {
                GlyphSize = IconSize.L, Width = Sizes.IconChip, Height = Sizes.IconChip, IsActive = true, Tint = Interaction.ActiveTintLight,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            _layout.Children.Add(_chip);
            _top.HorizontalAlignment = HorizontalAlignment.Right;
            _top.VerticalAlignment = VerticalAlignment.Center;
            _top.Height = Sizes.IconChip;
            ToolTip = Loc.T("tiles.profile.next");
        }
        else
        {
            _top.HorizontalAlignment = HorizontalAlignment.Left;
        }
        _layout.Children.Add(_top);
        var title = new TextBlock
        {
            Text = definition.Name, FontSize = TypeRamp.Caption, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
        }.WithInk(TextBlock.ForegroundProperty, InkRole.TextPrimary);
        Grid.SetRow(title, 2);
        _layout.Children.Add(title);
        _line = new TextBlock { FontSize = TypeRamp.Caption2, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, Space.Px, 0, 0) }
            .WithInk(TextBlock.ForegroundProperty, InkRole.TextSecondary);
        Grid.SetRow(_line, 3);
        _layout.Children.Add(_line);
        Refresh();
    }

    /// <summary>Profils montrés (W : 5 au plus, l'actif toujours dedans, dans l'ordre des profils).</summary>
    internal static List<Profile> Shown(IReadOnlyList<Profile> all, string? activeId, int max)
    {
        if (all.Count <= max) return all.ToList();
        var shown = all.Take(max).ToList();
        if (activeId != null && shown.All(p => p.Id != activeId) && all.FirstOrDefault(p => p.Id == activeId) is { } active)
        {
            shown[^1] = active;
            shown = all.Where(shown.Contains).ToList();
        }
        return shown;
    }

    /// <summary>Profil suivant (S) : celui d'après l'actif, en boucle.</summary>
    internal static string? Next(IReadOnlyList<Profile> all, string? activeId)
    {
        if (all.Count == 0) return null;
        int index = all.ToList().FindIndex(p => p.Id == activeId);
        return all[(index + 1) % all.Count].Id;
    }

    /// <summary>Appelé chaque seconde par la grille (page visible) : ne refait rien si aucun profil n'a changé.</summary>
    public override void Refresh()
    {
        var all = ProfileService.All;
        var active = ProfileService.Active;
        string signature = string.Join("|", all.Select(p => $"{p.Id}:{p.Name}:{p.Icon}:{p.AccentStart}:{p.AccentEnd}"))
                           + $"#{active?.Id}:{AppearanceService.AccentStart}:{AppearanceService.AccentEnd}";
        if (signature == _signature) return;
        _signature = signature;
        try
        {
            Show(all, active);
        }
        catch (Exception ex)
        {
            Log.Error("Tuile Profil : affichage", ex);
        }
    }

    private void Show(IReadOnlyList<Profile> all, Profile? active)
    {
        _line.Text = active?.Name ?? "";
        System.Windows.Automation.AutomationProperties.SetName(this, $"{Definition.Name} : {active?.Name}");
        _top.Children.Clear();
        if (TileSize == TileSize.S)
        {
            if (_chip != null)
            {
                _chip.Glyph = Icons.Glyph(active?.Icon);
                if (_pop != null && _pop == active?.Id) Pop(_chip);
            }
            // Un point par profil (6 au plus) ; celui du profil actif = petite pilule d'accent.
            foreach (var profile in Shown(all, active?.Id, LiveTileShape.PastillesW + 1))
            {
                bool on = profile == active;
                var dot = new SquircleBorder
                {
                    Width = on ? LiveTileShape.ProfileDotActive : LiveTileShape.ProfileDot, Height = LiveTileShape.ProfileDot,
                    CornerRadius = Corners.Pill, Margin = new Thickness(Space.S1, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                };
                if (on) dot.SetResourceReference(Border.BackgroundProperty, AppearanceService.AccentGradientKey);
                else dot.SetResourceReference(Border.BackgroundProperty, Paint.Key(InkRole.TextFaint));
                _top.Children.Add(dot);
            }
        }
        else
        {
            var shown = Shown(all, active?.Id, LiveTileShape.PastillesW);
            foreach (var profile in shown)
            {
                var pastille = Pastille(profile, profile == active);
                if (_top.Children.Count > 0) pastille.Margin = new Thickness(LiveTileShape.PastilleGap, 0, 0, 0);
                _top.Children.Add(pastille);
                if (profile == active && _pop == profile.Id) Pop(pastille);
            }
        }
        _pop = null;
    }

    /// <summary>Pastille d'un profil : icône dans sa couleur ; l'active est remplie de sa couleur, icône blanche.</summary>
    private static SquircleBorder Pastille(Profile profile, bool active)
    {
        var (start, end) = ProfileService.AccentOf(profile);
        var gradient = Paint.Gradient(start, end, 45);
        var glyph = new TextBlock
        {
            Text = Icons.Glyph(profile.Icon),
            FontFamily = FontStack.Icons,
            FontSize = IconSize.M,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = active ? Paint.White : gradient,
        };
        var pastille = new SquircleBorder
        {
            Width = LiveTileShape.Pastille, Height = LiveTileShape.Pastille, CornerRadius = Corners.IconChip, Child = glyph,
            Tag = profile.Id, RenderTransformOrigin = new Point(0.5, 0.5), Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = active ? Loc.T("profiles.is_active") : Loc.F("controls.switch_profile", profile.Name),
        };
        System.Windows.Automation.AutomationProperties.SetName(pastille, profile.Name);
        if (active)
        {
            pastille.Background = gradient;
        }
        else
        {
            pastille.SetResourceReference(Border.BackgroundProperty, Paint.Key(InkRole.Surface));
            pastille.MouseEnter += (_, _) => pastille.SetResourceReference(Border.BackgroundProperty, Paint.Key(InkRole.SurfaceHover));
            pastille.MouseLeave += (_, _) => pastille.SetResourceReference(Border.BackgroundProperty, Paint.Key(InkRole.Surface));
        }
        return pastille;
    }

    /// <summary>La pastille choisie saute de <see cref="LiveTileShape.PopFrom"/> à sa taille (rien si les animations sont réduites).</summary>
    private static void Pop(FrameworkElement element)
    {
        if (!MotionPrefs.Rich || !OpalTheme.EffectsOn) return;
        var scale = new ScaleTransform(LiveTileShape.PopFrom, LiveTileShape.PopFrom);
        element.RenderTransform = scale;
        var animation = new DoubleAnimation(LiveTileShape.PopFrom, 1, Motion.Medium)
        {
            EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) => element.RenderTransform = Transform.Identity;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    /// <summary>Clic : S = profil suivant ; W = la pastille touchée (ailleurs : rien, pas d'outil à ouvrir).</summary>
    protected override bool HandleClick(Point point)
    {
        var all = ProfileService.All;
        string? target;
        if (TileSize == TileSize.S)
        {
            target = Next(all, ProfileService.Active?.Id);
        }
        else
        {
            target = null;
            for (var hit = InputHitTest(point) as DependencyObject; hit != null && hit != this; hit = VisualTreeHelper.GetParent(hit))
                if (hit is FrameworkElement { Tag: string id }) { target = id; break; }
        }
        if (target == null || target == ProfileService.Active?.Id) return true;
        _pop = target;
        ProfileService.Switch(target);
        Refresh();
        return true;
    }
}
