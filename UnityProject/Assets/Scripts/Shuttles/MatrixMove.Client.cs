using System.Collections.Generic;
using System.IO;
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
	private float lastSpeed = 1f;

	/// <summary>
	/// Has this matrix move finished receiving its initial state from the server and rotating into its correct
	/// position?
	/// </summary>
	public bool Initialized => clientStarted && receivedInitialState;

	private HistoryNode[] serverHistory = new HistoryNode[8];
	private Dictionary<double, float> clientSpeedHistory = new Dictionary<double, float>();
	private Dictionary<double, Orientation> clientRotationHistory = new Dictionary<double, Orientation>();


	public override void OnStartClient()
	{
		fromPosition = toPosition;
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

	private string log = "";

	[ClientRpc]
	private void RpcReceiveServerHistoryNode(HistoryNode historyNode)
	{
		return;
		
		if (isServer) return;

		for (int i = serverHistory.Length - 2; i >= 0; i--)
		{
			serverHistory[i + 1] = serverHistory[i];
			if (i == 0)
			{
				serverHistory[i] = historyNode;
			}
		}

		if (serverHistory[1].networkTime != -1 && moveNodes.historyNodes[1].nodePos != Vector2.zero
		                                       && moveNodes.historyNodes[0].nodePos != Vector2.zero)
		{
			var travelTimePerTileServer = 1f / (serverHistory[0].networkTime - serverHistory[1].networkTime);
			var travelTimePerTileClient =
				1f / (moveNodes.historyNodes[0].networkTime - moveNodes.historyNodes[1].networkTime);
			SetAdjustmentSpeed(historyNode.nodePos - moveNodes.historyNodes[0].nodePos, travelTimePerTileClient);
			log +=
				$"[{serverMotionState.Speed}] Server {travelTimePerTileServer} tiles / sec || [{sharedMotionState.Speed}] Client {travelTimePerTileClient} tiles / sec \r\n";
		}

		log +=
			$"Server history pos {historyNode.nodePos} time: {historyNode.networkTime} our pos {moveNodes.historyNodes[0].nodePos} time: {moveNodes.historyNodes[0].networkTime} \r\n";

		var diff = NetworkTime.time - historyNode.networkTime;
		var diffWithRttAdjust = diff - NetworkTime.rtt;
		// Debug.Log($"Diff {diff} diff with rtt adjust {diffWithRttAdjust}");
		// Debug.Log($"Clients speed state {sharedMotionState.Speed}");

		if (sharedMotionState.Speed == 0)
		{
			fromPosition = transform.position;
			toPosition = historyNode.nodePos;
			speedAdjust = Mathf.Round(Vector2.Distance(fromPosition, toPosition)) * 2f;
			moveLerp = 0f;
			performingMove = true;
			moveNodes.GenerateMoveNodes(toPosition, sharedFacingState.FacingDirection.VectorInt);
			File.WriteAllText(Path.Combine(Application.streamingAssetsPath, "motionlog.txt"), log);
		}
	}

	bool TrySetClientSpeed(double networkTime, float speed)
	{
		if (clientSpeedHistory.ContainsKey(networkTime))
		{
			return false;
		}

		if (networkTime != 0.0)
		{
			clientSpeedHistory.Add(networkTime, speed);
		}

		sharedMotionState.Speed = speed;
		sharedMotionState.SpeedNetworkTime = networkTime;

		if (clientSpeedHistory.Count > 60)
		{
			clientSpeedHistory.Remove(clientSpeedHistory.ElementAt(0).Key);
		}

		return true;
	}

	///Only change orientation if rotation is finished
	public void TryRotate(bool clockwise)
	{
		if (!IsRotating)
		{
			sharedFacingState.RotationTime = 2f;
			var networkTime = NetworkTime.time;
			var newOrientation = sharedFacingState.FacingDirection.Rotate(clockwise ? 1 : -1);
			if (CanRotateTo(newOrientation))
			{
				if (TrySetClientRotation(networkTime, newOrientation))
				{
					StartRotateClient();
					MatrixMoveRotateRequest.Send(netId, PlayerManager.LocalPlayer, networkTime,
						newOrientation.AsEnum());
				}
			}
		}
	}

	bool TrySetClientRotation(double networkTime, Orientation orientation)
	{
		if (clientRotationHistory.ContainsKey(networkTime))
		{
			return false;
		}

		if (networkTime != 0.0)
		{
			clientRotationHistory.Add(networkTime, orientation);
		}

		sharedFacingState.FacingDirection = orientation;
		sharedFacingState.FacingDirectionNetworkTime = networkTime;

		if (clientRotationHistory.Count > 60)
		{
			clientRotationHistory.Remove(clientRotationHistory.ElementAt(0).Key);
		}

		return true;
	}

	void SetAdjustmentSpeed(Vector2 diff, double clientTilesPerSec)
	{
		if (diff == Vector2.zero || speedAdjust != 0)
		{
			return;
		}

		diff *= 0.5f;
		switch (sharedFacingState.FacingDirection.AsEnum())
		{
			case OrientationEnum.Left:
				var x = diff.x * -1;
				speedAdjust = (float) ((sharedMotionState.Speed * ((clientTilesPerSec + x) / clientTilesPerSec)) -
				                       sharedMotionState.Speed);

				break;
			case OrientationEnum.Right:
				speedAdjust = (float) ((sharedMotionState.Speed * ((clientTilesPerSec + diff.x) / clientTilesPerSec)) -
				                       sharedMotionState.Speed);
				break;
			case OrientationEnum.Up:
				speedAdjust = (float) ((sharedMotionState.Speed * ((clientTilesPerSec + diff.y) / clientTilesPerSec)) -
				                       sharedMotionState.Speed);
				break;
			case OrientationEnum.Down:
				var y = diff.y * -1;
				speedAdjust = (float) ((sharedMotionState.Speed * ((clientTilesPerSec + y) / clientTilesPerSec)) -
				                       sharedMotionState.Speed);
				break;
		}

		speedAdjust = Mathf.Clamp(speedAdjust, (sharedMotionState.Speed * -1) + 2f, 200f);

		//	Debug.Log("Set speed adjust: " + speedAdjust);
		log += $"Set speed adjust: {speedAdjust} \r\n";
	}

	public void UpdateClientMotionState(MatrixMotionState oldMotionState, MatrixMotionState newMotionState)
	{
		if (isServer) return;

		if (TrySetClientSpeed(newMotionState.SpeedNetworkTime, newMotionState.Speed))
		{
			if (!oldMotionState.IsMoving && newMotionState.IsMoving)
			{
				MatrixMoveEvents.OnStartMovementClient.Invoke();
				GetTargetMoveNode();
			}

			if (oldMotionState.IsMoving && !newMotionState.IsMoving)
			{
				MatrixMoveEvents.OnStopMovementClient.Invoke();
			}
		}

		if ((int) oldMotionState.Speed != (int) newMotionState.Speed)
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
			Logger.LogTraceFormat("{0} starting rotation progress to {1}", Category.Matrix, this,
				newFacingState.FacingDirection);
			MatrixMoveEvents.OnRotate.Invoke(new MatrixRotationInfo(this, inProgressRotation.Value, NetworkSide.Client,
				RotationEvent.Start));
		}

		if (TrySetClientRotation(newFacingState.FacingDirectionNetworkTime, newFacingState.FacingDirection))
		{
			sharedFacingState.RotationTime = newFacingState.RotationTime;
			sharedFacingState.FacingDirection = newFacingState.FacingDirection;
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
			sharedFacingState.FacingDirection.OffsetTo(sharedFacingState.FacingDirection), NetworkSide.Client,
			RotationEvent.Start));
		IsRotating = true;
	}

	private void UpdateOperationalState(bool oldState, bool newState)
	{
		if (oldState != newState)
		{
			if (newState)
			{
				moveLerp = 0f;
				moveNodes.GenerateMoveNodes(transform.position, serverFacingState.FacingDirection.VectorInt);
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