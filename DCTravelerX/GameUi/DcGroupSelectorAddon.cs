using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using DCTravelerX.Helpers;
using DCTravelerX.Infos;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using KamiToolKit.Premade.Node.Simple;

namespace DCTravelerX.GameUi;

internal unsafe class DcGroupSelectorAddon : NativeAddon, IDisposable
{
    private const float WindowWidth  = 600f;
    private const float WindowHeight = 320f;
    private const float ColumnSpacing = 8f;
    private const float RowHeight     = 24f;

    private VerticalListNode?                         rootNode;
    private List<Area>                                areas = [];
    private readonly List<SimpleNineGridNode>         overlayNodes   = [];
    private readonly List<IconImageNode>              bgImageNodes   = [];
    private readonly Dictionary<IconImageNode, uint>  originalIconIds = [];
    private static DcGroupSelectorAddon? CurrentInstance;

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        SetWindowSize(WindowWidth, WindowHeight);

        var screenSize = new Vector2(
            FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance()->Width,
            FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance()->Height
        );
        SetWindowPosition(new Vector2(
            (screenSize.X / 2f) - (WindowWidth / 2f),
            (screenSize.Y / 2f) - (WindowHeight / 2f)
        ));

        areas = DCTravelClient.Areas
                              .OrderBy(x => x.Key)
                              .Select(x => x.Value.Area)
                              .ToList();

        rootNode = new VerticalListNode
        {
            Size         = ContentSize,
            Position     = ContentStartPosition,
            ItemSpacing   = 8.0f,
            Anchor       = VerticalListAnchor.Top,
            FitWidth     = true,
        };

        if (Service.GameGui.GetAddonByName("_TitleMenu") == nint.Zero)
        {
            rootNode.AddNode(new TextNode
            {
                Width           = ContentSize.X,
                Height          = ContentSize.Y,
                String          = "必须在标题画面打开",
                AlignmentType   = AlignmentType.Center,
                FontSize        = 14,
                TextColor       = ColorHelper.GetColor(3),
                TextOutlineColor = ColorHelper.GetColor(7),
            });
            AddNode(rootNode);
            return;
        }

        if (areas.Count == 0)
        {
            rootNode.AddNode(new TextNode
            {
                Width           = ContentSize.X,
                Height          = ContentSize.Y,
                String          = "服务器信息加载失败",
                AlignmentType   = AlignmentType.Center,
                FontSize        = 14,
                TextColor       = ColorHelper.GetColor(3),
                TextOutlineColor = ColorHelper.GetColor(7),
            });
            AddNode(rootNode);
            return;
        }

        var columnsNode = new HorizontalListNode
        {
            Width      = ContentSize.X,
            Height     = ContentSize.Y,
            ItemSpacing = ColumnSpacing,
            Alignment  = HorizontalListAnchor.Left,
        };

        float columnWidth = (ContentSize.X - (ColumnSpacing * (areas.Count - 1))) / areas.Count;
        var   bgSize      = Math.Min(columnWidth, ContentSize.Y);
        float currentX    = 0;

        overlayNodes.Clear();
        bgImageNodes.Clear();
        originalIconIds.Clear();

        var useEasterEgg = Random.Shared.NextDouble() < 0.05;
        uint[] easterEggIcons = [234003u, 234742u];
        if (useEasterEgg)
        {
            Random.Shared.Shuffle(easterEggIcons);
            WindowNode?.SetTitle("河狸选择");
        }
        var easterEggIndex = 0;

        foreach (var area in areas)
        {
            uint iconId;
            if (useEasterEgg)
            {
                iconId = easterEggIcons[easterEggIndex % easterEggIcons.Length];
                easterEggIndex++;
            }
            else
            {
                iconId = area.AreaId switch
                {
                    1 => 234006u,
                    6 => 234001u,
                    7 => 234002u,
                    8 => 234022u,
                    _ => 234003u,
                };
            }

            var bgImage = new IconImageNode
            {
                IconId    = iconId,
                Size      = new Vector2(bgSize, bgSize),
                Position  = ContentStartPosition + new Vector2(currentX + (columnWidth - bgSize) / 2, ContentSize.Y - bgSize),
                FitTexture = true,
                Alpha     = 0.2f,
            };
            AddNode(bgImage);
            bgImageNodes.Add(bgImage);
            originalIconIds[bgImage] = iconId;

            var overlay = new SimpleNineGridNode
            {
                TexturePath         = "ui/uld/ListItemA.tex",
                TextureCoordinates  = new Vector2(0.0f, 0.0f),
                TextureSize         = new Vector2(64.0f, 22.0f),
                LeftOffset          = 16,
                RightOffset         = 16,
                TopOffset           = 8,
                BottomOffset        = 8,
                Size                = new Vector2(ContentSize.Y, columnWidth),
                Position            = ContentStartPosition + new Vector2(currentX, ContentSize.Y),
                MultiplyColor       = new Vector3(0, 0, 0),
                Alpha               = 1f,
                RotationDegrees     = -90f,
            };
            AddNode(overlay);
            overlayNodes.Add(overlay);

            var columnNode = CreateAreaColumn(area, columnWidth, overlay, bgImage, useEasterEgg);
            columnsNode.AddNode(columnNode);

            currentX += columnWidth + ColumnSpacing;
        }

        rootNode.AddNode(columnsNode);
        AddNode(rootNode);
    }

    private VerticalListNode CreateAreaColumn(Area area, float width, SimpleNineGridNode overlay, IconImageNode bgImage, bool useEasterEgg)
    {
        var columnNode = new VerticalListNode
        {
            Width      = width,
            Height     = ContentSize.Y,
            ItemSpacing = 0f,
            Anchor     = VerticalListAnchor.Top,
        };

        columnNode.CollisionNode.ShowClickableCursor = true;
        columnNode.CollisionNode.AddEvent(AtkEventType.MouseClick, () => OnAreaClicked(area.AreaName));
        columnNode.CollisionNode.AddEvent(AtkEventType.MouseOver, () =>
        {
            overlay.MultiplyColor = new Vector3(16, 16, 16);
            bgImage.Alpha         = 0.4f;
        });
        columnNode.CollisionNode.AddEvent(AtkEventType.MouseOut, () =>
        {
            overlay.MultiplyColor = new Vector3(0, 0, 0);
            bgImage.Alpha         = 0.2f;
        });

        var displayName = useEasterEgg && area.AreaName.Length > 0
            ? area.AreaName[..^1] + "狸"
            : area.AreaName;

        columnNode.AddNode(new TextNode
        {
            Width           = width,
            Height          = 32f,
            String          = displayName,
            AlignmentType   = AlignmentType.Center,
            FontSize        = 14,
            TextColor       = ColorHelper.GetColor(2),
            TextOutlineColor = ColorHelper.GetColor(7),
        });

        columnNode.AddNode(new HorizontalLineNode { Height = 2.0f, Width = width, ScaleX = 0.8f, OriginX = width / 2f });

        foreach (var group in area.GroupList)
        {
            columnNode.AddNode(new TextNode
            {
                Width           = width,
                Height          = RowHeight,
                String          = group.GroupName,
                AlignmentType   = AlignmentType.Center,
                FontSize        = 12,
                TextColor       = ColorHelper.GetColor(8),
                TextOutlineColor = ColorHelper.GetColor(7),
            });
        }

        return columnNode;
    }

    private static void OnAreaClicked(string areaName)
    {
        DcGroupSelectorHelper.SelectDcAndLoginAsync(areaName);
        CurrentInstance?.Close();
    }

    protected override void OnHide(AtkUnitBase* addon)
    {
        CurrentInstance = null;
        Service.AddonLifecycle.UnregisterListener(OnTitleMenuFinalize);
    }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        rootNode?.Dispose();
    }

    public static void Show()
    {
        if (CurrentInstance != null && CurrentInstance.IsOpen)
            return;

        var addon = new DcGroupSelectorAddon
        {
            InternalName = $"DCTravelerXDcGroupSelector_{Guid.NewGuid():N}",
            Title        = "大区选择",
        };

        CurrentInstance = addon;
        addon.Open();

        // 监听 _TitleMenu 销毁，离开标题界面时自动关闭
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "_TitleMenu", OnTitleMenuFinalize);
    }

    public new void Dispose()
    {
        Service.AddonLifecycle.UnregisterListener(OnTitleMenuFinalize);
        Close();
    }

    private static void OnTitleMenuFinalize(AddonEvent type, AddonArgs args)
    {
        CurrentInstance?.Close();
    }
}

internal static class DcGroupSelectorHelper
{
    internal static async Task SelectDcAndLoginAsync(string areaName)
    {
        try
        {
            await GameFunctions.SelectDCAndLogin(areaName, true);
        }
        catch (Exception ex)
        {
            await MessageBoxAddon.Show(
                "选择大区",
                $"大区切换失败:\n\n{ex.Message}"
            );
            Service.Log.Error(ex, "大区切换失败");
        }
    }
}
