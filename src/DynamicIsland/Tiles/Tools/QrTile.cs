using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Services.Remote;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Tools;

/// <summary>
/// QR code de ce qui est copié (tuile QR code) : le presse-papiers n'est lu qu'AU CLIC (jamais surveillé), un élément marqué
/// sensible (mot de passe d'un gestionnaire, coffre d'Opaline) est refusé, et rien n'est gardé ailleurs qu'en mémoire.
/// Encodeur maison <see cref="QrCode"/> (versions 1 à 10 : 213 octets au plus). Clic droit = copier l'image du QR code.
/// </summary>
internal static class QrClip
{
    public static BitmapSource? Image { get; private set; }

    /// <summary>Ce qui est encodé, en court (« exemple.com », ou le début du texte).</summary>
    public static string? Summary { get; private set; }

    /// <summary>Message d'erreur (clé de texte) du dernier essai ; null = réussi ou jamais essayé.</summary>
    public static string? Problem { get; private set; }

    /// <summary>« Image copiée » affiché jusqu'à ce moment.</summary>
    public static DateTime CopiedUntil { get; private set; } = DateTime.MinValue;

    public static void FromClipboard()
    {
        string? text = null;
        try
        {
            if (SecureClipboard.CurrentIsSensitive())
            {
                Fail("tiles.qr.sensitive");
                return;
            }
            if (Clipboard.ContainsText()) text = Clipboard.GetText();
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            Fail("tiles.qr.busy"); // presse-papiers pris par une autre appli : on réessaie au prochain clic
            return;
        }
        Encode(text);
    }

    /// <summary>Encode un texte (aussi utilisé par l'auto-test et les rendus) ; vrai si un QR code a été fait.</summary>
    internal static bool Encode(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            Fail("tiles.qr.empty");
            return false;
        }
        if (QrCode.Encode(text) is not { } qr)
        {
            Fail("tiles.qr.too_long");
            return false;
        }
        Image = qr.ToBitmap(ToolTileShape.QrModule);
        Image.Freeze();
        Summary = Describe(text);
        Problem = null;
        return true;
    }

    /// <summary>Un lien : son site ; sinon le début du texte, sur une ligne.</summary>
    internal static string Describe(string text)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host.Length > 0)
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var line = string.Join(" ", text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= SummaryLength ? line : line[..SummaryLength] + "…";
    }

    private const int SummaryLength = 40;

    public static void CopyImage()
    {
        if (Image == null) return;
        try
        {
            Clipboard.SetImage(Image);
            CopiedUntil = DateTime.UtcNow + TimeSpan.FromSeconds(2.5);
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            Log.Error("Tuile QR code : copie de l'image", ex);
        }
    }

    private static void Fail(string key)
    {
        Problem = key;
    }

    /// <summary>Auto-test : tout oublier.</summary>
    internal static void Reset()
    {
        Image = null;
        Summary = null;
        Problem = null;
        CopiedUntil = DateTime.MinValue;
    }
}

/// <summary>Tuile QR code (W ou L) : le QR code sur sa plaque blanche (lisible par un téléphone dans les deux thèmes), le site en dessous.</summary>
internal sealed class QrFace : ToolFace
{
    private readonly SquircleBorder _plate = new() { CornerRadius = Corners.S, Padding = new Thickness(ToolTileShape.QrPadding), Background = Paint.Frozen(Swatch.White) };
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly SquircleBorder _placeholder = new() { CornerRadius = Corners.S };
    private BitmapSource? _shown;

    public QrFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context)
    {
        double side = size == TileSize.L ? ToolTileShape.QrLarge : ToolTileShape.Qr;
        _plate.Width = _plate.Height = side;
        _plate.Child = _image;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor); // modules nets
        _placeholder.Width = _placeholder.Height = side;
        _placeholder.SetResourceReference(Border.BackgroundProperty, Paint.Key(InkRole.Control));
        var glyph = new TextBlock { Text = Icons.QrCode, FontFamily = FontStack.Icons, FontSize = size == TileSize.L ? IconSize.Huge : IconSize.XXL, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextFaint));
        _placeholder.Child = glyph;
    }

    protected override void Update()
    {
        var image = QrClip.Image;
        if (!ReferenceEquals(image, _shown) || _image.Source == null && image != null)
        {
            _shown = image;
            _image.Source = image;
        }
        SetAccessory(image != null ? _plate : _placeholder);
        string line;
        if (DateTime.UtcNow < QrClip.CopiedUntil) line = Loc.T("tiles.qr.copied");
        else if (QrClip.Problem is { } problem) line = Loc.T(problem);
        else line = QrClip.Summary ?? Loc.T("tiles.qr.hint");
        SetTone(Icons.QrCode, image != null ? ToolTone.Accent : ToolTone.Idle);
        SetTexts(null, line);
    }
}
