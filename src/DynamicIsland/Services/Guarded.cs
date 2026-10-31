using System.Windows;

namespace DynamicIsland.Services;

/// <summary>
/// Correctif urgent 1 : un rappel venu de Windows (crochet SetWinEventHook, événement système SystemEvents, fil à part) ne
/// passe pas par le filet du thread de l'interface. Une exception y tue tout le processus (elle ne peut pas traverser le code
/// natif de Windows). Ces rappels passent donc par ici : erreur → le module fautif est arrêté (<see cref="ModuleGuard"/>), sinon
/// une ligne de journal ; l'île continue.
/// </summary>
public static class Guarded
{
    /// <summary>Exécute tout de suite, dans un try.</summary>
    public static void Run(Action action, string context)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Report(ex, context);
        }
    }

    /// <summary>Exécute sur le thread de l'interface (événements système levés sur leur propre fil), dans un try.</summary>
    public static void OnUi(Action action, string context)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        if (dispatcher.CheckAccess()) Run(action, context);
        else dispatcher.BeginInvoke(() => Run(action, context));
    }

    /// <summary>Erreur attrapée ailleurs : module arrêté (sur le thread de l'interface) ou journal.</summary>
    public static void Report(Exception ex, string context)
    {
        var dispatcher = Application.Current?.Dispatcher;
        void Handle()
        {
            if (!ModuleGuard.TryFail(ex)) Log.Error(context, ex);
        }
        if (dispatcher == null || dispatcher.CheckAccess()) Handle();
        else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(Handle);
    }
}
