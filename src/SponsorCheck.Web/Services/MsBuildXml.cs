namespace SponsorCheck.Web.Services;

/// <summary>Shared MSBuild-flavoured XML emitters used by both generators.</summary>
public static class MsBuildXml
{
    public static string Escape(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    /// <summary>
    /// Renders a self-closing element with the first <paramref name="inlineCount"/> attributes on the
    /// element line and each remaining attribute on its own line, aligned under the first attribute.
    /// </summary>
    public static string SelfClosingElement(
        string elementName,
        IReadOnlyList<(string Name, string Value)> attributes,
        int inlineCount)
    {
        var head = $"<{elementName} ";
        var indent = new string(' ', head.Length);

        var builder = new StringBuilder();
        builder.Append(head);

        var inline = attributes.Take(inlineCount).Select(_ => $"{_.Name}=\"{Escape(_.Value)}\"");
        builder.Append(string.Join(" ", inline));

        foreach (var (name, value) in attributes.Skip(inlineCount))
        {
            builder.AppendLine();
            builder.Append(indent);
            builder.Append($"{name}=\"{Escape(value)}\"");
        }

        builder.Append(" />");
        return builder.ToString();
    }

    public static string PropertyGroup(IEnumerable<(string Name, string Value)> properties)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<PropertyGroup>");
        foreach (var (name, value) in properties)
        {
            builder.AppendLine($"  <{name}>{Escape(value)}</{name}>");
        }

        builder.Append("</PropertyGroup>");
        return builder.ToString();
    }

    public static string Fenced(string content, string language = "xml") =>
        $"```{language}\n{content}\n```";
}
