﻿using System.Collections;
using UnityEngine;
using Utility = UnityEngine.Networking.Utility;
using Mirror;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class SetActionUI : ServerMessage
{
	public static short MessageType = (short)MessageTypes.SetActionUI;

	public string soName;
	public int SpriteLocation;
	public int ComponentLocation;
	public uint NetObject;
	public bool showAlert;
	public Type ComponentType;
	public SetActionUIActions ProposedAction;

	public override IEnumerator Process()
	{
		IServerActionGUI IServerActionGUI = null;
		if (soName != null && soName.Length > 0)
		{
			IServerActionGUI = UIActionSOSingleton.Instance.ReturnFromName(soName);
		}
		else {

			yield return WaitFor(NetObject);
			var IServerIActionGUIs = NetworkObject.GetComponentsInChildren(ComponentType);
			if ((IServerIActionGUIs.Length > ComponentLocation))
			{
				IServerActionGUI = (IServerIActionGUIs[ComponentLocation] as IServerActionGUI);
			}
		}
		if (IServerActionGUI != null)
		{
			switch (ProposedAction)
			{
				case SetActionUIActions.FrontIcon:
					UIActionManager.Instance.SetSprite(IServerActionGUI, SpriteLocation);
					break;
				case SetActionUIActions.BackgroundIcon:
					UIActionManager.Instance.SetBackground(IServerActionGUI, SpriteLocation);
					break;
				case SetActionUIActions.StateChange:
					UIActionManager.Instance.SetAction(IServerActionGUI, showAlert);
					break;
			}
		}
	}


	private static SetActionUI _Send(GameObject recipient,
									 IServerActionGUI iServerActionGUI,
									 SetActionUIActions ProposedAction,
									 bool _showAlert = false,
									 int location = 0)
	{
		if (!(iServerActionGUI is UIActionScriptableObject))
		{
			var netObject = iServerActionGUI.GetNetworkIdentity();
			var _ComponentType = iServerActionGUI.GetType();
			var iServerActionGUIs = netObject.GetComponentsInChildren(_ComponentType);
			var _ComponentLocation = 0;
			bool Found = false;
			foreach (var _iServerActionGUI in iServerActionGUIs)
			{
				if ((_iServerActionGUI as IServerActionGUI) == iServerActionGUI)
				{
					Found = true;
					break;
				}
				_ComponentLocation++;
			}
			if (Found)
			{
				SetActionUI msg = new SetActionUI
				{
					NetObject = netObject.netId,
					ComponentLocation = _ComponentLocation,
					ComponentType = _ComponentType,
					showAlert = _showAlert,
					SpriteLocation = location,
					ProposedAction = ProposedAction
				};
				msg.SendTo(recipient);
				return msg;

			}
			else {

				Logger.LogError("Failed to find IServerActionGUI on NetworkIdentity");
			}
		}
		else {
			var _ComponentType = iServerActionGUI.GetType();
			SetActionUI msg = new SetActionUI
			{

				soName = (iServerActionGUI as UIActionScriptableObject).name,
				showAlert = _showAlert,
				SpriteLocation = location,
				ProposedAction = ProposedAction,
				ComponentType = _ComponentType,
			};
			msg.SendTo(recipient);
			return msg;
		}
		return (null);
	}

	public static SetActionUI SetAction(GameObject recipient, IServerActionGUI iServerActionGUI, bool _showAlert)
	{
		return (_Send(recipient, iServerActionGUI, SetActionUIActions.StateChange, _showAlert));
	}

	public static SetActionUI SetSprite(GameObject recipient, IServerActionGUI iServerActionGUI, int FrontIconlocation)
	{
		return (_Send(recipient, iServerActionGUI, SetActionUIActions.FrontIcon, location: FrontIconlocation));
	}

	public static SetActionUI SetBackgroundSprite(GameObject recipient, IServerActionGUI iServerActionGUI, int FrontIconlocation)
	{
		return (_Send(recipient, iServerActionGUI, SetActionUIActions.BackgroundIcon, location: FrontIconlocation));
	}


	public override void Deserialize(NetworkReader reader)
	{
		base.Deserialize(reader);

		soName = reader.ReadString();
		SpriteLocation = reader.ReadInt32();
		ComponentLocation = reader.ReadInt32();
		NetObject = reader.ReadUInt32();
		showAlert = reader.ReadBoolean();
		ComponentType = RequestGameAction.componentIDToComponentType[reader.ReadUInt16()];
		ProposedAction =  (SetActionUIActions)reader.ReadInt32(); 

	}

	public override void Serialize(NetworkWriter writer)
	{
		base.Serialize(writer);
		writer.WriteString(soName);
		writer.WriteInt32(SpriteLocation);
		writer.WriteInt32(ComponentLocation);
		writer.WriteUInt32(NetObject);
		writer.WriteBoolean(showAlert);
		writer.WriteUInt16(RequestGameAction.componentTypeToComponentID[ComponentType]);
		writer.WriteInt32((int) ProposedAction);

	}
}


public enum SetActionUIActions
{
	StateChange,
	BackgroundIcon,
	FrontIcon
}