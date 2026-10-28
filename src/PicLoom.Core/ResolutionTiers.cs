using System.Globalization;
using System.Text.RegularExpressions;

namespace PicLoom.Core;

/// <summary>
/// One preset entry of the authoritative 24-tier catalogue (8 aspect ratios x 3 image sizes).
/// </summary>
public sealed record ResolutionTier(string ImageSize, string AspectRatio, int Width, int Height);

/// <summary>
/// The single authoritative preset table. Mirrors the CookSleep reference table literally: every
/// width/height is a multiple of 16, no single edge exceeds 3840, the ratio never exceeds 3:1 and the
/// pixel count stays inside 655 360 .. 8 294 400.
/// </summary>
public static class ResolutionTiers
{
    /// <summary>Aspect ratios in display order (ratio-major ordering of <see cref="All"/>).</summary>
    public static readonly IReadOnlyList<string> AspectRatios =
        ["1:1", "3:2", "2:3", "16:9", "9:16", "4:3", "3:4", "21:9"];

    /// <summary>Image size buckets in display order.</summary>
    public static readonly IReadOnlyList<string> ImageSizes = ["1K", "2K", "4K"];

    /// <summary>The 24 presets: 8 ratios x 3 sizes, ratio-major order.</summary>
    public static readonly IReadOnlyList<ResolutionTier> All =
    [
        new("1K", "1:1", 1024, 1024),
        new("2K", "1:1", 2048, 2048),
        new("4K", "1:1", 2880, 2880),
        new("1K", "3:2", 1536, 1024),
        new("2K", "3:2", 2160, 1440),
        new("4K", "3:2", 3456, 2304),
        new("1K", "2:3", 1024, 1536),
        new("2K", "2:3", 1440, 2160),
        new("4K", "2:3", 2304, 3456),
        new("1K", "16:9", 1280, 720),
        new("2K", "16:9", 2560, 1440),
        new("4K", "16:9", 3840, 2160),
        new("1K", "9:16", 720, 1280),
        new("2K", "9:16", 1440, 2560),
        new("4K", "9:16", 2160, 3840),
        new("1K", "4:3", 1024, 768),
        new("2K", "4:3", 2048, 1536),
        new("4K", "4:3", 3200, 2400),
        new("1K", "3:4", 768, 1024),
        new("2K", "3:4", 1536, 2048),
        new("4K", "3:4", 2400, 3200),
        new("1K", "21:9", 1280, 544),
        new("2K", "21:9", 2560, 1088),
        new("4K", "21:9", 3840, 1600)
    ];

    /// <summary>Finds a preset by its (imageSize, aspectRatio) pair using ordinal comparison.</summary>
    public static bool TryFind(string? imageSize, string? aspectRatio, out ResolutionTier tier)
    {
        if (!string.IsNullOrEmpty(imageSize) && !string.IsNullOrEmpty(aspectRatio))
        {
            foreach (var candidate in All)
            {
                if (string.Equals(candidate.ImageSize, imageSize, StringComparison.Ordinal)
                    && string.Equals(candidate.AspectRatio, aspectRatio, StringComparison.Ordinal))
                {
                    tier = candidate;
                    return true;
                }
            }
        }

        tier = new ResolutionTier("", "", 0, 0);
        return false;
    }
}

/// <summary>
/// Requested resolution for an inline task. <see cref="Mode"/> is the explicit discriminator
/// (<c>auto</c> / <c>tier</c> / <c>ratio</c> / <c>pixels</c>); when it is <c>null</c> the legacy
/// field-based inference is used so request bodies written by earlier releases keep working.
/// <see cref="Mode"/> is the trailing parameter on purpose: every pre-existing positional call and
/// every stored JSON body still binds.
/// </summary>
public sealed record ResolutionTarget(string? ImageSize = null, string? AspectRatio = null,
    int? Width = null, int? Height = null, string? Mode = null);

/// <summary>
/// Turns a <see cref="ResolutionTarget"/> into an immutable <see cref="ResolutionSnapshot"/> and
/// back. Validation order is fixed and shared with the UI so that only the closest error surfaces.
/// </summary>
public static class ResolutionSizing
{
    /// <summary>Custom sizes must be a multiple of this value on both edges.</summary>
    public const int Multiple = 16;

    /// <summary>Largest allowed single edge.</summary>
    public const int MaxEdge = 3840;

    /// <summary>Largest allowed long-edge to short-edge ratio (both directions).</summary>
    public const int MaxRatio = 3;

    /// <summary>Smallest allowed total pixel count.</summary>
    public const int MinPixels = 655_360;

    /// <summary>Largest allowed total pixel count.</summary>
    public const int MaxPixels = 8_294_400;

    /// <summary>Explicit <see cref="ResolutionTarget.Mode"/> value: let the model decide.</summary>
    public const string ModeAuto = "auto";

    /// <summary>Explicit <see cref="ResolutionTarget.Mode"/> value: a preset (imageSize + ratio).</summary>
    public const string ModeTier = "tier";

    /// <summary>Explicit <see cref="ResolutionTarget.Mode"/> value: any ratio, preset or computed.</summary>
    public const string ModeRatio = "ratio";

    /// <summary>Explicit <see cref="ResolutionTarget.Mode"/> value: explicit pixel dimensions.</summary>
    public const string ModePixels = "pixels";

    /// <summary>Pixel budget per image size bucket, mirroring the CookSleep reference table.</summary>
    public static readonly IReadOnlyDictionary<string, long> TierPixelBudgets =
        new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["1K"] = 1_572_864L,
            ["2K"] = 4_194_304L,
            ["4K"] = 8_294_400L
        };

    /// <summary>
    /// Most decimal places accepted when scaling one part of a ratio. Six places cover every spelling
    /// the UI suggests (<c>2.39:1</c>) and keep the cross multiplication inside <see cref="long"/>.
    /// </summary>
    private const int MaxRatioScale = 6;

    /// <summary>Largest digit value accepted for one part of a ratio, for the same overflow reason.</summary>
    private const long MaxRatioPart = 1_000_000L;

    private static readonly Regex RatioPattern = new(
        @"^\s*(\d+(?:\.\d+)?)\s*[:xX×]\s*(\d+(?:\.\d+)?)\s*$",
        RegexOptions.CultureInvariant);

    /// <summary>Resolves a preset tier to concrete pixel dimensions.</summary>
    public static bool TryResolveTier(string? imageSize, string? aspectRatio, out int width, out int height)
    {
        if (ResolutionTiers.TryFind(imageSize, aspectRatio, out var tier))
        {
            width = tier.Width;
            height = tier.Height;
            return true;
        }

        width = 0;
        height = 0;
        return false;
    }

    /// <summary>
    /// Validates a custom pixel size in the documented order and returns the first matching error
    /// code, or <c>null</c> when the size is acceptable.
    /// </summary>
    public static string? ValidateCustom(int? width, int? height)
    {
        if (width is not > 0 || height is not > 0) return "resolution_invalid";

        var w = width.GetValueOrDefault();
        var h = height.GetValueOrDefault();
        if (w % Multiple != 0 || h % Multiple != 0) return "resolution_not_multiple_of_16";
        if (w > MaxEdge || h > MaxEdge) return "resolution_edge_too_large";

        var longEdge = Math.Max(w, h);
        var shortEdge = Math.Min(w, h);
        if (longEdge > shortEdge * MaxRatio) return "resolution_aspect_too_extreme";

        var pixels = (long)w * h;
        if (pixels < MinPixels || pixels > MaxPixels) return "resolution_pixels_out_of_range";
        return null;
    }

    /// <summary>
    /// Parses <c>"16:9"</c>, <c>"5:4"</c>, <c>"2.39:1"</c>, <c>"16x9"</c> or <c>"16×9"</c> into its two
    /// positive decimal parts. Returns <c>false</c> for anything else, including empty input.
    /// </summary>
    public static bool TryParseRatio(string? text, out double width, out double height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = RatioPattern.Match(text);
        if (!match.Success) return false;

        return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                   out width)
               && double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                   out height);
    }

    /// <summary>
    /// True when <paramref name="text"/> denotes the same value as one of the eight preset ratios.
    /// The comparison is mathematical (cross multiplication on decimal-scaled integers), never
    /// textual, so <c>21:9</c>, <c>42:18</c> and <c>7:3</c> all match the 21:9 preset while
    /// <c>5:4</c> and <c>2.39:1</c> match none of them. This is what PRD §3.1 rule 7 requires:
    /// "a ratio that hits one of the eight table ratios uses the table value".
    /// </summary>
    public static bool TryMatchPresetRatio(string? text, out string presetKey)
    {
        presetKey = "";
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = RatioPattern.Match(text);
        if (!match.Success) return false;

        if (!TryParseDecimalPart(match.Groups[1].Value, out var valueWidth, out var scaleWidth))
            return false;
        if (!TryParseDecimalPart(match.Groups[2].Value, out var valueHeight, out var scaleHeight))
            return false;

        // Put both parts on the same power of ten: "2.39:1" -> 239:100, "1.5:1" -> 15:10.
        var width = valueWidth * PowerOfTen(scaleHeight);
        var height = valueHeight * PowerOfTen(scaleWidth);
        if (height == 0) return false;

        foreach (var key in ResolutionTiers.AspectRatios)
        {
            if (!TryParseRatio(key, out var presetWidth, out var presetHeight)) continue;
            // Presets are whole numbers <= 21, so cross multiplication stays far inside long.
            if (width * (long)presetHeight == (long)presetWidth * height)
            {
                presetKey = key;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits one decimal part of a ratio into its digits and its scale: <c>"2.39"</c> becomes
    /// <c>(239, 2)</c> and <c>"16"</c> becomes <c>(16, 0)</c>. The documented bounds keep the cross
    /// multiplication in <see cref="TryMatchPresetRatio"/> from overflowing.
    /// </summary>
    private static bool TryParseDecimalPart(string text, out long value, out int scale)
    {
        value = 0;
        scale = 0;

        var dot = text.IndexOf('.');
        var digits = dot < 0 ? text : string.Concat(text.AsSpan(0, dot), text.AsSpan(dot + 1));
        if (digits.Length == 0) return false;
        foreach (var character in digits)
        {
            if (character is < '0' or > '9') return false;
        }

        scale = dot < 0 ? 0 : text.Length - dot - 1;
        if (scale > MaxRatioScale) return false;
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return false;

        return value is > 0 and <= MaxRatioPart;
    }

    private static long PowerOfTen(int exponent)
    {
        var result = 1L;
        for (var index = 0; index < exponent; index++) result *= 10L;
        return result;
    }

    /// <summary>
    /// Computes concrete pixel dimensions for an arbitrary ratio inside the pixel budget of
    /// <paramref name="imageSize"/>. A ratio that equals one of the eight presets resolves to the
    /// fixed table; every other ratio is searched on the 16px grid, keeping the largest result whose
    /// divergence from the requested ratio is at most 1%. The grid search itself is a byte-for-byte
    /// port of the CookSleep reference implementation; the TypeScript mirror in
    /// <c>src/PicLoom.Web/src/lib/resolutionTiers.ts</c> must stay identical.
    /// </summary>
    public static bool TryCalculateRatioSize(string? imageSize, string? aspectRatio, out int width,
        out int height)
    {
        width = 0;
        height = 0;

        if (!TryParseRatio(aspectRatio, out var ratioWidth, out var ratioHeight)) return false;
        if (imageSize is null || !TierPixelBudgets.TryGetValue(imageSize, out var budget)) return false;

        if (TryMatchPresetRatio(aspectRatio, out var presetKey)
            && ResolutionTiers.TryFind(imageSize, presetKey, out var preset))
        {
            width = preset.Width;
            height = preset.Height;
            return true;
        }

        var targetRatio = ratioWidth / ratioHeight;
        if (!double.IsFinite(targetRatio) || targetRatio <= 0) return false;

        var bestWidth = 0;
        var bestHeight = 0;
        long bestPixels = -1;

        for (var candidateWidth = Multiple; candidateWidth <= MaxEdge; candidateWidth += Multiple)
        {
            var idealHeight = candidateWidth / targetRatio;
            for (var step = 0; step < 2; step++)
            {
                var candidateHeight = step == 0 ? FloorToMultiple(idealHeight) : CeilToMultiple(idealHeight);

                // Written as negated comparisons on purpose: NaN and infinity fall through to the
                // skip branch exactly like they do in the reference implementation.
                if (!(candidateHeight >= Multiple) || !(candidateHeight <= MaxEdge)) continue;

                var pixels = (long)candidateWidth * (long)candidateHeight;
                if (pixels > budget || pixels < MinPixels) continue;

                var actualRatio = candidateWidth / candidateHeight;
                if (Math.Max(actualRatio, candidateHeight / candidateWidth) > MaxRatio) continue;
                if (Math.Abs((actualRatio - targetRatio) / targetRatio) > 0.01) continue;
                if (pixels <= bestPixels) continue;

                bestPixels = pixels;
                bestWidth = candidateWidth;
                bestHeight = (int)candidateHeight;
            }
        }

        if (bestPixels < 0) return false;
        width = bestWidth;
        height = bestHeight;
        return true;
    }

    /// <summary>Rounds a candidate edge down onto the 16px grid.</summary>
    public static double FloorToMultiple(double value) => Math.Floor(value / Multiple) * Multiple;

    /// <summary>Rounds a candidate edge up onto the 16px grid.</summary>
    public static double CeilToMultiple(double value) => Math.Ceiling(value / Multiple) * Multiple;

    /// <summary>Builds the immutable snapshot for a validated target.</summary>
    public static ResolutionSnapshot ToSnapshot(ResolutionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ResolveMode(target) switch
        {
            ModeAuto => new ResolutionSnapshot(Guid.Empty, "", ResolutionMode.Auto, null, null, null, null),
            ModeTier => TierSnapshot(target),
            ModeRatio => RatioSnapshot(target),
            ModePixels => PixelsSnapshot(target),
            _ => throw new DomainException("resolution_mode_invalid")
        };
    }

    /// <summary>
    /// Produces the upstream <c>size</c> string (<c>"{width}x{height}"</c>). Snapshots created by
    /// older releases may still carry an aspect-class pair without pixel dimensions, so the tier
    /// table is used as a fallback. An <see cref="ResolutionMode.Auto"/> snapshot has no size by
    /// design; callers must skip the field instead of asking for it.
    /// </summary>
    public static string SizeString(ResolutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Mode == ResolutionMode.Auto) throw new DomainException("resolution_invalid");

        if (snapshot.Width is > 0 && snapshot.Height is > 0)
            return $"{snapshot.Width}x{snapshot.Height}";

        if (TryResolveTier(snapshot.ImageSize, snapshot.AspectRatio, out var width, out var height))
            return $"{width}x{height}";

        throw new DomainException("resolution_invalid");
    }

    /// <summary>
    /// Normalises the discriminator. A missing mode keeps the historical field-based inference:
    /// pixel dimensions win over a tier pair, and an entirely empty target is <c>resolution_invalid</c>.
    /// </summary>
    private static string ResolveMode(ResolutionTarget target)
    {
        if (target.Mode is null)
        {
            if (target.Width is not null || target.Height is not null) return ModePixels;
            if (target.ImageSize is not null || target.AspectRatio is not null) return ModeTier;
            throw new DomainException("resolution_invalid");
        }

        var mode = target.Mode.Trim().ToLowerInvariant();
        return mode switch
        {
            ModeAuto or ModeTier or ModeRatio or ModePixels => mode,
            _ => throw new DomainException("resolution_mode_invalid")
        };
    }

    private static ResolutionSnapshot TierSnapshot(ResolutionTarget target)
    {
        if (target.Width is not null || target.Height is not null)
            throw new DomainException("resolution_invalid");
        if (!ResolutionTiers.TryFind(target.ImageSize, target.AspectRatio, out var tier))
            throw new DomainException("resolution_tier_unknown");

        return new ResolutionSnapshot(Guid.Empty, "", ResolutionMode.AspectClass,
            tier.Width, tier.Height, tier.AspectRatio, tier.ImageSize);
    }

    private static ResolutionSnapshot RatioSnapshot(ResolutionTarget target)
    {
        if (target.Width is not null || target.Height is not null)
            throw new DomainException("resolution_invalid");
        if (target.ImageSize is null || !TierPixelBudgets.ContainsKey(target.ImageSize))
            throw new DomainException("resolution_tier_unknown");
        if (!TryParseRatio(target.AspectRatio, out _, out _))
            throw new DomainException("resolution_ratio_invalid");
        if (!TryCalculateRatioSize(target.ImageSize, target.AspectRatio, out var width, out var height))
            throw new DomainException("resolution_ratio_invalid");

        // A ratio that hits a preset is stored under the canonical key ("42:18" -> "16:9") so the
        // snapshot reads the same as one produced by picking the preset card; everything else keeps
        // the text the user typed.
        var storedRatio = TryMatchPresetRatio(target.AspectRatio, out var presetKey)
            ? presetKey
            : target.AspectRatio!.Trim();

        return new ResolutionSnapshot(Guid.Empty, "", ResolutionMode.AspectClass,
            width, height, storedRatio, target.ImageSize);
    }

    private static ResolutionSnapshot PixelsSnapshot(ResolutionTarget target)
    {
        var error = ValidateCustom(target.Width, target.Height);
        if (error is not null) throw new DomainException(error);

        return new ResolutionSnapshot(Guid.Empty, "", ResolutionMode.ExactPixels,
            target.Width, target.Height, null, null);
    }
}
