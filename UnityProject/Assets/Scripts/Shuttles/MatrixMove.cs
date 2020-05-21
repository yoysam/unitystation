using System;
using System.Collections.Generic;
using System.Linq;
using Light2D;
using UnityEngine;
using Mirror;
using UnityEngine.Serialization;

/// <summary>
/// Behavior which allows an entire matrix to move and rotate (and be synced over the network).
/// This behavior must go on a gameobject that is the parent of the gameobject that has the actual Matrix component.
/// </summary>
public partial class MatrixMove : ManagedNetworkBehaviour, IPlayerControllable
{
	/// <summary>
	/// Set this to make sure collisions are correct for the MatrixMove
	/// For example, shuttles collide with floors but players don't
	/// </summary>
	public CollisionType matrixColliderType = CollisionType.Shuttle;

	/// <summary>
	/// If anything has a specific UI that needs to be set, it can change based off this var
	/// </summary>
	public UIType uiType = UIType.Nanotrasen;

	[Tooltip("Initial facing of the ship. Very important to set this correctly!")]
	[SerializeField]
	private OrientationEnum initialFacing;

	[Tooltip("Does it require fuel in order to fly?")]
	public bool RequiresFuel;

	/// <summary>
	/// Initial facing of the ship as mapped in the editor.
	/// </summary>
	public Orientation InitialFacing => Orientation.FromEnum(initialFacing);

	[Tooltip("Max flying speed of this matrix.")]
	[FormerlySerializedAs("maxSpeed")]
	public float MaxSpeed = 20f;

	[SyncVar][Tooltip("Whether safety is currently on, preventing collisions when sensors detect them.")]
	public bool SafetyProtocolsOn = true;


	[SyncVar(hook = nameof(SyncInitialPosition))]
	private Vector3 initialPosition;
	/// <summary>
	/// Initial position for offset calculation, set on start and never changed afterwards
	/// </summary>
	public Vector3Int InitialPosition => initialPosition.RoundToInt();

	[SyncVar(hook = nameof(SyncPivot))]
	private Vector3 pivot;
	/// <summary>
	/// local pivot point, set on start and never changed afterwards
	/// </summary>
	public Vector3Int Pivot => pivot.RoundToInt();

	/// <summary>
	/// All the various events that can be subscribed to on this matrix
	/// </summary>
	public readonly MatrixMoveEvents MatrixMoveEvents = new MatrixMoveEvents();

	/// <summary>
	/// Gets the rotation offset this matrix has from its initial mapped
	/// facing.
	/// </summary>
	public RotationOffset FacingOffsetFromInitial => ServerState.FacingOffsetFromInitial(this);

	/// <summary>
	/// If it is currently fuelled
	/// </summary>
	[NonSerialized]
	public bool IsFueled;

	private bool IsAutopilotEngaged => Target != TransformState.HiddenPos;

	private MatrixInfo matrixInfo;
	public MatrixInfo MatrixInfo => matrixInfo;
	private ShuttleFuelSystem shuttleFuelSystem;
	public ShuttleFuelSystem ShuttleFuelSystem => shuttleFuelSystem;

	private MatrixPositionFilter matrixPositionFilter = new MatrixPositionFilter();

	private Coroutine floatingSyncHandle;

	private List<ShipThruster> thrusters = new List<ShipThruster>();
	public bool HasWorkingThrusters => thrusters.Count > 0;

	private Vector3Int[] SensorPositions;
	private GameObject[] RotationSensors;
	private GameObject rotationSensorContainerObject;
	/// <summary>
	/// Tracks the rotation we are currently performing.
	/// Null when a rotation is not in progress.
	/// NOTE: This is not an offset from initialfacing, it's an offset from our current facing. So
	/// if we are turning 90 degrees right, this will be Right no matter what our initial conditions were.
	/// </summary>
	private RotationOffset? inProgressRotation;

	private readonly int rotTime = 90;
	[HideInInspector]
	private GUI_CoordReadout coordReadoutScript;

	private GUI_ShuttleControl shuttleControlGUI;
	private int moveCur = -1;
	private int moveLimit = -1;

	private bool IsRotating;
	private float rotateLerp = 0f;
	private Quaternion fromRotation;

	private MatrixMoveNodes moveNodes = new MatrixMoveNodes();

	public MatrixState SharedState;

	private float moveLerp = 1f;
	private Vector2 fromPosition;
	private Vector2 toPosition;
	private bool performingMove = false;

	private void RecheckThrusters()
	{
		thrusters = GetComponentsInChildren<ShipThruster>(true).ToList();
	}

	public void RegisterShuttleFuelSystem(ShuttleFuelSystem shuttleFuel)
	{
		this.shuttleFuelSystem = shuttleFuel;
	}

	public void RegisterShuttleGuiScript(GUI_ShuttleControl shuttleGui)
	{
		shuttleControlGUI = shuttleGui;
	}
	public void RegisterCoordReadoutScript(GUI_CoordReadout coordReadout)
	{
		coordReadoutScript = coordReadout;
	}

	public override void UpdateMe()
	{
		if (RotateMatrix()) return;
		MoveMatrix();
	}

	private bool RotateMatrix()
	{
		if (IsRotating)
		{
			//rotate our transform to our new facing direction
			if (SharedState.RotationTime != 0)
			{
				rotateLerp += Time.deltaTime * SharedState.RotationTime;
				//animate rotation
				transform.rotation =
					Quaternion.Lerp(fromRotation,
						InitialFacing.OffsetTo(SharedState.FacingDirection).Quaternion,
						rotateLerp);

				if (rotateLerp >= 1f)
				{
					moveNodes.GenerateMoveNodes(transform.position, SharedState.FlyingDirection.VectorInt);
					transform.rotation = InitialFacing.OffsetTo(SharedState.FacingDirection).Quaternion;
					IsRotating = false;
					GetTargetMoveNode();
				}
			}
			else
			{
				//rotate instantly
				transform.rotation = InitialFacing.OffsetTo(SharedState.FacingDirection).Quaternion;
				IsRotating = false;
				moveNodes.GenerateMoveNodes(transform.position, SharedState.FlyingDirection.VectorInt);
				GetTargetMoveNode();
			}

			return true;
		}
		return false;
	}

	private void MoveMatrix()
	{
		if (EnginesOperational && SharedState.Speed > 0f)
		{
			performingMove = true;
		//	if(!isServer) Debug.Log($"ml {moveLerp} from {fromPosition} to {toPosition}");
			moveLerp += Time.deltaTime * SharedState.Speed;
			transform.position = Vector2.Lerp(fromPosition, toPosition, moveLerp);
			matrixPositionFilter.FilterPosition(transform, transform.position, SharedState.FlyingDirection, rcsBurn);
			if (moveLerp >= 1f)
			{
				performingMove = false;
			//	Debug.Log("End pos: " + toPosition + " time:  " + NetworkTime.time);
				CreateHistoryNode();
				if (isServer)
				{
					UpdateServerStatePosition(toPosition);
					GetTargetMoveNode();
				}
				else
				{
					if (!ClientLagMonitor())
					{
						GetTargetMoveNode();
					}
				}


				if (rcsBurn) DoEndRcsBurnChecks();
			}
		}
		else
		{
			if (rcsBurn || performingMove)
			{
				performingMove = true;
			//	Debug.Log($"MOVE THIS THING TO : {toPosition}");
				moveLerp += Time.deltaTime * 1f;
				transform.position = Vector2.Lerp(fromPosition, toPosition, moveLerp);
				matrixPositionFilter.FilterPosition(transform, transform.position, SharedState.FlyingDirection, rcsBurn);
				if (moveLerp >= 1f)
				{
					performingMove = false;
					CreateHistoryNode();
					if (isServer)
					{
						UpdateServerStatePosition(toPosition);
						GetTargetMoveNode();
					}
					else
					{
						if (!ClientLagMonitor())
						{
							GetTargetMoveNode();
						}
					}

					transform.position = toPosition; //sometimes it is ever so slightly off the target
					DoEndRcsBurnChecks();
				}
			}
		}
	}

	private void CreateHistoryNode()
	{
		var node = moveNodes.AddHistoryNode(toPosition.To2Int(), NetworkTime.time);
		if (isServer)
		{
			RpcReceiveServerHistoryNode(node);
		}
	}

	void GetTargetMoveNode()
	{
		if (!CanMoveTo(SharedState.FlyingDirection) && SafetyProtocolsOn) return;

		moveLerp = 0f;
		fromPosition = transform.position;
		toPosition = moveNodes.GetTargetNode(SharedState.FlyingDirection.VectorInt);
	}

	///Only change orientation if rotation is finished
	/// Can be used on client for predictive rotating
	public void TryRotate(bool clockwise)
	{
		if (!IsRotating)
		{
			SteerTo(ServerState.FacingDirection.Rotate(clockwise ? 1 : -1));
		}
	}

	/// <summary>
	/// Change facing and flying direction to match specified direction if possible.
	/// If blocked, returns false. Can be used on client for predictive rotating
	/// </summary>
	/// <param name="desiredOrientation"></param>
	public bool SteerTo(Orientation desiredOrientation)
	{
		if (CanRotateTo(desiredOrientation))
		{
			if (isServer)
			{
				ServerState = new MatrixState
				{
					IsMoving = ServerState.IsMoving,
					Speed = ServerState.Speed,
					RotationTime = 2f,
					Position = ServerState.Position,
					FacingDirection = desiredOrientation,
					FlyingDirection = desiredOrientation,
				};
			}

			SharedState.RotationTime = 2f;
			SharedState.FacingDirection = desiredOrientation;
			SharedState.FlyingDirection = desiredOrientation;

			fromRotation = transform.rotation;
			rotateLerp = 0f;
			IsRotating = true;

			MatrixMoveEvents.OnRotate.Invoke(new MatrixRotationInfo(this,
				SharedState.FacingDirection.OffsetTo(desiredOrientation), isServer ? NetworkSide.Server : NetworkSide.Client, RotationEvent.Start));

			return true;
		}

		return false;
	}

	/// Set ship's speed using absolute value. it will be truncated if it's out of bounds
	public void SetSpeed(float absoluteValue)
	{
		var speed = Mathf.Clamp(absoluteValue, 0f, MaxSpeed);
		if (isServer)
		{
			ServerState = new MatrixState
			{
				IsMoving = ServerState.IsMoving,
				Speed = Mathf.Clamp(absoluteValue, 0f, MaxSpeed),
				RotationTime = ServerState.RotationTime,
				Position = ServerState.Position,
				FacingDirection = ServerState.FacingDirection,
				FlyingDirection = ServerState.FlyingDirection
			};
		}

		SharedState.Speed = speed;
	}

	public override void LateUpdateMe()
	{
		if (isClient)
		{
			if(coordReadoutScript != null) coordReadoutScript.SetCoords(ServerState.Position);
			if (shuttleControlGUI != null && rcsModeActive != shuttleControlGUI.RcsMode)
			{
				shuttleControlGUI.ClientToggleRcs(rcsModeActive);
			}
		}
	}

	private bool CanMoveTo(Orientation direction)
	{
		if (SensorPositions == null) return true;

		Vector3 dir = direction.Vector;

		//		check if next tile is passable
		for (var i = 0; i < SensorPositions.Length; i++)
		{
			var sensor = SensorPositions[i];
			Vector3Int sensorPos = MatrixManager.LocalToWorldInt(sensor, matrixInfo, ServerState);

			// Exclude the moving matrix, we shouldn't be able to collide with ourselves
			int[] excludeList = { matrixInfo.Id };
			if (!MatrixManager.IsPassableAt(sensorPos, sensorPos + dir.RoundToInt(), isServer: true,
											collisionType: matrixColliderType, excludeList: excludeList))
			{
				return false;
			}
		}

//		Logger.LogTrace( $"Passing {serverTargetState.Position}->{serverTargetState.Position+dir} ", Category.Matrix );
		return true;
	}

	private bool CanRotateTo(Orientation flyingDirection)
	{
		if (rotationSensorContainerObject == null) { return true; }

		// Feign a rotation using GameObjects for reference
		Transform rotationSensorContainerTransform = rotationSensorContainerObject.transform;
		rotationSensorContainerTransform.rotation = new Quaternion();
		rotationSensorContainerTransform.Rotate(0f, 0f, 90f * ServerState.FlyingDirection.RotationsTo(flyingDirection));

		for (var i = 0; i < RotationSensors.Length; i++)
		{
			var sensor = RotationSensors[i];
			// Need to pass an aggriate local vector in reference to the Matrix GO to get the correct WorldPos
			Vector3 localSensorAggrigateVector = (rotationSensorContainerTransform.localRotation * sensor.transform.localPosition) + rotationSensorContainerTransform.localPosition;
			Vector3Int sensorPos = MatrixManager.LocalToWorldInt(localSensorAggrigateVector, matrixInfo, ServerState);

			// Exclude the rotating matrix, we shouldn't be able to collide with ourselves
			int[] excludeList = { matrixInfo.Id };
			if (!MatrixManager.IsPassableAt(sensorPos, sensorPos, isServer: true,
											collisionType: matrixColliderType, includingPlayers: true, excludeList: excludeList))
			{
				return false;
			}
		}

		return true;
	}

#if UNITY_EDITOR
	//Visual debug
	private Vector3 size1 = Vector3.one;
	private Vector3 size2 = new Vector3(0.9f, 0.9f, 0.9f);
	private Vector3 size3 = new Vector3(0.8f, 0.8f, 0.8f);
	private Color color1 = Color.red;
	private Color color2 = DebugTools.HexToColor("81a2c7");
	private Color color3 = Color.white;

	private void OnDrawGizmos()
	{
		if ( !Application.isPlaying )
		{ //Showing matrix pivot if game is stopped
			Gizmos.color = color1.WithAlpha( 0.6f );
			Gizmos.DrawCube(transform.position, Vector3.one );
			Gizmos.color = color1;
			Gizmos.DrawWireCube(transform.position, Vector3.one );

			DebugGizmoUtils.DrawArrow(transform.position, ServerState.FlyingDirection.Vector*2);
			return;
		}

		//serverState
		Gizmos.color = color1;
		Vector3 serverPos = ServerState.Position;
		Gizmos.DrawWireCube(serverPos, size1);
		if (ServerState.IsMoving)
		{
			DebugGizmoUtils.DrawArrow(serverPos + Vector3.right / 3, ServerState.FlyingDirection.Vector * ServerState.Speed);
			DebugGizmoUtils.DrawText(ServerState.Speed.ToString(), serverPos + Vector3.right, 15);
		}
	}
#endif
}

public enum UIType
{
	Default = 0,
	Nanotrasen = 1,
	Syndicate = 2
};