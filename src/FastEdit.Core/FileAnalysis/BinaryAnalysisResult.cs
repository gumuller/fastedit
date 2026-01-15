namespace FastEdit.Core.FileAnalysis;

public class BinaryAnalysisResult
{
    public bool IsBinary { get; set; }
    public BinaryDetectionReason Reason { get; set; }
    public string? DetectedMimeType { get; set; }
    public string? DetectedEncoding { get; set; }
    public double Confidence { get; set; }
}

public enum BinaryDetectionReason
{
    MagicBytesMatch,
    NullBytesDetected,
    HighNonPrintableRatio,
    TextEncodingDetected,
    ExtensionHint,
    EmptyFile
}
