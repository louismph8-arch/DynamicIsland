using DynamicIsland.Themes;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Stage;

/// <summary>Ce que voit la colonne auto-masquée d'un écran à un instant (pixels physiques).</summary>
/// <param name="BandTop">Haut de la bande du bord gauche qui révèle la colonne (hauteur de la colonne).</param>
/// <param name="NeighborLeft">Un autre écran est collé à gauche à la hauteur de la souris (pas de vrai bord : la souris peut y passer).</param>
/// <param name="OverColumn">Souris sur la colonne affichée (ou son menu ouvert).</param>
/// <param name="HasGroups">La colonne a quelque chose à montrer (sinon elle reste cachée).</param>
/// <param name="OverHandle">Souris sur la zone de la poignée (<see cref="StageAutoHide.HandleRect"/>), affichée ou non.</param>
internal readonly record struct StageEdgeInput(DateTime Now, POINT Cursor, RECT Screen, int BandTop, int BandBottom,
    bool NeighborLeft, bool OverColumn, bool Gaming, bool HasGroups, bool OverHandle = false);

/// <summary>
/// Option « Cacher la colonne automatiquement » de Stage Manager (calcul pur, une instance par écran, vérifié par --self-test) :
/// la colonne apparaît quand la souris touche le bord gauche de l'écran (bande de <see cref="EdgeBand"/> px sur la hauteur de la
/// colonne) — tout de suite s'il y a un vrai bord, après <see cref="NeighborDwell"/> d'arrêt si un écran est collé à gauche (sinon
/// elle sortirait à chaque passage vers cet écran) — puis reste tant que la souris est dessus et <see cref="HideDelay"/> après.
/// <see cref="Flash"/> la montre un instant (une fenêtre vient d'y partir), <see cref="HideNow"/> la cache tout de suite (clic sur
/// une miniature). Jamais pendant un jeu sur cet écran.
/// <para>Poignée : quand un écran est collé à gauche (écran portrait de l'utilisateur), la souris qui va à gauche passe sur l'autre
/// écran sans jamais s'arrêter dans la bande. Une petite poignée collée au bord, au milieu de la colonne, la fait donc sortir après
/// <see cref="HandleDwell"/> de survol (ou d'un clic) ; elle compte comme le bord tant que la colonne est affichée.</para>
/// </summary>
internal sealed class StageAutoHide
{
    public const int EdgeBand = 2;
    public static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(700);
    public static readonly TimeSpan NeighborDwell = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan HandleDwell = TimeSpan.FromMilliseconds(120);
    /// <summary>Palette « afficher les fenêtres » : la colonne de l'écran de la souris sort ce temps-là.</summary>
    public static readonly TimeSpan PeekTime = TimeSpan.FromSeconds(4);

    private DateTime? _edgeSince, _handleSince;
    private DateTime _lastActive = DateTime.MinValue, _flashUntil = DateTime.MinValue;
    /// <summary>Cachée par un clic alors que la souris était dans la bande : pas de retour tant qu'elle n'en est pas sortie.</summary>
    private bool _suppressed;

    public bool Visible { get; private set; }

    /// <summary>Le temps seul peut changer la décision (sinon, souris immobile = rien à recalculer).</summary>
    public bool Pending => Visible || _edgeSince != null || _handleSince != null || _flashUntil != DateTime.MinValue;

    /// <summary>Montrée un instant (une fenêtre vient de partir dans la colonne, clic sur la poignée, palette).</summary>
    public void Flash(DateTime now, TimeSpan? duration = null)
    {
        var until = now + (duration ?? FlashTime);
        if (until > _flashUntil) _flashUntil = until;
        _suppressed = false;
    }

    /// <summary>Cachée tout de suite (clic sur une miniature : l'échange commence).</summary>
    public void HideNow()
    {
        Reset();
        _suppressed = true;
    }

    public void Reset()
    {
        Visible = false;
        _edgeSince = null;
        _handleSince = null;
        _suppressed = false;
        _lastActive = DateTime.MinValue;
        _flashUntil = DateTime.MinValue;
    }

    /// <summary>La souris est-elle dans la bande du bord gauche, sur la hauteur de la colonne ?</summary>
    public static bool InBand(POINT cursor, RECT screen, int top, int bottom) =>
        cursor.X >= screen.Left && cursor.X < screen.Left + EdgeBand && cursor.Y >= top && cursor.Y < bottom;

    /// <summary>Un autre écran est-il collé au bord gauche de celui-ci, à cette hauteur ?</summary>
    public static bool NeighborLeftAt(RECT screen, IEnumerable<RECT> all, int y) =>
        all.Any(s => !(s.Left == screen.Left && s.Top == screen.Top && s.Right == screen.Right && s.Bottom == screen.Bottom)
                     && Math.Abs(s.Right - screen.Left) <= 1 && y >= s.Top && y < s.Bottom);

    /// <summary>
    /// Zone de survol de la poignée (pixels) : collée au bord gauche de l'écran, centrée sur la hauteur de la colonne, dans l'écran.
    /// </summary>
    public static RECT HandleRect(RECT screen, int bandTop, int bandBottom, double scale)
    {
        int width = (int)Math.Round(StageShape.HandleHitWidth * scale), height = (int)Math.Round(StageShape.HandleHitHeight * scale);
        int top = (bandTop + bandBottom) / 2 - height / 2;
        top = Math.Max(screen.Top, Math.Min(top, screen.Bottom - height));
        return new RECT { Left = screen.Left, Top = top, Right = screen.Left + width, Bottom = top + height };
    }

    public static bool Inside(POINT p, RECT r) => p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;

    /// <summary>La poignée est-elle montrée ? Option active, des miniatures, pas de jeu (ni plein écran : pas de miniatures alors),
    /// et colonne pas déjà affichée.</summary>
    public static bool HandleVisible(bool autoHide, bool hasGroups, bool gaming, bool columnShown) =>
        autoHide && hasGroups && !gaming && !columnShown;

    /// <summary>Nouvelle décision : la colonne doit-elle être affichée ?</summary>
    public bool Update(StageEdgeInput input)
    {
        if (!input.HasGroups || input.Gaming)
        {
            Reset();
            return false;
        }
        bool inBand = InBand(input.Cursor, input.Screen, input.BandTop, input.BandBottom);
        if (!inBand) _edgeSince = null;
        else _edgeSince ??= input.Now;
        if (!input.OverHandle) _handleSince = null;
        else _handleSince ??= input.Now;
        if (!inBand && !input.OverHandle) _suppressed = false;
        bool edge = inBand && !_suppressed && (!input.NeighborLeft || input.Now - _edgeSince!.Value >= NeighborDwell);
        // Poignée : 120 ms de survol pour sortir ; la colonne affichée, la poignée compte comme elle (la souris va vers la droite).
        edge |= input.OverHandle && !_suppressed && (Visible || input.Now - _handleSince!.Value >= HandleDwell);
        if (edge || Visible && input.OverColumn) _lastActive = input.Now;
        if (_flashUntil != DateTime.MinValue && input.Now >= _flashUntil) _flashUntil = DateTime.MinValue;
        Visible = input.Now < _flashUntil || _lastActive != DateTime.MinValue && input.Now - _lastActive < HideDelay;
        return Visible;
    }
}
