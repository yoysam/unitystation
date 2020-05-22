using System.Linq;
using Mirror;
using UnityEngine;

/// <summary>
/// Matrix Move Client
/// </summary>
public partial class MatrixMove
{
	private bool IsMovingClient => serverMotionState.IsMoving && serverMotionState.Speed > 0f;

	//tracks status of initializing this matrix move
	private bool clientStarted;
	private bool receivedInitialState;
	private bool pendingInitialRotation;
	/// <summary>
	/// Has this matrix move finished receiving its initial state from the server and rotating into its correct
	/// position?
	/// </summary>
	public bool Initialized => clientStarted && receivedInitialState;
	private HistoryNode[] serverHistory = new HistoryNode[8];

	public override void OnStartClient()
	{
		SyncPivot(pivot, pivot);
		SyncInitialPosition(initialPosition, initialPosition);
		UpdateClientFacingState(new MatrixFacingState(), serverFacingState);
		UpdateClientMotionState(new MatrixMotionState(), serverMotionState);
		UpdateOperationalState(false, EnginesOperational);
		clientStarted = true;
	}

	private void SyncInitialPosition(Vector3 oldPos, Vector3 initialPos)
	{
		initialPosition = initialPos.RoundToInt();
	}

	private void SyncPivot(Vector3 oldPivot, Vector3 pivot)
	{
		this.pivot = pivot.RoundToInt();
	}

	[ClientRpc]
	private void RpcReceiveServerHistoryNode(HistoryNode historyNode)
	{
//		Debug.Log($"SERVER HISTORY: {historyNode.nodePos} {historyNode.networkTime}");
		for (int i = serverHistory.Length - 2; i >= 0; i--)
		{
			serverHistory[i + 1] = serverHistory[i];
			if (i == 0)
			{
				serverHistory[i] = historyNode;
			}
		}
	}

	/// Logic that monitors lag on the client
	/// and adjusts move nodes if client is too far behind
	/// server. Causing client to move faster to the next target
	// bool ClientLagMonitor()
	// {
	// 	if (serverHistory[0].networkTime == -1 ||
	// 	    serverHistory[0].nodePos == Vector2Int.zero)
	// 	{
	// 		Debug.Log("No client lag monitor");
	// 		return false;
	// 	}
	//
	// 	var timeDiff = NetworkTime.time - serverHistory[0].networkTime;
	// 	if (timeDiff < 0) timeDiff *= -1;
	//
	// //	Debug.Log($"Check Time diff {timeDiff} {serverHistory[0].nodePos} {toPosition}");
	//
	// 	var pastEntry = moveNodes.historyNodes.FirstOrDefault(x => x.nodePos == serverHistory[0].nodePos);
	// 	if (pastEntry.nodePos != Vector2Int.zero &&
	// 	    pastEntry.networkTime != 0)
	// 	{
	// 		Debug.Log($"Past entry found: {pastEntry.networkTime} {pastEntry.nodePos}");
	// 		var timeDiffEntry = (pastEntry.networkTime - serverHistory[0].networkTime) - NetworkTime.rtt;
	// 		if (timeDiffEntry < 0) timeDiffEntry *= -1;
	//
	// 		Debug.Log($"entry diff was {timeDiffEntry}");
	// 		if (timeDiffEntry < 0.5)
	// 		{
	// 			Debug.Log("Don't do any compensation");
	// 			return false;
	// 		}
	// 	}
	//
	// 	//check if we need to do anything:
	// 	if (serverHistory[0].nodePos != toPosition)
	// 	{
	// 		Debug.Log("Compensator ");
	// 		moveNodes.GenerateMoveNodes(serverHistory[1].nodePos, serverFacingState.FlyingDirection.VectorInt);
	// 		sharedFacingState = serverFacingState;
	// 		GetTargetMoveNode();
	// 		performingMove = true;
	// 		return true;
	// 	}
	//
	// 	Debug.Log($"Didn't do nothin server history {serverHistory[0].nodePos} " +
	// 	          $"to pos {toPosition}");
	// 	return false;
	// }

	public void UpdateClientMotionState(MatrixMotionState oldMotionState, MatrixMotionState newMotionState)
	{
		if (isServer) return;

		if (!oldMotionState.IsMoving && newMotionState.IsMoving)
		{
			MatrixMoveEvents.OnStartMovementClient.Invoke();
			GetTargetMoveNode();
		}
	//	Debug.Log($"Speed from server {newMotionState.Speed} client speed {sharedMotionState.Speed}");
		sharedMotionState.Speed = newMotionState.Speed;

		if (oldMotionState.IsMoving && !newMotionState.IsMoving)
		{
			MatrixMoveEvents.OnStopMovementClient.Invoke();
		}

		if ((int)oldMotionState.Speed != (int)newMotionState.Speed)
		{
			MatrixMoveEvents.OnSpeedChange.Invoke(oldMotionState.Speed, newMotionState.Speed);
		}

		serverMotionState = newMotionState;
	}

	public void UpdateClientFacingState(MatrixFacingState oldFacingState, MatrixFacingState newFacingState)
	{
		if (isServer) return;
		if (!Equals(oldFacingState.FacingDirection, newFacingState.FacingDirection))
		{
			if (!receivedInitialState && !pendingInitialRotation)
			{
				pendingInitialRotation = true;
			}
			inProgressRotation = oldFacingState.FacingDirection.OffsetTo(newFacingState.FacingDirection);
			Logger.LogTraceFormat("{0} starting rotation progress to {1}", Category.Matrix, this, newFacingState.FacingDirection);
			MatrixMoveEvents.OnRotate.Invoke(new MatrixRotationInfo(this, inProgressRotation.Value, NetworkSide.Client, RotationEvent.Start));
		}

		if (sharedFacingState.FacingDirection != newFacingState.FacingDirection)
		{
			sharedFacingState.RotationTime = newFacingState.RotationTime;
			sharedFacingState.FacingDirection = newFacingState.FacingDirection;
			sharedFacingState.FlyingDirection = newFacingState.FlyingDirection;
			StartRotateClient();
		}

		if (!receivedInitialState && !pendingInitialRotation)
		{
			receivedInitialState = true;
		}

		serverFacingState = newFacingState;
	}

	private void StartRotateClient()
	{
		rotateLerp = 0f;
		fromRotation = transform.rotation;
		MatrixMoveEvents.OnRotate.Invoke(new MatrixRotationInfo(this,
			sharedFacingState.FacingDirection.OffsetTo(sharedFacingState.FacingDirection), NetworkSide.Client, RotationEvent.Start));
		IsRotating = true;
	}

	private void UpdateOperationalState(bool oldState, bool newState)
	{
		if (oldState != newState)
		{
			if (newState)
			{
				moveLerp = 0f;
				moveNodes.GenerateMoveNodes(transform.position, serverFacingState.FlyingDirection.VectorInt);
				GetTargetMoveNode();
			}
		}

		EnginesOperational = newState;
	}

	/// <summary>
	/// Performs the rotation / movement animation on all clients and server. Called every UpdateMe()
	/// </summary>
	private void AnimateMovement()
	{
//		if (Equals(clientState, MatrixState.Invalid))
//		{
//			return;
//		}
//
//		if (NeedsRotationClient)
//		{
//			//rotate our transform to our new facing direction
//			if (clientState.RotationTime != 0)
//			{
//				//animate rotation
//				transform.rotation =
//					Quaternion.RotateTowards(transform.rotation,
//						 InitialFacing.OffsetTo(clientState.FacingDirection).Quaternion,
//						Time.deltaTime * clientState.RotationTime);
//			}
//			else
//			{
//				//rotate instantly
//				transform.rotation = InitialFacing.OffsetTo(clientState.FacingDirection).Quaternion;
//			}
//		}
//		else if (IsMovingClient)
//		{
//			//Only move target if rotation is finished
//			//predict client state because we don't get constant updates when flying in one direction.
//			clientState.Position += (clientState.Speed * Time.deltaTime) * clientState.FlyingDirection.Vector;
//		}
//
//		//finish rotation (rotation event will be fired in lateupdate
//		if (!NeedsRotationClient && inProgressRotation != null)
//		{
//			// Finishes the job of Lerp and straightens the ship with exact angle value
//			transform.rotation = InitialFacing.OffsetTo(clientState.FacingDirection).Quaternion;
//		}
//
//		//Lerp
//		if (clientState.Position != transform.position)
//		{
//			float distance = Vector3.Distance(clientState.Position, transform.position);
//
//			//Teleport (Greater then 30 unity meters away from server target):
//			if (distance > 30f)
//			{
//				matrixPositionFilter.FilterPosition(transform, clientState.Position, clientState.FlyingDirection);
//				return;
//			}
//
//			transform.position = clientState.Position;
//
//			//If stopped then lerp to target (snap to grid)
//			if (!clientState.IsMoving )
//			{
//				if ( clientState.Position == transform.position )
//				{
//					MatrixMoveEvents.OnFullStopClient.Invoke();
//				}
//				if ( distance > 0f )
//				{
//					//TODO: Why is this needed? Seems weird.
//					matrixPositionFilter.SetPosition(transform.position);
//					return;
//				}
//			}
//
//			matrixPositionFilter.FilterPosition(transform, transform.position, clientState.FlyingDirection);
//		}
	}
}
