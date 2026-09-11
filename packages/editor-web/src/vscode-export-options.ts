import { typographyStyles, colorThemes } from './vscode-settings'

export const exportFormats = ['pdf', 'html', 'png', 'jpg', 'print'] as const
export const paperSizes = ['A4', 'A5', 'Letter', 'Legal'] as const
export const exportDefaults = {
  format: 'pdf' as typeof exportFormats[number],
  typography: 'minimal' as typeof typographyStyles[number],
  colorTheme: 'default-light' as Exclude<typeof colorThemes[number], 'vscode'>,
  fontSize: 16, lineHeight: 1.75, contentWidth: 820,
  paperSize: 'A4' as typeof paperSizes[number], landscape: false,
  marginTop: 16, marginRight: 16, marginBottom: 16, marginLeft: 16,
  header: '', footer: '', pageNumbers: true,
  keepTablesTogether: true, keepHeadingsWithNext: true,
  imageScale: 2, imageMaxHeight: 16000, jpegQuality: 90,
}
export type ExportOptions = typeof exportDefaults
export type ExportFormat = ExportOptions['format']
export type ExportHtmlResult = { html: string; images: string[] }

/** Validate user input at the message/persistence boundary; never silently clamp it. */
export function isExportOptions(value: unknown): value is ExportOptions {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const o = value as Record<string, unknown>
  const number = (key: string, min: number, max: number, integer = false): boolean =>
    typeof o[key] === 'number' && Number.isFinite(o[key]) && o[key] >= min && o[key] <= max && (!integer || Number.isInteger(o[key]))
  return exportFormats.includes(o.format as ExportFormat)
    && typographyStyles.includes(o.typography as ExportOptions['typography'])
    && o.colorTheme !== 'vscode' && colorThemes.includes(o.colorTheme as ExportOptions['colorTheme'])
    && paperSizes.includes(o.paperSize as ExportOptions['paperSize'])
    && ['landscape', 'pageNumbers', 'keepTablesTogether', 'keepHeadingsWithNext'].every(key => typeof o[key] === 'boolean')
    && ['header', 'footer'].every(key => typeof o[key] === 'string' && o[key].length <= 500)
    && number('fontSize', 10, 32) && number('lineHeight', 1, 3) && number('contentWidth', 320, 2400, true)
    && ['marginTop', 'marginRight', 'marginBottom', 'marginLeft'].every(key => number(key, 0, 50))
    && number('imageScale', 1, 3, true) && number('imageMaxHeight', 1000, 30000, true) && number('jpegQuality', 1, 100, true)
}
