namespace Coconut.Audio;

/// <summary>Re-applied as drivers initialize or change effects; AEC and ordinary noise processing are untouched.</summary>
public static class AudioEffectPolicy
{
    public static readonly Guid AiNoiseSuppression = new("6f64add0-8211-11e2-8c70-2c27d7f001fa");

    public static IReadOnlyList<AudioEffect> Enforce(Func<IReadOnlyList<AudioEffect>> query, Action<Guid, bool> set, bool disableAi)
    {
        ArgumentNullException.ThrowIfNull(query); ArgumentNullException.ThrowIfNull(set);
        var effects = query();
        if (disableAi && effects.Any(e => e.Id == AiNoiseSuppression && e.CanSetState && e.IsOn))
        { set(AiNoiseSuppression, false); return query(); }
        return effects;
    }
}
