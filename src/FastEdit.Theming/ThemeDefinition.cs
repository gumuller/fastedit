using System.Text.Json.Serialization;

namespace FastEdit.Theming;

public class ThemeDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("isDark")]
    public bool IsDark { get; set; }

    [JsonPropertyName("colors")]
    public ThemeColors Colors { get; set; } = new();

    [JsonPropertyName("syntaxColors")]
    public SyntaxTheme SyntaxColors { get; set; } = new();
}

public class ThemeColors
{
    [JsonPropertyName("windowBackground")]
    public string WindowBackground { get; set; } = "#FFFFFF";

    [JsonPropertyName("windowForeground")]
    public string WindowForeground { get; set; } = "#000000";

    [JsonPropertyName("titleBarBackground")]
    public string TitleBarBackground { get; set; } = "#F0F0F0";

    [JsonPropertyName("titleBarForeground")]
    public string TitleBarForeground { get; set; } = "#000000";

    [JsonPropertyName("editorBackground")]
    public string EditorBackground { get; set; } = "#FFFFFF";

    [JsonPropertyName("editorForeground")]
    public string EditorForeground { get; set; } = "#000000";

    [JsonPropertyName("editorLineNumbersForeground")]
    public string EditorLineNumbersForeground { get; set; } = "#808080";

    [JsonPropertyName("editorCurrentLineBackground")]
    public string EditorCurrentLineBackground { get; set; } = "#F0F0F0";

    [JsonPropertyName("editorSelectionBackground")]
    public string EditorSelectionBackground { get; set; } = "#ADD6FF";

    [JsonPropertyName("editorSelectionForeground")]
    public string EditorSelectionForeground { get; set; } = "#000000";

    [JsonPropertyName("hexOffsetForeground")]
    public string HexOffsetForeground { get; set; } = "#0000FF";

    [JsonPropertyName("hexBytesForeground")]
    public string HexBytesForeground { get; set; } = "#000000";

    [JsonPropertyName("hexAsciiForeground")]
    public string HexAsciiForeground { get; set; } = "#008000";

    [JsonPropertyName("hexModifiedBackground")]
    public string HexModifiedBackground { get; set; } = "#FFFF00";

    [JsonPropertyName("hexNullByteForeground")]
    public string HexNullByteForeground { get; set; } = "#C0C0C0";

    [JsonPropertyName("panelBackground")]
    public string PanelBackground { get; set; } = "#F5F5F5";

    [JsonPropertyName("panelBorder")]
    public string PanelBorder { get; set; } = "#E0E0E0";

    [JsonPropertyName("treeViewBackground")]
    public string TreeViewBackground { get; set; } = "#FFFFFF";

    [JsonPropertyName("treeViewItemHover")]
    public string TreeViewItemHover { get; set; } = "#E8E8E8";

    [JsonPropertyName("treeViewItemSelected")]
    public string TreeViewItemSelected { get; set; } = "#CCE8FF";

    [JsonPropertyName("tabBackground")]
    public string TabBackground { get; set; } = "#F0F0F0";

    [JsonPropertyName("tabActiveBackground")]
    public string TabActiveBackground { get; set; } = "#FFFFFF";

    [JsonPropertyName("tabForeground")]
    public string TabForeground { get; set; } = "#606060";

    [JsonPropertyName("tabActiveForeground")]
    public string TabActiveForeground { get; set; } = "#000000";

    [JsonPropertyName("tabBorder")]
    public string TabBorder { get; set; } = "#E0E0E0";

    [JsonPropertyName("statusBarBackground")]
    public string StatusBarBackground { get; set; } = "#007ACC";

    [JsonPropertyName("statusBarForeground")]
    public string StatusBarForeground { get; set; } = "#FFFFFF";

    [JsonPropertyName("buttonBackground")]
    public string ButtonBackground { get; set; } = "#E0E0E0";

    [JsonPropertyName("buttonForeground")]
    public string ButtonForeground { get; set; } = "#000000";

    [JsonPropertyName("buttonHoverBackground")]
    public string ButtonHoverBackground { get; set; } = "#D0D0D0";

    [JsonPropertyName("scrollBarBackground")]
    public string ScrollBarBackground { get; set; } = "#F0F0F0";

    [JsonPropertyName("scrollBarThumb")]
    public string ScrollBarThumb { get; set; } = "#C0C0C0";

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#007ACC";

    [JsonPropertyName("errorColor")]
    public string ErrorColor { get; set; } = "#FF0000";

    [JsonPropertyName("warningColor")]
    public string WarningColor { get; set; } = "#FFA500";

    [JsonPropertyName("successColor")]
    public string SuccessColor { get; set; } = "#00FF00";
}

public class SyntaxTheme
{
    [JsonPropertyName("comment")]
    public string Comment { get; set; } = "#008000";

    [JsonPropertyName("string")]
    public string String { get; set; } = "#A31515";

    [JsonPropertyName("keyword")]
    public string Keyword { get; set; } = "#0000FF";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "#2B91AF";

    [JsonPropertyName("number")]
    public string Number { get; set; } = "#098658";

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = "#000000";

    [JsonPropertyName("preprocessor")]
    public string Preprocessor { get; set; } = "#808080";

    [JsonPropertyName("function")]
    public string Function { get; set; } = "#795E26";

    [JsonPropertyName("variable")]
    public string Variable { get; set; } = "#001080";

    [JsonPropertyName("constant")]
    public string Constant { get; set; } = "#0070C1";

    [JsonPropertyName("attribute")]
    public string Attribute { get; set; } = "#2B91AF";

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = "#800000";

    [JsonPropertyName("attributeName")]
    public string AttributeName { get; set; } = "#FF0000";

    [JsonPropertyName("attributeValue")]
    public string AttributeValue { get; set; } = "#0000FF";
}
