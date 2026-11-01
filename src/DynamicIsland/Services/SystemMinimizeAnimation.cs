using System.IO;
using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <summary>
/// Animation de réduction / agrandissement de Windows. Coupée pendant que le dock fait l'effet Génie et le temps d'un échange
/// de Stage Manager (sinon les deux animations se superposent), puis remise à la fermeture, après un plantage (gardien) ou au
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

    /// <summary>
    /// Qui la veut coupée : « dock » (effet Génie, tant qu'il est actif) et « stage » (échange de Stage Manager, une demi-seconde).
    /// Elle n'est remise que quand plus personne ne la demande (l'un ne la rallume pas sous les pieds de l'autre).
    /// </summary>
    private static readonly HashSet<string> Holders = [];

    public const string Dock = "dock";
    public const string Stage = "stage";

    /// <param name="notify">Prévenir les applis (WM_SETTINGCHANGE diffusé). Faux pour une coupure brève : la diffusion attend toutes
    /// les fenêtres (une appli figée la ralentirait) et Windows applique le réglage tout de suite sans elle.</param>
    public static void Disable(string owner = Dock, bool notify = true)
    {
        Holders.Add(owner);
        if (_disabledHere || !IsEnabled()) return; // déjà coupée, par nous ou par l'utilisateur
        AppPaths.EnsureDataFolder();
        File.WriteAllText(AppPaths.MinimizeAnimationState, "1"); // la trace d'abord : le gardien saura la remettre
        Set(false, notify);
        _disabledHere = true;
    }

    public static void Restore(string owner = Dock, bool notify = true)
    {
        Holders.Remove(owner);
        if (!_disabledHere || Holders.Count > 0) return;
        _disabledHere = false;
        Set(true, notify);
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

    private static void Set(bool enabled, bool notify = true)
    {
        var info = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>(), iMinAnimate = enabled ? 1 : 0 };
        SystemParametersInfo(SPI_SETANIMATION, info.cbSize, ref info, notify ? SPIF_SENDCHANGE : 0);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref ANIMATIONINFO info, uint flags);
}
