using System;

/// <summary>
/// Version-independent identity for a UnityEngine.Object.
///
/// Unity 6000.3:
///     Object.GetInstanceID()
///
/// Unity 6000.4+:
///     Object.GetEntityId()
///
/// This identifier is intended for in-memory/session identity only.
/// It must not be persisted between Unity Editor or Player sessions.
/// </summary>
public readonly struct GamaUnityObjectId : IEquatable<GamaUnityObjectId>
{
#if UNITY_6000_4_OR_NEWER

    private readonly UnityEngine.EntityId value;

    private GamaUnityObjectId(UnityEngine.EntityId value)
    {
        this.value = value;
    }

#else

    private readonly int value;

    private GamaUnityObjectId(int value)
    {
        this.value = value;
    }

#endif

    public static GamaUnityObjectId From(UnityEngine.Object obj)
    {
        if (obj == null)
        {
            return default;
        }

#if UNITY_6000_4_OR_NEWER

        return new GamaUnityObjectId(obj.GetEntityId());

#else

        return new GamaUnityObjectId(obj.GetInstanceID());

#endif
    }

    public bool Equals(GamaUnityObjectId other)
    {
        return value.Equals(other.value);
    }

    public override bool Equals(object obj)
    {
        return obj is GamaUnityObjectId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return value.GetHashCode();
    }

    public override string ToString()
    {
        return value.ToString();
    }

    public static bool operator ==(
        GamaUnityObjectId left,
        GamaUnityObjectId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(
        GamaUnityObjectId left,
        GamaUnityObjectId right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Convenience extension used throughout SIMPLE so Unity object identity
/// is isolated from Unity-version-specific APIs.
/// </summary>
public static class GamaUnityObjectIdExtensions
{
    public static GamaUnityObjectId GetGamaObjectId(
        this UnityEngine.Object obj)
    {
        return GamaUnityObjectId.From(obj);
    }
}
