using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DCTravelerX.Infos;
using DCTravelerX.Managers;
using DCTravelerX.Windows.MessageBox;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;

namespace DCTravelerX.GameUi;

internal unsafe class WorldSelectorAddon : NativeAddon, IDisposable
{
    private static readonly string[] DcStates = { "通畅", "热门", "火爆?" };

    // 上次选择（按模式分别存储）
    private static int LastSourceAreaIndex;
    private static int LastSourceServerIndex;
    private static int LastTargetAreaIndex;
    private static int LastTargetServerIndex;

    private List<Area> pendingAreas          = [];
    private int        pendingAreaIndex;
    private int        pendingServerIndex;
    private bool       pendingIsBack;
    private bool       pendingIsSourceMode;
    private Group?     pendingSourceGroup;

    private int currentAreaIndex  = -1;
    private int currentServerIndex = -1;

    // UI 节点引用 — 每次 OnSetup 都会重新创建
    private TextNode?           titleLabel;
    private VerticalListNode?   areaListNode;
    private VerticalListNode?   serverListNode;
    private TextButtonNode?     confirmButton;

    private TaskCompletionSource<SelectWorldResult?>? selectWorldTaskCompletionSource;

    private const float ListWidth  = 140f;
    private const float ListHeight = 250f;
    private const float ItemHeight = 24f;

    // ─────────── OnSetup ───────────

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        SetWindowSize(320f, 370f);

        titleLabel  = null;
        areaListNode = null;
        serverListNode = null;
        confirmButton = null;

        currentAreaIndex  = pendingAreaIndex;
        currentServerIndex = pendingServerIndex;

        // ── 大区按钮 ──
        var areaButtons = new List<NodeBase>();
        for (var i = 0; i < pendingAreas.Count; i++)
        {
            var area     = pendingAreas[i];
            var index    = i;
            var selected = i == currentAreaIndex;

            var displayName = !pendingIsSourceMode && area.State >= 0 && area.State < DcStates.Length
                ? $"{area.AreaName}  [{DcStates[area.State]}]"
                : area.AreaName;

            areaButtons.Add(new ListButtonNode
            {
                String   = displayName,
                Height   = ItemHeight,
                Selected = selected,
                OnClick  = () => OnAreaSelected(index),
            });
        }

        // ── 服务器按钮 ──
        var serverButtons = new List<NodeBase>();
        if (currentAreaIndex >= 0 && currentAreaIndex < pendingAreas.Count)
        {
            var groupList = pendingAreas[currentAreaIndex].GroupList;
            for (var i = 0; i < groupList.Count; i++)
            {
                var group    = groupList[i];
                var index    = i;
                var selected = i == currentServerIndex;

                serverButtons.Add(new ListButtonNode
                {
                    String   = group.GroupName,
                    Height   = ItemHeight,
                    Selected = selected,
                    OnClick  = () => OnServerSelected(index),
                });
            }
        }

        // ── 布局 ──
        AddNode(new VerticalListNode
        {
            Size       = ContentSize,
            Position   = ContentStartPosition,
            FitWidth   = true,
            ItemSpacing = 4.0f,
            Anchor     = VerticalListAnchor.Top,
            InitialNodes =
            [
                // 标题
                titleLabel = new TextNode
                {
                    Height          = 24.0f,
                    FontSize        = 14,
                    String          = pendingIsSourceMode ? "选择当前服务器" : "选择目标服务器",
                    AlignmentType   = AlignmentType.Center,
                    TextColor       = ColorHelper.GetColor(3),
                    TextOutlineColor = ColorHelper.GetColor(7),
                },
                // 列表区域（水平排列）
                new HorizontalListNode
                {
                    Height     = ListHeight,
                    FitHeight  = true,
                    ItemSpacing = 8.0f,
                    Alignment  = HorizontalListAnchor.Left,
                    InitialNodes =
                    [
                        // 大区列表
                        new VerticalListNode
                        {
                            Width      = ListWidth,
                            FitWidth   = true,
                            ItemSpacing = 2.0f,
                            Anchor     = VerticalListAnchor.Top,
                            InitialNodes =
                            [
                                new TextNode
                                {
                                    Height          = 20.0f,
                                    FontSize        = 12,
                                    String          = "大区",
                                    AlignmentType   = AlignmentType.Center,
                                    TextColor       = ColorHelper.GetColor(2),
                                    TextOutlineColor = ColorHelper.GetColor(7),
                                },
                                new HorizontalLineNode { Height = 2.0f },
                                areaListNode = new VerticalListNode
                                {
                                    Height          = ListHeight - 24.0f,
                                    FitWidth        = true,
                                    ItemSpacing     = 2.0f,
                                    Anchor          = VerticalListAnchor.Top,
                                    ClipListContents = true,
                                    InitialNodes    = areaButtons,
                                },
                            ],
                        },
                        // 服务器列表
                        new VerticalListNode
                        {
                            Width      = ListWidth,
                            FitWidth   = true,
                            ItemSpacing = 2.0f,
                            Anchor     = VerticalListAnchor.Top,
                            InitialNodes =
                            [
                                new TextNode
                                {
                                    Height          = 20.0f,
                                    FontSize        = 12,
                                    String          = "服务器",
                                    AlignmentType   = AlignmentType.Center,
                                    TextColor       = ColorHelper.GetColor(2),
                                    TextOutlineColor = ColorHelper.GetColor(7),
                                },
                                new HorizontalLineNode { Height = 2.0f },
                                serverListNode = new VerticalListNode
                                {
                                    Height          = ListHeight - 24.0f,
                                    FitWidth        = true,
                                    ItemSpacing     = 2.0f,
                                    Anchor          = VerticalListAnchor.Top,
                                    ClipListContents = true,
                                    InitialNodes    = serverButtons,
                                },
                            ],
                        },
                    ],
                },
                // 按钮区域
                new HorizontalListNode
                {
                    Height          = 28.0f,
                    FitHeight       = true,
                    ItemSpacing     = 16.0f,
                    FirstItemSpacing = 36.0f,
                    Alignment       = HorizontalListAnchor.Left,
                    InitialNodes    =
                    [
                        confirmButton = new TextButtonNode
                        {
                            Width  = 100f,
                            String = pendingIsBack ? "返回" : "传送",
                            OnClick = OnConfirmClicked,
                        },
                        new TextButtonNode
                        {
                            Width   = 100f,
                            String  = "取消",
                            OnClick = OnCancelClicked,
                        },
                    ],
                },
            ],
        });
    }

    protected override void OnShow(AtkUnitBase* addon) { }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        titleLabel     = null;
        areaListNode   = null;
        serverListNode = null;
        confirmButton  = null;
    }

    // ─────────── 选择回调 ───────────

    private void OnAreaSelected(int index)
    {
        currentAreaIndex  = index;
        currentServerIndex = 0;

        if (pendingIsSourceMode)
        {
            LastSourceAreaIndex   = currentAreaIndex;
            LastSourceServerIndex = currentServerIndex;
        }
        else
        {
            LastTargetAreaIndex   = currentAreaIndex;
            LastTargetServerIndex = currentServerIndex;
        }

        // 更新大区选中态
        if (areaListNode != null)
        {
            var i = 0;
            foreach (var node in areaListNode.GetNodes<ListButtonNode>())
            {
                node.Selected = i == currentAreaIndex;
                i++;
            }
        }

        // 重建服务器列表
        if (serverListNode != null)
        {
            serverListNode.Clear();

            if (currentAreaIndex >= 0 && currentAreaIndex < pendingAreas.Count)
            {
                var groupList = pendingAreas[currentAreaIndex].GroupList;
                for (var i = 0; i < groupList.Count; i++)
                {
                    var idx    = i;
                    var sel    = i == currentServerIndex;
                    var group  = groupList[i];

                    serverListNode.AddNode(new ListButtonNode
                    {
                        String   = group.GroupName,
                        Height   = ItemHeight,
                        Selected = sel,
                        OnClick  = () => OnServerSelected(idx),
                    });
                }
            }
        }
    }

    private void OnServerSelected(int index)
    {
        currentServerIndex = index;

        if (pendingIsSourceMode)
            LastSourceServerIndex = currentServerIndex;
        else
            LastTargetServerIndex = currentServerIndex;

        if (serverListNode != null)
        {
            var i = 0;
            foreach (var node in serverListNode.GetNodes<ListButtonNode>())
            {
                node.Selected = i == currentServerIndex;
                i++;
            }
        }
    }

    private void OnConfirmClicked()
    {
        Group? selectedGroup = null;
        if (currentAreaIndex >= 0 && currentAreaIndex < pendingAreas.Count)
        {
            if (currentServerIndex >= 0 && currentServerIndex < pendingAreas[currentAreaIndex].GroupList.Count)
                selectedGroup = pendingAreas[currentAreaIndex].GroupList[currentServerIndex];
        }

        var tcs = selectWorldTaskCompletionSource;

        if (pendingIsSourceMode)
        {
            // 返回模式：选源服务器，直接确认
            var result = new SelectWorldResult { Source = selectedGroup };
            tcs?.TrySetResult(result);
            Close();
        }
        else if (selectedGroup != null)
        {
            // 传送模式：弹出确认对话框
            WorldSelectorHelper.ConfirmTravelAsync(
                pendingSourceGroup, selectedGroup, tcs, this
            );
        }
    }

    private void OnCancelClicked()
    {
        selectWorldTaskCompletionSource?.TrySetResult(null);
        Close();
    }

    protected override void OnHide(AtkUnitBase* addon)
    {
        selectWorldTaskCompletionSource?.TrySetResult(null);
    }

    // ─────────── 公共 API ───────────

    public Task<SelectWorldResult?> OpenTravelWindow(
        bool                   showSourceWorld,
        bool                   showTargetWorld,
        bool                   isBack,
        IEnumerable<Area>      areas,
        Group?                 sourceGroup = null,
        Group?                 targetGroup = null)
    {
        selectWorldTaskCompletionSource = new TaskCompletionSource<SelectWorldResult?>(TaskCreationOptions.RunContinuationsAsynchronously);

        pendingAreas      = areas.ToList();
        pendingIsBack     = isBack;
        pendingIsSourceMode = showSourceWorld && !showTargetWorld;
        pendingSourceGroup = sourceGroup;

        var targetIndexGroup = pendingIsSourceMode ? sourceGroup : targetGroup;
        pendingAreaIndex = pendingAreas.FindIndex(x => x.AreaId == targetIndexGroup?.AreaId);

        if (pendingAreaIndex == -1)
        {
            if (pendingIsSourceMode)
            {
                pendingAreaIndex   = LastSourceAreaIndex;
                pendingServerIndex = LastSourceServerIndex;
            }
            else
            {
                pendingAreaIndex   = LastTargetAreaIndex;
                pendingServerIndex = LastTargetServerIndex;
            }

            if (pendingAreaIndex < 0 || pendingAreaIndex >= pendingAreas.Count)
                pendingAreaIndex = 0;

            if (pendingAreaIndex >= 0 && pendingAreaIndex < pendingAreas.Count)
            {
                if (pendingServerIndex < 0 || pendingServerIndex >= pendingAreas[pendingAreaIndex].GroupList.Count)
                    pendingServerIndex = 0;
            }
            else
            {
                pendingServerIndex = 0;
            }
        }
        else
        {
            pendingServerIndex = pendingAreas[pendingAreaIndex].GroupList.FindIndex(x => x.GroupID == targetIndexGroup?.GroupID);
            if (pendingServerIndex == -1) pendingServerIndex = 0;
        }

        Open();
        return selectWorldTaskCompletionSource.Task;
    }

    public new void Dispose()
    {
        selectWorldTaskCompletionSource?.TrySetResult(null);
        Close();
    }
}

internal static class WorldSelectorHelper
{
    internal static async Task ConfirmTravelAsync(
        Group?                                   sourceGroup,
        Group                                    targetGroup,
        TaskCompletionSource<SelectWorldResult?>? tcs,
        WorldSelectorAddon                       addon)
    {
        var message = sourceGroup != null
            ? BuildConfirmMessage(sourceGroup, targetGroup)
            : $"确认超域传送至 {targetGroup.AreaName} - {targetGroup.GroupName}";

        var result = await MessageBoxWindow.Show(
            WindowManager.WindowSystem,
            "超域旅行",
            message,
            MessageBoxType.YesNo
        );

        if (result == MessageBoxResult.Yes)
        {
            tcs?.TrySetResult(new SelectWorldResult { Target = targetGroup });
            addon.Close();
        }
    }

    private static string BuildConfirmMessage(Group source, Group target)
    {
        var sameArea = string.Equals(source.AreaName, target.AreaName, StringComparison.Ordinal);

        if (sameArea)
            return $"确认超域传送\n{source.AreaName} - {source.GroupName} > {target.GroupName}";

        return $"确认超域传送\n{source.AreaName} - {source.GroupName} > {target.AreaName} - {target.GroupName}";
    }
}