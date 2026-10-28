using System.Globalization;
using System.IO;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Enregistre une barre comme « appbar » Windows : la zone de travail rétrécit, donc les fenêtres maximisées
/// s'arrêtent avant elle. Le dock (mode Fixe) réserve le bas de l'écran principal, la barre de menus le haut.
/// Le handle est noté sur disque (un fichier par barre) pour pouvoir libérer la zone même si l'app est tuée
/// (voir <see cref="ShellGuard"/>).
/// </summary>
internal sealed class AppBar
{
    public const uint EdgeTop = 1;
    public const uint EdgeBottom = ABE_BOTTOM;
    private readonly IntPtr _window;
    private readonly string _handleFile;
    private bool _registered;

    /// <summary>Dock : bas de l'écran principal par défaut (bord et écran changeables, 5I).</summary>
    public AppBar(IntPtr window) : this(window, ABE_BOTTOM, AppPaths.AppBarHandle, "DynamicIsland.Dock.AppBar")
    {
    }

    public AppBar(IntPtr window, uint edge, string handleFile, string messageName)
    {
        _window = window;
        Edge = edge;
        _handleFile = handleFile;
        CallbackMessage = RegisterWindowMessage(messageName);
    }

    /// <summary>Bord réservé (<see cref="EdgeTop"/> ou <see cref="EdgeBottom"/>) ; pris en compte à la réservation suivante.</summary>
    public uint Edge { get; set; }

    /// <summary>Message envoyé à la fenêtre quand la barre des tâches ou une autre appbar bouge.</summary>
    public uint CallbackMessage { get; }

    public bool IsRegistered => _registered;

    /// <summary>
    /// Réserve une bande de <paramref name="sizePx"/> pixels sur le bord de l'écran donné (l'écran principal par défaut) ;
    /// renvoie la zone obtenue.
    /// </summary>
    public RECT ReserveRect(int sizePx, RECT? screen = null)
    {
        var bounds = screen ?? ScreenInfo.Primary().Bounds;
        var data = NewAppBarData(_window);
        if (!_registered)
        {
            data.uCallbackMessage = CallbackMessage;
            SHAppBarMessage(ABM_NEW, ref data);
            _registered = true;
            SaveHandle();
        }

        data.uEdge = Edge;
        bool top = Edge == EdgeTop;
        data.rc = new RECT
        {
            Left = bounds.Left,
            Right = bounds.Right,
            Top = top ? bounds.Top : bounds.Bottom - sizePx,
            Bottom = top ? bounds.Top + sizePx : bounds.Bottom,
        };
        SHAppBarMessage(ABM_QUERYPOS, ref data); // Windows ajuste (ex. au-dessus de la barre des tâches)
        if (top) data.rc.Bottom = data.rc.Top + sizePx;
        else data.rc.Top = data.rc.Bottom - sizePx;
        SHAppBarMessage(ABM_SETPOS, ref data);
        return data.rc;
    }

    public void Release()
    {
        if (!_registered) return;
        var data = NewAppBarData(_window);
        SHAppBarMessage(ABM_REMOVE, ref data);
        _registered = false;
        try { File.Delete(_handleFile); }
        catch (IOException) { }
    }

    /// <summary>Libère les zones d'une session précédente tuée sans avoir pu le faire (dock et barre de menus).</summary>
    public static void RemoveStale()
    {
        foreach (var file in new[] { AppPaths.AppBarHandle, AppPaths.MenuBarHandle })
        {
            if (!File.Exists(file)) continue;
            if (long.TryParse(File.ReadAllText(file), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                var handle = new IntPtr(value);
                if (!IsWindow(handle))
                {
                    var data = NewAppBarData(handle);
                    SHAppBarMessage(ABM_REMOVE, ref data);
                }
            }
            File.Delete(file);
        }
    }

    private void SaveHandle()
    {
        try
        {
            AppPaths.EnsureDataFolder();
            File.WriteAllText(_handleFile, _window.ToInt64().ToString(CultureInfo.InvariantCulture));
        }
        catch (IOException ex)
        {
            Log.Error("Appbar : sauvegarde du handle", ex);
        }
    }
}
