﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

[CreateAssetMenu(fileName = "UIActionScriptableObject", menuName = "ScriptableObjects/UIActionSO")]
public class UIActionScriptableObject : ScriptableObject, IServerActionGUI
{
	[SerializeField]
	private ActionData actionData;
	public ActionData ActionData => actionData;

	public void CallActionClient()
	{
		Logger.Log("CallActionClient SO");
		//Do whatever you want
		//Remember if its networked do validationNot just
	}

	public void CallActionServer(ConnectedPlayer SentByPlayer)
	{
		Logger.Log("CallActionServer SO");
		//Validation
		//do Action
	}
	public NetworkIdentity GetNetworkIdentity()
	{
		Logger.Log("GetNetworkIdentity SO");
		//Return the network identity that its going to find it from,
		//**Dont** ues stuff that could change across server and client E.G station matrix
		return (null);
	}

	public void Awake()
	{
#if UNITY_EDITOR
		{
			if (UIActionSOSingleton.Instance == null)
			{
				Resources.LoadAll<UIActionSOSingleton>("ScriptableObjects/SOs singletons");
			}
			if (!UIActionSOSingleton.Instance.uIActions.Contains(this))
			{
				UIActionSOSingleton.Instance.uIActions.Add(this);
			}

		}
#endif
	}
}
