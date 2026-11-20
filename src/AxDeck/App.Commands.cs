using System.IO;
using System.Windows;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// What a second copy of AxDeck asks the running one to do: an axdeck:// address, or a command line such as "--page music".
// The second copy writes the command into the data folder (AppCommandInbox) and rings its own bell; the running app reads the
// command and does it. Nothing is sent over a network, and nothing leaves this machine.
public partial class App
{
    // The command this copy was started with, if any: the first axdeck:// address among the arguments, or the first command
    // line switch that names one.
    private static (AppCommandKind Kind, string? Argument) StartCommand(string[] args)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith(AppCommand.Scheme + "://", StringComparison.OrdinalIgnoreCase)) continue;
            var fromUrl = AppCommand.FromUrl(arg);
            if (fromUrl.Kind != AppCommandKind.None) return fromUrl;
        }
        return AppCommand.FromArguments(args);
    }

    // The start's own command is handed over to the running copy, and this copy goes away.
    private static void HandOverCommand(string[] args, string folder)
    {
        var (kind, argument) = StartCommand(args);
        if (kind == AppCommandKind.None) return;
        try { AppCommandInbox.Write(folder, kind, argument); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // The addresses are registered for this user on the first start, and point at the executable that was started. An
    // executable moved since (a new version installed elsewhere) is put in place again here.
    private void RegisterProtocol()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (executable is not null && !Native.ProtocolRegistration.PointsAt(executable)) Native.ProtocolRegistration.Register();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    // A second copy wrote a command and rang the bell: it is read and run here.
    private void ReadCommand()
    {
        if (Exiting) return;
        if (AppCommandInbox.Read(Store.DirectoryPath) is not { } command) return;
        RunCommand(command.Kind, command.Argument);
    }

    // Runs a command from the address, from the command line or from the command palette.
    internal void RunCommand(AppCommandKind kind, string? argument)
    {
        switch (kind)
        {
            case AppCommandKind.OpenSettings:
                SetUserHidden(false);
                OpenSettings();
                break;
            case AppCommandKind.OpenPage when AppCommand.Page(argument) is { } page:
                SetUserHidden(false);
                Deck.CommandShow(page);
                break;
            case AppCommandKind.Focus:
                SetUserHidden(false);
                FocusStart(AppCommand.Minutes(argument) ?? FocusTimer.Presets[1]);
                break;
            case AppCommandKind.FocusProject when argument is not null && Guid.TryParse(argument, out var focusProject):
                SetUserHidden(false);
                FocusStartProject(focusProject);
                break;
            case AppCommandKind.FocusProject:
                SetUserHidden(false);
                SystemNotice(Loc.T("项目尚未登记", "Project not registered"),
                    Loc.T("这个专注命令没有有效的项目编号。", "This focus command has no valid project id."), 1);
                break;
            case AppCommandKind.Project when Projects.Find(argument) is { } project:
                SetUserHidden(false);
                OpenProject(project.Id);
                break;
            case AppCommandKind.Project:
                SetUserHidden(false);
                SystemNotice(Loc.T("项目尚未登记", "Project not registered"),
                    Loc.T($"“{argument}”还没有在设置中登记，因此没有打开。", $"\"{argument}\" has not been registered in the settings, so it was not opened."), 1);
                break;
            case AppCommandKind.RestoreProject when Projects.Find(argument) is { } restore:
                SetUserHidden(false);
                RestoreProject(restore.Id);
                break;
            case AppCommandKind.RestoreProject:
                SetUserHidden(false);
                SystemNotice(Loc.T("项目尚未登记", "Project not registered"),
                    Loc.T($"“{argument}”还没有在设置中登记，因此没有恢复。", $"\"{argument}\" has not been registered in the settings, so it was not restored."), 1);
                break;
            case AppCommandKind.Search:
                SetUserHidden(false);
                Deck.PaletteOpen(argument);
                break;
            case AppCommandKind.Refresh:
                Deck.SyncNow();
                break;
            case AppCommandKind.Show:
                SetUserHidden(false);
                Deck.OpenFromHotkey();
                break;
            case AppCommandKind.Hide:
                SetUserHidden(true);
                break;
        }
    }
}
