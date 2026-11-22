using System;
using Unity.Netcode;

// One atomic replicated value also gives late joiners the correct overlay and clock.
public struct CapturePresentationState : INetworkSerializable, IEquatable<CapturePresentationState>
{
    public const int Taken=1, Defended=2, GoalReached=3, SwitchSides=4, NextPoint=5;
    public int kind, point, team, next;
    public double started;
    public float duration;
    public bool Active(double now) => kind != 0 && now >= started && now < started + duration;
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T:IReaderWriter
    {
        serializer.SerializeValue(ref kind); serializer.SerializeValue(ref point);
        serializer.SerializeValue(ref team); serializer.SerializeValue(ref next);
        serializer.SerializeValue(ref started); serializer.SerializeValue(ref duration);
    }
    public bool Equals(CapturePresentationState other) => kind==other.kind && point==other.point && team==other.team
        && next==other.next && started.Equals(other.started) && duration.Equals(other.duration);
}
