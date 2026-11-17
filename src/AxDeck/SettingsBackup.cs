using System.IO;
using System.Text.Json;
using AxDeck.Core;
using Microsoft.Win32;

namespace AxDeck;

public sealed partial class SettingsWindow
{
    // "备份与迁移": write the settings to a file to carry to another PC, and read one back. What a file carries and what it
    // leaves behind is SettingsTransfer's; this page only asks where the file goes and says what happened.
    private void Backup()
    {
        SectionTitle(Loc.T("备份与迁移", "Back up and move"), help: () => Loc.T(
            "导出的文件带着你选过的所有设置：页面、颜色、主题、快捷键、应用列表与价格。显示器、显卡、无线设备这些只属于这台电脑的选择留在本机，导入时不会覆盖对方机器的选择；API 密钥不在文件里（它们单独加密保存），导入后需要在对应的应用里重新填写。",
            "An exported file holds every setting you chose: pages, colors, themes, shortcuts, the app list and prices. Choices that name this PC alone — displays, graphics adapter, wireless devices — stay behind and are not overwritten on the other machine. API keys are not in the file (they are kept separately, encrypted), so a row needs its key entered again there."));
        Group(
            Row(Loc.T("导出设置", "Export settings"), ActionButton(Loc.T("导出…", "Export…"), ExportSettings),
                help: () => Loc.T("把当前设置保存成一个 .json 文件。文件里没有密钥，也没有与这台电脑绑定的显示器、显卡和设备选择。",
                    "Saves the current settings as a .json file. It holds no keys, and none of the display, graphics adapter or device choices bound to this PC.")),
            Row(Loc.T("导入设置", "Import settings"), ActionButton(Loc.T("导入…", "Import…"), ImportSettings),
                help: () => Loc.T("读取一个导出的设置文件，替换当前的设置。导入前会先问一次；显示器、显卡与设备选择仍用这台电脑的。",
                    "Reads an exported settings file and replaces the current settings. It asks first; the display, graphics adapter and device choices stay this PC's.")));
    }

    private void ExportSettings()
    {
        var dialog = new SaveFileDialog
        {
            Title = Loc.T("导出 AxDeck 设置", "Export AxDeck settings"),
            FileName = $"axdeck-settings-{DateTime.Now:yyyyMMdd}.json",
            DefaultExt = ".json", Filter = Loc.T("设置文件 (*.json)|*.json", "Settings file (*.json)|*.json"), AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var text = SettingsTransfer.Export(S, ApplicationVersion.Current, DateTimeOffset.Now);
            File.WriteAllText(dialog.FileName, text);
            SaveStatus("");
            Confirm(Loc.T("已导出", "Exported"), Loc.T($"设置已保存到：\n{dialog.FileName}\n\n把它拷到另一台电脑，用「导入设置」读取即可。", $"The settings were saved to:\n{dialog.FileName}\n\nCopy it to the other PC and read it with \"Import settings\"."),
                Loc.T("好", "OK"), danger: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { SaveStatus(Loc.T("无法导出设置：", "The settings could not be exported: ") + ex.Message, true); }
    }

    private void ImportSettings()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("导入 AxDeck 设置", "Import AxDeck settings"),
            Filter = Loc.T("设置文件 (*.json)|*.json|所有文件 (*.*)|*.*", "Settings file (*.json)|*.json|All files (*.*)|*.*"), CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        DeckSettings imported; Version version;
        try { (imported, version) = SettingsTransfer.Import(File.ReadAllText(dialog.FileName), ApplicationVersion.Parse(ApplicationVersion.Current)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SaveStatus("", true);
            Confirm(Loc.T("无法读取", "Could not read"), Loc.T($"这个文件不是 AxDeck 导出的设置，或者已经损坏：\n{ex.Message}", $"This file is not an AxDeck settings export, or it is damaged:\n{ex.Message}"),
                Loc.T("好", "OK"), danger: false);
            return;
        }
        // The file's own version is named, so a file from an older AxDeck is known to have been upgraded on the way in.
        string from = version > new Version(0, 0, 0) ? Loc.T($"来自 AxDeck {version.ToString(3)} 的设置。", $"Settings from AxDeck {version.ToString(3)}.") : Loc.T("来自更早版本的设置。", "Settings from an earlier version.");
        if (!Confirm(Loc.T("导入设置", "Import settings"),
            Loc.T($"{from}\n\n当前的设置会被它替换；显示器、显卡与无线设备的选择仍保留这台电脑的。这一步不能用「恢复默认」以外的办法撤销。",
                $"{from}\n\nThe current settings are replaced by them; this PC's display, graphics adapter and wireless device choices are kept. This cannot be undone except by restoring defaults."),
            Loc.T("导入", "Import"))) return;
        // The imported settings replace the current ones whole, which is what UpdateSettings takes; only this PC's own choices
        // are filled back in first.
        _app.UpdateSettings(SettingsTransfer.KeepLocal(imported, S));
        // The language, theme and font may have changed with the file, so the window is built again in them.
        RebuildTheme();
    }
}