using Mirror;
using UnityEngine;

public class MatrixMoveRotateRequest : ClientMessage
{
	public uint MatrixMove;
	public double NetworkTime;
	public OrientationEnum FacingDirection;
	public GameObject Interactee;

	public override void Process()
	{
		LoadNetworkObject(MatrixMove);
		//TODO: Validation with the interactee. Try to find the shuttle gui and measure the distance
		NetworkObject.GetComponent<MatrixMove>().SteerTo(Orientation.FromEnum(FacingDirection), NetworkTime);
	}

	public static MatrixMoveRotateRequest Send(uint matrixMoveNetId, GameObject interactee, double networkTime, OrientationEnum dir)
	{
		MatrixMoveRotateRequest msg = new MatrixMoveRotateRequest
		{
			MatrixMove = matrixMoveNetId,
			NetworkTime = networkTime,
			FacingDirection = dir,
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
		FacingDirection = (OrientationEnum)reader.ReadInt32();
		Interactee = reader.ReadGameObject();
	}

	public override void Serialize(NetworkWriter writer)
	{
		base.Serialize(writer);
		writer.WriteUInt32(MatrixMove);
		writer.WriteDouble(NetworkTime);
		writer.WriteInt32((int)FacingDirection);
		writer.WriteGameObject(Interactee);
	}
}
