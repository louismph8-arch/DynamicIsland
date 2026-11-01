using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DynamicIsland.Controls;
using DynamicIsland.Design;
using DynamicIsland.Services;
using DynamicIsland.Services.Remote;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Tools;

/// <summary>
/// Tuile Téléphone (télécommande, 6D / 7E) : téléphones reliés, transfert en cours, fichiers qui attendent ; glisser un fichier sur
/// la tuile = « Envoyer au téléphone » (<see cref="RemoteOutbox"/> : le fichier reste sur le PC, le téléphone le télécharge par
/// son lien) quand le transfert de fichiers est activé ; clic = page Connecté. Cachée si la télécommande est coupée.
/// Tout est lu en mémoire (réglages, transferts, liste d'envoi) : rien sur le disque à chaque seconde.
/// </summary>
internal sealed class PhoneFace : ToolFace
{
    private readonly Rectangle? _zone;
    private readonly TextBlock? _zoneGlyph;
    private bool _dragging;
    private DateTime _sentUntil = DateTime.MinValue;
    private int _sentCount;

    public PhoneFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context)
    {
        AllowDrop = true;
        if (size != TileSize.S)
        {
            // Zone de dépôt dessinée (pointillés) : là où l'on lâche un fichier pour l'envoyer.
            double side = size == TileSize.L ? ToolTileShape.ButtonLarge : ToolTileShape.Button;
            _zone = new Rectangle { Width = side, Height = side, RadiusX = Radius.L, RadiusY = Radius.L, StrokeThickness = Stroke.Selected, StrokeDashArray = [ToolTileShape.DropDash, ToolTileShape.DropDash] };
            _zoneGlyph = new TextBlock { Text = Icons.FileDown, FontFamily = FontStack.Icons, FontSize = size == TileSize.L ? IconSize.Huge : IconSize.XXL, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var grid = new Grid { IsHitTestVisible = false };
            grid.Children.Add(_zone);
            grid.Children.Add(_zoneGlyph);
            SetAccessory(grid);
        }
        DragEnter += OnDrag;
        DragOver += OnDrag;
        DragLeave += (_, e) =>
        {
            _dragging = false;
            Highlight(false);
            Refresh();
            e.Handled = true;
        };
        Drop += OnDrop;
    }

    private static string[]? Files(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;

    private void OnDrag(object sender, DragEventArgs e)
    {
        if (Files(e) == null) return; // autre chose qu'un fichier : l'île décide (rien)
        e.Effects = RemoteOutbox.Available ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (_dragging) return;
        _dragging = true;
        Highlight(true);
        Refresh();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _dragging = false;
        Highlight(false);
        if (Files(e) is not { Length: > 0 } files) return;
        e.Handled = true; // jamais dans l'étagère par-dessous : l'utilisateur a visé le téléphone
        if (!RemoteOutbox.Available)
        {
            Refresh();
            return;
        }
        _sentCount = RemoteOutbox.Add(files.Where(File.Exists));
        _sentUntil = DateTime.UtcNow + SentShown;
        Refresh();
    }

    private static readonly TimeSpan SentShown = TimeSpan.FromSeconds(3);

    /// <summary>Pendant un glisser : liseré d'accent ; sinon la bordure irisée de la tuile.</summary>
    private void Highlight(bool on)
    {
        if (on) SetResourceReference(BorderBrushProperty, AppearanceService.AccentBrushKey);
        else SetResourceReference(BorderBrushProperty, OpalTheme.TileRimKey);
    }

    protected override void Update()
    {
        var remote = SettingsStore.Current.Remote;
        var transfers = RemoteTransfers.Snapshot();
        var overall = RemoteTransfers.Overall(transfers);
        int waiting = RemoteOutbox.Count;
        string? value = null;
        string line;
        bool active = _dragging || overall != null || DateTime.UtcNow < _sentUntil;
        if (_dragging) line = Loc.T(RemoteOutbox.Available ? "tiles.phone.drop" : "tiles.phone.drop.off");
        else if (DateTime.UtcNow < _sentUntil) line = _sentCount > 0 ? Loc.T("tiles.phone.sent") : Loc.T("tiles.phone.drop.off");
        else if (overall is { } progress)
        {
            value = $"{Math.Round(progress.Progress * 100):0} %";
            line = Loc.T(progress.Incoming ? "tiles.phone.receiving" : "tiles.phone.sending");
        }
        else if (RemoteControl.State == RemoteControl.Status.NoPrivateNetwork) line = Loc.T("tiles.phone.public");
        else if (remote.Devices.Count == 0) line = Loc.T("tiles.phone.none");
        else if (waiting > 0 && remote.FileTransfer) line = waiting == 1 ? Loc.T("tiles.phone.waiting.one") : Loc.F("tiles.phone.waiting.many", waiting);
        else line = remote.Devices.Count == 1 ? Loc.T("tiles.phone.linked.one") : Loc.F("tiles.phone.linked.many", remote.Devices.Count);
        SetTone(Icons.Smartphone, active ? ToolTone.Accent : ToolTone.Idle);
        SetTexts(value, line);
        if (_zone != null && _zoneGlyph != null)
        {
            bool canDrop = remote.FileTransfer && remote.Devices.Count > 0;
            if (_dragging || DateTime.UtcNow < _sentUntil)
            {
                _zone.SetResourceReference(Shape.StrokeProperty, AppearanceService.AccentBrushKey);
                _zoneGlyph.SetResourceReference(TextBlock.ForegroundProperty, AppearanceService.AccentGradientKey);
                _zoneGlyph.Text = DateTime.UtcNow < _sentUntil && _sentCount > 0 ? Icons.Check : Icons.FileDown;
            }
            else
            {
                _zone.SetResourceReference(Shape.StrokeProperty, Paint.Key(canDrop ? InkRole.TextTertiary : InkRole.TextFaint));
                _zoneGlyph.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(canDrop ? InkRole.TextSecondary : InkRole.TextFaint));
                _zoneGlyph.Text = Icons.FileDown;
            }
        }
    }
}
