using System.Collections;
using System.Linq;
using Mirror;
using UnityEngine;

/// <summary>
/// Matrix Move Server
/// </summary>
public partial class MatrixMove
{
	//server-only values
	[SyncVar(hook=nameof(UpdateClientFacingState))]
	public MatrixFacingState serverFacingState;
	[SyncVar(hook=nameof(UpdateClientMotionState))]
	public MatrixMotionState serverMotionState;
	public bool IsMovingServer => serverMotionState.IsMoving && serverMotionState.Speed > 0f;

	//Autopilot target
	private Vector3 Target = TransformState.HiddenPos;

	///Zero means 100% accurate, but will lead to peculiar behaviour (autopilot not reacting fast enough on high speed -> going back/in circles etc)
	private int AccuracyThreshold = 1;

	[SyncVar(hook=nameof(UpdateOperationalState))] private bool EnginesOperational;

	public override void OnStartServer()
	{
		StartCoroutine(ServerWaitForMatrixManager());
		base.OnStartServer();
	}

	IEnumerator ServerWaitForMatrixManager()
	{
		while (!MatrixManager.IsInitialized)
		{
			yield return WaitFor.EndOfFrame;
		}

		InitServerState();
	}

	[Server]
	private void InitServerState()
	{
		Vector3Int initialPositionInt =
			Vector3Int.RoundToInt(new Vector3(transform.position.x, transform.position.y, 0));
		SyncInitialPosition(initialPosition, initialPositionInt);
		var child = transform.GetChild(0);
		matrixInfo = MatrixManager.Get(child.gameObject);
		var childPosition =
			Vector3Int.CeilToInt(new Vector3(child.transform.position.x, child.transform.position.y, 0));
		SyncPivot(pivot, initialPosition - childPosition);

		serverFacingState = new MatrixFacingState
		{
			FlyingDirection = InitialFacing,
			FacingDirection = InitialFacing
		};

		serverMotionState = new MatrixMotionState
		{
			Position = initialPositionInt
		};

		RecheckThrusters();
		if (thrusters.Count > 0)
		{
			Logger.LogFormat("{0}: Initializing {1} thrusters!", Category.Transform, matrixInfo.Matrix.name,
				thrusters.Count);
			foreach (var thruster in thrusters)
			{
				var integrity = thruster.GetComponent<Integrity>();
				if (integrity)
				{
					integrity.OnWillDestroyServer.AddListener(destructionInfo =>
					{
						if (thrusters.Contains(thruster))
						{
							thrusters.Remove(thruster);
						}

						if (thrusters.Count == 0 && IsMovingServer)
						{
							Logger.LogFormat("All thrusters were destroyed! Stopping {0} soon!", Category.Transform,
								matrixInfo.Matrix.name);
							ToggleEngines(false);
						}
					});
				}
			}
		}

		if (SensorPositions == null)
		{
			CollisionSensor[] sensors = GetComponentsInChildren<CollisionSensor>();
			if (sensors.Length == 0)
			{
				SensorPositions = new Vector3Int[0];
				return;
			}

			SensorPositions = sensors.Select(sensor => Vector3Int.RoundToInt(sensor.transform.localPosition)).ToArray();

			Logger.Log($"Initialized sensors at {string.Join(",", SensorPositions)}," +
			           $" direction is {serverFacingState.FlyingDirection}", Category.Matrix);
		}

		if (RotationSensors == null)
		{
			RotationCollisionSensor[] sensors = GetComponentsInChildren<RotationCollisionSensor>();
			if (sensors.Length == 0)
			{
				RotationSensors = new GameObject[0];
				return;
			}

			if (rotationSensorContainerObject == null)
			{
				rotationSensorContainerObject = sensors[0].transform.parent.gameObject;
			}

			RotationSensors = sensors.Select(sensor => sensor.gameObject).ToArray();
		}

		sharedFacingState = serverFacingState;
	}

	[Server]
	public void ToggleEngines(bool on, ConnectedPlayer subject = null)
	{
		if (on && HasWorkingThrusters && (IsFueled || !RequiresFuel))
		{
			MatrixMoveEvents.OnStartEnginesServer.Invoke();
			EnginesOperational = true;
			moveNodes.GenerateMoveNodes(transform.position, serverFacingState.FlyingDirection.VectorInt);
			GetTargetMoveNode();
		}
		else
		{
			MatrixMoveEvents.OnStopEnginesServer.Invoke();
			EnginesOperational = false;
			if (on)
			{
				if (subject != null)
				{
					//Could not toggle the engines on for some reason, inform the player
					if (!HasWorkingThrusters)
					{
						Chat.AddExamineMsg(subject.GameObject,
							"The shuttle has no working thrusters and cannot be started.");
						return;
					}

					if (RequiresFuel && !IsFueled)
					{
						Chat.AddExamineMsg(subject.GameObject, "This shuttle has no fuel and cannot be started.");
						return;
					}
				}
			}
		}
	}

	[Server]
	public void ToggleRcs(bool on, ConnectedPlayer subject, uint consoleId)
	{
		rcsModeActive = on;
		if (on)
		{
			if (subject != null)
			{
				ToggleRcsPlayerControl.UpdateClient(subject, consoleId, true);
				CacheRcs();
				playerControllingRcs = subject;
				Chat.AddExamineMsg(subject.GameObject, "Rcs has been activated. Use movement keys to pilot");
			}
		}
		else
		{
			if (playerControllingRcs != null)
			{
				ToggleRcsPlayerControl.UpdateClient(playerControllingRcs, consoleId, false);
				playerControllingRcs = null;
			}
		}
	}

	[Server]
	public void StartMovement()
	{
		if (!HasWorkingThrusters)
		{
			RecheckThrusters();
		}

		//Not allowing movement without any thrusters:
		if (HasWorkingThrusters && (IsFueled || !RequiresFuel))
		{
			MatrixMoveEvents.OnStartEnginesServer.Invoke();

			RequestNotify();
		}
	}

	/// <summary>
	/// Change facing and flying direction to match specified direction if possible.
	/// If blocked, returns false. Can be used on client for predictive rotating
	/// </summary>
	/// <param name="desiredOrientation"></param>
	[Server]
	public bool SteerTo(Orientation desiredOrientation, double networkTime = 0.0)
	{
		if (CanRotateTo(desiredOrientation))
		{
			if (isServer)
			{
				double serverTime = NetworkTime.time;
				if (networkTime != 0.0)
				{
					serverTime = networkTime;
				}

				serverFacingState = new MatrixFacingState
				{
					RotationTime = 2f,
					FacingDirection = desiredOrientation,
					FlyingDirection = desiredOrientation,
					FacingDirectionNetworkTime = serverTime
				};
			}

			sharedFacingState = serverFacingState;

			rotateLerp = 0f;
			fromRotation = transform.rotation;

			MatrixMoveEvents.OnRotate.Invoke(new MatrixRotationInfo(this,
				sharedFacingState.FacingDirection.OffsetTo(desiredOrientation), isServer ? NetworkSide.Server : NetworkSide.Client, RotationEvent.Start));

			IsRotating = true;
			return true;
		}

		return false;
	}

	/// Stop movement
	[Server]
	public void StopMovement()
	{
		MatrixMoveEvents.OnStopEnginesServer.Invoke();

		//To stop autopilot
		DisableAutopilotTarget();
		TryNotifyPlayers();
	}

	[Server]
	public void ProcessStopRequest(ConnectedPlayer requestee, Vector2Int proposedStopTile)
	{
		Debug.Log($"Stop request received from: {requestee.Name} tile {proposedStopTile}");
	}

	/// Move for n tiles, regardless of direction, and stop
	[Server]
	public void MoveFor(int tiles)
	{
		if (tiles < 1)
		{
			tiles = 1;
		}

		if (!IsMovingServer)
		{
			StartMovement();
		}

		moveCur = 0;
		moveLimit = tiles;
	}

	/// Checks if it still can move according to MoveFor limits.
	/// If true, increment move count
	[Server]
	private bool CanMoveFor()
	{
		if (moveCur == moveLimit && moveCur != -1)
		{
			moveCur = -1;
			moveLimit = -1;
			return false;
		}

		moveCur++;
		return true;
	}

	/// Call to stop chasing target
	[Server]
	public void DisableAutopilotTarget()
	{
		Target = TransformState.HiddenPos;
	}



	[Server]
	public void ServerSetMoving(bool isMoving)
	{
		serverMotionState = new MatrixMotionState
		{
			IsMoving = isMoving,
			Speed = serverMotionState.Speed,
			Position = serverMotionState.Position,
			SpeedNetworkTime = serverMotionState.SpeedNetworkTime
		};
	}

	/// Serverside movement routine
	// [Server]
	// private void CheckMovementServer()
	// {
		//ServerState lerping to its target tile

		//	Vector3? actualNewPosition = null;
		//if (!ServerPositionsMatch || rcsBurn)
		//	{
		//some special logic needs to fire when we exactly reach our target tile,
		//but we want movement to continue as far as it should based on deltaTime
		//despite reaching / exceeding the target tile. So we save the actual new position
		//here and only update serverState.Position after that special logic has run.
		//Otherwise, movement speed will fluctuate slightly due to discarding excess movement that happens
		//when reaching an exact tile position and result in server movement jerkiness and inconsistent client predicted movement.

		//actual position we should reach this update, regardless of if we passed through the target position
//			actualNewPosition = serverState.Position + rcsValue +
//			                    serverState.FlyingDirection.Vector * (serverState.Speed * Time.deltaTime);
//			//update position without passing the target position
//			serverState.Position =
//				Vector3.MoveTowards(serverState.Position,
//					serverTargetState.Position,
//					serverState.Speed * Time.deltaTime);

		//At this point, if serverState.Position reached an exact tile position,
		//you can see that actualNewPosition != serverState.Position, so we will
		//need to carry that extra movement forward after processing the logic that
		//occurs on the exact tile position.
		//	TryNotifyPlayers();
		//	}

//		bool isGonnaStop = !serverTargetState.IsMoving;
//		if (!IsMovingServer || isGonnaStop || !ServerPositionsMatch)
//		{
//			return;
//		}

//		if (CanMoveFor() && (!SafetyProtocolsOn || CanMoveTo(serverTargetState.FlyingDirection)))
//		{
//			var goal = Vector3Int.RoundToInt(serverState.Position + rcsValue + serverTargetState.FlyingDirection.Vector);
//
//			//keep moving
//			serverTargetState.Position = goal;
//			if (IsAutopilotEngaged && ((int) serverState.Position.x == (int) Target.x
//			                           || (int) serverState.Position.y == (int) Target.y))
//			{
//				StartCoroutine(TravelToTarget());
//			}
//			//now we can carry on with any excess movement we had discarded earlier, now
//			//that we've already ran the logic that needs to happen on the exact tile position
//			if (actualNewPosition != null)
//			{
//				serverState.Position = actualNewPosition.Value;
//			}
//		}
//		else
//		{
////			Logger.LogTrace( "Stopping due to safety protocols!",Category.Matrix );
//			StopMovement();
//			TryNotifyPlayers();
//		}
//	}



	[Server]
	private void UpdateServerStatePosition(Vector2 position)
	{
		serverMotionState = new MatrixMotionState
		{
			IsMoving = serverMotionState.IsMoving,
			Speed = serverMotionState.Speed,
			Position = position,
			SpeedNetworkTime = serverMotionState.SpeedNetworkTime
		};
	}

	/// Manually set matrix to a specific position.
	[Server]
	public void SetPosition(Vector3 pos)
	{
		Vector3Int intPos = Vector3Int.RoundToInt(pos);
		transform.position = intPos;
		UpdateServerStatePosition(intPos.To2Int());
	}

	/// Schedule notification for the next ServerPositionsMatch
	/// And check if it's able to send right now
	[Server]
	private void RequestNotify()
	{
		TryNotifyPlayers();
	}

	///	Inform players when on integer position
	[Server]
	private void TryNotifyPlayers()
	{
//		if (ServerPositionsMatch)
//		{
////				When serverState reaches its planned destination,
////				embrace all other updates like changed speed and rotation
//			serverState = serverTargetState;
//			serverState.Inform = true;
//			Logger.LogTraceFormat("{0} setting server state from target state {1}", Category.Matrix, this, serverState);
//			NotifyPlayers();
//		}
	}

	///  Currently sending to everybody, but should be sent to nearby players only
	[Server]
	private void NotifyPlayers()
	{
//		//Generally not sending mid-flight updates (unless there's a sudden change of course etc.)
//		if (!IsMovingServer || serverState.Inform)
//		{
//			serverState.RotationTime = rotTime;
//			//fixme: this whole class behaves like ass!
//			if ( serverState.RotationTime != serverTargetState.RotationTime )
//			{ //Doesn't guarantee that matrix will stop
//				MatrixMoveMessage.SendToAll(gameObject, serverState);
//			} else
//			{ //Ends up in instant rotations
//				MatrixMoveMessage.SendToAll(gameObject, serverTargetState);
//			}
//			//Clear inform flags
//			serverTargetState.Inform = false;
//			serverState.Inform = false;
//		}
	}

	/// Changes flying direction without rotating the shuttle, for use in reversing in EscapeShuttle
	[Server]
	public void ChangeFlyingDirection(Orientation newFlyingDirection)
	{
		//	serverTargetState.FlyingDirection = newFlyingDirection;
		Logger.LogTraceFormat("{0} server target flying {1}", Category.Matrix, this, newFlyingDirection);
	}

	/// Changes facing direction without changing flying direction, for use in reversing in EscapeShuttle
	[Server]
	public bool ChangeFacingDirection(Orientation newFacingDirection)
	{
		if (CanRotateTo(newFacingDirection))
		{
			//		serverTargetState.FacingDirection = newFacingDirection;
			Logger.LogTraceFormat("{0} server target facing  {1}", Category.Matrix, this, newFacingDirection);

			MatrixMoveEvents.OnRotate.Invoke(new MatrixRotationInfo(this,
				serverFacingState.FacingDirection.OffsetTo(newFacingDirection), NetworkSide.Server, RotationEvent.Start));

			RequestNotify();
			return true;
		}

		return false;
	}

	/// Makes matrix start moving towards given world pos
	[Server]
	public void AutopilotTo(Vector2 position)
	{
		Target = position;
		StartCoroutine(TravelToTarget());
	}

	public void SetAccuracy(int newAccuracy)
	{
		AccuracyThreshold = newAccuracy;
	}

	private IEnumerator TravelToTarget()
	{
		if (IsAutopilotEngaged)
		{
			var pos = serverMotionState.Position;
			if (Vector3.Distance(pos, Target) <= AccuracyThreshold)
			{
				StopMovement();
				yield break;
			}

			Orientation currentDir = serverFacingState.FlyingDirection;

			Vector3 xProjection = Vector3.Project(pos, Vector3.right);
			int xProjectionX = (int) xProjection.x;
			int targetX = (int) Target.x;

			Vector3 yProjection = Vector3.Project(pos, Vector3.up);
			int yProjectionY = (int) yProjection.y;
			int targetY = (int) Target.y;

			bool xNeedsChange = Mathf.Abs(xProjectionX - targetX) > AccuracyThreshold;
			bool yNeedsChange = Mathf.Abs(yProjectionY - targetY) > AccuracyThreshold;

			Orientation xDesiredDir = targetX - xProjectionX > 0 ? Orientation.Right : Orientation.Left;
			Orientation yDesiredDir = targetY - yProjectionY > 0 ? Orientation.Up : Orientation.Down;

			if (xNeedsChange || yNeedsChange)
			{
				int xRotationsTo = xNeedsChange ? currentDir.RotationsTo(xDesiredDir) : int.MaxValue;
				int yRotationsTo = yNeedsChange ? currentDir.RotationsTo(yDesiredDir) : int.MaxValue;

				//don't rotate if it's not needed
				if (xRotationsTo != 0 && yRotationsTo != 0)
				{
					//if both need change determine faster rotation first
					SteerTo(xRotationsTo < yRotationsTo ? xDesiredDir : yDesiredDir);
					//wait till it rotates
					yield return WaitFor.Seconds(1);
				}
			}

			if (!serverMotionState.IsMoving)
			{
				StartMovement();
			}

			//Relaunching self once in a while as CheckMovementServer check can fail in rare occasions
			yield return WaitFor.Seconds(1);
			StartCoroutine(TravelToTarget());
		}

		yield return null;
	}
}