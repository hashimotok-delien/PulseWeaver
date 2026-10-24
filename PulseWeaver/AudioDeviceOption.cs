namespace PulseWeaver;

internal sealed record AudioDeviceOption(string Id, string Name)
{
    public override string ToString() => Name;
}
