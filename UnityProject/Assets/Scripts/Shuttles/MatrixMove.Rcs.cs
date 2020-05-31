using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

	[SyncVar] [HideInInspector] public bool rcsModeActive;
	private bool rcsBurn = false;

	private Queue<PendingRcsMove> pendingRcsMoves = new Queue<PendingRcsMove>();
	private Dictionary<double, Vector2Int> clientRcsHistory = new Dictionary<double, Vector2Int>();

	private struct PendingRcsMove
	{
		public Vector2Int dir;
		public double networkTime;
	}

	private DateTime lastPlayerBurn = DateTime.Now;

	//For Rcs Movement
	public void ReceivePlayerMoveAction(PlayerAction moveActions)
	{
		if (moveActions.Direction() != Vector2Int.zero && !rcsBurn)
		{
			var dir = moveActions.Direction();
			var networkTime = NetworkTime.time;
			if (MoveViaRcs(networkTime, dir))
			{
				RcsMovementMessage.Send(dir, netId, networkTime);
			}
		}
	}

	bool TryUseRcs(double networkTime, Vector2Int direction)
	{
		if (clientRcsHistory.ContainsKey(networkTime))
		{
			return false;
		}

		if (networkTime != 0.0)
		{
			clientRcsHistory.Add(networkTime, direction);
		}

		if (clientRcsHistory.Count > 60)
		{
			clientRcsHistory.Remove(clientRcsHistory.ElementAt(0).Key);
		}

		return true;
	}

	[Server]
	public void ProcessRcsMoveRequest(double networkTime, Vector2Int dir)
	{
		if (MoveViaRcs(networkTime, dir))
		{
			RpcRcsMove(dir, networkTime);
		}
	}

	private bool ProcessPendingBurns()
	{
		if (!isServer)
		{

		}

		if (pendingRcsMoves.Count > 0)
		{
			var pendingMove = pendingRcsMoves.Dequeue();
			if (isServer)
			{
				if (MoveViaRcs(pendingMove.networkTime, pendingMove.dir))
				{
					RpcRcsMove(pendingMove.dir, pendingMove.networkTime);
					return true;
				}
			}
			else
			{
				if (MoveViaRcs(pendingMove.networkTime, pendingMove.dir))
				{
					return true;
				}
			}
		}
		rcsBurn = false;
		return false;
	}

	[ClientRpc]
	private void RpcRcsMove(Vector2Int dir, double networkTime)
	{
		if (rcsBurn)
		{
			pendingRcsMoves.Enqueue(new PendingRcsMove
			{
				dir = dir,
				networkTime = networkTime
			});
		}
		else
		{
			MoveViaRcs(networkTime, dir);
		}
	}

	private bool MoveViaRcs(double networkTime, Vector2Int dir)
	{
		if (!TryUseRcs(networkTime, dir))
		{
			rcsBurn = false;
			return false;
		}

		rcsBurn = true;
		if (sharedMotionState.Speed > 0f)
		{
			//matrix is moving we need to strafe instead
			//(forward and reverse will be ignored)
			if (sharedFacingState.FacingDirection.VectorInt != dir &&
			    sharedFacingState.FacingDirection.VectorInt * -1 != dir)
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
		foreach (Transform t in matrixInfo.Objects)
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
			if (mappedOrientation == OrientationEnum.Up) bowRcsThrusters.Add(thruster);
			if (mappedOrientation == OrientationEnum.Down) sternRcsThrusters.Add(thruster);
			if (mappedOrientation == OrientationEnum.Right) portRcsThrusters.Add(thruster);
			if (mappedOrientation == OrientationEnum.Left) starBoardRcsThrusters.Add(thruster);
		}

		if (InitialFacing == Orientation.Right)
		{
			if (mappedOrientation == OrientationEnum.Up) portRcsThrusters.Add(thruster);
			if (mappedOrientation == OrientationEnum.Down) starBoardRcsThrusters.Add(thruster);
			if (mappedOrientation == OrientationEnum.Right) sternRcsThrusters.Add(thruster);
			if (mappedOrientation == OrientationEnum.Left) bowRcsThrusters.Add(thruster);
		}

		if (InitialFacing == Orientation.Down)
		{
			if (mappedOrientation == OrientationEnum.Up) sternRcsThrusters.Add(thruster);
			if (mappedOrientation == OrientationEnum.Down) bowRcsThrusters.Add(thruster);
			if (mappedOrientation == OrientationEnum.Right) starBoardRcsThrusters.Add(thruster);
			if (mappedOrientation == OrientationEnum.Left) portRcsThrusters.Add(thruster);
		}

		if (InitialFacing == Orientation.Left)
		{
			if (mappedOrientation == OrientationEnum.Up) starBoardRcsThrusters.Add(thruster);
			if (mappedOrientation == OrientationEnum.Down) portRcsThrusters.Add(thruster);
			if (mappedOrientation == OrientationEnum.Right) bowRcsThrusters.Add(thruster);
			if (mappedOrientation == OrientationEnum.Left) sternRcsThrusters.Add(thruster);
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