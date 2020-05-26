using Mirror;
using UnityEngine;

public class MatrixMoveRequestStop : ClientMessage
{
	public uint MatrixMove;
	public Vector2Int ProposedStopTile;

	public override void Process()
	{
		LoadNetworkObject(MatrixMove);
		NetworkObject.GetComponent<MatrixMove>().ProcessStopRequest(SentByPlayer, ProposedStopTile);
	}

	public static MatrixMoveRequestStop Send(uint matrixMoveNetId, Vector2Int proposedStopTile)
	{
		MatrixMoveRequestStop msg = new MatrixMoveRequestStop
		{
			MatrixMove = matrixMoveNetId,
			ProposedStopTile = proposedStopTile
		};
		msg.Send();
		return msg;
	}

	public override void Deserialize(NetworkReader reader)
	{
		base.Deserialize(reader);
		MatrixMove = reader.ReadUInt32();
		ProposedStopTile = reader.ReadVector2Int();
	}

	public override void Serialize(NetworkWriter writer)
	{
		base.Serialize(writer);
		writer.WriteUInt32(MatrixMove);
		writer.WriteVector2Int(ProposedStopTile);
	}
}
