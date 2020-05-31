using Mirror;
using UnityEngine;

public class MatrixMoveSpeedRequest : ClientMessage
{
	public uint MatrixMove;
	public double NetworkTime;
	public float Speed;
	public GameObject Interactee;

	public override void Process()
	{
		LoadNetworkObject(MatrixMove);
		if (Vector3.Distance(Interactee.transform.localPosition, NetworkObject.transform.localPosition) < 4f)
		{
			NetworkObject.GetComponent<MatrixMove>().SetSpeed(Speed, NetworkTime);
		}
	}

	public static MatrixMoveSpeedRequest Send(uint matrixMoveNetId, GameObject interactee, double networkTime, float speed)
	{
		MatrixMoveSpeedRequest msg = new MatrixMoveSpeedRequest
		{
			MatrixMove = matrixMoveNetId,
			NetworkTime = networkTime,
			Speed = speed,
			Interactee = interactee
		};
		msg.Send();
		return msg;
	}

	public override void Deserialize(NetworkReader reader)
	{
		base.Deserialize(reader);
		MatrixMove = reader.ReadUInt32();
		NetworkTime = reader.ReadDouble();
		Speed = (float)reader.ReadDouble();
		Interactee = reader.ReadGameObject();
	}

	public override void Serialize(NetworkWriter writer)
	{
		base.Serialize(writer);
		writer.WriteUInt32(MatrixMove);
		writer.WriteDouble(NetworkTime);
		writer.WriteDouble(Speed);
		writer.WriteGameObject(Interactee);
	}
}
