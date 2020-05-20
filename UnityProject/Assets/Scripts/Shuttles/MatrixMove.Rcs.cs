using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Matrix Move Rcs Controller
/// </summary>
public partial class MatrixMove
{
	private List<RcsThruster> bowRcsThrusters = new List<RcsThruster>(); //front
	private List<RcsThruster> sternRcsThrusters = new List<RcsThruster>(); //back
	private List<RcsThruster> portRcsThrusters = new List<RcsThruster>(); //left
	private List<RcsThruster> starBoardRcsThrusters = new List<RcsThruster>(); //right
	public ConnectedPlayer playerControllingRcs { get; private set; }

	[SyncVar] [HideInInspector]
	public bool rcsModeActive;
	private bool rcsBurn = false;

	//For Rcs Movement
	public void ReceivePlayerMoveAction(PlayerAction moveActions)
	{
		if (moveActions.Direction() != Vector2Int.zero && !rcsBurn)
		{
			var dir = moveActions.Direction();
			if (!isServer)
			{
				if (MoveViaRcs(dir))
				{
					RcsMovementMessage.Send(dir, netId);
				}
			}
			else
			{
				ProcessRcsMoveRequest(playerControllingRcs, dir);
			}
		}
	}

	[Server]
	public void ProcessRcsMoveRequest(ConnectedPlayer sentBy, Vector2Int dir)
	{
		if (sentBy == playerControllingRcs && dir != Vector2Int.zero && !rcsBurn)
		{
			MoveViaRcs(dir);
			RpcRcsMove(dir, sentBy.GameObject);
		}
	}

	[ClientRpc]
	private void RpcRcsMove(Vector2Int dir, GameObject requestBy)
	{
		if (PlayerManager.LocalPlayer != null && PlayerManager.LocalPlayer == requestBy)
		{
			return;
		}
		MoveViaRcs(dir);
	}

	private bool MoveViaRcs(Vector2Int dir)
	{
		rcsBurn = true;
		if (SharedState.Speed > 0f)
		{
			//matrix is moving we need to strafe instead
			//(forward and reverse will be ignored)
			if (SharedState.FlyingDirection.VectorInt != dir &&
			    SharedState.FlyingDirection.VectorInt * -1 != dir)
			{
				moveNodes.AdjustFutureNodes(dir);
				toPosition += dir;
				fromPosition = transform.position;
				moveLerp = 0f;
			}
			else
			{
				rcsBurn = false;
				return false;
			}
		}
		else
		{
			fromPosition = transform.position.To2Int();
			toPosition = (fromPosition + dir).To2Int();
			moveLerp = 0f;
		}

		return true;
	}

	//Searches the matrix for RcsThrusters
	public void CacheRcs()
	{
		ClearRcsCache();
		foreach(Transform t in matrixInfo.Objects)
		{
			if (t.tag.Equals("Rcs"))
			{
				CacheRcs(t.GetComponent<DirectionalRotatesParent>().MappedOrientation,
					t.GetComponent<RcsThruster>());
			}
		}
	}

	void CacheRcs(OrientationEnum mappedOrientation, RcsThruster thruster)
	{
		if (InitialFacing == Orientation.Up)
		{
			if(mappedOrientation == OrientationEnum.Up) bowRcsThrusters.Add(thruster);
			if(mappedOrientation == OrientationEnum.Down) sternRcsThrusters.Add(thruster);
			if(mappedOrientation == OrientationEnum.Right) portRcsThrusters.Add(thruster);
			if(mappedOrientation == OrientationEnum.Left) starBoardRcsThrusters.Add(thruster);
		}

		if (InitialFacing == Orientation.Right)
		{
			if(mappedOrientation == OrientationEnum.Up) portRcsThrusters.Add(thruster);
			if(mappedOrientation == OrientationEnum.Down) starBoardRcsThrusters.Add(thruster);
			if(mappedOrientation == OrientationEnum.Right) sternRcsThrusters.Add(thruster);
			if(mappedOrientation == OrientationEnum.Left) bowRcsThrusters.Add(thruster);
		}

		if (InitialFacing == Orientation.Down)
		{
			if(mappedOrientation == OrientationEnum.Up) sternRcsThrusters.Add(thruster);
			if(mappedOrientation == OrientationEnum.Down) bowRcsThrusters.Add(thruster);
			if(mappedOrientation == OrientationEnum.Right) starBoardRcsThrusters.Add(thruster);
			if(mappedOrientation == OrientationEnum.Left) portRcsThrusters.Add(thruster);
		}

		if (InitialFacing == Orientation.Left)
		{
			if(mappedOrientation == OrientationEnum.Up) starBoardRcsThrusters.Add(thruster);
			if(mappedOrientation == OrientationEnum.Down) portRcsThrusters.Add(thruster);
			if(mappedOrientation == OrientationEnum.Right) bowRcsThrusters.Add(thruster);
			if(mappedOrientation == OrientationEnum.Left) sternRcsThrusters.Add(thruster);
		}
	}

	void ClearRcsCache()
	{
		bowRcsThrusters.Clear();
		sternRcsThrusters.Clear();
		portRcsThrusters.Clear();
		starBoardRcsThrusters.Clear();
	}
}
