namespace PulseWeaver;

internal static class VersionComparer
{
    public static string Normalize(string? version)
    {
        var value = (version ?? string.Empty).Trim();
        return value.StartsWith('v') || value.StartsWith('V') ? value[1..] : value;
    }

    public static int Compare(string? left, string? right)
    {
        var a = Parse(left);
        var b = Parse(right);

        for (var i = 0; i < 3; i++)
        {
            var difference = a.Numbers[i].CompareTo(b.Numbers[i]);
            if (difference != 0)
            {
                return difference;
            }
        }

        if (a.Prerelease is null && b.Prerelease is null)
        {
            return 0;
        }

        if (a.Prerelease is null)
        {
            return 1;
        }

        if (b.Prerelease is null)
        {
            return -1;
        }

        return ComparePrerelease(a.Prerelease, b.Prerelease);
    }

    private static (int[] Numbers, string? Prerelease) Parse(string? version)
    {
        var value = Normalize(version);
        string? prerelease = null;
        var dashIndex = value.IndexOf('-');
        if (dashIndex >= 0)
        {
            prerelease = value[(dashIndex + 1)..];
            value = value[..dashIndex];
        }

        var numbers = new int[3];
        var parts = value.Split('.');
        for (var i = 0; i < 3 && i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], out var number))
            {
                numbers[i] = number;
            }
        }

        return (numbers, string.IsNullOrEmpty(prerelease) ? null : prerelease);
    }

    private static int ComparePrerelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');

        for (var i = 0; i < Math.Max(leftParts.Length, rightParts.Length); i++)
        {
            if (i >= leftParts.Length)
            {
                return -1;
            }

            if (i >= rightParts.Length)
            {
                return 1;
            }

            var leftIsNumber = int.TryParse(leftParts[i], out var leftNumber);
            var rightIsNumber = int.TryParse(rightParts[i], out var rightNumber);
            var difference = leftIsNumber && rightIsNumber
                ? leftNumber.CompareTo(rightNumber)
                : string.CompareOrdinal(leftParts[i], rightParts[i]);

            if (difference != 0)
            {
                return Math.Sign(difference);
            }
        }

        return 0;
    }
}
