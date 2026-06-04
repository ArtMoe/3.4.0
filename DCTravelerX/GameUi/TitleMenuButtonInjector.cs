using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using DCTravelerX.GameUi;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;

namespace DCTravelerX.GameUi;

/// <summary>
/// 激活 _TitleMenu 中隐藏的原生大区选择按钮 (NodeId 5)。
/// </summary>
internal unsafe class TitleMenuButtonInjector : IDisposable
{
    private const uint DcButtonNodeId        = 5;
    private const uint FirstButtonNodeId     = 4;
    private const uint ContainerNodeId       = 3;
    private const int  AlphaFixDurationTicks = 120;

    private AtkComponentButton*  dcButton;
    private AtkResNode*          dcButtonNode;
    private AtkCollisionNode*    dcButtonCollision;
    private AtkResNode*          dcBgResNode;
    private AtkResNode*          dcTextResNode;
    private CustomEventListener? eventListener;
    private bool                 isMouseDown;
    private bool                 isInitialized;

    public TitleMenuButtonInjector()
    {
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "_TitleMenu", OnTitleMenuSetup);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "_TitleMenu", OnTitleMenuRefresh);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "_TitleMenu", OnTitleMenuFinalize);
    }

    #region Lifecycle Events

    private void OnTitleMenuSetup(AddonEvent type, AddonArgs args)
    {
        try
        {
            var addon = (AtkUnitBase*)args.Addon.Address;
            if (addon == null) return;

            dcButton = FindButtonByNodeId(addon, DcButtonNodeId);
            if (dcButton == null)
            {
                Service.Log.Warning("Could not find DC button (NodeId 5) in _TitleMenu");
                return;
            }

            var firstButton = FindButtonByNodeId(addon, FirstButtonNodeId);
            if (firstButton == null)
            {
                Service.Log.Warning("Could not find first button (NodeId 4) in _TitleMenu");
                return;
            }

            SetupDcButton(dcButton, firstButton);
            addon->UpdateCollisionNodeList(false);
            ScheduleAlphaFix();

            Service.Log.Information("DC Travel button enabled in _TitleMenu");
        }
        catch (Exception ex)
        {
            Service.Log.Error($"Error enabling DC button: {ex}");
        }
    }

    private void OnTitleMenuRefresh(AddonEvent type, AddonArgs args)
    {
        if (dcButtonNode != null && isInitialized)
            dcButtonNode->ToggleVisibility(true);
    }

    private void OnTitleMenuFinalize(AddonEvent type, AddonArgs args)
    {
        Cleanup();
    }

    #endregion

    #region Button Setup

    private void SetupDcButton(AtkComponentButton* dcBtn, AtkComponentButton* refBtn)
    {
        dcButtonNode = (AtkResNode*)dcBtn->OwnerNode;
        var refBtnNode = (AtkResNode*)refBtn->OwnerNode;

        dcButtonNode->NodeFlags |= NodeFlags.Visible | NodeFlags.Enabled;
        dcButtonNode->Color.A = 255;
        dcButtonNode->ToggleVisibility(true);

        dcButtonNode->SetPositionFloat(0, 3);
        refBtnNode->SetPositionFloat(0, -26);

        AtkCollisionNode* dcCollision = null;
        AtkResNode*       dcBgRes     = null;
        AtkResNode*       dcTextRes   = null;
        AtkTextNode*      dcText      = null;
        FindButtonChildNodes(dcBtn, &dcCollision, &dcBgRes, &dcTextRes, &dcText);

        AtkCollisionNode* refCollision = null;
        AtkResNode*       refBgRes     = null;
        AtkResNode*       refTextRes   = null;
        AtkTextNode*      refText      = null;
        FindButtonChildNodes(refBtn, &refCollision, &refBgRes, &refTextRes, &refText);

        if (dcCollision == null)
        {
            Service.Log.Error("DC button missing collision node");
            return;
        }

        SetupCollisionNode(dcCollision);
        CopyTimelines(dcCollision, dcBgRes, dcTextRes, dcText, refCollision, refBgRes, refTextRes, refText);
        SetButtonText(dcText);

        dcButtonCollision = dcCollision;
        dcBgResNode       = dcBgRes;
        dcTextResNode     = dcTextRes;

        RegisterMouseEvents(dcCollision);
        isInitialized = true;
    }

    private void FindButtonChildNodes(AtkComponentButton* btn,
        AtkCollisionNode** outCollision, AtkResNode** outBgRes, AtkResNode** outTextRes, AtkTextNode** outText)
    {
        var uldManager = &btn->AtkComponentBase.UldManager;
        *outCollision = null;
        *outBgRes     = null;
        *outTextRes   = null;
        *outText      = null;

        for (uint i = 0; i < uldManager->NodeListCount; i++)
        {
            var childNode = uldManager->NodeList[i];
            if (childNode == null) continue;

            if (childNode->Type == NodeType.Collision)
            {
                *outCollision = (AtkCollisionNode*)childNode;
            }
            else if (childNode->Type == NodeType.Res && childNode->ChildNode != null)
            {
                var resChild = childNode->ChildNode;
                if (resChild->Type == NodeType.NineGrid && *outBgRes == null)
                    *outBgRes = childNode;
                else if (resChild->Type == NodeType.Text && *outText == null)
                {
                    *outTextRes = childNode;
                    *outText    = (AtkTextNode*)resChild;
                }
            }
        }
    }

    private void SetupCollisionNode(AtkCollisionNode* collision)
    {
        collision->AtkResNode.NodeFlags |= NodeFlags.Visible | NodeFlags.Enabled |
                                           NodeFlags.HasCollision | NodeFlags.RespondToMouse |
                                           NodeFlags.EmitsEvents;
        collision->AtkResNode.DrawFlags |= 0x100000;
    }

    private void CopyTimelines(
        AtkCollisionNode* dcCollision, AtkResNode* dcBgRes, AtkResNode* dcTextRes, AtkTextNode* dcText,
        AtkCollisionNode* refCollision, AtkResNode* refBgRes, AtkResNode* refTextRes, AtkTextNode* refText)
    {
        if (refCollision != null && refCollision->AtkResNode.Timeline != null)
            dcCollision->AtkResNode.Timeline = refCollision->AtkResNode.Timeline;
        if (refBgRes != null && refBgRes->Timeline != null && dcBgRes != null)
            dcBgRes->Timeline = refBgRes->Timeline;
        if (refTextRes != null && refTextRes->Timeline != null && dcTextRes != null)
            dcTextRes->Timeline = refTextRes->Timeline;
        if (refText != null && refText->AtkResNode.Timeline != null && dcText != null)
            dcText->AtkResNode.Timeline = refText->AtkResNode.Timeline;
    }

    private static void SetButtonText(AtkTextNode* textNode)
    {
        if (textNode == null) return;

        var seString = new SeStringBuilder()
            .Append("大区选择")
            .Build();

        textNode->SetText(seString.Encode());
    }

    private void RegisterMouseEvents(AtkCollisionNode* collision)
    {
        eventListener = new CustomEventListener(HandleMouseEvent);
        var resNode     = &collision->AtkResNode;
        var eventTarget = &collision->AtkResNode.AtkEventTarget;

        resNode->AtkEventManager.RegisterEvent(AtkEventType.MouseOver, 0, resNode, eventTarget, eventListener, false);
        resNode->AtkEventManager.RegisterEvent(AtkEventType.MouseOut,  0, resNode, eventTarget, eventListener, false);
        resNode->AtkEventManager.RegisterEvent(AtkEventType.MouseDown, 0, resNode, eventTarget, eventListener, false);
        resNode->AtkEventManager.RegisterEvent(AtkEventType.MouseUp,   0, resNode, eventTarget, eventListener, false);
    }

    #endregion

    #region Mouse Event Handling

    private void HandleMouseEvent(AtkEventListener* thisPtr, AtkEventType eventType, int eventParam, AtkEvent* atkEvent, AtkEventData* atkEventData)
    {
        switch (eventType)
        {
            case AtkEventType.MouseOver: OnMouseOver(); break;
            case AtkEventType.MouseOut:  OnMouseOut();  break;
            case AtkEventType.MouseDown: OnMouseDown(); break;
            case AtkEventType.MouseUp:   OnMouseUp();   break;
        }
    }

    private void OnMouseOver()
    {
        if (dcBgResNode != null) dcBgResNode->ToggleVisibility(true);
        if (dcTextResNode != null)
        {
            dcTextResNode->AddRed   = 16;
            dcTextResNode->AddGreen = 16;
            dcTextResNode->AddBlue  = 16;
        }
    }

    private void OnMouseOut()
    {
        isMouseDown = false;
        if (dcBgResNode != null) dcBgResNode->ToggleVisibility(false);
        if (dcTextResNode != null)
        {
            dcTextResNode->AddRed   = 0;
            dcTextResNode->AddGreen = 0;
            dcTextResNode->AddBlue  = 0;
        }
    }

    private void OnMouseDown()
    {
        isMouseDown = true;
        if (dcBgResNode != null) dcBgResNode->Color.A = 255;
    }

    private void OnMouseUp()
    {
        if (isMouseDown)
        {
            isMouseDown = false;
            DcGroupSelectorAddon.Show();
        }
        if (dcBgResNode != null) dcBgResNode->Color.A = 255;
    }

    #endregion

    #region Helper Methods

    private static AtkComponentButton* FindButtonByNodeId(AtkUnitBase* addon, uint nodeId)
    {
        var containerNode = addon->GetNodeById(ContainerNodeId);
        if (containerNode == null) return null;

        var currentNode = containerNode->ChildNode;
        while (currentNode != null)
        {
            if (currentNode->NodeId == nodeId && currentNode->Type == unchecked((NodeType)1001))
            {
                var componentNode = (AtkComponentNode*)currentNode;
                if (componentNode->Component != null)
                    return (AtkComponentButton*)componentNode->Component;
            }
            currentNode = currentNode->PrevSiblingNode;
        }
        return null;
    }

    private void FixButtonAlphas(AtkUnitBase* addon)
    {
        var containerNode = addon->GetNodeById(ContainerNodeId);
        if (containerNode == null) return;

        var currentNode = containerNode->ChildNode;
        while (currentNode != null)
        {
            if (currentNode->Type == unchecked((NodeType)1001))
            {
                var componentNode = (AtkComponentNode*)currentNode;
                if (componentNode->Component != null)
                {
                    var btn         = (AtkComponentButton*)componentNode->Component;
                    var uldManager  = &btn->AtkComponentBase.UldManager;
                    for (uint i = 0; i < uldManager->NodeListCount; i++)
                    {
                        var childNode = uldManager->NodeList[i];
                        if (childNode != null && childNode->Type == NodeType.Res && childNode->Color.A != 255)
                            childNode->Color.A = 255;
                    }
                }
            }
            currentNode = currentNode->PrevSiblingNode;
        }
    }

    private void ScheduleAlphaFix()
    {
        for (var i = 0; i <= AlphaFixDurationTicks; i++)
        {
            var tickDelay = i;
            Service.Framework.RunOnTick(() =>
            {
                try
                {
                    var addon = Service.GameGui.GetAddonByName("_TitleMenu").Address;
                    if (addon != nint.Zero)
                        FixButtonAlphas((AtkUnitBase*)addon);
                }
                catch { }
            }, delayTicks: tickDelay);
        }
    }

    #endregion

    #region Cleanup

    private void Cleanup()
    {
        if (dcButtonCollision != null && eventListener != null)
        {
            var resNode = &dcButtonCollision->AtkResNode;
            resNode->AtkEventManager.UnregisterEvent(AtkEventType.MouseOver, 0, eventListener, false);
            resNode->AtkEventManager.UnregisterEvent(AtkEventType.MouseOut,  0, eventListener, false);
            resNode->AtkEventManager.UnregisterEvent(AtkEventType.MouseDown, 0, eventListener, false);
            resNode->AtkEventManager.UnregisterEvent(AtkEventType.MouseUp,   0, eventListener, false);
        }

        eventListener?.Dispose();
        eventListener     = null;
        dcButton          = null;
        dcButtonNode      = null;
        dcButtonCollision = null;
        dcBgResNode       = null;
        dcTextResNode     = null;
        isInitialized     = false;
    }

    public void Dispose()
    {
        Service.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup,   "_TitleMenu", OnTitleMenuSetup);
        Service.AddonLifecycle.UnregisterListener(AddonEvent.PostRefresh, "_TitleMenu", OnTitleMenuRefresh);
        Service.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "_TitleMenu", OnTitleMenuFinalize);
        Cleanup();
    }

    #endregion
}
