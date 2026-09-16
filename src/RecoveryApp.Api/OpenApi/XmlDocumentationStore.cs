using System.Collections.Frozen;
using System.Reflection;
using System.Xml.Linq;

namespace RecoveryApp.Api.OpenApi;

/// <summary>
/// Reads the XML documentation files emitted next to the application binaries and indexes them by
/// the compiler's member id, so the OpenAPI document can carry the same prose as IntelliSense.
/// </summary>
/// <remarks>
/// Records document their positional members with <c>&lt;param&gt;</c> elements attached to the type
/// rather than <c>&lt;summary&gt;</c> elements on the generated properties, so both shapes are indexed.
/// </remarks>
public sealed class XmlDocumentationStore
{
    private readonly FrozenDictionary<string, string> _summaries;
    private readonly FrozenDictionary<string, string> _parameters;

    private XmlDocumentationStore(
        Dictionary<string, string> summaries,
        Dictionary<string, string> parameters)
    {
        _summaries = summaries.ToFrozenDictionary(StringComparer.Ordinal);
        _parameters = parameters.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Loads every <c>.xml</c> documentation file sitting beside the given assemblies.</summary>
    /// <param name="assemblies">Assemblies whose documentation should be indexed.</param>
    /// <returns>The populated store. Missing files are skipped silently.</returns>
    public static XmlDocumentationStore Load(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        Dictionary<string, string> summaries = new(StringComparer.Ordinal);
        Dictionary<string, string> parameters = new(StringComparer.Ordinal);

        foreach (Assembly assembly in assemblies)
        {
            string path = Path.ChangeExtension(assembly.Location, ".xml");

            if (string.IsNullOrEmpty(assembly.Location) || !File.Exists(path))
            {
                continue;
            }

            foreach (XElement member in XDocument.Load(path).Descendants("member"))
            {
                string? name = member.Attribute("name")?.Value;

                if (name is null)
                {
                    continue;
                }

                if (Normalize(member.Element("summary")?.Value) is { } summary)
                {
                    summaries[name] = summary;
                }

                foreach (XElement parameter in member.Elements("param"))
                {
                    string? parameterName = parameter.Attribute("name")?.Value;

                    if (parameterName is not null && Normalize(parameter.Value) is { } text)
                    {
                        parameters[$"{name}:{parameterName}"] = text;
                    }
                }
            }
        }

        return new XmlDocumentationStore(summaries, parameters);
    }

    /// <summary>Returns the summary documented on a type.</summary>
    /// <param name="type">The type to look up.</param>
    /// <returns>The summary text, or <see langword="null"/> when the type is undocumented.</returns>
    public string? GetTypeSummary(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _summaries.GetValueOrDefault($"T:{FullName(type)}");
    }

    /// <summary>
    /// Returns the documentation for a member, preferring the <c>&lt;summary&gt;</c> on the property
    /// and falling back to the <c>&lt;param&gt;</c> the record declared on its primary constructor.
    /// </summary>
    /// <param name="declaringType">The type declaring the member.</param>
    /// <param name="memberName">The CLR member name.</param>
    /// <returns>The documentation text, or <see langword="null"/> when the member is undocumented.</returns>
    public string? GetMemberSummary(Type declaringType, string memberName)
    {
        ArgumentNullException.ThrowIfNull(declaringType);

        string typeName = FullName(declaringType);

        return _summaries.GetValueOrDefault($"P:{typeName}.{memberName}")
            ?? _summaries.GetValueOrDefault($"F:{typeName}.{memberName}")
            ?? _parameters.GetValueOrDefault($"T:{typeName}:{memberName}");
    }

    private static string FullName(Type type) =>
        (type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type).FullName?.Replace('+', '.')
        ?? type.Name;

    private static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', lines.Select(line => line.Trim())).Trim();
    }
}
