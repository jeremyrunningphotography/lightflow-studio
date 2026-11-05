using System.Globalization;
using System.Text;
namespace LightflowStudio;
public enum LutDimension { ThreeDimensional }
public sealed record LutValidationResult(bool IsValid, LutDimension? Dimension = null, int? Size = null, string? Diagnostic = null);
public sealed record CubeLutData(int Size, float[] Samples)
{
    public System.Numerics.Vector3 DomainMin { get; init; } = System.Numerics.Vector3.Zero;
    public System.Numerics.Vector3 DomainMax { get; init; } = System.Numerics.Vector3.One;

    public static CubeLutData Load(string path)
    {
        var content = File.ReadAllBytes(path);
        var validation = CubeLutValidator.Validate(content);
        if (!validation.IsValid) throw new InvalidDataException(validation.Diagnostic);
        var samples = new List<float>(checked(validation.Size!.Value * validation.Size.Value * validation.Size.Value * 4));
        var text = new UTF8Encoding(false, true).GetString(content);
        var domainMin = System.Numerics.Vector3.Zero;
        var domainMax = System.Numerics.Vector3.One;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            var comment = line.IndexOf('#');
            if (comment >= 0) line = line[..comment].Trim();
            if (line.Length == 0) continue;
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 4 && fields[0].Equals("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase))
            { domainMin = new(Parse(fields[1]), Parse(fields[2]), Parse(fields[3])); continue; }
            if (fields.Length == 4 && fields[0].Equals("DOMAIN_MAX", StringComparison.OrdinalIgnoreCase))
            { domainMax = new(Parse(fields[1]), Parse(fields[2]), Parse(fields[3])); continue; }
            if (fields.Length != 3 || !fields.All(field => float.TryParse(field, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _))) continue;
            samples.Add(float.Parse(fields[0], CultureInfo.InvariantCulture));
            samples.Add(float.Parse(fields[1], CultureInfo.InvariantCulture));
            samples.Add(float.Parse(fields[2], CultureInfo.InvariantCulture));
            samples.Add(1);
        }
        if (domainMax.X <= domainMin.X || domainMax.Y <= domainMin.Y || domainMax.Z <= domainMin.Z)
            throw new InvalidDataException("DOMAIN_MAX must be greater than DOMAIN_MIN on every channel.");
        return new(validation.Size.Value, samples.ToArray()) { DomainMin = domainMin, DomainMax = domainMax };
        static float Parse(string value) => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

public sealed record PlayerColorPipeline(CubeLutData? Camera, CubeLutData? Creative)
{
    public bool HasColor => Camera is not null || Creative is not null;
}

public static class CubeLutValidator
{
    public const int MaximumBytes = 16 * 1024 * 1024;

    public static LutValidationResult Validate(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty) return Invalid("The file is empty.");
        if (content.Length > MaximumBytes) return Invalid("The file is larger than the supported 16 MB limit.");
        string text;
        try { text = new UTF8Encoding(false, true).GetString(content); }
        catch (DecoderFallbackException) { return Invalid("The file is not valid UTF-8 text."); }

        var size = 0;
        var declared = false;
        var values = 0L;
        double[]? domainMin = null, domainMax = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var comment = line.IndexOf('#');
            if (comment >= 0) line = line[..comment].Trim();
            if (line.Length == 0) continue;
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields[0].Equals("TITLE", StringComparison.OrdinalIgnoreCase)) continue;
            if (fields[0].Equals("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase)
                || fields[0].Equals("DOMAIN_MAX", StringComparison.OrdinalIgnoreCase))
            {
                if (fields.Length != 4 || !fields.Skip(1).All(IsFiniteNumber))
                    return Invalid($"{fields[0]} must contain three finite numbers.");
                var parsed = fields.Skip(1).Select(field => double.Parse(field, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
                if (fields[0].Equals("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase)) domainMin = parsed;
                else domainMax = parsed;
                continue;
            }
            if (fields[0].Equals("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase))
                return Invalid("1D LUTs are not supported; use a 3D .cube LUT.");
            if (fields[0].Equals("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                if (declared || fields.Length != 2 || !int.TryParse(fields[1], NumberStyles.None,
                        CultureInfo.InvariantCulture, out size))
                    return Invalid("The file must contain exactly one valid LUT_3D_SIZE declaration.");
                declared = true;
                if (size is < 2 or > 256) return Invalid("The declared 3D LUT size must be between 2 and 256.");
                continue;
            }
            if (fields.Length != 3 || !fields.All(IsFiniteNumber))
                return Invalid("Each LUT data row must contain three finite numbers.");
            values++;
        }
        if (!declared) return Invalid("The file is missing a LUT_3D_SIZE declaration.");
        domainMin ??= [0, 0, 0];
        domainMax ??= [1, 1, 1];
        if (Enumerable.Range(0, 3).Any(index => domainMax[index] <= domainMin[index]))
            return Invalid("DOMAIN_MAX must be greater than DOMAIN_MIN on every channel.");
        var expected = checked((long)size * size * size);
        if (values != expected) return Invalid($"The LUT declares {expected:N0} data rows but contains {values:N0}.");
        return new(true, LutDimension.ThreeDimensional, size);
    }

    private static bool IsFiniteNumber(string field) =>
        double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value);
    private static LutValidationResult Invalid(string diagnostic) => new(false, Diagnostic: diagnostic);
}

