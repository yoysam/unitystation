using UnityEngine;

/// <summary>
/// Sends clients rcs move commands to the server
/// </summary>
public class RcsMovementMessage : ClientMessage
{
	public Vector2Int Direction;
	public uint MatrixMoveNetId;
	public double NetworkTime;

	public override void Process()
	{
		LoadNetworkObject(MatrixMoveNetId);
		//TODO Validate the distance between the shuttle console and the sentbyplayer
		NetworkObject.GetComponent<MatrixMove>().ProcessRcsMoveRequest(NetworkTime, Direction);
	}

	public static RcsMovementMessage Send(Vector2Int direction, uint matrixMoveId, double networkTime)
	{
		var msg = new RcsMovementMessage
		{
			Direction = direction,
			MatrixMoveNetId = matrixMoveId,
			NetworkTime = networkTime
		};
		msg.Send();
		return msg;
	}
}
