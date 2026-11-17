namespace Coconut.Audio.Tests;

public sealed class AudioEffectPolicyTests
{
    [Fact]
    public void Driver_enabling_ai_after_reopen_is_corrected_without_disabling_aec()
    {
        var echo = Guid.NewGuid(); var aiOn = false; var changes = 0;
        IReadOnlyList<AudioEffect> Query() => [new(echo, "AEC", true, true), new(AudioEffectPolicy.AiNoiseSuppression, "IA", true, aiOn)];
        void Set(Guid id, bool enabled) { Assert.Equal(AudioEffectPolicy.AiNoiseSuppression, id); Assert.False(enabled); aiOn = enabled; changes++; }
        AudioEffectPolicy.Enforce(Query, Set, true); Assert.Equal(0, changes);
        aiOn = true; var effects = AudioEffectPolicy.Enforce(Query, Set, true);
        Assert.Equal(1, changes); Assert.False(effects.Single(e => e.Id == AudioEffectPolicy.AiNoiseSuppression).IsOn); Assert.True(effects.Single(e => e.Id == echo).IsOn);
        aiOn = true; AudioEffectPolicy.Enforce(Query, Set, true); Assert.Equal(2, changes);
    }

    [Fact]
    public void Unsupported_or_explicitly_enabled_processing_is_not_changed()
    {
        IReadOnlyList<AudioEffect> Query() => [new(AudioEffectPolicy.AiNoiseSuppression, "IA", false, true)];
        Assert.True(AudioEffectPolicy.Enforce(Query, (_, _) => throw new InvalidOperationException(), true)[0].IsOn);
        Assert.True(AudioEffectPolicy.Enforce(Query, (_, _) => throw new InvalidOperationException(), false)[0].IsOn);
    }
}
