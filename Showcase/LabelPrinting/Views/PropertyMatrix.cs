using BluePrint.Core.Models;

namespace LabelPrinting.Views;

/// <summary>A group of fields in the Properties panel.</summary>
public enum PropertyGroup
{
    Text,
    Column,
    Expression,
    Picture,
    Barcode,
    Shape,
    Font,
    Color,
    Line,
    Fill,
    Align,
    Format,
    WrapText,
    Border,
    Position,
}

/// <summary>
/// Which groups the Properties panel shows for each kind of element - the one
/// place that decides it. Only what a label needs: everything else an element
/// has stays in the file as it is.
/// </summary>
public static class PropertyMatrix
{
    private static readonly PropertyGroup[] TextLike =
        [PropertyGroup.Font, PropertyGroup.Color, PropertyGroup.Align, PropertyGroup.WrapText, PropertyGroup.Border, PropertyGroup.Position];

    /// <summary>The element types the panel edits, and their groups in the order shown.</summary>
    public static IReadOnlyDictionary<ElementType, IReadOnlyList<PropertyGroup>> Groups { get; } =
        new Dictionary<ElementType, IReadOnlyList<PropertyGroup>>
        {
            [ElementType.Label]    = [PropertyGroup.Text, .. TextLike],
            [ElementType.Field]    = [PropertyGroup.Column, .. Insert(TextLike, PropertyGroup.Format)],
            [ElementType.Computed] = [PropertyGroup.Expression, .. Insert(TextLike, PropertyGroup.Format)],
            [ElementType.Image]    = [PropertyGroup.Picture, PropertyGroup.Border, PropertyGroup.Position],
            [ElementType.Line]     = [PropertyGroup.Line],
            [ElementType.Shape]    = [PropertyGroup.Shape, PropertyGroup.Line, PropertyGroup.Fill, PropertyGroup.Position],
            [ElementType.Barcode]  = [PropertyGroup.Expression, PropertyGroup.Barcode, PropertyGroup.Border, PropertyGroup.Position],
        };

    /// <summary>Groups that belong to one element only: they are left out when several are selected.</summary>
    public static IReadOnlySet<PropertyGroup> SingleElementOnly { get; } = new HashSet<PropertyGroup>
    {
        PropertyGroup.Text, PropertyGroup.Column, PropertyGroup.Expression, PropertyGroup.Picture, PropertyGroup.Position,
    };

    /// <summary>
    /// The groups for a selection: one element gets all of its groups; several get
    /// the groups every one of them has, without the per-element ones. Empty when
    /// one of them is a kind the panel does not edit (a chart, a sub band).
    /// </summary>
    public static IReadOnlyList<PropertyGroup> For(IReadOnlyList<ElementType> types)
    {
        if (types.Count == 0 || types.Any(t => !Groups.ContainsKey(t))) return [];
        if (types.Count == 1) return Groups[types[0]];

        IEnumerable<PropertyGroup> common = Groups[types[0]];
        foreach (var type in types.Skip(1)) common = common.Intersect(Groups[type]);
        return common.Where(g => !SingleElementOnly.Contains(g)).ToList();
    }

    /// <summary>The name of an element type on screen.</summary>
    public static string DisplayName(ElementType type) => type switch
    {
        ElementType.Label    => "Text",
        ElementType.Computed => "Compute",
        ElementType.Image    => "Picture",
        _                    => type.ToString(),
    };

    // Format goes after Align, before Wrap text.
    private static PropertyGroup[] Insert(PropertyGroup[] groups, PropertyGroup extra)
    {
        var list = groups.ToList();
        list.Insert(list.IndexOf(PropertyGroup.WrapText), extra);
        return [.. list];
    }
}
