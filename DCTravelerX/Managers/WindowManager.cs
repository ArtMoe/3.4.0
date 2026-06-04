using System.Linq;
using Dalamud.Interface.Windowing;
using DCTravelerX.GameUi;

namespace DCTravelerX.Managers;

public class WindowManager
{
    public static WindowSystem WindowSystem { get; } = new("DCTravelerX");

    internal static WorldSelectorAddon? WorldSelector { get; private set; }

    internal static void Init()
    {
        WorldSelector = new WorldSelectorAddon
        {
            InternalName = "DCTravelerXWorldSelector",
            Title        = "超域传送",
            Subtitle     = "",
        };

        InternalWindows.Init();

        Service.UIBuilder.Draw += DrawWindows;
    }

    private static void DrawWindows()
    {
        using var font = FontManager.UIFont.Push();

        WindowSystem.Draw();
    }

    public static bool AddWindow(Window? window)
    {
        if (window == null) return false;

        var addedWindows = WindowSystem.Windows;
        if (addedWindows.Contains(window) || addedWindows.Any(x => x.WindowName == window.WindowName))
            return false;

        WindowSystem.AddWindow(window);
        return true;
    }

    public static bool RemoveWindow(Window? window)
    {
        if (window == null) return false;

        var addedWindows = WindowSystem.Windows;
        if (!addedWindows.Contains(window)) return false;

        WindowSystem.RemoveWindow(window);
        return true;
    }

    public static T? Get<T>() where T : Window
        => WindowSystem?.Windows.FirstOrDefault(x => x.GetType() == typeof(T)) as T;

    public static void OpenDcSelectWindow() =>
        DcGroupSelectorAddon.Show();

    internal static void Uninit()
    {
        Service.UIBuilder.Draw -= DrawWindows;

        InternalWindows.Uninit();

        WorldSelector?.Dispose();
        WorldSelector = null;

        WindowSystem.RemoveAllWindows();
    }

    private static class InternalWindows
    {
        internal static void Init()
        {
            // MessageBoxWindow 仍在 ImGui WindowSystem 中（原生消息框未迁移）
        }

        internal static void Uninit()
        {
        }
    }
}