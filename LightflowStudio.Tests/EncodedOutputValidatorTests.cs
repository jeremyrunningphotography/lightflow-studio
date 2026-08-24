using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class EncodedOutputValidatorTests
{
    [Fact]
    public void Validate_RequiresPlayableStreamsAndExpectedDuration()
    {
        const string valid = """{"streams":[{"codec_type":"video","codec_name":"h264"},{"codec_type":"audio","codec_name":"aac"}],"format":{"duration":"1.501"}}""";
        Assert.True(EncodedOutputValidator.TryValidate(valid, TimeSpan.FromSeconds(1.5), true, out _));
        Assert.False(EncodedOutputValidator.TryValidate(valid, TimeSpan.FromSeconds(4), true, out var durationError));
        Assert.Contains("differs", durationError);
        Assert.StartsWith("Exported duration", durationError);
        Assert.False(EncodedOutputValidator.TryValidate("""{"streams":[{"codec_type":"video"}],"format":{"duration":"1.5"}}""", TimeSpan.FromSeconds(1.5), true, out var audioError));
        Assert.Contains("exported file", audioError);
        Assert.DoesNotContain("encoded", durationError + audioError, StringComparison.OrdinalIgnoreCase);
    }
}
