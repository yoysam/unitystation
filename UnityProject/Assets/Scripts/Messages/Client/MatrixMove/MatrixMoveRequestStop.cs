using Mirror;
using UnityEngine;

public class MatrixMoveRequestStop : ClientMessage
{
	public uint MatrixMove;
	public Vector2Int ProposedStopTile;
	public double TimeOfStop;

	public override void Process()
	{
		LoadNetworkObject(MatrixMove);
		NetworkObject.GetComponent<MatrixMove>().ProcessStopRequest(SentByPlayer, ProposedStopTile, TimeOfStop);
	}

	public static MatrixMoveRequestStop Send(uint matrixMoveNetId, Vector2Int proposedStopTile, double stopTime)
	{
		MatrixMoveRequestStop msg = new MatrixMoveRequestStop
		{
			MatrixMove = matrixMoveNetId,
			ProposedStopTile = proposedStopTile,
			TimeOfStop = stopTime
		};
		msg.Send();
		return msg;
	}

	public override void Deserialize(NetworkReader reader)
	{
		base.Deserialize(reader);
		MatrixMove = reader.ReadUInt32();
		ProposedStopTile = reader.ReadVector2Int();
		TimeOfStop = reader.ReadDouble();
	}

	public override void Serialize(NetworkWriter writer)
	{
		base.Serialize(writer);
		writer.WriteUInt32(MatrixMove);
		writer.WriteVector2Int(ProposedStopTile);
		writer.WriteDouble(TimeOfStop);
	}
}
