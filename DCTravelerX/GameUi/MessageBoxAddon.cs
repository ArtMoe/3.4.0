using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using DCTravelerX.Windows.MessageBox;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;

namespace DCTravelerX.GameUi;

internal unsafe class MessageBoxAddon : NativeAddon, IDisposable
{
    private TextNode?        messageNode;
    private VerticalListNode? rootNode;

    private TaskCompletionSource<MessageBoxResult>? messageTaskCompletionSource;

    private MessageBoxType pendingType          = MessageBoxType.Ok;
    private string         pendingMessage        = string.Empty;

    private const float WindowWidth  = 400f;
    private const float ButtonWidth  = 100f;
    private const float ButtonHeight = 28f;

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        var windowHeight = 150f;
        SetWindowSize(WindowWidth, windowHeight);

        rootNode = new VerticalListNode
        {
            Size       = ContentSize,
            Position   = ContentStartPosition,
            ItemSpacing = 8.0f,
            Anchor     = VerticalListAnchor.Top,
            FitWidth   = true,
        };

        var messageHeight = ContentSize.Y - ButtonHeight - 16f;

        messageNode = new TextNode
        {
            Width           = ContentSize.X,
            Height          = messageHeight,
            String          = pendingMessage,
            AlignmentType   = AlignmentType.Center,
            FontSize        = 14,
            TextColor       = ColorHelper.GetColor(8),
            TextOutlineColor = ColorHelper.GetColor(7),
            TextFlags       = TextFlags.MultiLine | TextFlags.WordWrap,
        };
        rootNode.AddNode(messageNode);

        var buttonsNode = CreateButtonsForType(pendingType, ContentSize.X);
        rootNode.AddNode(buttonsNode);

        AddNode(rootNode);
    }

    private HorizontalListNode CreateButtonsForType(MessageBoxType type, float contentWidth)
    {
        var buttons = new List<TextButtonNode>();

        switch (type)
        {
            case MessageBoxType.Ok:
                buttons.Add(new TextButtonNode
                {
                    Width   = ButtonWidth,
                    String  = "确定",
                    OnClick = () => CloseWithResult(MessageBoxResult.Ok),
                });
                break;

            case MessageBoxType.OkCancel:
                buttons.Add(new TextButtonNode
                {
                    Width   = ButtonWidth,
                    String  = "确定",
                    OnClick = () => CloseWithResult(MessageBoxResult.Ok),
                });
                buttons.Add(new TextButtonNode
                {
                    Width   = ButtonWidth,
                    String  = "取消",
                    OnClick = () => CloseWithResult(MessageBoxResult.Cancel),
                });
                break;

            case MessageBoxType.YesCancel:
                buttons.Add(new TextButtonNode
                {
                    Width   = ButtonWidth,
                    String  = "是",
                    OnClick = () => CloseWithResult(MessageBoxResult.Yes),
                });
                buttons.Add(new TextButtonNode
                {
                    Width   = ButtonWidth,
                    String  = "取消",
                    OnClick = () => CloseWithResult(MessageBoxResult.Cancel),
                });
                break;

            case MessageBoxType.YesNo:
                buttons.Add(new TextButtonNode
                {
                    Width   = ButtonWidth,
                    String  = "是",
                    OnClick = () => CloseWithResult(MessageBoxResult.Yes),
                });
                buttons.Add(new TextButtonNode
                {
                    Width   = ButtonWidth,
                    String  = "否",
                    OnClick = () => CloseWithResult(MessageBoxResult.No),
                });
                break;

            case MessageBoxType.YesNoCancel:
                buttons.Add(new TextButtonNode
                {
                    Width   = ButtonWidth,
                    String  = "是",
                    OnClick = () => CloseWithResult(MessageBoxResult.Yes),
                });
                buttons.Add(new TextButtonNode
                {
                    Width   = ButtonWidth,
                    String  = "否",
                    OnClick = () => CloseWithResult(MessageBoxResult.No),
                });
                buttons.Add(new TextButtonNode
                {
                    Width   = ButtonWidth,
                    String  = "取消",
                    OnClick = () => CloseWithResult(MessageBoxResult.Cancel),
                });
                break;
        }

        var totalButtonWidth = (buttons.Count * ButtonWidth) + ((buttons.Count - 1) * 16f);
        var firstItemSpacing = (contentWidth - totalButtonWidth) / 2f;

        var buttonsNode = new HorizontalListNode
        {
            Height          = ButtonHeight,
            FitHeight       = true,
            ItemSpacing     = 16f,
            FirstItemSpacing = firstItemSpacing,
            Alignment       = HorizontalListAnchor.Left,
        };

        foreach (var button in buttons)
            buttonsNode.AddNode(button);

        return buttonsNode;
    }

    private void CloseWithResult(MessageBoxResult result)
    {
        messageTaskCompletionSource?.TrySetResult(result);
        Close();
    }

    protected override void OnHide(AtkUnitBase* addon)
    {
        messageTaskCompletionSource?.TrySetResult(MessageBoxResult.None);
    }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        messageNode = null;
        rootNode    = null;
    }

    /// <summary>
    /// 显示原生消息框。
    /// </summary>
    public static Task<MessageBoxResult> Show(string title, string message, MessageBoxType type = MessageBoxType.Ok)
    {
        var addon = new MessageBoxAddon
        {
            InternalName = $"DCTravelerXMessageBox_{Guid.NewGuid():N}",
            Title        = title,
        };

        addon.pendingMessage = message;
        addon.pendingType    = type;
        addon.messageTaskCompletionSource = new TaskCompletionSource<MessageBoxResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        addon.Open();

        return addon.messageTaskCompletionSource.Task;
    }

    public new void Dispose()
    {
        messageTaskCompletionSource?.TrySetResult(MessageBoxResult.None);
        Close();
    }
}