using System.Globalization;
using System.Text;

namespace AgentQueue;

internal static class SlugGenerator
{
    public static string Create(string title)
    {
        string normalized = title.Normalize(NormalizationForm.FormD).ToLowerInvariant();
        StringBuilder builder = new();
        bool previousWasDash = false;

        foreach (char character in normalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(character);
                previousWasDash = false;
            }
            else if (!previousWasDash)
            {
                builder.Append('-');
                previousWasDash = true;
            }

            if (builder.Length >= 80)
            {
                break;
            }
        }

        string slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "finding" : slug;
    }
}
