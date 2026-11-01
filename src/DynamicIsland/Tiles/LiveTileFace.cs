using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>
/// Base des tuiles vivantes (O6, lot « vivantes A ») : leur propre contenu (<see cref="TileFace.SetContent"/>), relu au plus une
/// fois par seconde par la grille (<see cref="Update"/>), et une animation continue (<see cref="ILiveAnimation.Tick"/>) qui ne
/// tourne que si la tuile est à l'écran, que les animations sont permises et que la tuile en a besoin (<see cref="WantsAnimation"/>).
/// Une erreur n'arrête que cette tuile (journal) : elle garde sa dernière image.
/// </summary>
public abstract class LiveTileFace : TileFace, ILiveAnimation
{
    private bool _failed;

    protected LiveTileFace(TileDefinition definition, TileSize size) : base(definition, size, null)
    {
    }

    /// <summary>La tuile a quelque chose à animer en ce moment (musique qui joue, ciel, trotteuse).</summary>
    protected abstract bool WantsAnimation { get; }

    /// <summary>Relit ses données (en mémoire seulement : services partagés de l'île).</summary>
    protected abstract void Update();

    /// <summary>Image du temps <paramref name="seconds"/>.</summary>
    protected abstract void Animate(double seconds);

    /// <summary>La tuile vient d'apparaître (vrai) ou de disparaître de l'écran.</summary>
    protected virtual void OnShown(bool shown)
    {
    }

    public sealed override void Refresh()
    {
        if (_failed) return;
        Guard(() =>
        {
            Update();
            SyncAnimation();
        }, "lecture");
    }

    protected sealed override void OnLiveChanged(bool live)
    {
        Guard(() =>
        {
            OnShown(live);
            if (live) Update();
            SyncAnimation();
        }, live ? "affichée" : "cachée");
    }

    /// <summary>Démarre / arrête l'animation selon l'état actuel (à appeler quand <see cref="WantsAnimation"/> change).</summary>
    protected void SyncAnimation()
    {
        bool run = !_failed && IsLive && LiveTicker.Allowed && WantsAnimation;
        LiveTicker.Set(this, run);
        if (run) Animate(LiveTicker.Now); // première image tout de suite (pas d'attente d'un tour de minuterie)
    }

    /// <summary>L'animation tourne (--self-test).</summary>
    internal bool IsAnimating => LiveTicker.Has(this);

    void ILiveAnimation.Tick(double seconds) => Animate(seconds);

    /// <summary>--snapshot : pose une image figée au temps <paramref name="seconds"/>.</summary>
    internal void Pose(double seconds) => Guard(() =>
    {
        Update();
        Animate(seconds);
    }, "rendu");

    private void Guard(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _failed = true;
            LiveTicker.Set(this, false);
            Log.Error($"Tuile « {Definition.Id} » : {what}", ex);
        }
    }

    // ------------------------------------------------------------------ petits outils partagés

    /// <summary>Texte d'une tuile vivante : sur une image (blanc) ou sur le verre (brosses du thème, jamais figées).</summary>
    protected static void Ink(TextBlock text, bool onArt, InkRole role)
    {
        if (onArt)
        {
            text.Foreground = role switch
            {
                InkRole.TextPrimary => LiveTiles.OnArt,
                InkRole.TextSecondary => LiveTiles.OnArtSecondary,
                _ => LiveTiles.OnArtTertiary,
            };
        }
        else
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(role));
        }
    }

    protected static TextBlock Text(double size, FontWeight weight, int lines = 1)
    {
        var text = new TextBlock
        {
            FontSize = size,
            FontWeight = weight,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (lines > 1)
        {
            text.TextWrapping = TextWrapping.Wrap;
            text.MaxHeight = lines * size * TileGridShape.LineHeight;
        }
        return text;
    }

    /// <summary>Voile du haut vers le bas (texte blanc lisible sur une image claire), figé.</summary>
    protected static Brush Scrim()
    {
        var brush = new LinearGradientBrush(Swatch.WithAlpha(LiveTiles.ScrimColor, LiveTiles.ScrimTop),
            Swatch.WithAlpha(LiveTiles.ScrimColor, LiveTiles.ScrimBottom), 90);
        brush.Freeze();
        return brush;
    }
}
