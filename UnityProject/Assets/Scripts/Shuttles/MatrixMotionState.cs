using System;
using UnityEngine;

/// <summary>
/// Encapsulates the state of a matrix's motion / position
/// </summary>
public struct MatrixMotionState : IEquatable<MatrixMotionState>
{
	public bool IsMoving;

	public float Speed;

	//last time the speed was adjusted
	public double SpeedNetworkTime;

	public Vector3 Position;

	public bool Equals(MatrixMotionState other)
	{
		return IsMoving == other.IsMoving && Speed.Equals(other.Speed) &&
		       SpeedNetworkTime.Equals(other.SpeedNetworkTime) && Position.Equals(other.Position);
	}

	public override bool Equals(object obj)
	{
		return obj is MatrixMotionState other && Equals(other);
	}

	public override int GetHashCode()
	{
		unchecked
		{
			var hashCode = IsMoving.GetHashCode();
			hashCode = (hashCode * 397) ^ Speed.GetHashCode();
			hashCode = (hashCode * 397) ^ SpeedNetworkTime.GetHashCode();
			hashCode = (hashCode * 397) ^ Position.GetHashCode();
			return hashCode;
		}
	}
}