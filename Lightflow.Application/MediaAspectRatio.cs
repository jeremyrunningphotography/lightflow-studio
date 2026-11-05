using System.Globalization;
using System.Text.Json;

namespace LightflowStudio;

/// <summary>Exact rational identity, independent of formatted floating point values.</summary>
public readonly record struct MediaAspectRatio
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public int Numerator { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public int Denominator { get; }
    [System.Text.Json.Serialization.JsonConstructor]
    public MediaAspectRatio(int numerator, int denominator)
    {
        if (numerator <= 0 || denominator <= 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        var a = numerator;
        var b = denominator;
        while (b != 0) (a, b) = (b, a % b);
        Numerator = numerator / a;
        Denominator = denominator / a;
    }
    public MediaAspectRatio Rotate(VideoRotation rotation)
    {
        var size = rotation.Dimensions(Numerator, Denominator);
        return new(size.Width, size.Height);
    }
}

