using System;
using UnityEngine;

/// <summary>
/// Encapsulates the state of a matrix's motion / position
/// </summary>
public struct MatrixMotionState : IEquatable<MatrixMotionState>
{
	public bool IsMoving;
	public float Speed;

	public Vector3 Position;
	public uint Interactee;

	public bool Equals(MatrixMotionState other)
	{
		return IsMoving == other.IsMoving && Speed.Equals(other.Speed) && Position.Equals(other.Position) && Equals(Interactee, other.Interactee);
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
			hashCode = (hashCode * 397) ^ Position.GetHashCode();
			hashCode = (hashCode * 397) ^ (Interactee != null ? Interactee.GetHashCode() : 0);
			return hashCode;
		}
	}
}
