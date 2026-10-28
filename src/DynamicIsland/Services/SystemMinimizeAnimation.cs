using System.IO;
using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <summary>
/// Animation de réduction / agrandissement de Windows. Coupée pendant que le dock fait l'effet Génie (sinon
/// les deux animations se superposent), puis remise à la fermeture, après un plantage (gardien) ou au
/// lancement suivant. Le changement n'est pas écrit dans le profil Windows : un redémarrage la remet aussi.
/// </summary>
public static class SystemMinimizeAnimation
{
    private const uint SPI_GETANIMATION = 0x0048;
    private const uint SPI_SETANIMATION = 0x0049;
    private const uint SPIF_SENDCHANGE = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    private struct ANIMATIONINFO
    {
        public uint cbSize;
        public int iMinAnimate;
    }

    /// <summary>Coupée par CE processus (un rendu --snapshot lancé à côté ne doit jamais y toucher).</summary>
    private static bool _disabledHere;

    public static void Disable()
    {
        if (_disabledHere || !IsEnabled()) return; // déjà coupée, par nous ou par l'utilisateur
        AppPaths.EnsureDataFolder();
        File.WriteAllText(AppPaths.MinimizeAnimationState, "1"); // la trace d'abord : le gardien saura la remettre
        Set(false);
        _disabledHere = true;
    }

    public static void Restore()
    {
        if (!_disabledHere) return;
        _disabledHere = false;
        Set(true);
        File.Delete(AppPaths.MinimizeAnimationState);
    }

    /// <summary>Au lancement et dans le gardien : une session précédente l'a coupée sans la remettre.</summary>
    public static void RecoverIfNeeded()
    {
        if (!File.Exists(AppPaths.MinimizeAnimationState)) return;
        Set(true);
        File.Delete(AppPaths.MinimizeAnimationState);
    }

    private static bool IsEnabled()
    {
        var info = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>() };
        return SystemParametersInfo(SPI_GETANIMATION, info.cbSize, ref info, 0) && info.iMinAnimate != 0;
    }

    private static void Set(bool enabled)
    {
        var info = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>(), iMinAnimate = enabled ? 1 : 0 };
        SystemParametersInfo(SPI_SETANIMATION, info.cbSize, ref info, SPIF_SENDCHANGE);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref ANIMATIONINFO info, uint flags);
}
