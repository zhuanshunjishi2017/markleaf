package android.print;

/**
 * The SDK stubs omit this callback's public runtime constructor. Keep the
 * compatibility boundary here, without reflection; PdfExporter provides a
 * system-print fallback if a platform stops permitting direct PDF generation.
 */
public abstract class MarkLeafLayoutResultCallback extends PrintDocumentAdapter.LayoutResultCallback {}
