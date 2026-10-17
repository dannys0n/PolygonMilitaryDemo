// Air crew locomotion is disabled by their mounted seats; the vehicle's air AI owns input.
public sealed class SandboxCrewInput : SoldierInputSource
{
    public override SoldierInputFrame ReadInput()=>default;
}
