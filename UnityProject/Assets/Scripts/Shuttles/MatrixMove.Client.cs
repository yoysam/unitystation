using Mirror;
using UnityEngine;

/// <summary>
/// Matrix Move Client
/// </summary>
public partial class MatrixMove
{
	private bool IsMovingClient => ServerState.IsMoving && ServerState.Speed > 0f;

	//tracks status of initializing this matrix move
	private bool clientStarted;
	private bool receivedInitialState;
	private bool pendingInitialRotation;
	/// <summary>
	/// Has this matrix move finished receiving its initial state from the server and rotating into its correct
	/// position?
	/// </summary>
	public bool Initialized => clientStarted && receivedInitialState;
	private HistoryNode[] serverHistory = new HistoryNode[4];

	public override void OnStartClient()
	{
		SyncPivot(pivot, pivot);
		SyncInitialPosition(initialPosition, initialPosition);
		UpdateClientState(new MatrixState(), ServerState);
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
		Debug.Log($"SERVER HISTORY: {historyNode.nodePos} {historyNode.networkTime}");
		for (int i = serverHistory.Length - 2; i >= 0; i--)
		{
			serverHistory[i + 1] = serverHistory[i];
			if (i == 0)
			{
				serverHistory[i] = historyNode;
			}
		}
	}

	/// Called when MatrixMoveMessage is received
	public void UpdateClientState(MatrixState oldState, MatrixState newState)
	{
		Debug.Log($"New State spd: {newState.Speed} pos: {newState.Position} ismove: {newState.IsMoving}  rotTime: {newState.RotationTime} flyDir: {newState.FlyingDirection}");

		if (!Equals(oldState.FacingDirection, newState.FacingDirection))
		{
			if (!receivedInitialState && !pendingInitialRotation)
			{
				pendingInitialRotation = true;
			}
			inProgressRotation = oldState.FacingDirection.OffsetTo(newState.FacingDirection);
			Logger.LogTraceFormat("{0} starting rotation progress to {1}", Category.Matrix, this, newState.FacingDirection);
			MatrixMoveEvents.OnRotate.Invoke(new MatrixRotationInfo(this, inProgressRotation.Value, NetworkSide.Client, RotationEvent.Start));
		}

		//Todo need to mark each rotation state with an uuid from point of origin so we can
		//filter out predictive rotations that have succeeded here instead of checking the sharedstate
		//this will allow rotation prediction to work passed the first rotation in laggy situations -doobly
		if (SharedState.FacingDirection != newState.FacingDirection)
		{
			SharedState.RotationTime = newState.RotationTime;
			SharedState.FacingDirection = newState.FacingDirection;
			SharedState.FlyingDirection = newState.FlyingDirection;
			StartRotateClient();
		}

		if (!oldState.IsMoving && newState.IsMoving)
		{
			MatrixMoveEvents.OnStartMovementClient.Invoke();
		}

		SharedState.Speed = newState.Speed;

		if (oldState.IsMoving && !newState.IsMoving)
		{
			MatrixMoveEvents.OnStopMovementClient.Invoke();
		}

		if ((int)oldState.Speed != (int)newState.Speed)
		{
			MatrixMoveEvents.OnSpeedChange.Invoke(oldState.Speed, newState.Speed);
		}

		if (!receivedInitialState && !pendingInitialRotation)
		{
			receivedInitialState = true;
		}

		ServerState = newState;
	}

	private void StartRotateClient()
	{
		rotateLerp = 0f;
		fromRotation = transform.rotation;
		MatrixMoveEvents.OnRotate.Invoke(new MatrixRotationInfo(this,
			SharedState.FacingDirection.OffsetTo(SharedState.FacingDirection), NetworkSide.Client, RotationEvent.Start));
		IsRotating = true;
	}

	private void UpdateOperationalState(bool oldState, bool newState)
	{
		if (oldState != newState)
		{
			if (newState)
			{
				moveLerp = 0f;
				moveNodes.GenerateMoveNodes(transform.position, ServerState.FlyingDirection.VectorInt);
				GetTargetMoveNode();
			}
		}

		EnginesOperational = newState;
	}

	private void CheckMovementClient()
	{
		if (RotateMatrix()) return;
		MoveMatrix();
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
