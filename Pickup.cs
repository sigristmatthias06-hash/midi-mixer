namespace MidiMixer;

sealed class Pickup
{
    const float Tolerance = 1.5f / 127;
    float? previous;
    public bool Acquired { get; private set; }
    public void Reset() { Acquired = false; previous = null; }
    public bool Accept(float position, float target)
    {
        if (!Acquired)
            Acquired = Math.Abs(position - target) <= Tolerance ||
                previous.HasValue && ((previous.Value < target && position >= target) || (previous.Value > target && position <= target));
        previous = position;
        return Acquired;
    }
    public string Hint(float target) => Acquired ? "" : previous.HasValue ? (previous < target ? " ↑ abholen" : " ↓ abholen") : " · abholen";
    internal static void Verify()
    {
        var p = new Pickup();
        if (p.Accept(.1f, .7f) || p.Accept(.5f, .7f) || !p.Accept(.72f, .7f) || !p.Accept(.2f, .72f)) throw new Exception("Pickup upward crossing failed");
        p.Reset();
        if (p.Accept(.9f, .2f) || !p.Accept(.19f, .2f)) throw new Exception("Pickup downward crossing failed");
        p.Reset(); if (!p.Accept(0, 0)) throw new Exception("Pickup zero failed");
        p.Reset(); if (!p.Accept(1, 1)) throw new Exception("Pickup full failed");
        p.Reset(); if (p.Accept(.1f, .8f)) throw new Exception("Pickup reset failed");
    }
}
