using System;

/// <summary>
/// Encapsulates the state of a matrix's rotation / facing
/// </summary>
public struct MatrixFacingState : IEquatable<MatrixFacingState>
{
	public float RotationTime;

	/// <summary>
	/// Direction we are facing. Not always the same as flying direction, as some shuttles
	/// can back up.
	/// </summary>
	public Orientation FacingDirection;

	/// <summary>
	/// Current flying direction. Note this may not always match the rotation of the ship, as shuttles
	/// can back up.
	/// </summary>
	public Orientation FlyingDirection;

	/// <summary>
	/// Last time facing direction was changed
	/// </summary>
	public double FacingDirectionNetworkTime;

	/// <summary>
	/// Gets the rotation offset this state represents from the matrix move's initial mapped
	/// facing.
	/// </summary>
	/// <param name="matrixMove"></param>
	public RotationOffset FacingOffsetFromInitial(MatrixMove matrixMove)
	{
		return matrixMove.InitialFacing.OffsetTo(FacingDirection);
	}

	public bool Equals(MatrixFacingState other)
	{
		return RotationTime.Equals(other.RotationTime) && FacingDirection.Equals(other.FacingDirection) &&
		       FlyingDirection.Equals(other.FlyingDirection) &&
		       FacingDirectionNetworkTime.Equals(other.FacingDirectionNetworkTime);
	}

	public override bool Equals(object obj)
	{
		return obj is MatrixFacingState other && Equals(other);
	}

	public override int GetHashCode()
	{
		unchecked
		{
			var hashCode = RotationTime.GetHashCode();
			hashCode = (hashCode * 397) ^ FacingDirection.GetHashCode();
			hashCode = (hashCode * 397) ^ FlyingDirection.GetHashCode();
			hashCode = (hashCode * 397) ^ FacingDirectionNetworkTime.GetHashCode();
			return hashCode;
		}
	}
}